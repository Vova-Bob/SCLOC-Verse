# SCLOC-Verse — Final Architecture Review (Zero-Regression)

> **Дата:** 2026-07-05
> **Версія:** 1.0.0.1 (pre-release)
> **Тип:** Архітектурне рев'ю (read-only, без змін коду)
> **Метод:** 8 паралельних агентів розвідки + верифікація конфліктів
> **Обсяг:** 4 проєкти монорепо + Supabase (2 схеми, 26 міграцій, ~24 SECURITY DEFINER функції)

---

## Executive Summary

SCLOC-Verse — це **монорепо з чотирма проєктами** (WPF-клієнт, Blazor Server Control Center, Worker Notifier, Notifications contracts library), які спілкуються виключно через спільний Postgres/Supabase. Архітектура **процесоізольована** — жодне крос-проєктне посилання на рівні коду, крім `Notifier → Notifications`.

**Підсумкова оцінка архітектури: 6.5 / 10.**

Система **функціонально працездатна** та має ряд архітектурно сильних рішень (процесна ізоляція, at-least-once доставка, RLS everywhere, єдиний інтерфейс телеметрії, clean notification provider abstraction). Однак вона **не готова до масштабування понад поточні обсяги** і має **5 P0-дефектів**, які варто усунути ДО або НЕЗАБАРОМ після релізу 1.0.0.1.

**Ключова теза:** Архітектура не потребує переписування. Вона потребує **добудови відсутніх частин** (auth на Control Center, батч-інсерт телеметрії, таймаути, bridge-event оновлень, pin сертифіката L.I.A.) — не заміни існуючих.

---

## Зміст

