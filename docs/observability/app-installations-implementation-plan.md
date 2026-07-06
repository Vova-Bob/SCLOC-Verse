# План реалізації: `localization_version` + `game_folder_path` + `selected_environment`

> **Дата:** 2026-07-05 (v2 — перероблено після архітектурного рев'ю)
> **Тип:** Покроковий план (без реалізації). Чекає на погодження.
> **Основа:** `docs/observability/app-installations-forensic-2026-07-05.md`
> **Мета:** Активувати 3 з 6 мертвих колонок `app_installations` через прокидання даних з C# у БД.
> **Архітектурний принцип:** additive-only, без DROP, без зміни RLS, без зміни DDL.

---

## Що змінилось у v2 (враховано архітектурне рев'ю)

| # | Було у v1 | Стало у v2 |
|---|---|---|
| 1 | `SelectedEnvironment` → `Settings.Default` → InstallationService | In-memory transient state в `IInstallationContextProvider` |
| 2 | `InstalledLocalizationVersion` → `Settings.Default` → InstallationService | In-memory transient state в `IInstallationContextProvider` |
| 3 | `MainWindow` + параметр `ISettingsService` (22-й параметр) | `MainWindow` + параметр `IInstallationContextUpdater` ( GOD-class не розростається сервісом Settings) |
| 4 | `InstallationService` залежить від `ISettingsService` | `InstallationService` залежить лише від `IInstallationContextProvider` (не знає про Settings) |
| 5 | `LocalizationInstaller` пише в Settings | `LocalizationInstaller` пише лише в `IInstallationContextUpdater` |
| 6 | `Settings.settings` розширюється 2 полями | **`Settings.settings` НЕ зачіпається взагалі** |

**Ключова ідея:** введення єдиного `IInstallationContextProvider` як **read-only снапшота контексту інсталяції**, що агрегує дані з природних джерел (Settings для `GameFolder`, UI для `SelectedEnvironment`, LocalizationInstaller для `LocalizationVersion`).

---

## Архітектура після змін

```
                     ┌──────────────────────────────────┐
                     │  IInstallationContextProvider    │
                     │  ─────────────────────────────   │
                     │  + GameFolderPath                │ ← SettingsService.GetGameFolder()
                     │  + SelectedEnvironment           │ ← EnvironmentSelector (transient)
                     │  + LocalizationVersion           │ ← LocalizationInstaller (transient)
                     │                                  │
                     │  IInstallationContextUpdater     │
                     │  ─────────────────────────────   │
                     │  + UpdateSelectedEnvironment()   │ ← MainWindow (UI event)
                     │  + UpdateLocalizationVersion()   │ ← LocalizationInstaller (post-Install)
                     └────────────────┬─────────────────┘
                                      │
                                      ▼
                          InstallationService.Sync()
                                      │
                                      ▼
                          app_installations (3 колонки заповнюються)
```

**Переваги архітектури:**
- `InstallationService` **не знає про Settings** — лише про контекст
- `LocalizationInstaller` **не пише в Settings** — лише в transient-контекст
- `MainWindow` не отримує `ISettingsService` (не погіршує God-class)
- `Settings.settings` залишається **хоча б для справжніх user preferences**
- Кожне поле живе у своєму природному джерелі

---

## Попередні умови

| Умова | Стан |
|---|:---:|
| Колонки в БД існують (міграція 1) | ✅ |
| `user_analytics` view читає `selected_environment` | ✅ |
| `control_center.installations` view читає всі колонки | ✅ |
| RLS-політики `INSERT/UPDATE` дозволяють юзеру писати власні рядки | ✅ |
| `ISettingsService.GetGameFolder()` існує і стабільний | ✅ |

---

## Порядок виконання

### Крок 0 — Резервний commit

```bash
git add .
git commit -m "Резервна точка перед активацією полів app_installations"
```

---

### Крок 1 — Створити інтерфейси

#### 1.1. `SCLOCVerse/Interfaces/IInstallationContextProvider.cs` (новий)

```csharp
namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Read-only доступ до контексту інсталяції.
    /// Агрегує дані з різних джерел (Settings, UI, LocalizationInstaller).
    /// Споживач: InstallationService.
    /// </summary>
    public interface IInstallationContextProvider
    {
        /// <summary>Шлях до директорії Star Citizen (з Settings).</summary>
        string? GameFolderPath { get; }

        /// <summary>Поточне вибране середовище гри (transient, з UI).</summary>
        string? SelectedEnvironment { get; }

        /// <summary>Версія встановленої локалізації (transient, після Install/Update).</summary>
        string? LocalizationVersion { get; }
    }
}
```

#### 1.2. `SCLOCVerse/Interfaces/IInstallationContextUpdater.cs` (новий)

```csharp
namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Write-only інтерфейс для оновлення transient-полів контексту.
    /// Споживачі: MainWindow (SelectedEnvironment), LocalizationInstaller (LocalizationVersion).
    /// GameFolderPath сюди не входить — він приходить зі Settings, а не з runtime.
    /// </summary>
    public interface IInstallationContextUpdater
    {
        void UpdateSelectedEnvironment(string? environment);
        void UpdateLocalizationVersion(string? version);
    }
}
```

> 💡 **Розділення read/write (ISP):** `InstallationService` залежить лише від read-інтерфейсу, `MainWindow`/`LocalizationInstaller` — лише від write-інтерфейсу. Один клас реалізує обидва.

**Verification:** `dotnet build` без помилок.

---

### Крок 2 — Створити реалізацію `InstallationContextProvider`

**Файл:** `SCLOCVerse/Services/Auth/InstallationContextProvider.cs` (новий)

```csharp
using SCLOCVerse.Interfaces;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Singleton-реалізація контексту інсталяції.
    /// GameFolderPath — read from Settings (через ISettingsService).
    /// SelectedEnvironment / LocalizationVersion — transient in-memory state.
    /// </summary>
    public sealed class InstallationContextProvider
        : IInstallationContextProvider, IInstallationContextUpdater
    {
        private readonly ISettingsService _settings;
        private volatile string? _selectedEnvironment;
        private volatile string? _localizationVersion;

        public InstallationContextProvider(ISettingsService settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        // --- IInstallationContextProvider (read) ---

        public string? GameFolderPath => _settings.GetGameFolder();

        public string? SelectedEnvironment => _selectedEnvironment;

        public string? LocalizationVersion => _localizationVersion;

        // --- IInstallationContextUpdater (write) ---

        public void UpdateSelectedEnvironment(string? environment)
        {
            _selectedEnvironment = string.IsNullOrWhiteSpace(environment) ? null : environment;
        }

        public void UpdateLocalizationVersion(string? version)
        {
            _localizationVersion = string.IsNullOrWhiteSpace(version) ? null : version;
        }
    }
}
```

**Design notes:**
- `volatile` для безпеки reader/writer (MainWindow-потік пише, sync-потік читає).
- Не-locking: допускаємо неатомарну консистентність (для телеметрії прийнятно).
- `GameFolderPath` проксіюється в `ISettingsService` — НЕ дублюється в пам'яті.
- `null`-safe: `UpdateX(null)` скидає в `null`.

**Verification:** `dotnet build` без помилок.

---

### Крок 3 — Розширити модель `AppInstallation`

**Файл:** `SCLOCVerse/Models/Auth/AppInstallation.cs`

**Додати 3 властивості** (після `OsVersion`, рядок 31):

```csharp
[Supabase.Postgrest.Attributes.Column("localization_version")]
public string? LocalizationVersion { get; set; }

[Supabase.Postgrest.Attributes.Column("game_folder_path")]
public string? GameFolderPath { get; set; }

[Supabase.Postgrest.Attributes.Column("selected_environment")]
public string? SelectedEnvironment { get; set; }
```

**Verification:** `dotnet build` без помилок.

---

### Крок 4 — Розширити `InstallationService`

**Файл:** `SCLOCVerse/Services/Auth/InstallationService.cs`

#### 4.1. Конструктор (рядок 25)

```csharp
public InstallationService(
    ISupabaseClientFactory clientFactory,
    ITelemetryService? telemetry = null,
    IInstallationContextProvider? context = null)  // ← додати
{
    _supabase = clientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(clientFactory));
    _installId = GetOrCreateInstallId();
    _telemetry = telemetry;
    _context = context;  // ← додати
}

private readonly IInstallationContextProvider? _context;  // ← додати
```

> 💡 Optional (`= null`) — backward compatible з існуючими unit-тестами.

#### 4.2. INSERT-гілка (рядки 71-84)

Додати 3 поля:

```csharp
var installation = new AppInstallation
{
    UserId = userId,
    InstallId = _installId,
    AppVersion = appVersion,
    Platform = platform,
    MachineId = machineId,
    OsVersion = osVersion,
    LocalizationVersion = _context?.LocalizationVersion,    // ← додати
    GameFolderPath = _context?.GameFolderPath,              // ← додати
    SelectedEnvironment = _context?.SelectedEnvironment,    // ← додати
    FirstSeen = now,
    CreatedAt = now,
    LastSeen = now,
    UpdatedAt = now,
    IsActive = true
};
```

#### 4.3. UPDATE-гілка (рядки 100-108)

Додати 3 `.Set(...)` рядки:

```csharp
await _supabase
    .From<AppInstallation>()
    .Filter("install_id", Supabase.Postgrest.Constants.Operator.Equals, _installId)
    .Set(i => i.UserId, userId)
    .Set(i => i.AppVersion, appVersion)
    .Set(i => i.Platform, platform)
    .Set(i => i.MachineId, machineId)
    .Set(i => i.OsVersion, osVersion)
    .Set(i => i.LocalizationVersion, _context?.LocalizationVersion)     // ← додати
    .Set(i => i.GameFolderPath, _context?.GameFolderPath)               // ← додати
    .Set(i => i.SelectedEnvironment, _context?.SelectedEnvironment)     // ← додати
    .Set(i => i.LastSeen, now)
    .Set(i => i.UpdatedAt, now)
    .Set(i => i.IsActive, true)
    .Update(cancellationToken: cancellationToken)
    .ConfigureAwait(false);
```

> 💡 **Resolver-методи НЕ потрібні** — `_context?.X` повертає `null`-safe без зайвого коду.

**Verification:** `dotnet build` без помилок.

---

### Крок 5 — Розширити `LocalizationInstaller` (лише write-інтерфейс)

**Файл:** `SCLOCVerse/Services/LocalizationServices/LocalizationInstaller.cs`

#### 5.1. Конструктор (рядок 18)

```csharp
public sealed class LocalizationInstaller : ILocalizationInstaller
{
    private readonly IInstallationContextUpdater? _contextUpdater;  // ← додати

    public LocalizationInstaller(IInstallationContextUpdater? contextUpdater = null)  // ← додати
    {
        _contextUpdater = contextUpdater;
    }
    /* ...решта без змін... */
}
```

#### 5.2. У `InstallAsync` після успішної установки (після рядка 118)

```csharp
var message = LocalizationMessages.InstallCompleted(
    environmentName, release.TagName, userCfgPathCreated != null, localizationUpdated);

// ← Додати блок: оновлюємо версію локалізації в transient-контексті
try
{
    _contextUpdater?.UpdateLocalizationVersion(release.TagName);
}
catch
{
    // Стаття 1: оновлення контексту — non-throwing.
}
```

> 💡 `UpdateAsync` не існує окремо — `InstallAsync` робить install + update (умовний download через ETag). Цей крок покриває обидва сценарії.

**Verification:** `dotnet build` без помилок.

---

### Крок 6 — Розширити `MainWindow` (лише write-інтерфейс)

**Файл:** `SCLOCVerse/MainWindow.xaml.cs`

#### 6.1. Додати поле та параметр конструктора

```csharp
private readonly IInstallationContextUpdater _contextUpdater;  // ← додати

// У конструкторі (рядок 81) — додати в кінець:
public MainWindow(
    /* ...існуючі параметри... */,
    IInstallationContextUpdater contextUpdater)  // ← додати
{
    /* ... */
    _contextUpdater = contextUpdater;
    /* ... */
}
```

> 💡 **Зауваження:** це +1 параметр до вже перевантаженого конструктора. **Проте** це НЕ сервіс (як `ISettingsService`), а lightweight context-updater. Альтернатива (впатнути в `EnvironmentSelector` напряму) — більша інвазія в XAML/UserControl. Обрано найменше зло.

#### 6.2. Розширити обробник `SelectedEnvironmentChanged` (рядок 152)

```csharp
EnvSelector.SelectedEnvironmentChanged += (s, e) =>
{
    var envName = EnvSelector?.SelectedEnvironment?.Name;
    try
    {
        _contextUpdater.UpdateSelectedEnvironment(envName);  // ← додати
    }
    catch
    {
        // Стаття 1: оновлення контексту — non-throwing.
    }

    BtnInstall.Content = _buttonHelper.GetInstallButtonText(
        EnvSelector?.SelectedEnvironment, _viewModel.GameFolder);
};
```

**Verification:** `dotnet build` без помилок. Дебаг-вивід після зміни ComboBox — значення попадає в `_contextProvider.SelectedEnvironment`.

---

### Крок 7 — Прокинути через Composition Roots

#### 7.1. `AppCompositionRoot` (конструктор, рядок 50)

**Файл:** `SCLOCVerse/Composition/AppCompositionRoot.cs`

**Додати поле + створити singleton:**

```csharp
private readonly InstallationContextProvider _installationContextProvider;  // ← додати

public AppCompositionRoot()
{
    _ignoreRulesProvider = new IgnoreRulesProvider();
    _folderSearchService = new FolderSearchService(_ignoreRulesProvider);
    _settingsService = new SettingsService();

    // ← Додати: створюємо контекст після SettingsService, до telemetry/auth.
    _installationContextProvider = new InstallationContextProvider(_settingsService);

    /* ...існуючий код створення telemetry, updater, etc... */

    _authCompositionRoot = new AuthCompositionRoot(
        supabaseUrl,
        supabaseAnonKey,
        _telemetryClient,
        _installationContextProvider);  // ← додати параметр
}
```

#### 7.2. `AppCompositionRoot.CreateMainWindow` (рядок 140)

```csharp
public MainWindow CreateMainWindow()
{
    var searchFolder = new SearchFolder(_folderSearchService, _settingsService);
    var viewModel = new MainWindowViewModel(searchFolder, _settingsService);

    var windowHelper = new WindowHelper();
    var localizationInstaller = new LocalizationInstaller(_installationContextProvider);  // ← +param
    var readmeService = new ReadmeService();

    return new MainWindow(
        viewModel,
        windowHelper,
        localizationInstaller,
        readmeService,
        _updater,
        /* ...існуючі параметри... */,
        _hangarTimerService,
        _hotkeyService,
        _installationContextProvider);  // ← додати в кінець
}
```

#### 7.3. `AuthCompositionRoot` (рядок 19)

**Файл:** `SCLOCVerse/Composition/AuthCompositionRoot.cs`

```csharp
public AuthCompositionRoot(
    string supabaseUrl,
    string supabaseAnonKey,
    ITelemetryService telemetry,
    IInstallationContextProvider context)  // ← додати
{
    _secureStorage = new SecureSessionStorage();
    _clientFactory = new SupabaseClientFactory(supabaseUrl, supabaseAnonKey, _secureStorage);
    _callbackListener = new LoopbackCallbackListener();
    _installationService = new InstallationService(_clientFactory, telemetry, context);  // ← +context
    /* ...решта без змін... */
}
```

**Verification:** `dotnet build` без помилок. Усі 3 consumer'и (`InstallationService`, `LocalizationInstaller`, `MainWindow`) отримали інтерфейс через DI.

---

### Крок 8 — Фінальна перевірка

#### 8.1. Збірка

```bash
dotnet build SCLOCVerse.sln -c Release
```

Очікуваний результат: `Build succeeded. 0 Error(s)`.

#### 8.2. Smoke-тест під реальним акаунтом

> ⚠ За чеклистом `docs/checklists/Database-Verification.md`.

1. Запустити `SCLOCVerse.exe`.
2. Увійти під існуючим акаунтом.
3. Вибрати папку Star Citizen через Settings.
4. Вибрати середовище `LIVE` в `EnvironmentComboBox`.
5. Натиснути Install (локалізація).
6. **Перевірити в Supabase Studio:**

```sql
SELECT install_id, app_version, localization_version,
       game_folder_path, selected_environment, last_seen
FROM public.app_installations
WHERE install_id = '<your-install-id>';
```

**Очікуваний результат:** усі 3 колонки заповнені:
- `localization_version` = `"vX.X.X"` (TagName з GitHub)
- `game_folder_path` = `"C:\Program Files\Roberts Space Industries\StarCitizen"`
- `selected_environment` = `"LIVE"`

#### 8.3. Edge Function та views

```bash
curl https://nrytczdbhehiotflaagl.supabase.co/functions/v1/ecosystem-status
```

`totalUsers` +1, помилок немає.

```sql
SELECT user_id, install_id, localization_version, game_folder_path, selected_environment
FROM public.user_analytics
WHERE user_id = '<your-user-uuid>';
```

Нові колонки заповнені.

---

### Крок 9 — Фінальний commit

```bash
git add .
git commit -m "Активовано телеметрію: localization_version, game_folder_path, selected_environment через IInstallationContextProvider"
```

---

## Матриця залежностей кроків

| Крок | Залежить від | Впливає на |
|---|---|---|
| 0 (commit) | — | усі наступні |
| 1 (інтерфейси) | — | 2, 4, 5, 6, 7 |
| 2 (реалізація Provider) | 1 | 7 |
| 3 (AppInstallation model) | — | 4 |
| 4 (InstallationService) | 1, 3 | 7 |
| 5 (LocalizationInstaller) | 1 | 7 |
| 6 (MainWindow) | 1 | 7 |
| 7 (CompositionRoots) | 2, 4, 5, 6 | 8 |
| 8 (verification) | 7 | 9 |
| 9 (commit) | 8 | — |

---

## Оцінка обсягу роботи

| Крок | Файл | Рядків змін | Складність |
|---|---|---:|---|
| 1 | `IInstallationContextProvider.cs`, `IInstallationContextUpdater.cs` (нові) | +30 | низька |
| 2 | `InstallationContextProvider.cs` (новий) | +40 | низька |
| 3 | `AppInstallation.cs` | +9 | низька |
| 4 | `InstallationService.cs` | +12 | низька |
| 5 | `LocalizationInstaller.cs` | +12 | низька |
| 6 | `MainWindow.xaml.cs` | +12 | низька |
| 7 | `AppCompositionRoot.cs`, `AuthCompositionRoot.cs` | +6 | низька |
| 8 | — (verification) | — | — |
| **РАЗОМ** | **8 файлів (3 нові + 5 існуючих)** | **~120 рядків** | **низька** |

---

## Що НЕ робити в цьому таску

| Дія | Причина |
|---|---|
| ❌ DDL-міграція (CREATE/ALTER) | Колонки вже існують з міграції 1 |
| ❌ **Змінювати `Settings.settings`** | У v2 не потрібно — `SelectedEnvironment`/`LocalizationVersion` живуть transient |
| ❌ Активація `os_build`, `install_source`, `update_channel` | Поза рамками таску (див. форензик) |
| ❌ DROP будь-яких колонок | Additive-only контракт |
| ❌ Зміна RLS-політик | Не потрібна |
| ❌ Зміна views (user_analytics, installations) | Вже читають потрібні колонки |
| ❌ Зміна Edge Functions | `ecosystem-status` не зачіпається |
| ❌ Глобальний рефакторинг MainWindow | Тільки +1 параметр +1 handler-рядок |
| ❌ Зберігати `tagName` у `LocalizationMetadata` (meta.json) | Транзитний transient-state достатній; meta.json можна розширити окремим таском, якщо потрібна персистентність між перезапусками |

---

## Відомі обмеження v2

| Обмеження | Причина | Прийнятність |
|---|---|---|
| `LocalizationVersion` = `null` після перезапуску програми (до наступного Install/Update) | Transient-state не зберігається між сесіями | 🟢 Прийнятно: при sync відправляється null, при наступному Install — реальне значення. Не блокує телеметрію. |
| `SelectedEnvironment` = `null` під час **першого** sync при SignIn (UI ще не завантажив середовища) | `AuthService.SignInAsync` викликається до повного UI render | 🟢 Прийнятно: другий sync (RestoreSession при наступному старті, або SignIn при повторному логіні) вже має значення |
| Якщо користувач не обрав жодного середовища (LIVE/PTU/EPTU/HOTFIX відсутні) — `SelectedEnvironment` = `null` | Природна поведінка | 🟢 Прийнятно — позначає "не активне середовище" |

> 💡 **Якщо в майбутньому потрібна персистентність `LocalizationVersion` між перезапусками** — додати поле `tagName` у `LocalizationMetadata` (per-environment meta.json). Це окремий таск, не блокує поточний.

---

## Ризики виконання

| Ризик | Ймовірність | Мінімізація |
|---|---|---|
| Thread-safety: MainWindow-потік пише `_selectedEnvironment`, sync-потік читає | 🟢 | `volatile` на полях (non-blocking read; accept неатомарну консистентність для телеметрії) |
| `MainWindow` конструктор перевантажений (23 параметри) | 🟡 | Lightweight інтерфейс, не сервіс. Альтернатива (впатнути в `EnvironmentSelector`) — більша інвазія |
| Unit-тести `InstallationService` / `LocalizationInstaller` зламаються | 🟢 | Нові параметри optional (`= null`) |
| `InstallationContextProvider` створюється як transient замість singleton | 🟡 | Поле `private readonly` в `AppCompositionRoot` гарантує singleton |
| Споживачі платформи очікують `InstallationService(clientFactory, telemetry)` | 🟢 | Optional 3-й параметр — backward compatible |

---

## Verification-чеклист (після виконання)

- [ ] `dotnet build` успішний (Release конфіг)
- [ ] Створено `IInstallationContextProvider.cs`, `IInstallationContextUpdater.cs`, `InstallationContextProvider.cs`
- [ ] `Settings.settings` **не зачіпалось** (на відміну від v1)
- [ ] Smoke-тест: 3 колонки в `app_installations` заповнені після Install
- [ ] Edge Function `ecosystem-status` повертає успішну відповідь
- [ ] View `user_analytics` показує нові дані для тестового юзера
- [ ] View `control_center.installations` показує нові дані
- [ ] Існуючі unit-тести проходять
- [ ] Жодних mojibake в українському тексті
- [ ] Commit-повідомлення українською

---

## Ключова перевага v2 над v1

```
v1: Settings ← (дубльовані) ← InstallationService → БД
                  ↑
            джерело істини розмите

v2: UI/Installer → Provider (transient) ─┐
                                         ├─→ InstallationService → БД
        Settings ────────────────────────┘
                  ↑
            єдине природне джерело для кожного поля
```

Кожне поле живе у своєму природному місці:
- `GameFolderPath` → Settings (визначене користувачем, персистентне)
- `SelectedEnvironment` → Provider (поточний UI-стан, transient)
- `LocalizationVersion` → Provider (отримано з останнього Install, transient)
