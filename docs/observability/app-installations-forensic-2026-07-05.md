# Форензик-аналіз: `app_installations` + реалізація 3 полів

> **Дата:** 2026-07-05
> **Тип:** Лише форензик. Без реалізації, без commit.
> **Метод:** MCP Supabase (production-дані) + grep локального репозиторію.
> **Project ID:** `nrytczdbhehiotflaagl` (SCLOC-Verse, eu-west-1)

---

# ЧАСТИНА 1 — Використання `app_installations` у Edge Functions Supabase

## 1.1 Знайдені Edge Functions

На проєкті розгорнуто **2 Edge Functions** (Deno/TS):

| Slug | Version | JWT | Призначення |
|---|---|---|---|
| `ecosystem-status` | v6 | verify | Агрегований статус екосистеми (релізи + кількість юзерів) |
| `hangar-anchor` | v2 | verify | Anchor-таймер для Hangar Overlay (зовнішній fetch `exec.xyxyll.com`) |

## 1.2 Матриця використання `app_installations`

| Edge Function | Читає `app_installations`? | Колонки | Як |
|---|:---:|---|---|
| `ecosystem-status` | ✅ ТАК (опосередковано) | `is_active`, `last_seen` | через RPC `public.ecosystem_stats()` |
| `hangar-anchor` | ❌ НІ | — | зовнішній fetch `https://exec.xyxyll.com/app.js` |

### Як саме `ecosystem-status` читає `app_installations`

Edge Function викликає PostgREST-RPC (anon-ключем):

```typescript
fetch(`${supabaseUrl}/rest/v1/rpc/ecosystem_stats`, {
  method: 'POST',
  headers: { apikey: anonKey, Authorization: `Bearer ${anonKey}` },
  body: '{}',
}, 2000);
```

RPC `public.ecosystem_stats()` (SECURITY DEFINER) повертає json:

```sql
SELECT count(*) FROM app_installations                              → totalInstallations
SELECT count(*) FROM app_installations WHERE is_active = true       → activeTotal
SELECT count(*) FROM app_installations
  WHERE is_active = true AND last_seen > now() - interval '30 days' → active30d
```

**Використовуються лише 2 колонки: `is_active`, `last_seen`.**

## 1.3 Повна карта залежностей `app_installations` (усі споживачі)

| Споживач | Тип | Операція | Колонки, що читаються/пишуться |
|---|---|---|---|
| **Edge `ecosystem-status`** | RPC `ecosystem_stats()` | read | `is_active`, `last_seen` (через count) |
| **Edge `hangar-anchor`** | — | — | НЕ ЧИТАЄ |
| **VIEW `control_center.installations`** | view (CC адмінка) | read | **УСІ 18 колонок** (`SELECT *`) |
| **VIEW `control_center.health`** | view (CC Home) | read | `last_seen` (для `active_installations_last_7d`) |
| **VIEW `control_center.statistics`** | view (CC Statistics) | read | `count(*)` |
| **VIEW `public.user_analytics`** | view (CC Users) | read | `user_id, install_id, country, app_version, platform, machine_id, os_version, update_channel, install_source, selected_environment, is_active, first_seen, last_seen, created_at` |
| **TRIGGER `trg_app_installations_set_country`** | BEFORE INSERT/UPDATE | write | `country` (GeoIP з Cloudflare headers) |
| **RLS policy** × 5 | INSERT/UPDATE/DELETE/SELECT | — | за `user_id = auth.uid()` |
| **C# `InstallationService`** | PostgREST-клієнт | read+write | `user_id, install_id, app_version, platform, machine_id, os_version, last_seen, first_seen, created_at, is_active, updated_at` (11 колонок) |
| **FK з `telemetry_events.install_id`** | ON DELETE SET NULL | — | `install_id` |
| **FK з `error_reports.install_id`** | ON DELETE SET NULL | — | `install_id` (таблиця порожня) |
| **FK з `admin_audit_log.target_install_id`** | ON DELETE SET NULL | — | `install_id` (таблиця порожня) |

## 1.4 Реальні дані production-таблиці (станом на 2026-07-05)

| Показник | Значення |
|---|---:|
| Усього рядків | **46** |
| `update_channel` = NULL | 0 |
| `update_channel` = 'stable' | **46** |
| `update_channel` = 'dev' | 0 |
| `install_source` = NULL | 0 |
| `install_source` = 'unknown' | **46** |
| `localization_version` ≠ NULL | **0** |
| `game_folder_path` ≠ NULL | **0** |
| `selected_environment` ≠ NULL | **0** |
| `os_build` ≠ NULL | **0** |