1. [Task 1 — Architecture Dependency Graph](#task-1)
2. [Task 2 — Lifetime Audit](#task-2)
3. [Task 3 — Async Safety Audit](#task-3)
4. [Task 4 — State Consistency](#task-4)
5. [Task 5 — Failure Propagation](#task-5)
6. [Task 6 — Resilience Audit](#task-6)
7. [Task 7 — Security Surface](#task-7)
8. [Task 8 — Performance Audit](#task-8)
9. [Task 9 — Evolution Readiness](#task-9)
10. [Task 10 — Technical Debt Matrix](#task-10)
11. [Architecture Strengths](#strengths)
12. [Architecture Weaknesses](#weaknesses)
13. [Hidden Risks](#hidden-risks)
14. [Future Risks (6–24 міс)](#future-risks)
15. [Scalability Risks](#scalability-risks)
16. [Security Risks](#security-risks)
17. [Maintainability Score](#maintainability)
18. [Technical Debt Score](#tech-debt-score)
19. [Zero-Regression Recommendations](#zero-regression)
20. [Long-Term Roadmap](#roadmap)

---

<a id="task-1"></a>
## Task 1 — Architecture Dependency Graph

### Топологія

```
┌──────────────────────────────────────────────────────────────┐
│  App.xaml.cs                                                 │
│    └─ AppCompositionRoot (partial: .HotkeyBackend)           │
│         ├─ TelemetryClient ──────► (6 services inject it)    │
│         ├─ AuthCompositionRoot                               │
│         │    ├─ AuthService                                  │
│         │    ├─ SupabaseClientFactory ──► (4 consumers)      │
│         │    ├─ InstallationService                          │
│         │    ├─ DiscordGuildSyncService (DEAD — never called)│
│         │    ├─ LoopbackCallbackListener                     │
│         │    └─ SecureSessionStorage                         │
│         ├─ Updater (L.I.A.)                                  │
│         ├─ ApplicationUpdateService                          │
│         ├─ BackgroundUpdateMonitor                           │
│         ├─ UpdateDownloader / Installer / Verifier           │
│         ├─ HangarTimerService / OverlayService / Provider    │
│         ├─ HotkeyService / HotkeyBackend                     │
│         └─ CreateMainWindow() ──► MainWindow (20 ctor params)│
│              ├─ CanvasManager(MainWindow)                    │
│              ├─ ButtonHelper / ButtonStateManager            │
│              ├─ CleanupController / CacheCleaner             │
│              ├─ ToastService / LinkService                   │
│              ├─ WpfMessageSource                             │
│              └─ 8 Canvas controls                            │
└──────────────────────────────────────────────────────────────┘
```

### Fan-In (топ-10 — найбільш залежних вузлів)

| # | Вузол | Fan-In | Тип |
|---|-------|--------|-----|
| 1 | `Settings.Default` (static singleton) | **6** | Hidden coupling |
| 2 | `ITelemetryService` / TelemetryClient | **6** | Interface ✓ |
| 3 | `Application.Current` (static) | **5** | Hidden coupling |
| 4 | Supabase `Client` (singleton via factory) | **4** | Interface ✓ |
| 5 | `IAuthService` | 3 | Interface ✓ |
| 6 | `IGitHubReleaseClient` | 3 | Interface ✓ |
| 7 | `IApplicationUpdateService` | 3 | Interface ✓ |
| 8 | `IUpdater` (L.I.A.) | 2 | Interface ✓ |
| 9 | `IHotkeyService` | 2 | Interface ✓ |
| 10 | `MainWindow` (concrete) | 2 | Tight coupling ⚠️ |

### Fan-Out (топ-5 — найбільш залежних вузлів)

| # | Вузол | Fan-Out |
|---|-------|---------|
| 1 | `MainWindow` | **~33** (20 ctor + 13 internal) |
| 2 | `AppCompositionRoot` | **~24** |
| 3 | `AuthCompositionRoot` | ~6 |
| 4 | `AuthService` | 6 |
| 5 | `ApplicationUpdateService` | 4 |

### Single Points of Failure

| Вузол | Радіус ураження | Ризик |
|-------|-----------------|-------|
| `AppCompositionRoot` ctor | 24 inline-конструкції; будь-який throw → додаток не стартує | **Critical** |
| `Settings.Default` user.config | Пошкодження → всі налаштування (folder, channel, hangar, hotkeys) невалідні | **Critical** |
| Supabase singleton `Client` | Місконфіг → auth + install sync + telemetry upload одночасно падають | High |
| `MainWindow` | 903 рядки, update+auth+canvas+cache логіка; краш → blank UI | High |

### Циклічні залежності

**Жодних compile-time циклів не виявлено.** Один навмисно розірваний цикл auth↔telemetry через two-phase init (`AppCompositionRoot.cs:59,103-107`) — Tel­emetryClient конструюється до AuthCompositionRoot, потім доін'єктиться через `SetInstallId` + `AttachClientFactory`. Це **хистке часове спаровування** (temporal coupling) — будь-який сервіс, сконструйований між рядками 59 і 103, отримає TelemetryClient без uploader/client.

### Hidden Coupling

| Патерн | Локація | Споживачі |
|--------|---------|-----------|
| `Settings.Default` static | `SettingsService`, `HangarSettingsService`, `UpdateChannelService`, `AppCompositionRoot`, `App`, `HotkeyBackend` | 6 |
| `Application.Current` static | `HangarTimerService`, `HangarOverlayService`, `DialogService`, `CleanupController`, `MainWindow` | 5 |
| 4 окремих `static readonly HttpClient` | `Updater.cs:17`, `DiscordGuildSyncService.cs:27`, `LocalizationInstaller.cs:26` | 3 (bypass composition root) |

### Leaky Abstractions

| Абстракція | Витік | Доказ |
|------------|-------|-------|
| `IHangarOverlayService` | 9 cast'ів до concrete `HangarOverlayService` | `HangarTimerService.cs:122,174,187,240,253,266,279,292,305` |
| `IHotkeyBackend` | 3 cast'и до concrete `RegisterHotkeyBackend` | `HotkeyService.cs:98,130,187` |
| `CanvasManager(MainWindow)` | Залежність від concrete Window, не інтерфейсу | `CanvasManager.cs:10,13` |
| `ReadmeService.LoadReadme(MainWindow)` | Пряме写入 в `window.TxtReadme` | `ReadmeService.cs:11` |
| `SupabaseClientFactory.CreateClient()` | Повертає concrete `Supabase.Client` | Усі 4 споживача прив'язані до SDK |

### Dead Code / Duplicates

| Об'єкт | Доказ | Рекомендація |
|--------|-------|-------------|
| `UpdateChannelService.cs` (повний клас) | `new UpdateChannelService` — **0 references**; `AppCompositionRoot.cs:65` кастує `SettingsService` | Видалити (дублює `SettingsService.cs:30-40`) |
| `UpdateCheckerService.CheckForPendingUpdatesAsync` | Метод визначено, але **ніколи не викликається** | Видалити або використати |
| `LiaForensicParser.TryParseMinimal` | 0 call sites (elevated шлях використовує `TryParse`) | Видалити |
| GitHubRelease model | Визначено **3 рази**: `Models/ApplicationUpdate/GitHubRelease.cs`, `Updater.cs:734-756`, `LocalizationInstaller.cs:534-544` | Уніфікувати |
| PowerShell escaping | Дублювання: `UpdateScriptBuilder.cs:23-24` і `Updater.cs:727-730` | Винести в хелпер |
| Channel matching | Дублювання: `ApplicationUpdateService.cs:157-162` і `UpdateHistoryWindow.xaml.cs:135-140` | Уніфікувати |

---

<a id="task-2"></a>
## Task 2 — Lifetime Audit

### Shutdown Sequence

```
App.OnExit → AppCompositionRoot.Dispose()
  1. TelemetryClient.Dispose()      — Timer stop + blocking flush(5s) + Uploader dispose
  2. BackgroundUpdateMonitor.Dispose() — Timer stop + SemaphoreSlim dispose
  3. HangarOverlayService.Dispose()    — Timer stop + overlay window close
  4. HangarTimerService.Dispose()      — HotkeyService dispose + overlay re-dispose (no-op guard)
  5. AuthCompositionRoot.Dispose()     — AuthService.Shutdown + ClientFactory.Shutdown
```

**Жодного application-wide `CancellationTokenSource`** не існує. Кожен сервіс зупиняє власні таймери; in-flight асинхронні операції не скасовуються централізовано.

### Критичні lifetime-проблеми

| ID | Проблема | Локація | Severity |
|----|---------|---------|----------|
| L-A1 | `WpfMessageSource` (owns `HwndSource`) **ніколи не dispose'иться** | `MainWindow.xaml.cs:178` | **MEDIUM** |
| L-A2 | `HangarOverlayService` dispose'иться **двічі** (2 власники) | `AppCompositionRoot.cs:123` + `HangarTimerService.cs:142` | LOW |
| L-A3 | `Supabase.Auth.Shutdown()` викликається **двічі** на одному singleton | `AuthService.cs:270` + `SupabaseClientFactory.cs:63` | LOW-MED |
| L-A4 | Timer.Dispose race з in-flight OnFlushTick | `TelemetryClient.cs:170` | LOW-MED |
| L-A5 | BackgroundUpdateMonitor: dispose під час in-flight check → `ObjectDisposedException` у `finally { _semaphore.Release() }` через async-void Tick | `BackgroundUpdateMonitor.cs:29,77,81` | **MEDIUM** |
| L-A6 | Orphaned elevated PowerShell process при cancellation | `Updater.cs:604,627` | **MEDIUM** |
| L-A7 | `ToastService` CTS не dispose'иться при reassign | `ToastService.cs:27` | LOW |
| L-A8 | Shared `HttpClient` не dispose'иться | `AppCompositionRoot.cs:67` | LOW (process exit) |
| L-A9 | MainWindow створює 13 сервісів, жоден не dispose'иться | `MainWindow.xaml.cs:106-128` | LOW |

### Timer Inventory

| Timer | Тип | Інтервал | Dispose'иться? |
|-------|-----|----------|----------------|
| Telemetry flush | `System.Threading.Timer` | 30s | ✅ `TelemetryClient.cs:170` (race: L-A4) |
| Background update check | `DispatcherTimer` | 30 хв | ✅ `BackgroundUpdateMonitor.cs:85` (race: L-A5) |
| Hangar overlay countdown | `DispatcherTimer` | 200ms | ✅ `HangarOverlayService.cs:105` (Tick never -= ) |
| Hangar card countdown | `DispatcherTimer` | 250ms | ✅ (UI lifecycle) |
| Home smooth scroll | `DispatcherTimer` | 16ms | ✅ (only during animation) |

### Background Tasks

| Task | Локація | Cancelable? | Orphan risk? |
|------|---------|-------------|-------------|
| Telemetry flush tick | `TelemetryClient.cs:157` | ⚠️ Timer only | Низький |
| Loopback listener | `LoopbackCallbackListener.cs:42` | ✅ via StopAsync | Низький |
| Hotkey handler | `HotkeyService.cs:229` | ❌ CTS never cancelled | Середній |
| LIA install process | `Updater.cs:604` | ❌ Process.Dispose ≠ kill | **Високий** (orphan elevated) |

---

<a id="task-3"></a>
## Task 3 — Async Safety Audit

### `async void` — НЕ-обробники подій (порушення AGENTS.md)

| Локація | Метод | Ризик |
|---------|-------|-------|
| `HangarTimerService.cs:55,58` | `InitializeCycleStartAsync()` — виклик з ctor | **MEDIUM** — exceptions ковтаються try/catch |
| `MainWindow.xaml.cs:558` | `UpdateAppMode(AuthState)` — виклик з OnAuthStatusChanged | LOW-MED |
| `HomeCanvas.xaml.cs:148` | `OpenUrl(string)` — виклик з click handlers | LOW |
| `BackgroundUpdateMonitor.cs:29` | async-void lambda на `Tick += async (s,e) =>` | **MEDIUM** — dispose race (L-A5) |

> Усі інші `async void` — **WPF event handlers** (MainWindow_Loaded, BtnInstall_Click, тощо), що відповідає правилу AGENTS.md.

### Sync-over-Async

| Локація | Патерн | Блокує UI? | Severity |
|---------|--------|-----------|----------|
| `TelemetryClient.cs:181` | `flushTask.Wait(TimeSpan.FromSeconds(5))` | ✅ До 5с на shutdown | MEDIUM |
| `LoopbackCallbackListener.cs:93` | `StopAsync().GetAwaiter().GetResult()` | ✅ На shutdown | LOW-MED |
| `CacheCleaner.cs:99` | `Task.Delay(...).GetAwaiter().GetResult()` | ❌ На thread-pool | LOW |

### CancellationToken Coverage

| Сервіс | Приймає CT? | Коректно використовує? |
|--------|------------|----------------------|
| AuthService | ✅ | ✅ |
| InstallationService | ✅ | ✅ |
| ApplicationUpdateService | ✅ | ✅ |
| UpdateDownloader | ✅ | ✅ |
| Updater (L.I.A.) | ✅ | ⚠️ UI передає `CancellationToken.None` |
| DiscordGuildSyncService | ✅ | ✅ |
| LoopbackCallbackListener | ✅ | ✅ |
| HangarTimerService | ✅ | ✅ |
| **BackgroundUpdateMonitor** | ❌ | Передає `CancellationToken.None` |
| **HotkeyService** | ⚠️ | CTS створюється, але **ніколи не скасується** |
| **AppCompositionRoot** | ❌ | **Жодного app-wide CTS** |

### Race Conditions

| ID | Локація | Опис | Severity |
|----|---------|------|----------|
| R-1 | `TelemetryClient.cs:170` vs `:180,190` | Timer.Dispose не чекає callback; race з dispose-flush + Uploader.Dispose | LOW-MED |
| R-2 | `BackgroundUpdateMonitor.cs:77,81` | `finally { _semaphore.Release() }` на disposed semaphore через async-void Tick | **MEDIUM** |
| R-3 | `LoopbackCallbackListener.cs:26,64,101` | Mixed `SemaphoreSlim _lock` (await) + `lock (_lock)` на тому ж полі | LOW |
| R-4 | `HangarStartTimeProvider.cs:22-23` | `_cached`/`_lastRemote` mutated без sync з різних async-контекстів | LOW |
| R-5 | `AuthService.cs:50,52,362-368` | `State`/`Profile` non-atomic read-modify-write; SetState fires event з background thread | **MEDIUM** |

---

<a id="task-4"></a>
## Task 4 — State Consistency

### Карта стану за доменами

#### Auth State — ✅ Clean
```
SecureSessionStorage (.auth, DPAPI CurrentUser)
        │ load/save
        ▼
AuthService.State / Profile  ◄── single source of truth
        │ StatusChanged event
        ├─► AuthGateCanvas → Dispatcher.Invoke(UpdateUi)
        ├─► MainWindow → Dispatcher.Invoke(UpdateAppMode)
        └─► AuthStatusPresenter → UpdateButton
```
**Проблема:** State/Profile mutated з background thread без lock (R-5).

#### Install ID — ⚠️ Redundant but correct
```
File (install-id)  ──AUTHORITY──►  InstallationService._installId  ──►  TelemetryClient._installId
        ▲ repair                                                              │
Registry (InstallId) ───────────────────────────────────────────────►  DB app_installations
```
5 копій; file — авторитетна. Registry write failures ковтаються → щоразу reconciliation.

#### GameFolder — 🔴 Duplicated
3 копії: `Settings.Default.GameFolder` → `MainWindowViewModel._gameFolder` → `MainWindow.localFolder`. Синхронізація manual by convention.

#### Cycle Start — 🔴 4-way duplication
`Settings.Default.HangarCycleStartOverride` → `HangarStartTimeProvider._cached` → `HangarTimerService._cycleStartMs` → `HangarOverlayService._cycleStartMs`. Кожен мутатор оновлює лише subset копій.

### Знайдені проблеми консистентності

| ID | Домен | Проблема | Severity |
|----|-------|---------|----------|
| SC-1 | Updater | `_suppressStartupUpdateCheckUntil` — in-memory, не переживає restart; призначення «suppress on next launch» **не функціонує** | **HIGH** |
| SC-2 | Settings | `UpdateChannelService.cs` — дублює `SettingsService.cs:30-40` byte-for-byte; ніколи не інстанціюється | **HIGH** |
| SC-3 | Settings | Production Supabase URL + anon JWT hardcoded у `SupabaseConfig.cs:10-12` | **HIGH** |
| SC-4 | Hangar | Overlay opacity не персиститься при hotkey-change (тільки при close); asymmetric з Scale | **HIGH** |
| SC-5 | Settings | Telemetry channel = `"Stable"` (з великої), schema default = `"stable"` — case mismatch | MEDIUM |
| SC-6 | Settings | GameFolder у 3 місцях | MEDIUM |
| SC-7 | Updater | `_lastNotifiedVersion` не персиститься → re-notification після restart | MEDIUM |
| SC-8 | Telemetry | Event queue volatile; втрата при hard crash | MEDIUM |
| SC-9 | Auth | `_guildSyncService` впроваджено, але `SyncGuildsAsync` ніколи не викликається | LOW |

---

<a id="task-5"></a>
## Task 5 — Failure Propagation

### Карта поширення помилок

| Сервіс | Throw? | Catch? | Swallow? | Observability? |
|--------|--------|--------|----------|----------------|
| AuthService | AuthResult.Failure | ✅ try/catch w/ LogError | ❌ | ✅ Telemetry |
| InstallationService | ❌ | ✅ try/catch | Registry write — silent | ⚠️ Debug only |
| Updater (L.I.A.) | LiaInstallException | ✅ granular phases | ❌ | ✅ Telemetry (Terminal Flush) |
| UpdateDownloader | ❌ | ❌ propagates | ❌ | ⚠️ Caller logs |
| UpdateInstaller | ❌ | ❌ propagates | ❌ | ⚠️ Caller logs |
| UpdateVerifier | ❌ | ❌ propagates | ❌ | ⚠️ Caller logs |
| GitHubReleaseClient | ❌ | ❌ propagates | ❌ | ⚠️ Caller logs |
| BackgroundUpdateMonitor | ❌ | ✅ try/catch w/ Debug.WriteLine | ⚠️ Debug-only | ❌ **Black hole in Release** |
| TelemetryUploader | ❌ | ✅ try/catch w/ Debug.WriteLine + requeue | ⚠️ Debug-only | ❌ **Black hole in Release** |
| TelemetryClient | ❌ | ✅ try/catch w/ Debug.WriteLine | ⚠️ Debug-only | ❌ **Black hole in Release** |
| LoopbackCallbackListener | ❌ | ✅ try/catch empty | 🔴 **YES** | ❌ **Black hole** |
| DiscordGuildSyncService | ❌ | ✅ try/catch empty | 🔴 **YES** | ❌ **Black hole** |
| HangarTimerService | ❌ | ✅ try/catch in async void | ⚠️ Swallowed | ❌ |
| HangarStartTimeProvider | ❌ | ❌ propagates to async void | ⚠️ Swallowed | ❌ |

### Виявлені «чорні діри»

| ID | Локація | Тип | Severity |
|----|---------|-----|----------|
| BH-1 | `LoopbackCallbackListener.cs:132-134` | `catch (Exception) { }` — повне ігнорування | **HIGH** |
| BH-2 | `DiscordGuildSyncService.cs:145-148` | `catch (Exception) { }` — мережеві збої невидимі | MEDIUM |
| BH-3 | `TelemetryUploader.cs:78-81` | `Debug.WriteLine` only — invisible in Release | MEDIUM |
| BH-4 | `TelemetryClient.cs:51,63` | `Debug.WriteLine` only — invisible in Release | MEDIUM |
| BH-5 | `BackgroundUpdateMonitor.cs:72` | `Debug.WriteLine` only — invisible in Release | MEDIUM |

### Відсутність global UnhandledException handler

`App.xaml.cs` не реєструє `AppDomain.CurrentDomain.UnhandledException` або `DispatcherUnhandledException`. Будь-який невловлений виняток у non-async-void методі крашить процес **без телеметрії**.

---

<a id="task-6"></a>
## Task 6 — Resilience Audit

### Resilience Scorecard за зовнішніми залежностями

| Залежність | Retry? | Timeout? | Graceful degradation? | Data loss? | Оцінка |
|------------|--------|----------|----------------------|------------|--------|
| **Supabase (auth)** | ❌ | ❌ | ✅ AuthResult.Failure | ⚠️ Session втрата | 5/10 |
| **Supabase (telemetry)** | ✅ requeue | ❌ | ✅ Queue + backpressure | ⚠️ Volatile | 6/10 |
| **Supabase (install sync)** | ❌ | ❌ | ⚠️ Silent fail | ❌ | 4/10 |
| **GitHub API (update)** | ❌ | ❌ (100s default) | ❌ Propagates | ❌ | 3/10 |
| **GitHub API (L.I.A.)** | ❌ | ❌ | ❌ Propagates | ❌ | 3/10 |
| **GitHub API (localization)** | ✅ SendWithRetryAsync (3x, exp backoff) | ❌ | ✅ | ❌ | **7/10** |
| **Discord webhook** | ❌ | ❌ | ✅ Returns null | ⚠️ Guilds unsynced | 5/10 |
| **PowerShell (L.I.A.)** | ⚠️ cert retry | ❌ no timeout | ⚠️ ct=None | ❌ | 4/10 |
| **PowerShell (self-update)** | ❌ | ❌ infinite parent wait | ❌ | ❌ | 2/10 |
| **Hangar HTTP** | ❌ | ❌ | ✅ cached fallback | ❌ | 5/10 |

### Сценарії відмови

| Сценарій | Поведінка | Severity |
|----------|-----------|----------|
| Supabase недоступний при старті | Auth restore fail → SignedOut; telemetry buffer up to 5000 events | MEDIUM |
| GitHub недоступний при update check | Exception propagate → UI error toast; LIA status freeze | MEDIUM |
| PowerShell завис (Add-AppxPackage) | UI фаза зависає назавжди (ct=None) | **HIGH** |
| AppX Installer завис | PS wait-process без таймауту → нескінченне очікування | **HIGH** |
| Пошкоджений .auth файл | `DestroySession()` при будь-якій помилці load → irreversible sign-out | LOW-MED |
| Пошкоджений update-cache.json | `catch → new List()` → silent втрата кешу | LOW |
| Пошкоджений update-history.json | `catch → new List()` → silent втрата всієї історії | MEDIUM |
| Discord webhook 429 | No retry_after parsing; own 2^n backoff (discord-independent) | MEDIUM |

### Єдиний сервіс з retry: LocalizationInstaller

`LocalizationInstaller.cs:472-504` — `SendWithRetryAsync`: 3 спроби, exponential backoff, retry на 429/403/503. **Єдиний конвеєр з retry.** GitHubReleaseClient, Updater (L.I.A.), DiscordGuildSyncService — **не мають retry взагалі**.

---

<a id="task-7"></a>
## Task 7 — Security Surface

### Attack Surface Map

```
┌────────── EXTERNAL TRUST BOUNDARY (Internet) ──────────┐
│  Supabase (RLS)  │  GitHub API  │  Discord API        │
│  JWT/PKCE        │  Downloads   │  Guild sync         │
└──┬───────────────┴──────┬───────┴─────────────────────┘
   │                      │
───┼──────── LOCAL TRUST BOUNDARY (Windows user) ─────────
   │                      │
   ▼                      ▼
┌─────────────────────────────────────────────────────────┐
│ SCLOCVerse.exe (user privilege)                         │
│  • HttpListener 127.0.0.1:rand (OAuth callback)        │
│  • DPAPI .auth (%LOCALAPPDATA%\SCLOCVerse)              │
│  • install-id (file + registry)                         │
│  • Telemetry → Supabase via JWT                         │
└──────────────────┬──────────────────────────────────────┘
                   │ spawns
       ┌───────────┼──────────────────────┐
       ▼ non-elev  ▼ ELEVATED (UAC runas) │
┌──────────────┐  ┌─────────────────────┐ │
│ update.ps1   │  │ LIA wrapper.ps1     │ │
│ Inno /SILENT │  │ → Import-Cert Root  │◄┘
└──────────────┘  │ → Add-AppxPackage   │
                  └─────────────────────┘
```

### Критичні Security-знаходки

| ID | Severity | Зона | Проблема | Локація |
|----|----------|------|----------|---------|
| SEC-1 | **CRITICAL** | Control Center | **Жодної автентифікації** на адмін-панелі | `Program.cs:1-43` — 0 `[Authorize]` |
| SEC-2 | **CRITICAL** | L.I.A. | Інсталятор без перевірки цілісності (тільки розмір) + elevation | `Updater.cs:228-265,711-717` |
| SEC-3 | **CRITICAL** | L.I.A. | Довільний `.cer` → `LocalMachine\Root` + `TrustedPeople` | `Updater.cs:379-380` |
| SEC-4 | **CRITICAL** | App update | Обхід verify при порожньому checksum | `ApplicationUpdateService.cs:164-192` + `MainWindow.xaml.cs:357-360` |
| SEC-5 | MEDIUM | Telemetry | `MachineName` як structured PII (не sanitized) | `InstallationService.cs:53,77,103` |
| SEC-6 | MEDIUM | Telemetry | `Detail` JSON (appx_log, cert subject) без sanitization | `TelemetryClient.cs:142-144` |
| SEC-7 | MEDIUM | Telemetry | `PrivacySanitizer` regex занадто вузький | `PrivacySanitizer.cs:17-30` |
| SEC-8 | MEDIUM | Auth | Немає валідації OAuth `state` (PKCE-only) | `AuthService.cs:96-121` |
| SEC-9 | MEDIUM | Update | TOCTOU між verify та install (локальний атакувальник) | `MainWindow.xaml.cs:363-394` |
| SEC-10 | MEDIUM | Update | SHA256 = byte-equality, не Authenticode publisher | `UpdateVerifier.cs:53-58` |
| SEC-11 | MEDIUM | Supabase | `SECURITY DEFINER` без `SET search_path` (~20 функцій) | Міграції 00015-00023 |
| SEC-12 | MEDIUM | Supabase | `cc_readonly` має write-capability через definer functions | Міграція 00015:124-126 |

### Defense-in-Depth: що зроблено правильно

| Контроль | Статус |
|----------|--------|
| OAuth PKCE | ✅ `AuthService.cs:69-76` |
| DPAPI CurrentUser для сесії | ✅ `SecureSessionStorage.cs:42` |
| RLS everywhere (deny-all for anon) | ✅ Всі таблиці |
| SQL parameterized (no injection) | ✅ Усі функції |
| PowerShell single-quote escaping | ✅ `EscapePowerShellString` |
| HTML encoding на OAuth callback | ✅ `LoopbackCallbackListener.cs:243` |
| SignOut = Global token revoke | ✅ `AuthService.cs:238` |
| No `service_role` in client | ✅ Verified |

---

<a id="task-8"></a>
## Task 8 — Performance Audit

### HTTP Client Inventory

| Власник | Локація | Pattern | Timeout? |
|---------|---------|---------|----------|
| Composition root | `AppCompositionRoot.cs:67` | Shared `new HttpClient()` → 3 сервіси | ❌ |
| L.I.A. Updater | `Updater.cs:17` | `static readonly HttpClient` | ❌ |
| Discord Guild Sync | `DiscordGuildSyncService.cs:27` | `static readonly HttpClient` | ❌ |
| Localization | `LocalizationInstaller.cs:26` | `static readonly HttpClient` | ❌ |
| Notifier Discord | `Program.cs:31` | `IHttpClientFactory` ✅ | ❌ (default 100s) |

**Жоден HttpClient не має явного Timeout.** Усі покладаються на .NET default 100с.

### P0: Encoding Violations

| Файл | Рядок | Проблема |
|------|-------|---------|
| `UpdateCacheService.cs` | `:31, :50` | `ReadAllTextAsync` / `WriteAllTextAsync` без `Encoding.UTF8` |
| `UpdateHistoryService.cs` | `:66, :80` | Те саме |
| `JsonService.cs` | `:23` | `File.ReadAllText` без encoding, **синхронний** |
| `HomeCanvas.xaml.cs` | ~114 місць | **Mojibake** — UTF-8 decoded як Windows-1251 (P0 за AGENTS.md) |

### P1: Scalability Bottlenecks (детально в Task 9)

| ID | Проблема | Локація |
|----|---------|---------|
| PERF-1 | Per-event INSERT (немає батчу) | `TelemetryUploader.cs:63-83` |
| PERF-2 | Missing index `telemetry_events(user_id)` | Міграція 00009 |
| PERF-3 | Non-materialized dashboard views | Міграції 00011, 00014 |
| PERF-4 | Non-concurrent REFRESH MV on every incident change | `pipeline_automation.sql:53,290` |
| PERF-5 | Per-failed-event trigger: auto_close + promoter | `auto_close_on_event.sql:73-96` |

### P2: Hot Path Allocations

| Локація | Проблема |
|---------|---------|
| `MainWindow.xaml.cs:790,822` | `TxtLiaSetupe.Text += $"{msg}\n"` — O(n²) string concat у log loop через Dispatcher.Invoke |
| `HangarStartTimeProvider.cs:91` | Regex compiled per-call (не `static readonly`) |
| `UpdateHistoryService.cs:28-42` | Read-modify-write entire file on every append; O(n²) over n appends; no cap |

---

<a id="task-9"></a>
## Task 9 — Evolution Readiness

### Оцінка готовності до 10× навантаження

| Компонент | Готовність | Вузьке місце |
|-----------|-----------|-------------|
| **Telemetry INSERT** | 🔴 Не готовий | 100 sequential HTTP round-trips per flush × 10× = 1000/30s |
| **Telemetry events table** | 🟡 Потрібні індекси | Missing `user_id` index → RLS full-scan per query |
| **Dashboard views** | 🔴 Не готовий | Non-materialized `COUNT(DISTINCT)` over 24h/7d windows per read |
| **Incident trigger** | 🟡 Деградує | Per-failed-event `auto_close` scan + promoter on client's INSERT tx |
| **MV knowledge_coverage** | 🟡 Блокує | Non-concurrent REFRESH = AccessExclusive lock per incident change |
| **Notifier polling** | 🟢 Готовий | `FOR UPDATE SKIP LOCKED` → horizontal-safe |
| **Control Center circuits** | 🟡 Потрібен ErrorBoundary | Single component crash → full circuit teardown |

### Оцінка готовності до нових модулів

| Сценарій | Готовність | Доказ |
|----------|-----------|-------|
| **Новий Canvas** | 🟢 Easy | Патерн встановлений: `Controls/<Name>Canvas.xaml(.cs)` + inject через `MainWindow` ctor |
| **Новий тип телеметрії** | 🟢 Trivial | `ITelemetryService.Track(comp, op, outcome, ctx)` — нові комбінації без міграцій |
| **Новий notification channel** | 🟢 Easy (best seam) | `INotificationProvider` + register in DI; `notification_queue.provider` free-text |
| **Новий OAuth provider** | 🔴 Hard | `AuthService.cs:69-76` hardcodes `Provider.Discord`; не config-driven |
| **Заміна telemetry backend** | 🟡 Medium | `ITelemetryService` ізолює callers; але `TelemetryUploader` приварений до Supabase SDK |
| **Заміна auth provider** | 🔴 Hard | `SupabaseClientFactory` повертає concrete `Supabase.Client`; 4 споживача прив'язані до SDK |

### Абстракції: оцінка якості

| Принцип | Оцінка | Деталі |
|---------|--------|--------|
| **ISP** | ✅ 8/10 | 38 дрібних single-purpose інтерфейсів |
| **SRP** | 🟡 6/10 | `MainWindow` (God class); `AuthService` (6 deps, growing) |
| **DRY** | 🟡 5/10 | GitHubRelease ×3; PS escaping ×2; channel match ×2 |
| **OCP** | 🟡 6/10 | Notification provider — відмінний; auth provider — закритий |

---

<a id="task-10"></a>
## Task 10 — Technical Debt Matrix

### P0 — Критичний борг (блокує реліз або безпеку)

| ID | Зона | Ризик | Impact | Probability | Technical Debt | Рекомендація |
|----|------|-------|--------|-------------|----------------|-------------|
| TD-1 | Security | Control Center без автентифікації | Несанкціонований доступ до адмін-панелі: transition/close incidents, edit knowledge | Високий (якщо доступний з мережі) | Повна відсутність auth middleware | Додати `AddAuthentication` + `[Authorize]` на всі сторінки |
| TD-2 | Security | L.I.A.: cert → `LocalMachine\Root` без pin | Компрометація зовнішнього repo → persistent root-CA injection на всіх клієнтах | Низький (потребує repo compromise) | Pin відсутній | Pin thumbprint/subject в `AppSettings` перед import |
| TD-3 | Security | L.I.A.: інсталятор без перевірки цілісності | Supply-chain attack через third-party repo | Низький | Size-only validation | SHA256 verification перед elevated execution |
| TD-4 | Security | App update: checksum bypass на порожнє | Пошкоджений/підроблений .exe без `.sha256` → мовчки skip verify | Середній | Empty = skip, not fail | Empty checksum = `Verify.Failed`, не `Skipped` |
| TD-5 | Text encoding | Mojibake у `HomeCanvas.xaml.cs` | ~114 пошкоджених UTF-8 символів; P0 за AGENTS.md | Вже present | Корумпований файл | Перекодувати файл |

### P1 — Високий борг (функціональні дефекти, scalability blockers)

| ID | Зона | Ризик | Impact | Probability | Technical Debt | Рекомендація |
|----|------|-------|--------|-------------|----------------|-------------|
| TD-6 | Coupling | MainWindow God Class (20 ctor, 903 lines) | Неможливість тестування; будь-який краш = blank UI | Високий | ~31 field, ~35 methods | Витягти update-оркестрацію в controller |
| TD-7 | Coupling | `Settings.Default` global mutable singleton | Race conditions; corruption = total settings loss | Середній | 6 direct consumers | Wrap в injected interface |
| TD-8 | Updater | "Install.Success" до реального install | Історія оновлень систематично бреше; провали невидимі | Високий | Відсутній bridge-event | Реалізувати bridge-event з detach-процесу |
| TD-9 | Updater | Infinite parent-PID wait у update.ps1 | Процес зависає при shutdown hang; діагностики немає | Середній | Немає лічильника в while-loop | Додати timeout (30с) |
| TD-10 | Updater | PowerShell без timeout (L.I.A.) | UI фаза зависає при Add-AppxPackage hang | Середній | ct=None з UI | Додати timeout + proper CT |
| TD-11 | Notifier | Only IncidentCreated enqueued | transition_incident не повідомляє Discord при close/escalate | Високий | Функція незавершена | Додати enqueue в transition_incident |
| TD-12 | CC Auth | Hardcoded "admin" actor | Audit trail не attributability | Високий | Немає identity | Реальний user identity у changedBy |
| TD-13 | State | `_suppressStartupUpdateCheckUntil` не працює | Призначене "next launch" suppression не функціонує | Високий | In-memory field | Перенести в Settings.Default |
| TD-14 | State | Overlay opacity не персиститься (asymmetric з Scale) | Втрата налаштувань при shutdown без graceful close | Середній | SetOverlayOpacity не викликається | Викликати SetOverlayOpacity при зміні |
| TD-15 | Scalability | Per-event telemetry INSERT (немає батчу) | 10× users = 1000 sequential round-trips / 30s | Високий (при зростанні) | One INSERT per HTTP | `Insert(List<T>)` batch |
| TD-16 | Scalability | Missing index `telemetry_events(user_id)` | RLS owner-scanned SELECT = full scan | Високий | Missing index | `CREATE INDEX … ON telemetry_events(user_id)` |
| TD-17 | Scalability | Non-materialized dashboard views | `COUNT(DISTINCT)` over 24h/7d per page load | Високий (при зростанні) | No rollup tables | Materialized views + cron refresh |
| TD-18 | Scalability | Non-concurrent REFRESH MV per incident | AccessExclusive lock serializes reads/writes | Середній | Non-concurrent refresh | `REFRESH MATERIALIZED VIEW CONCURRENTLY` |
| TD-19 | Security | Production Supabase credentials hardcoded | Неможливість ротації без rebuild | Середній | Hardcoded fallback | Тільки env vars |
| TD-20 | Dead code | `UpdateChannelService.cs` дублює `SettingsService` | Future desync при редагуванні одного | Високий | 100% duplicate | Видалити |

### P2 — Середній борг (якість коду, maintainability)

| ID | Зона | Проблема | Локація |
|----|------|---------|---------|
| TD-21 | Leaky abstraction | `IHangarOverlayService` → 9 casts до concrete | `HangarTimerService.cs:122+` |
| TD-22 | Leaky abstraction | `IHotkeyBackend` → 3 casts до concrete | `HotkeyService.cs:98+` |
| TD-23 | Tight coupling | `CanvasManager(MainWindow)`, `ReadmeService(MainWindow)` | `CanvasManager.cs:10`, `ReadmeService.cs:11` |
| TD-24 | Lifetime | `WpfMessageSource` (owns `HwndSource`) never disposed | `MainWindow.xaml.cs:178` |
| TD-25 | Lifetime | No app-wide shutdown CTS | `AppCompositionRoot.cs:110` |
| TD-26 | Lifetime | `BackgroundUpdateMonitor` dispose race (async-void Tick + semaphore) | `BackgroundUpdateMonitor.cs:29,77` |
| TD-27 | Lifetime | Orphaned elevated PowerShell on cancel | `Updater.cs:604,627` |
| TD-28 | Async | Blocking `.Wait(5s)` on UI thread at shutdown | `TelemetryClient.cs:181` |
| TD-29 | Async | `CancellationToken.None` in background update check | `BackgroundUpdateMonitor.cs:29` |
| TD-30 | State | AuthService.State/Profile non-thread-safe mutation | `AuthService.cs:50,52,362-368` |
| TD-31 | State | Telemetry channel "Stable" vs schema default "stable" | `BuildInfo` + `Settings` |
| TD-32 | State | GameFolder in 3 redundant locations | Settings + VM + MainWindow |
| TD-33 | Failure | No global UnhandledException handler | `App.xaml.cs` |
| TD-34 | Failure | LoopbackCallbackListener empty catch = black hole | `:132-134` |
| TD-35 | Failure | Debug.WriteLine-only catches (invisible in Release) | `TelemetryClient`, `TelemetryUploader`, `BackgroundUpdateMonitor` |
| TD-36 | Security | OAuth `state` not validated (PKCE-only) | `AuthService.cs:96-121` |
| TD-37 | Security | TOCTOU verify→install window | `MainWindow.xaml.cs:363-394` |
| TD-38 | Security | SHA256 ≠ Authenticode publisher identity | `UpdateVerifier.cs:53-58` |
| TD-39 | Security | `SECURITY DEFINER` без `SET search_path` (~20 fns) | Міграції 00015-00023 |
| TD-40 | Security | `cc_readonly` має write-capability | Міграція 00015 |
| TD-41 | Security | `MachineName` / `Detail` не sanitized | `InstallationService`, `TelemetryClient` |
| TD-42 | CC | No Blazor `<ErrorBoundary>` | `App.razor` |
| TD-43 | CC | Concurrency-conflict detection by string matching | `ControlCenterRepository.cs:121-124` |
| TD-44 | Notifier | No Discord 429 / retry_after handling | `DiscordNotificationProvider.cs:33-56` |
| TD-45 | Notifier | Zombie timeout 10 min → Critical alert delay | `NotificationDispatcher.cs:117` |
| TD-46 | Perf | No HTTP timeouts anywhere (5 clients, all default 100s) | Global |
| TD-47 | Perf | Non-atomic installation upsert race | `InstallationService.cs:58-111` |
| TD-48 | Perf | History read-modify-write without cap | `UpdateHistoryService.cs:28-42` |
| TD-49 | Perf | Non-atomic cache file race (latent) | `UpdateCacheService.cs:40-51` |
| TD-50 | Encoding | Missing `Encoding.UTF8` in cache/history readers | `UpdateCacheService`, `UpdateHistoryService`, `JsonService` |

### P3 — Низький борг (гігієна, polish)

| ID | Зона | Проблема |
|----|------|---------|
| TD-51 | Naming | `BtnResetCash` → `Cache`, `TxtLiaSetupe` → `Setup` |
| TD-52 | Dead code | `LiaForensicParser.TryParseMinimal`, `LocalizationMessages.*` (3 unused), `ReleaseAssetInfo.cs` |
| TD-53 | Dead code | `DiscordGuildSyncService` injected but `SyncGuildsAsync` never called |
| TD-54 | Dead code | `NotificationChannel.Email`/`Telegram` declared but no providers exist |
| TD-55 | Versioning | Npgsql version skew: CC 10.0.3 vs Notifier 9.0.2 |
| TD-56 | Info leak | `appsettings.json` hardcodes Supabase pooler host |
| TD-57 | UX | No user-facing telemetry kill-switch (env var only) |
| TD-58 | UX | No auto-refresh on Control Center Overview |
| TD-59 | Resilience | User.cfg unconditionally deleted on localization uninstall |
| TD-60 | Config | OAuth provider hardcoded to Discord |

---

<a id="strengths"></a>
## Architecture Strengths

1. **Процесна ізоляція.** 4 проєкти — 4 процеси. Спілкування тільки через Postgres. Notifier продовжує працювати при падінні UI. Control Center незалежний від клієнта.
2. **At-least-once доставка повідомлень.** `FOR UPDATE SKIP LOCKED` + UNIQUE dedup + zombie recovery — horizontally-safe design.
3. **RLS everywhere.** Усі таблиці мають RLS; `anon` deny-all; `authenticated` owner-only; `telemetry_events` append-only.
4. **Єдиний інтерфейс телеметрії.** `ITelemetryService` — єдиний санкціонований sink (Стаття 7 Конституції). Callers повністю ізольовані від backend.
5. **Clean notification provider abstraction.** `INotificationProvider` — найкращий seam у системі. Додавання каналу = 1 клас + 1 DI line.
6. **SupabaseClientFactory singleton.** Lazy, thread-safe, double-check lock; reverse-order dispose.
7. **Bounded telemetry queue with backpressure.** `MaxInMemory=5000`, drop-oldest, counter logging.
8. **Well-disciplined timer scoping.** High-frequency timers (200ms/250ms) only while feature active; no periodic spikes.
9. **DPAPI CurrentUser for session storage.** Correct scope, not LocalMachine.
10. **PowerShell encoding handled correctly in L.I.A.** `EncodingPreamble` + `[Console]::OutputEncoding=UTF8` + `StandardOutputEncoding=UTF8` — глибоке розуміння OEM/UTF-8 пастки PowerShell 5.1.
11. **Append-only audit tables.** `incident_status_log`, `notification_attempts` — immutable history at schema level.
12. **Least-privilege DB roles.** `cc_readonly` and `cc_notifier` separated; webhook URLs from env, never persisted.

---

<a id="weaknesses"></a>
## Architecture Weaknesses

### Структурні

1. **MainWindow — God Class.** 903 рядки, 20 ctor params, ~31 field, ~35 methods. Містить update-оркестрацію, auth state machine, cache cleanup, LIA install/uninstall, canvas navigation. **Найбільший maintainability risk.** Порушує власне правило AGENTS.md «Не збільшувати відповідальність MainWindow».

2. **Відсутність шару оркестрації.** Немає controller/facade між Composition Root та UI. MainWindow виконує роль, яку повинен виконувати окремий `UpdateOrchestrator` / `AuthCoordinator`.

3. **Hidden coupling через statics.** `Settings.Default` (6 consumers), `Application.Current` (5 consumers) — обидва bypass DI. Робить сервіси untestable та створює гонки.

4. **Leaky abstractions.** 12 cast'ів до concrete типів через інтерфейси (9 overlay + 3 hotkey). Абстракції існують формально, але не забезпечують поліморфізм.

### Процесні

5. **Відсутність auth на Control Center.** Адмін-панель без автентифікації — найбільший security gap у системі.

6. **Відсутність bridge-event з detach-процесу оновлення.** Історія оновлень систематично бреше («успіх» до реального встановлення).

7. **Три неуніфіковані конвеєри встановлення.** App-update / L.I.A. / Localization мають різні рівні зрілості: Localization — найзріліший (retry + atomic + conditional), L.I.A. — найслабший (no integrity, no timeout).

8. **Відсутність global exception handler.** Невловлені винятки крашать процес без телеметрії.

---

<a id="hidden-risks"></a>
## Hidden Risks

1. **Temporal coupling у composition root.** TelemetryClient two-phase init (`:59` construct → `:103` AuthRoot → `:106-107` wire). Будь-який сервіс, сконструйований між рядками 59 та 103, отримає TelemetryClient без uploader. Архітектурно дозволено, але не документовано як constraint.

2. **`_lastNotifiedVersion` in-memory → спам toast.** Після кожного restart користувач повторно отримує toast про вже відоме оновлення. UX-дефект, який при збільшенні frequency release-циклу стає дратівливим.

3. **Non-atomic Discord guild delete+insert.** `DiscordGuildSyncService.cs:77-89` — DELETE потім INSERT без транзакції. Crash між ними = втрата guild data. Функція наразі disabled (dead), ризик латентний.

4. **Single-instance Mutex без `Global\` prefix.** `App.xaml.cs:17` — per-session scope. Два процеси під різними користувачами/сесіями одночасно → подвійний cert import у machine store.

5. **MV REFRESH під час Blazor render handshake.** `Home.razor:235` + `Releases.razor:153` викликають `REFRESH MATERIALIZED VIEW` синхронно при кожному завантаженні сторінки. Разом з trigger-driven refresh → redundant exclusive locks.

6. **Npgsql version skew.** Control Center 10.0.3 vs Notifier 9.0.2 на спільній БД. Major-version skew → потенційні розбіжності type-handling.

7. **`cc_readonly` misleading name.** Незважаючи на назву, має write-capability через SECURITY DEFINER functions. Leak credentials = full incident/knowledge write access.

---

<a id="future-risks"></a>
## Future Risks (6–24 місяці)

1. **Telemetry table зросте до мільйонів рядків без retention policy.** Жоден cron/pg_cron job не визначений у міграціях. Без retention + rollup tables dashboards стануть непрацездатними через ~12 міс.

2. **Update history file ростиме без обмежень.** `UpdateHistoryService` — read-modify-write на кожен append, без truncation. O(n²) growth. При ~100 записах — непомітно; при 1000+ — секунди на кожен append.

3. **MainWindow продовжить рости.** Кожна нова функція додасть ще один залежний параметр або блок логіки. Без витягування controller — через 12 міс буде 1200+ рядків.

4. **Hardcoded OAuth provider.** Додавання Google/GitHub auth = редагування AuthService + AuthCompositionRoot + UI + config. Без абстракції — 5+ точок модифікації.

5. **Notifier без rate-limiting.** При зростанні кількості incidents → Discord ban (rate limit). Без retry_after parsing → exponential 429s.

6. **Control Center без auth → не можна expose публічно.** Будь-яке публічне розгортання (VPN-less) = несанкціонований доступ.

7. **Cert trust chain накопичення.** Кожен L.I.A. install додає cert у `LocalMachine\Root` без cleanup. За 2 роки → десятки orphaned trusted roots.

---

<a id="scalability-risks"></a>
## Scalability Risks

| Компонент | Поточна ємність | 10× межа | 100× межа | Рішення |
|-----------|----------------|----------|-----------|---------|
| Telemetry INSERT | ~1 event/HTTP | ~10 round-trips/sec per client | Непрацездатно | Batch insert + Edge Function |
| telemetry_events table | ~100K rows | Needs user_id index | Needs retention + rollup | Index + pg_cron + materialized views |
| Dashboard views | Real-time scan | Slow page loads | Timeout | Materialized views + cron refresh |
| Incident trigger | Per-event scan | Added latency on INSERT | Write amplification | Move to async/queue |
| Notifier | 1 instance | N instances ✅ (SKIP LOCKED) | Connection pool pressure | Scale-out ✅ |
| Control Center | 1 instance | Blazor circuits ✅ | DB connection limit | Pool tuning |
| user.config | Per-user | ✅ | ✅ | Not a bottleneck |

---

<a id="security-risks"></a>
## Security Risks

| Ризик | Імовірність | Вплив | Поточний контроль | Рекомендація |
|-------|------------|-------|-------------------|-------------|
| Repo compromise (L.I.A.) → RCE elevated | Низька | **Critical** | Size-only check | SHA256 + cert pinning |
| Repo compromise (SCLOC-Verse) → RCE | Низька | High | SHA256 (bypass on empty) | Authenticode verify + mandatory checksum |
| CC unauthorized access | Залежить від deployment | **Critical** | None | Auth middleware |
| Search-path hijacking (SECURITY DEFINER) | Дуже низька | High | None (no search_path pin) | `SET search_path = public, pg_temp` |
| OAuth CSRF (no state validation) | Низька | Medium | PKCE only | Validate state parameter |
| TOCTOU local file swap | Низька (local attacker) | High | SHA256 (same source) | Authenticode + temp+move |
| MachineName leakage in telemetry | 100% (by design) | Medium | None | Hash or redact |

---

<a id="maintainability"></a>
## Maintainability Score

| Критерій | Оцінка | Вага | Зважена |
|----------|--------|------|---------|
| ISP compliance (interfaces) | 8/10 | 15% | 1.20 |
| SRP compliance (services) | 5/10 | 20% | 1.00 |
| DRY compliance | 5/10 | 15% | 0.75 |
| Testability | 4/10 | 20% | 0.80 |
| Readability | 7/10 | 10% | 0.70 |
| Documentation quality | 8/10 | 10% | 0.80 |
| Consistency (naming, patterns) | 6/10 | 10% | 0.60 |
| **Загальний бал** | | **100%** | **5.85 / 10** |

**Найслабші місця maintainability:**
- Testability (4/10) — MainWindow God Class, static coupling, concrete dependencies
- DRY (5/10) — 3× GitHubRelease, 2× PS escaping, 2× channel match
- SRP (5/10) — MainWindow, AuthService (growing facade)

**Найсильніші місця:**
- ISP (8/10) — 38 дрібних цілеспрямованих інтерфейсів
- Documentation (8/10) — Конституція, ADR, inline коментарі українською

---

<a id="tech-debt-score"></a>
## Technical Debt Score

| Категорія | P0 | P1 | P2 | P3 | Загалом |
|-----------|----|----|----|----|---------|
| Security | 4 | 2 | 5 | 1 | 12 |
| Architecture/Coupling | 0 | 3 | 4 | 2 | 9 |
| Lifetime/Async | 0 | 2 | 8 | 0 | 10 |
| State Consistency | 0 | 4 | 2 | 1 | 7 |
| Failure/Resilience | 0 | 2 | 3 | 0 | 5 |
| Performance/Scalability | 1 | 4 | 3 | 0 | 8 |
| Dead Code/DRY | 0 | 1 | 0 | 4 | 5 |
| Encoding/Text | 1 | 0 | 1 | 0 | 2 |
| **Загалом** | **6** | **18** | **26** | **8** | **58** |

**Технічний борг: ~58 одиниць**, з яких:
- **6 P0** — блокують безпечний реліз/розгортання
- **18 P1** — функціональні дефекти або blockers масштабування
- **26 P2** — якість коду / maintainability
- **8 P3** — гігієна / polish

**Оцінка технічного боргу: 6.5 / 10** (прийнятно для проєкту даної зрілості; основний борг зосереджений у security та scalability, не в структурі).

---

<a id="zero-regression"></a>
## Zero-Regression Recommendations

> Усі рекомендації — **additive-only**, не потребують переписування існуючого коду.
> Порядок — за пріоритетомImpact/Effort.

### Фаза 0 — Негайно (до публічного розгортання CC)

| # | Дія | Файли | Зусилля |
|---|-----|-------|---------|
| ZR-1 | Додати auth middleware на Control Center | `Program.cs` (CC) | 1 день |
| ZR-2 | Pin L.I.A. cert thumbprint/subject в `AppSettings` | `AppSettings.cs`, `Updater.cs:379-380` | 0.5 дня |
| ZR-3 | Перекодувати `HomeCanvas.xaml.cs` (mojibake P0) | `HomeCanvas.xaml.cs` | 0.5 дня |

### Фаза 1 — Короткостроково (1–2 тижні)

| # | Дія | Файли | Зусилля |
|---|-----|-------|---------|
| ZR-4 | App update: empty checksum = Fail, не Skip | `MainWindow.xaml.cs:357-360` | 0.5 дня |
| ZR-5 | Timeout на parent-PID wait loop в update.ps1 | `UpdateScriptBuilder.cs:34-39` | 0.5 дня |
| ZR-6 | L.I.A.: SHA256 verification перед elevated execution | `Updater.cs:228-265` | 1 день |
| ZR-7 | Додати `SET search_path = public, pg_temp` на SECURITY DEFINER функції | Міграції 00015-00023 | 1 день |
| ZR-8 | Реалізувати bridge-event з update.ps1 → history | `UpdateScriptBuilder.cs`, новий startup-check | 2 дні |
| ZR-9 | Додати global `UnhandledException` handler в App | `App.xaml.cs` | 0.5 дня |
| ZR-10 | Persist `_lastNotifiedVersion` + `_suppressStartupUpdateCheckUntil` | `Settings.Designer.cs`, `BackgroundUpdateMonitor.cs` | 0.5 дня |

### Фаза 2 — Середньостроково (3–6 тижнів)

| # | Дія | Файли | Зусилля |
|---|-----|-------|---------|
| ZR-11 | Batch insert telemetry (`Insert(List<T>)`) | `TelemetryUploader.cs:63-83` | 1 день |
| ZR-12 | Index `telemetry_events(user_id)` | Нова міграція | 0.5 дня |
| ZR-13 | Materialized views для dashboard + cron refresh | Нові міграції | 3 дні |
| ZR-14 | HTTP timeouts (15-30с) на всіх HttpClient | `AppCompositionRoot.cs:67`, static clients | 1 день |
| ZR-15 | Додати retry/backoff для GitHubReleaseClient + Updater | Новий `HttpRetryHandler` | 2 дні |
| ZR-16 | Додати enqueue в `transition_incident()` для IncidentClosed/Escalated | Міграція 00015 extension | 1 день |
| ZR-17 | Витягнути UpdateOrchestrator з MainWindow | Новий `UpdateOrchestrator.cs` | 3 дні |
| ZR-18 | Замінити Debug.WriteLine catches на proper telemetry logging | 4 сервіси | 1 день |

### Фаза 3 — Довгостроково (2–3 місяці)

| # | Дія | Файли | Зусилля |
|---|-----|-------|---------|
| ZR-19 | Замінити `Settings.Default` direct access на injected interfaces | 6 файлів | 3 дні |
| ZR-20 | Розширити `IHangarOverlayService` / `IHotkeyBackend` (усунути casts) | 2 інтерфейси + 2 сервіси | 2 дні |
| ZR-21 | Уніфікувати три GitHubRelease моделі | Спільна модель | 1 день |
| ZR-22 | Retention policy + pg_cron для telemetry_events | Нова міграція | 2 дні |
| ZR-23 | DiscordNotificationProvider: 429/retry_after handling | `DiscordNotificationProvider.cs` | 1 день |
| ZR-24 | Config-driven OAuth provider | `AuthService.cs`, config | 2 дні |

---

<a id="roadmap"></a>
## Long-Term Roadmap (3–5 років)

### Рік 1 — Стабілізація
- Усунути всі P0 (Фаза 0-1)
- Batch telemetry + materialized views (Фаза 2)
- Витягнути контролери з MainWindow (ZR-17)
- Retention policy для telemetry (ZR-22)
- Автоматизовані тести для критичних шляхів (update, auth, telemetry)

### Рік 2 — Масштабування
- Config-driven OAuth providers (Google, GitHub)
- Email/Telegram notification providers
- Telemetry Edge Function для pre-aggregation
- Control Center auth з Supabase Auth integration
- Rollup tables (hourly/daily telemetry aggregates)
- Observability health dashboard

### Рік 3 — Еволюція
- Заміна `SupabaseClientFactory` на vendor-neutral абстракцію (ITelemetryBackend)
- Plugin-система для notification providers (runtime registration)
- Localization integrity verification (publisher-pinned hash)
- Authenticode signature enforcement на всіх конвеєрах
- Multi-tenant Control Center

### Метрики успіху
| Метрика | Поточно | Рік 1 ціль | Рік 3 ціль |
|---------|---------|-----------|-----------|
| Maintainability Score | 5.85/10 | 7.0/10 | 8.0/10 |
| Technical Debt items | 58 | 30 | 15 |
| P0 items | 6 | 0 | 0 |
| P1 items | 18 | 5 | 2 |
| Test coverage (critical paths) | 0% | 40% | 80% |
| Telemetry INSERT mode | 1 event/HTTP | Batch | Edge Function |
| Dashboard refresh | Real-time scan | Materialized | Pre-aggregated rollup |

---

## Висновок

Архітектура SCLOC-Verse — **функціонально зріла, структурно прийнятна, але не готова до масштабування**. Вона не потребує переписування; вона потребує **добудови відсутніх частин**.

**Топ-3 пріоритети:**
1. **Auth на Control Center** — без цього не можна публічно розгортати.
2. **Integrity verification на L.I.A.** — supply-chain attack surface неприйнятний.
3. **Batch telemetry + materialized views** — без цього dashboards помруть через рік.

Усі рекомендації — **additive-only**, сумісні з існуючим стилем автора та не порушують стабільність поточного коду.

---

*Документ згенеровано на основі повного аналізу вихідного коду 4 проєктів + 26 міграцій Supabase. Жодні файли не модифіковано.*