**Висновок:** усі 46 записів мають DEFAULT-значення `update_channel='stable'` (PostgREST автоматично підставив DEFAULT при INSERT, оскільки клієнт не шле цього поля). `install_source='unknown'` — те саме. **Жодне з 4 полів-кандидатів (`localization_version`, `game_folder_path`, `selected_environment`, `os_build`) ніколи не заповнювалось.**

---

# ЧАСТИНА 2 — Реалізація `localization_version`, `game_folder_path`, `selected_environment`

Користувач просить реалізувати ці три поля «відповідно в коді та БД згідно документації» — це важливо для телеметрії. Нижче — форензик-карта джерел даних у C# коді.

## 2.1 Зона впливу

### А) БД-складова (additive-only, низький ризик)

| Файл БД | Вплив |
|---|---|
| `public.app_installations` (міграція 1) | Колонки **вже існують** (TEXT nullable). Не треба CREATE/ALTER. |
| `public.user_analytics` view (міграція 6) | **Вже читає `selected_environment`** (рядок у SELECT). Не читає `localization_version`, `game_folder_path`. |
| `control_center.installations` view (міграція 7) | Читає всі колонки (SELECT *) — зміна не потрібна. |
| RPC `ecosystem_stats()` | Не зачіпається. |

### Б) C#-складова

| Файл | Поточний стан | Що треба |
|---|---|---|
| `Models/Auth/AppInstallation.cs` | Має 11 колонок з 18 (нема `localization_version`, `game_folder_path`, `selected_environment`, `os_build`, `update_channel`, `install_source`, `country`) | **Додати 3 властивості** для трьох полів, що реалізуємо |
| `Services/Auth/InstallationService.cs` | INSERT/UPDATE лише 11 полів (рядки 71-84 INSERT, 100-108 UPDATE) | **Додати 3 `.Set(...)` рядки** в обидві гілки + прийняти через конструктор/параметри |
| `Composition/AppCompositionRoot.cs` | Створює `InstallationService(clientFactory, telemetry)` (тільки 2 залежності) | **Додати залежності** для постачання значень |
| `App.xaml.cs:95-97` | Ініціалізує `Settings.Default.UpdateChannel = "Stable"` при першому запуску | (рецедент — те саме треба для `localization_version`) |

### В) Джерела даних для трьох полів

#### Поле `game_folder_path` — ✅ ВЖЕ ДОСТУПНЕ

**Джерело:** `Settings.Default.GameFolder` (string, User scope)
**API:** `ISettingsService.GetGameFolder()` (SettingsService.cs:15)
**ViewModel:** `MainWindowViewModel.GameFolder` (з INotifyPropertyChanged)
**Ризик реалізації:** 0 — просто прокинути рядок у `InstallationService`.

#### Поле `selected_environment` — ⚠ НЕ ПЕРСИСТЕНТНЕ

**Джерело (transient):** `EnvSelector.SelectedEnvironment.Name` (EnvironmentSelector.xaml.cs:16)
**Можливі значення:** `"LIVE"`, `"PTU"`, `"HOTFIX"`, `"EPTU"` (EnvironmentSelector.xaml.cs:63)
**Проблема:** зберігається лише в UI-стані (`EnvironmentComboBox`), **НЕ** в `Settings.settings`. Після перезапуску втрачається.
**Що треба для реалізації:**
1. Додати `Setting Name="SelectedEnvironment" Type="System.String" Scope="User"` в `Settings.settings`.
2. Оновлювати при кожній зміні `EnvironmentComboBox` (`SelectedEnvironmentChanged` event у MainWindow.xaml.cs:152).
3. У `SettingsService.cs` додати getter/setter (або розширити існуючий інтерфейс).

#### Поле `localization_version` — ❌ НЕ ФІКСУЄТЬСЯ ВЗАГАЛОМ

**Джерело під час install/update:** `GitHubRelease.TagName` (напр. `"v1.2.0"`) — `LocalizationInstaller.cs:53, 118`, `Updater.cs:139`.
**Проблема:** встановлена версія **не зберігається ніде** між запусками. При старті програма не знає, яка версія локалізації встановлена — лише під час чергового install/update дізнається з GitHub.
**Що треба для реалізації:**
1. Додати `Setting Name="InstalledLocalizationVersion" Type="System.String" Scope="User"` в `Settings.settings`.
2. В `LocalizationInstaller.cs` після успішного Install/Update зберігати `release.TagName` в `SettingsService`.
3. У `InstallationService.SyncCurrentInstallationAsync` брати версію з `SettingsService.GetInstalledLocalizationVersion()`.
4. **Альтернатива (складніша):** при старті читати метадані встановленого `global.ini` (JSON-заголовок з assetId/etag — `LocalizationMetadata`, рядок 546). Але цей шлях не реалізований зараз.

## 2.2 Ризики

| Ризик | Рівень | Опис |
|---|---|---|
| **`game_folder_path` — PII-ризик** | 🟢 Відсутній | Поле **не містить персональних даних** за архітектурою SCLOC-Verse. `FolderSearchService.IsValidGameRoot` приймає лише папку з іменем `StarCitizen`, що містить хоча б одну підтримувану підпапку середовища (`LIVE`/`PTU`/`EPTU`/`HOTFIX`). Star Citizen через RSI Launcher встановлюється в `C:\Program Files\Roberts Space Industries\StarCitizen\` або на окремий диск — профіль користувача Windows (`%USERPROFILE%`) не входить до підтримуваних сценаріїв розташування гри. Поле є **діагностичним**, не PII. |
| **Sync момент:** `selected_environment` змінюється в UI лише після старту програми | 🟡 Середній | `InstallationService.SyncCurrentInstallationAsync` викликається при логіні (раніше за UI вибору середовища). Перший sync може не мати актуального значення. Другий sync (наступний старт) — вже матиме збережене. |
| **Конкуренція Settings.settings:** одночасний запис з різних місць | 🟢 Низький | .NET Settings.settings — singleton per-process, write-blocking. |
| **RLS-провал:** клієнт не може оновити власні поля (42501, історичний інцидент) | 🟢 Низький | Інцидент 42501 вже закритий (RLS-політики IS INSERT/UPDATE). Нові колонки — nullable, RLS не змінюється. |
| **View `user_analytics` де-синхронізація:** selected_environment вже там читається, але завжди NULL | 🟢 Низький | Після реалізації — заповниться автоматично. CC-сторінка `Users.razor` просто покаже реальні значення. |
| **Розширення інтерфейсу `IInstallationService` або `IInstallationDataProvider`:** breaking change для Composition Root | 🟢 Низький | CompositionRoot — єдиний creator, легко адаптувати. |
| **UTF-8 mojibake в `localization_version`:** TagName з GitHub — ASCII-безпечний (semver) | 🟢 Низький | Не стосується. |
| **Зациклення залежностей:** InstallationService потрібен ISettingsService, а SettingsService не залежить від installation | 🟢 Низький | Додавання `ISettingsService` у конструктор InstallationService — безпечне. |

## 2.3 Регресії

| Можлива регресія | Ймовірність |
|---|---|
| Злам `ecosystem-status` EF після додавання колонок | **0%** — нові колонки nullable, RPC не зачіпає їх |
| Злам `hangar-anchor` EF | **0%** — не читає БД |
| Злам view `user_analytics` | **0%** — `selected_environment` вже в SELECT |
| Злам view `control_center.installations` | **0%** — `SELECT *` автоматично включить нові колонки |
| Злам `InstallationService.SyncCurrentInstallationAsync` (вже стабільний) | 🟡 Мінімальний — додаються лише `.Set(...)` рядки для нових полів |
| Злами CC UI (Users.razor, Installations.razor) | **0%** — отримають більше даних без формату-зміни |
| 42501 RLS regression | **0%** — RLS-політики не змінюються |

## 2.4 Рекомендації

### Рекомендований план реалізації (після погодження)

| Крок | Файл | Дія |
|---|---|---|
| **1** | `Settings.settings` + `Settings.Designer.cs` (auto-regen) | Додати `InstalledLocalizationVersion` (string) та `SelectedEnvironment` (string) |
| **2** | `Interfaces/ISettingsService.cs` | Додати `string? GetInstalledLocalizationVersion()`, `void SetInstalledLocalizationVersion(string?)`, `string? GetSelectedEnvironment()`, `void SetSelectedEnvironment(string?)` |
| **3** | `Services/SettingsService.cs` | Реалізувати getter/setter для обох полів |
| **4** | `Models/Auth/AppInstallation.cs` | Додати `[Column("localization_version")] string? LocalizationVersion`, `[Column("game_folder_path")] string? GameFolderPath`, `[Column("selected_environment")] string? SelectedEnvironment` |
| **5** | `Services/Auth/InstallationService.cs` | 1. Розширити конструктор параметром `ISettingsService` (або обгорткою `IInstallationDataProvider`). 2. У INSERT-гілку додати 3 поля. 3. У UPDATE-гілку додати 3 `.Set(...)` рядки |
| **6** | `Controls/EnvironmentSelector.xaml.cs` або `MainWindow.xaml.cs:152` | При зміні `SelectedEnvironmentChanged` зберігати в `SettingsService.SetSelectedEnvironment(env.Name)` |
| **7** | `Services/LocalizationServices/LocalizationInstaller.cs` | Після успішного Install/Update ( рядки 118, місце формування повідомлення) зберігати `release.TagName` у `SettingsService.SetInstalledLocalizationVersion(...)` |
| **8** | `Composition/AppCompositionRoot.cs` | Прокинути нову залежність `ISettingsService` у `InstallationService` |

### Що НЕ робити (поєднання з Optimization Matrix)

- ❌ Активувати `os_build` — недоступне в C# без додаткового коду (`Environment.OSVersion.Version.Build`), ризик/PKI-цінність низькі. (Натомість `os_version` містить build у рядку.)
- ❌ Активувати `install_source` — InnoSetup не передає runtime-параметр у додаток (`OutputBaseFilename=SCLOC-Verse_Setup`, без `/SOURCE=`). Можна захардкодити `"InnoSetup"` як константу, але це вже «для майбутнього».
- ❌ Активувати `update_channel` через цей же таск — він частково працює (DEFAULT 'stable' з БД), окремий таск.
- ❌ DROP COLUMN — порушить additive-only контракт (Стаття P0 з AGENTS.md).
- ❌ Нормалізувати/маскувати `game_folder_path` — не потрібно, PII-ризик відсутній за архітектурою.

### Діагностична цінність `game_folder_path` (рекомендація ✅)

**PII-ризик відсутній за архітектурою SCLOC-Verse.** Підтвердження в коді:

- `FolderSearchService.IsValidGameRoot` (FolderSearchService.cs:55-63) — приймає лише папку з іменем `StarCitizen`, що містить хоча б одну підтримувану підпапку середовища (`LIVE`/`PTU`/`EPTU`/`HOTFIX`). Інші локації відхиляються.
- Star Citizen через RSI Launcher встановлюється в `C:\Program Files\Roberts Space Industries\StarCitizen\` або на окремий диск — профіль користувача Windows не входить до підтримуваних сценаріїв.

Поле зберігає **шлях до директорії встановлення Star Citizen** — не містить імені користувача Windows, не є персональними даними.

**Діагностична цінність для Control Center** (підтверджена попередніми аудитами):

- скільки користувачів використовують нестандартний шлях;
- чи пов'язані певні помилки лише з конкретною директорією;
- чи виникають проблеми лише в `LIVE` або `PTU`;
- чи впливає місце встановлення гри на успішність локалізації.

Тобто це **діагностичне поле**, а не PII. Передається в БД без нормалізації.

---

# Підсумок форензика

## 1) Edge Functions

**`app_installations` використовується в 1 з 2 Edge Functions:**
- `ecosystem-status` → через RPC `ecosystem_stats()` → колонки `is_active`, `last_seen` (через `count(*)`)
- `hangar-anchor` → **не використовує**

## 2) Реалізація 3 полів

| Поле | Джерело в C# | Стан джерела | Складність |
|---|---|---|---|
| `game_folder_path` | `Settings.Default.GameFolder` | ✅ персистентне, доступне | низька |
| `selected_environment` | `EnvSelector.SelectedEnvironment.Name` | ❌ transient, не зберігається | середня |
| `localization_version` | `GitHubRelease.TagName` під час install | ❌ не фіксується після install | середня |

**Усі 3 колонки в БД вже існують** (міграція 1, TEXT nullable) — **DDL-зміни не потрібні.**

**Загальний ризик:** 🟢 Низький. Усі зміни additive-only, без DROP, без зміни RLS, без зміни views. PII-ризик для `game_folder_path` відсутній за архітектурою SCLOC-Verse (валідація `IsValidGameRoot`).
