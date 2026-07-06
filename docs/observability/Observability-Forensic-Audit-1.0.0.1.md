# SCLOC-Verse Observability — Forensic Audit (Pre-Release 1.0.0.1)

> **Дата:** 2026-07-05
> **Тип:** Лише дослідження. **Без змін у коді. Без комітів. Без рефакторингу.**
> **Метод:** Читання всіх C#/SQL/PS1/MD-файлів, крос-верифікація продюсерів/споживачів.
> **Вхідні дані:** 26 міграцій Supabase, ~85 .cs-файлів, 4 проєкти монорепо, 11 .md-файлів документації.

---

## 0. Executive Summary

### 0.1. Склад монорепо (верифіковано)

| Проєкт | Тип | Роль | БД-роль |
|---|---|---|---|
| `SCLOCVerse` | WPF .NET 9 | Клієнт — пише телеметрію | `authenticated` |
| `SCLOCVerse.ControlCenter` | Blazor Server | UI Dashboard + Workflow | `cc_readonly` |
| `SCLOCVerse.Notifier` | Worker Service | Notification Dispatcher | `cc_notifier` |
| `SCLOCVerse.Notifications` | Class Library | Контракти `INotificationProvider` | — |

> **Важливо:** У завданні фігурували поняття "Control Center" та "Dashboard" як різні сутності. Фактично **`Dashboard = Control Center UI`** (Конституція Стаття 20 буквально називає його "Control Center (Dashboard)"). Спеціалізовані `dashboard_*.sql` міграції лише додають VIEW у схему `control_center`.

### 0.2. Схема об'єктів БД (верифіковано)

| Тип | К-сть | Детально |
|---|---:|---|
| Схеми | 2 | `public`, `control_center` |
| Базові таблиці | **15** | 14 у `public` + 1 singleton у `control_center` |
| Звичайні VIEW | **18** | 17 у `control_center` + 1 у `public` (`user_analytics`) |
| Materialized VIEW | **1** | `control_center.knowledge_coverage` |
| SECURITY DEFINER функції | **~24** | промоутери, workflow, knowledge |
| Triggers | **4** | geoip, telemetry-promote, incident-refresh, knowledge-audit |
| БД-ролі | **4** | `anon`, `authenticated`, `cc_readonly`, `cc_notifier` |

### 0.3. Топ-10 критичних знахідок (короткий виклад)

| # | Серйозність | Знахідка | Де |
|---|---|---|---|
| **F1** | 🔴 P0 | `control_center.release_health_detail` referenced 6×, **не створена** жодною міграцією | C# `ControlCenterRepository.cs:392` + SQL `20260704000023:33,39,59,64` |
| **F2** | 🔴 P0 | Enum `telemetry_incidents.status` (CHECK) **не містить** `Mitigated`/`Acknowledged`, які використовують `transition_incident` та `create_knowledge_from_incident` | SQL міграції 12 vs 15/20 |
| **F3** | 🟠 P1 | Відсутній global `UnhandledException` handler — WPF-краш повністю минає Observability | C# `App.xaml.cs` |
| **F4** | 🟠 P1 | `PrivacySanitizer` покриває лише `ErrorMessage`; `Detail` (з `appx_log`, cert-поля) проходить неочищеним | C# `TelemetryClient.cs:142` |
| **F5** | 🟠 P1 | Terminal `FlushAsync` пропущено у всіх `ApplicationUpdate`-сервісах (Downloader/Installer/Verifier) | C# `UpdateDownloader.cs:59`, `UpdateInstaller.cs:46,82`, `UpdateVerifier.cs:65` |
| **F6** | 🟠 P1 | Три копії логіки promotion-engine (m13 batch, m16 batch+notify, m05021100 per-event); batch — мертвий код | SQL міграції 13/16/05021100 |
| **F7** | 🟡 P2 | `LiaForensicParser.TryParseMinimal` — мертвий код (0 викликів) | C# `LiaForensicParser.cs:67` |
| **F8** | 🟡 P2 | `telemetry_events.country` — мертва колонка (завжди NULL, тригер лише на `app_installations`) | SQL міграції 2 + 9 |
| **F9** | 🟡 P2 | 6 з 19 колонок `app_installations` завжди NULL/DEFAULT (не замаплені в C#) | SQL міграція 1 + C# `AppInstallation.cs` |
| **F10** | 🟡 P2 | Мертві таблиці без продюсера: `admin_audit_log`, `error_reports`, `user_discord_guilds` (порожні) | SQL міграції 3/4/5 |

---

## ЗАВДАННЯ 1 — Повний аудит Telemetry Events

### 1.1. Структура Observability-шару C# (верифіковано)

```
SCLOCVerse/
├─ Interfaces/
│  └─ ITelemetryService.cs          [контракт: Track() + FlushAsync()]
├─ Models/Observability/
│  ├─ TelemetryEvent.cs             [ORM public.telemetry_events]
│  └─ TelemetryContext.cs           [DTO diagnostic-контексту]
└─ Services/Observability/
   ├─ TelemetryClient.cs            [SOLE impl ITelemetryService; BuildEvent; Timer; Dispose]
   ├─ TelemetryUploader.cs          [Supabase Insert; idempotency 23505/409; SemaphoreSlim]
   ├─ TelemetryEventQueue.cs        [ConcurrentQueue; cap 5000; drop-oldest]
   ├─ ErrorContextExtractor.cs      [Exception → TelemetryContext; reflection; LIA-forensic]
   ├─ PrivacySanitizer.cs           [Regex: UserPath/UNC/Bearer/JWT]
   ├─ TraceContext.cs               [SessionId/CorrelationId/Step (monotonic)]
   ├─ BuildInfo.cs                  [AppVersion/Channel/TelemetryVersion=1/GitCommit]
   ├─ HResultCatalog.cs             [14 symbolic HRESULT→signal mappings]
   ├─ LiaEvents.cs                  [Helper: LIA.* events]
   └─ UpdateEvents.cs               [Helper: Updater.* events]
```

**Продюсери (емітери `.Track()`):**
- `App.xaml.cs` → `Application.Start.Started`
- `AuthService.TrackAuth` → `Auth.SignIn/RestoreSession`
- `InstallationService.TrackSync` → `Installation.Sync`
- `LiaEvents.Track` → `LIA.Install/Download/RunInstallerScript`
- `UpdateEvents.Track` → `Updater.Download/Install/Verify`

**DI:** Ручна композиція в `AppCompositionRoot.cs:59,135` + ін'єкція в Updater/Downloader/Installer/Verifier + `AuthCompositionRoot.cs:24,26,103`.
**Kill-switch:** env `SCLOCVERSE_TELEMETRY_DISABLED=1|true` (`AppCompositionRoot.cs:200-205`).
**Інших impl `ITelemetryService` немає.**

### 1.2. Таблиця всіх 44 TelemetryEvents (емітовані точки)

> Колонка "Rec" = Recommendation: **KEEP** / **OPT** (оптимізувати) / **CASCADE** (дубль у каскаді) / **GAP** (сліпа зона — рекомендовано додати, не видалити).

| # | File:Line | Component.Operation.Outcome | Severity | Context Fields | Rec |
|---|---|---|---|---|---|
| 1 | `App.xaml.cs:64` | Application.Start.Started | Info | null | KEEP |
| 2 | `AuthService.cs:63` | Auth.SignIn.Started | Info | durationMs | KEEP |
| 3 | `AuthService.cs:80` | Auth.SignIn.Failed (URI==null) | Error | **null (без контексту!)** ⚠ | OPT (додати контекст) |
| 4 | `AuthService.cs:92` | Auth.SignIn.Cancelled (callback==null) | Info | null | KEEP |
| 5 | `AuthService.cs:104` | Auth.SignIn.Cancelled (access_denied) | Info | null | KEEP |
| 6 | `AuthService.cs:110` | Auth.SignIn.Failed (errorCode) | Error | **null** ⚠ | OPT |
| 7 | `AuthService.cs:116` | Auth.SignIn.Failed (порожній code) | Error | **null** ⚠ | OPT |
| 8 | `AuthService.cs:125` | Auth.SignIn.Failed (session==null) | Error | **null** ⚠ | OPT |
| 9 | `AuthService.cs:133` | Auth.SignIn.Succeeded | Info | durationMs | KEEP |
| 10 | `AuthService.cs:138` | Auth.SignIn.Cancelled (OCE) | Info | null | KEEP |
| 11 | `AuthService.cs:144` | Auth.SignIn.Failed (catch) | Error | ErrorContext + durationMs + **FlushAsync** | KEEP |
| 12 | `AuthService.cs:175` | Auth.RestoreSession.Started | Info | durationMs | KEEP |
| 13 | `AuthService.cs:209` | Auth.RestoreSession.Succeeded | Info | durationMs | KEEP |
| 14 | `AuthService.cs:222` | Auth.RestoreSession.Failed (catch) | Error | ErrorContext + durationMs + **FlushAsync** | KEEP |
| 15 | `InstallationService.cs:46` | Installation.Sync.Started | Info | detail={phase, retry_count} | KEEP |
| 16 | `InstallationService.cs:114` | Installation.Sync.Succeeded | Info | durationMs + detail | KEEP |
| 17 | `InstallationService.cs:118` | Installation.Sync.Failed | Error | ErrorContext + durationMs + detail + **FlushAsync** | KEEP |
| 18 | `Updater.cs:79` | LIA.Install.Started (phase="Download") | Info | detail | CASCADE |
| 19 | `Updater.cs:95` | LIA.Download.Started (InstallerAsset) | Info | detail | KEEP |
| 20 | `Updater.cs:104` | LIA.Download.Failed (InstallerAsset) | Error | ErrorContext + FlushAsync + **re-throw** | CASCADE |
| 21 | `Updater.cs:109` | LIA.Download.Succeeded (InstallerAsset) | Info | durationMs + detail | KEEP |
| 22 | `Updater.cs:116` | LIA.Download.Started (CertificateAsset) | Info | detail | KEEP |
| 23 | `Updater.cs:124` | LIA.Download.Failed (CertificateAsset) | Error | ErrorContext + FlushAsync + **re-throw** | CASCADE |
| 24 | `Updater.cs:129` | LIA.Download.Succeeded (CertificateAsset) | Info | durationMs + detail | KEEP |
| 25 | `Updater.cs:137` | LIA.Install.Started (phase="RunInstallerScript") | Info | detail | CASCADE |
| 26 | `Updater.cs:143` | LIA.Install.Succeeded (phase="Complete") | Info | durationMs + detail | KEEP |
| 27 | `Updater.cs:151` | LIA.Install.Failed (outer catch) | Error | ErrorContext + FlushAsync + **re-throw** | CASCADE (термінал каскаду) |
| 28 | `Updater.cs:274` | LIA.RunInstallerScript.Started | Info | null | KEEP |
| 29 | `Updater.cs:293` | LIA.RunInstallerScript.Failed (ProcessExecution) | Error | ErrorContext + FlushAsync + re-throw | CASCADE |
| 30 | `Updater.cs:308` | LIA.RunInstallerScript.Failed (liaEx + forensic) | Error | ErrorContext(LiaInstallException) + **hresult, signal_name, phase, cert_*, activity_id, appx_log** + FlushAsync + re-throw | KEEP (детермінована forensic) |
| 31 | `Updater.cs:316` | LIA.RunInstallerScript.Failed (fallback UnknownExitCode) | Error | ErrorContext + FlushAsync + re-throw | CASCADE |
| 32 | `Updater.cs:323` | LIA.RunInstallerScript.Succeeded | Info | durationMs | KEEP |
| 33 | `UpdateDownloader.cs:44` | Updater.Download.Started | Info | null | KEEP |
| 34 | `UpdateDownloader.cs:54` | Updater.Download.Succeeded | Info | durationMs | KEEP |
| 35 | `UpdateDownloader.cs:59` | Updater.Download.Failed | Error | ErrorContext + durationMs, **БЕЗ FlushAsync!** ⚠ | OPT (додати Flush) |
| 36 | `UpdateInstaller.cs:40` | Updater.Install.Started | Info | null | KEEP |
| 37 | `UpdateInstaller.cs:46` | Updater.Install.Failed (InstallerNotFound) | Error | durationMs + detail, **БЕЗ FlushAsync** | OPT |
| 38 | `UpdateInstaller.cs:76` | Updater.Install.Succeeded/Failed (LauncherStarted/LaunchFailed) | Info/Error | durationMs + detail, **БЕЗ FlushAsync** | OPT |
| 39 | `UpdateInstaller.cs:82` | Updater.Install.Failed (exception) | Error | ErrorContext + durationMs, **БЕЗ FlushAsync** + re-throw | OPT |
| 40 | `UpdateVerifier.cs:34` | Updater.Verify.Started | Info | null | KEEP |
| 41 | `UpdateVerifier.cs:40` | Updater.Verify.Skipped (NoChecksum) | Info | durationMs + detail | KEEP |
| 42 | `UpdateVerifier.cs:46` | Updater.Verify.Failed (FileNotFound) | Error | durationMs + detail | KEEP |
| 43 | `UpdateVerifier.cs:59` | Updater.Verify.Succeeded/Failed (ChecksumMismatch) | Info/Error | durationMs + detail | KEEP |
| 44 | `UpdateVerifier.cs:65` | Updater.Verify.Failed (exception) | Error | ErrorContext + durationMs, **БЕЗ FlushAsync** + re-throw | OPT |

### 1.3. GAP-и (заплановані, але не реалізовані події — НЕ мертві)

| Очікувалось (декларовано) | Стан | Ризик |
|---|---|---|
| `UnhandledException` (контракт `ITelemetryService.cs:29`) | **НЕ реалізовано** (0 продюсерів) | Високий — краш не фіксується |
| `Application.Shutdown` / `Application.Exit` | Відсутня (лише Start) | Сесія не закривається, неможливо відрізнити краш від штатного виходу |
| `Localization.*` events | 0 телеметрії в `LocalizationInstaller` | Сліпа зона встановлення локалізації |
| `Network.*` events | Мережеві помилки в `GitHubReleaseClient`/`ApplicationUpdateService` ковтаються без Track | Сліпа зона перевірки оновлень |
| `BackgroundUpdateMonitor.CheckFailed` | Тільки `Debug.WriteLine`, без телеметрії | Сліпа зона фонового чеку |
| `UpdateInstaller` після restart (фінальний результат) | Лише в `UpdateHistoryService` (локальна JSON), не в телеметрії | Розрив фази завершення |
| `git_commit` (з `BuildInfo.cs:30`) | Завжди NULL (MSBuild-таргет відкладено) | Неможливо фільтрувати за build |

### 1.4. Висновки Завдання 1

- **0 формально "мертвих" подій** (споживач завжди — Supabase `telemetry_events`).
- **5 FAILED-подій без контексту** (#3,#6,#7,#8,#11) — слід додати причину.
- **5 FAILED-подій без Terminal Flush** (#35,#37,#38,#39,#44) — порушення Статті 16 Конституції.
- **Каскад з 3-х Failed** на 1 фізичну відмову LIA — свідома гранулярність, але потребує de-dup в Dashboard.

---

## ЗАВДАННЯ 2 — Аудит трасування

### 2.1. Ключовий архітектурний висновок

У проєкті **НЕ існує** класичного `Microsoft.Extensions.Logging` (0 згадок `ILogger`, `LogInformation`, `_logger`). Одиниця локального виводу — `System.Diagnostics.Debug.WriteLine` (~30 згадок). Тому поділ за .NET-рівнями **не застосовується**. Натомість існує власна severity-модель у `TelemetryEvent`.

### 2.2. Карта всіх лог-викликів

#### 2.2.1. Observability-інфраструктура (діагностика самого каналу) — КРИТИЧНО

> ⚠ Це єдина діагностика власних відмов телеметрії. У продакшні без debugger — **губиться безповоротно** (немає файлового sink).

| File:Line | Повідомлення |
|---|---|
| `TelemetryClient.cs:51,65,83,104,112,182,186` | `[Telemetry] ... failed/timed out: {msg}` |
| `TelemetryUploader.cs:80,90` | `[Telemetry] Помилка відправки події / FlushAsync: {msg}` |
| `TelemetryEventQueue.cs:39` | `[Telemetry] Черга переповнена, втрачено: {N}` |
| `ErrorContextExtractor.cs:78` | `[Telemetry] ErrorContextExtractor failed: {msg}` |
| `LiaEvents.cs:59` / `UpdateEvents.cs:46` | `[Telemetry] Track failed: {msg}` |

#### 2.2.2. Бізнес-сервіси (паралельний локальний вивід)

| File:Line | Префікс | Дублює телеметрію? |
|---|---|---|
| `AuthService.cs:374` | `[AuthState] {time} {from}->{to} ({caller})` | ні (state-transition) |
| `AuthService.cs:401` | `[AuthService] {msg}: {ex}` (повний exception) | **частково** — дублює #11/#14, але з повним stack |
| `InstallationService.cs:193,247` | `[InstallId]` конфлікт/помилка запису | ні |
| `Updater.cs:282,287` | `[LIA] RunInstallerScript: elevation ...` | ні |
| `BackgroundUpdateMonitor.cs:72` | `[BackgroundUpdateMonitor] Check failed: {ex}` | **GAP** — телеметрії нема |
| `MainWindow.xaml.cs:317` | `[BackgroundUpdateCheckFailed] {ex}` | **GAP** |
| `MainWindow.xaml.cs:607` | `[RestoreAuthSessionAsync] {ex}` | частково дублює #14 |
| `AuthGateCanvas.xaml.cs:173,192` | `[AuthGateCanvas] SignIn/Retry failed: {ex}` | дублює UI-факт #11 |
| `HotkeyService.cs:268` + `InputDiagnostics.cs:39,47` | input-diagnostics.log + `Debug.WriteLine` | **тіньовий канал** |
| `SettingsService.cs:58` | `[SettingsService] Ігноруємо невалідний шлях` | ні |
| `LinkService.cs:37,41` | `[LinkService] ...` | ні |

### 2.3. Запропонована нова модель (Production / Debug-only)

> ⚠ Зараз ця модель **не реалізована** (всі події йдуть у єдину чергу без фільтра). Це рекомендація.

#### Production (завжди)
- Severity `Critical` (краш, інцидент з N ураженими)
- Severity `Error` (Failed-події)
- Outcome `Failed` (для promotion-тригера)

#### Optional (увімкнено у Stable, вимкнено для критичних компонентів)
- Severity `Warning`
- Outcome `Cancelled`, `Skipped`

#### Debug build only (або gated через `BuildInfo.IsDebug`)
- Outcome `Started`, `Succeeded` (бізнес-метрики)
- `Debug.WriteLine` (уже зараз працює лише з debugger)
- Тіньовий `InputDiagnostics` (вже gated через `IsHotkeyDiagnosticsEnabled()`)

#### Місця де `Debug.WriteLine` використовується "замість телеметрії" (аномалії)
- `BackgroundUpdateMonitor.cs:72` — **повинна бути телеметрія** (GAP)
- `MainWindow.xaml.cs:317` — дублює BackgroundUpdateMonitor, **повинна бути телеметрія** (GAP)
- `MainWindow.xaml.cs:607` — дублює AuthService.TryRestoreSession (вже логує)

### 2.4. Висновки Завдання 2

1. Класична .NET severity-модель **відсутня** — натомість власна `TelemetryEvent.severity`.
2. `Debug.WriteLine` — єдиний локальний канал, у продакшні без debugger — губиться.
3. Рекомендована нова модель Production/Optional/Debug-only — див. §2.3.
4. **2 GAP-и** (`BackgroundUpdateMonitor`, фоновий чек оновлень) потребують міграції з `Debug.WriteLine` на телеметрію.
5. **Тіньовий канал `InputDiagnostics`** — окрема інфраструктура діагностики гарячих клавіш (gated, тимчасова).

---

## ЗАВДАННЯ 3 — Database Forensics (Supabase таблиці)

### 3.1. Матриця Producer / Consumer / Дублі / Можливості

| # | Table | Schema | Producer | Consumer | Дублює? | Can Merge | Can Remove |
|---|---|---|---|---|---|---|---|
| 1 | `app_installations` | public | C# `InstallationService` | user_analytics, cc.users/installations/statistics/health, observability_health | ні (гібрид cache+history) | ні | ❌ |
| 2 | `error_reports` | public | **ЖОДНОГО** | `control_center.errors` (мертвий) | ⚠ концепт. дублює telemetry_events | теор. з telemetry_events (але зарезервовано автором) | ⚠ кандидат (reserved) |
| 3 | `admin_audit_log` | public | **ЖОДНОГО** | — | ні | ні | ⚠ кандидат (Future Capability) |
| 4 | `user_discord_guilds` | public | `DiscordGuildSyncService` (вимкнений) | — | ні | ні | ❌ (код готовий до вмикання) |
| 5 | `telemetry_events` ★ | public | C# `TelemetryUploader` | вся observability-вітка | ні (ядро) | ❌ | ❌ |
| 6 | `telemetry_incidents` | public | promote-функції (trigger) | cc.incidents, knowledge | ні | ❌ | ❌ |
| 7 | `incident_policy` | public | seed + адмін | promote-функції, auto_close | ні | ні | ❌ (production config) |
| 8 | `incident_status_log` | public | transition_incident() | incident_timeline | ні | ні | ❌ |
| 9 | `incident_notes` | public | add_incident_note() | incident_notes_view | ні | ні | ❌ |
| 10 | `notification_queue` | public | promote-функції | Notifier Worker, cc.notifications | ні | ❌ | ❌ |
| 11 | `notification_attempts` | public | Notifier Worker | аудит | ні | ні | ❌ |
| 12 | `knowledge_entries` | public | knowledge-функції | knowledge views | ні | ❌ | ❌ |
| 13 | `knowledge_references` | public | add_knowledge_reference() | knowledge_entry_detail | ні | ні | ❌ |
| 14 | `knowledge_version_history` | public | audit-тригер | get_knowledge_* | ні | ні | ❌ |
| 15 | `pipeline_health_meta` | cc | refresh_knowledge_coverage() | observability_health | ні | ні | ❌ (singleton) |

### 3.2. Мертві таблиці (без продюсера)

| Таблиця | Стан | Коментар міграції |
|---|---|---|
| `error_reports` | 0 записів, RLS deny-all | "Reserved for future" (міграція 05021100:301) |
| `admin_audit_log` | 0 записів, RLS deny-all | "майбутня адмін-панель" |
| `user_discord_guilds` | 0 записів | "Продюсер відключено у клієнті" (OAuth scope="identify" без `guilds`) |

### 3.3. VIEWs — дублікати та кандидати на консолідацію

| View | Дублює? | Can Remove/Consolidate |
|---|---|---|
| `control_center.errors` | проєкція порожньої `error_reports` | ⚠ мертвий (разом з error_reports) |
| `control_center.health` (1 колонка `active_installations_last_7d`) | перекривається `observability_health` + `platform_stats.active_installations` | ⚠ deprecated |
| `control_center.contract_info/users/installations/statistics` | не споживаються C# напряму (йдуть через інші views) | ⚠ перевірити зовнішнього споживача |
| `control_center.release_health` vs **`release_health_detail` (відсутня)** | несинхронізовано | 🔴 P0 — див. F1 |

### 3.4. Висновки Завдання 3

- **12 з 15 таблиць — живі та використовуються**.
- **3 таблиці Future Capability** (`error_reports`, `admin_audit_log`, `user_discord_guilds`) — порожні, але залишені за рішенням автора (additive-only контракт).
- **1 мертвий VIEW** (`control_center.errors`) над порожньою таблицею.
- **1 відсутній критичний VIEW** (`release_health_detail`) — P0.

---

## ЗАВДАННЯ 4 — Column Audit

### 4.1. `app_installations` (19 колонок)

| Стан | Колонки | К-сть |
|---|---|---:|
| **Живі** (заповнюються клієнтом) | install_id, user_id, app_version, platform, machine_id, os_version, first_seen, last_seen, created_at, is_active, updated_at | 11 |
| **Жива через тригер** | country (Cloudflare cf-ipcountry) | 1 |
| **Завжди DEFAULT** | update_channel='stable', install_source='unknown' | 2 |
| **Завжди NULL** (не замаплені в C#) | localization_version, os_build, game_folder_path, selected_environment | 4 |
| **Системна** | id (uuid PK) | 1 |

**Рекомендація по колонках:**

| Колонка | Дія | Причина |
|---|---|---|
| `localization_version` | KEEP (додати продюсер у C# `InstallationService`) | Контрактна — additive-only |
| `os_build` | KEEP (додати продюсер) | Контрактна |
| `game_folder_path` | KEEP (додати продюсер, але з PII-санітизацією!) | Корисна для Support |
| `selected_environment` | KEEP (додати продюсер з `EnvironmentSelector`) | Корисна |
| `update_channel` | KEEP (оновлювати при зміні в SettingsCanvas) | Зараз завжди 'stable' |
| `install_source` | KEEP (заповнювати з `.iss` installer) | Зараз завжди 'unknown' |
| `created_at` vs `first_seen` | ⚠ Дубль (обидва `DEFAULT now()`, обидва immutable-intent) | MERGE — використовувати лише одне |

### 4.2. `telemetry_events` (29 колонок)

| Колонка | Стан | Дія |
|---|---|---|
| `country` | **Завжди NULL** (тригер лише на `app_installations`) | KEEP + додати тригер, або REMOVE (additive — краще додати тригер) |
| `git_commit` | Завжди NULL (MSBuild-таргет відкладено) | KEEP + реалізувати таргет |
| `http_status`, `hresult`, `supabase_code`, `exception_type`, `error_message`, `duration_ms`, `detail` | Additive slots — багато NULL-ів залежно від типу події | KEEP (by design, Стаття 13) |
| `client_event_id` | UNIQUE, використовується для idempotency | KEEP |
| `session_id`, `correlation_id`, `step` | Trace-реконструкція | KEEP |

### 4.3. `telemetry_incidents`

| Колонка | Стан | Дія |
|---|---|---|
| `fingerprint_hash` | Computed `md5(fingerprint_key)` — дубль навмисний для індексу | KEEP |
| `owner` | Додано ALTER у міграції 15 (не в оригінальній CREATE 12) | KEEP (прийнятно) |
| `status` enum | ⚠ **НЕ містить** `Mitigated`/`Acknowledged` які використовують функції | **EXTEND** CHECK (F2) |

### 4.4. Інші таблиці

| Таблиця | Колонки | Зауваження |
|---|---|---|
| `error_reports` (reserved) | всі колонки | Невикористані (таблиця порожня) |
| `admin_audit_log` (reserved) | всі колонки | Невикористані |
| `notification_queue` | `retry_count` vs `max_retries` | `retry_count` оновлюється Worker'ом, `max_retries` — ліміт. Коректно. |

### 4.5. Висновки Завдання 4

- **4 колонки `app_installations`** — завжди NULL (не замаплені в C#).
- **2 колонки `app_installations`** — завжди DEFAULT (не перезаписуються).
- **1 колонка `telemetry_events.country`** — мертва (тригер лише на installations).
- **1 дубль `created_at`/`first_seen`** — семантично ідентичні.
- **1 критична невідповідність enum** `telemetry_incidents.status` (F2).

---

## ЗАВДАННЯ 5 — Forensic Pipeline

### 5.1. Повна схема

```
┌─────────────────────────────────────────────────────────────────────────┐
│ 1. PRODUCER — inline PowerShell в C#                                    │
│    Updater.cs::BuildInstallerScript (рядки 339-466)                     │
│    catch CertificateImport (382-406) + catch AddAppxPackage (419-464)   │
│    $forensic hashtable → ConvertTo-Json -Compress -Depth 3             │
│    Write-Output '##SCLOC_FORENSIC##'                                   │
│    Write-Output $forensicJson                                          │
└──────────────────────────────┬──────────────────────────────────────────┘
                               │ stdout (UTF-8)
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ 2. TRANSPORT — wrapper.ps1 → дочірній powershell.exe (elevation)        │
│    RunElevatedAsync: [IO.File]::WriteAllText($stdoutPath, UTF8NoBom)    │
│    → PowerShellResult(ExitCode, Output, Error)                          │
└──────────────────────────────┬──────────────────────────────────────────┘
                               ▼ result.Output
┌─────────────────────────────────────────────────────────────────────────┐
│ 3. PARSER — LiaForensicParser.TryParse                                  │
│    Шукає "##SCLOC_FORENSIC##" → JsonConvert.DeserializeObject<LiaForensic> │
└──────────────────────────────┬──────────────────────────────────────────┘
                               ▼ LiaForensic (9 полів)
┌─────────────────────────────────────────────────────────────────────────┐
│ 4. EXCEPTION WRAP — new LiaInstallException(forensic, exitCode, raw)    │
│    ⚠ Копіює 8 полів property-by-property + ForensicJson=RawJson         │
└──────────────────────────────┬──────────────────────────────────────────┘
                               ▼ LiaInstallException
┌─────────────────────────────────────────────────────────────────────────┐
│ 5. TELEMETRY ENRICHMENT                                                 │
│    ErrorContextExtractor.ApplyLiaForensic → TelemetryContext.Detail     │
│    HResultCatalog.ResolveSymbol → detail.signal_name                    │
└──────────────────────────────┬──────────────────────────────────────────┘
                               ▼ TelemetryContext
┌─────────────────────────────────────────────────────────────────────────┐
│ 6. EVENT BUILD — TelemetryClient.BuildEvent → TelemetryEvent            │
│    PrivacySanitizer.Sanitize(ErrorMessage) ← лише ErrorMessage!         │
└──────────────────────────────┬──────────────────────────────────────────┘
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ 7. QUEUE — TelemetryEventQueue (in-memory, cap 5000)                    │
└──────────────────────────────┬──────────────────────────────────────────┘
                               ▼ batch ≤100
┌─────────────────────────────────────────────────────────────────────────┐
│ 8. UPLOAD — TelemetryUploader.FlushAsync (Postgrest Insert по одному)   │
│    RLS: user_id = auth.uid() (блокує pre-auth події)                    │
└──────────────────────────────┬──────────────────────────────────────────┘
                               ▼ INSERT
┌─────────────────────────────────────────────────────────────────────────┐
│ 9. SUPABASE — public.telemetry_events (append-only, 14d retention)      │
│    CHECK chk_telemetry_failed_has_signal                                │
│    TRIGGER trg_telemetry_failed_promote (AFTER INSERT WHEN Failed)      │
└──────────────────────────────┬──────────────────────────────────────────┘
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ 10. INCIDENT PROMOTION — tg_promote_after_failed() (SECURITY DEFINER)   │
│     1) auto_close_stale_incidents()                                     │
│     2) promote_incident_candidates_for_event(NEW.id)                    │
│     → UPSERT telemetry_incidents + INSERT notification_queue            │
└──────────────────────────────┬──────────────────────────────────────────┘
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ 11. NOTIFIER — SCLOCVerse.Notifier / NotificationDispatcher             │
│     FOR UPDATE SKIP LOCKED → DiscordNotificationProvider → webhook      │
│     → notification_attempts (append-only audit)                         │
└──────────────────────────────┬──────────────────────────────────────────┘
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ 12. CONTROL CENTER — Blazor (cc_readonly)                               │
│     Читає control_center.* views (incidents, traces, release_health)    │
└─────────────────────────────────────────────────────────────────────────┘
```

### 5.2. Зайві перетворення, парсери, моделі

| # | Тип | Деталь | Файл |
|---|---|---|---|
| E1 | Подвійне представлення | `LiaInstallException` копіює 8 полів з `LiaForensic` + `ForensicJson` (= `RawJson`) | `LiaInstallException.cs` |
| E2 | 3 "переливання" даних | `LiaForensic → LiaInstallException → TelemetryContext.Detail → TelemetryEvent.detail(jsonb)` | — |
| E3 | Мертвий парсер | `LiaForensicParser.TryParseMinimal` — 0 викликів (артефакт "перехідного періоду") | `LiaForensicParser.cs:67` |
| E4 | Подвійна модель GitHub | Вкладена `GitHubRelease`/`GitHubReleaseAsset` в `Updater.cs:734-756` дублює `Models/ApplicationUpdate/GitHubRelease*.cs` | `Updater.cs` |
| E5 | Подвійний forensic-блок у PS | `BuildInstallerScript` має 2 catch (cert + appx) з ідентичною структурою ~30 рядків | `Updater.cs:382-464` |
| E6 | Подвійна JSON-бібліотека | `ErrorContextExtractor.cs:135` використовує `System.Text.Json`, інші — `Newtonsoft.Json` | різні файли |
| E7 | `ForensicJson`/`RawJson` без споживача | Зберігаються, але після створення не читаються | `LiaInstallException.cs:20` |

### 5.3. Де інформація губиться

| # | Місце | Серйозність |
|---|---|---|
| L1 | `Updater.cs:386,420` — CLR generic HRESULT `0x80131500` замість deployment-коду (документовано в `LIA_INSTALLATION.md:144-148`) | Висока |
| L2 | Pre-auth події блокуються RLS (CurrentUser==null → early return). LIA-встановлення до логіну → forensic у черзі, втрачається при закритті | Середня |
| L3 | `TelemetryEventQueue` cap 5000 drop-oldest при тривалому офлайн | Середня (by design) |
| L4 | `PrivacySanitizer` не покриває `Detail` (зокрема `appx_log`, `certificate_subject`) | Середня-Висока |

### 5.4. Висновки Завдання 5

- Pipeline архітектурно цілісний (контракт `##SCLOC_FORENSIC##` + JSON).
- 1 мертвий парсер (E3), 1 зайве виключення-обгортка (E1), 1 дубль моделей GitHub (E4), 1 дубль JSON-бібліотеки (E6).
- Головна відома вада (документована) — generic CLR HRESULT.
- Головна не-документована вада — `Detail` минає `PrivacySanitizer`.

---

## ЗАВДАННЯ 6 — Error Pipeline

### 6.1. Повна схема

```
Exception (throw у C#)
    │
    ▼
catch (Exception ex) ── ErrorContextExtractor.Extract(ex)
    │                    • обхід inner-ланцюга
    │                    • ClassifySource (за namespace)
    │                    • TryGetHttpStatus / SupabaseCode / Hresult
    │                    • ApplyLiaForensic (phase/cert/activity_id/appx_log)
    │                    • HResultCatalog.ResolveSymbol → signal_name
    ▼
TelemetryContext ── LiaEvents/UpdateEvents/TrackAuth/TrackSync
    │
    ▼
ITelemetryService.Track(component, operation, outcome, ctx)
    │
    ▼
TelemetryClient.BuildEvent()
    │   └ PrivacySanitizer.Sanitize(ErrorMessage) ⚠ лише ErrorMessage!
    ▼
TelemetryEventQueue.Enqueue (in-memory)
    │
    ├── Timer 30с ── TelemetryUploader.FlushAsync()
    └── catch-block ── FlushAsync(5с) ПЕРЕД throw (Terminal Flush)
                        │
                        ▼
                Pre-auth? → RETURN (втрата)
                        │ RLS: user_id = auth.uid()
                        ▼
            Postgrest INSERT telemetry_events (idempotent client_event_id)
                        │
                        ▼ [TRIGGER trg_telemetry_failed_promote WHEN outcome='Failed']
            tg_promote_after_failed() (EXCEPTION-safe)
                        │
                        ├── auto_close_stale_incidents()
                        │       └ UPDATE telemetry_incidents SET status='Closed'
                        │
                        └── promote_incident_candidates_for_event(NEW.id)
                                • fingerprint = component|operation|signal|app_version
                                • вікно 10 хв: failed_now/total_now/failure_pct
                                • policy lookup
                                │
                                ▼ за порогами
                            telemetry_incidents (UPSERT за fingerprint_hash)
                                + INSERT notification_queue ('IncidentCreated')
                        │
                        ▼
                SCLOCVerse.Notifier / NotificationDispatcher (claim)
                        │ FOR UPDATE SKIP LOCKED → DiscordNotificationProvider
                        ▼
                Discord webhook → notification_attempts (audit)
                        │
                        ▼
                Control Center (cc_readonly) читає cc.* views
                        │
                        ▼
                Developer ( Discord + Dashboard )
```

### 6.2. Де інформація губиться (Error Pipeline)

| # | Точка | Наслідок |
|---|---|---|
| L-Err1 | Відсутній `UnhandledException`/`DispatcherUnhandledException` handler | WPF-краш повністю минає Observability. Severity `Crash` (дозволений CHECK) ніколи не фіксується |
| L-Err2 | Pre-auth події (CurrentUser==null → early return) | Auth.Failed до логіну + Application.Start втрачаються при закритті |
| L-Err3 | `PrivacySanitizer` не покриває `Detail`/`ExceptionType`/`Source` | PII-ризик для `certificate_subject`, `appx_log` |
| L-Err4 | `country` у `telemetry_events` завжди NULL | Гео-розріз інцидентів неможливий |
| L-Err5 | `StackTrace` свідомо відкидається (`TelemetryContext.ErrorMessage = "НЕ повний stack"`) | Немає куди покласти stack при майбутньому crash-handler |
| L-Err6 | `event_count` семантично різний (batch: +failed_now, per-event: +1) | Дашборд показує некоректну к-ть подій |
| L-Err7 | UI catch-блоки `MainWindow.xaml.cs` (248,346,380,411,510,544,605,744,772,798,830) без телеметрії | UI-операції частково невидимі |
| L-Err8 | `IncidentEscalated`/`IncidentClosed` never enqueued | Ескалація Warning→Critical та авто-close без сповіщення |
| L-Err9 | Trigger EXCEPTION-safe ковтає збої промоутера | Систематичний збій → silent gap |

### 6.3. Incident State Machine

#### Декларовані стани (з розбіжністю F2)

| Статус | CHECK (m12) | transition_incident (m15) | create_knowledge_from_incident (m20) |
|---|:---:|:---:|:---:|
| `Active` | ✅ | ✅ | — |
| `Confirmed` | ✅ | ✅ | — |
| `Investigating` | ✅ | ✅ | — |
| **`Mitigated`** | ❌ **ВІДСУТНІЙ** | ✅ (order=4) | ✅ (умова створення Knowledge) |
| `Resolved` | ✅ | ✅ | ✅ |
| `Closed` | ✅ (термінальний) | ✅ | ✅ |

**⚠ F2:** Перехід у `Mitigated` завершиться CHECK violation — неможливий у поточній схемі.

#### Таблиця переходів (forward-only)

| From \ To | Active | Confirmed | Investigating | Mitigated | Resolved | Closed |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| **Active** | — | ✅ ручний | ✅ ручний | ⚠ CHECK-fail | ✅ ручний | ✅ авто / ручний |
| **Confirmed** | ✗ | — | ✅ | ⚠ CHECK-fail | ✅ | ✅ |
| **Investigating** | ✗ | ✗ | — | ⚠ CHECK-fail | ✅ | ✅ |
| **Mitigated** | ✗ | ✗ | ✗ | — | ✅ | ✅ |
| **Resolved** | ✗ | ✗ | ✗ | ✗ | — | ✅ |
| **Closed** | ✗ | ✗ | ✗ | ✗ | ✗ | — (рецидив → новий incident_id) |

**Хто тригерить:**
- **Авто (`system`):** `auto_close_stale_incidents()` — `Active → Closed` (лише Active, не чіпає Investigating/Resolved).
- **Ручний (`admin` через `cc_readonly`):** `transition_incident(id, to_status, ...)` — всі forward-переходи.
- **Промоутер:** не міняє статус (тільки метрики + INSERT з `status='Active'`.

### 6.4. Promotion Engine — дублювання логіки

| Шлях | Файл | Стан | Виклик |
|---|---|---|---|
| BATCH (ориг.) | міграція 13 | superseded | ніким (cron відсутній) |
| BATCH (з notify) | міграція 16 (перевизначено) | superseded | ніким |
| PER-EVENT | міграція 05021100 | **LIVE** | trigger `trg_telemetry_failed_promote` |

**3 копії логіки** з вже наявною розбіжністю: `event_count` (+failed_now vs +1), payload (`version:1` є/немає).

### 6.5. Notification Pipeline (верифіковано — Worker існує)

| Компонент | Стан | Файл |
|---|---|---|
| `notification_queue` (m16) | жива | міграція 16 |
| `notification_attempts` (m04000017) | жива (append-only audit) | міграція 17 |
| `cc_notifier` role (m18) | жива | міграція 18 |
| **Worker `NotificationDispatcher`** | ✅ **існує** | `SCLOCVerse.Notifier/Dispatcher/NotificationDispatcher.cs` |
| **Provider `DiscordNotificationProvider`** | ✅ **існує** | `SCLOCVerse.Notifier/Providers/DiscordNotificationProvider.cs` |
| `INotificationProvider` контракт | ✅ існує | `SCLOCVerse.Notifications/INotificationProvider.cs` |
| Idempotency `UNIQUE(incident_id, type) WHERE status!='Failed'` | ✅ (Стаття 25) | міграція 16 |

> **Корекція звітів:** Один з розвідників стверджував, що Worker відсутній. **Верифіковано особисто:** `SCLOCVerse.Notifier/Dispatcher/NotificationDispatcher.cs` + `Providers/DiscordNotificationProvider.cs` існують. Notification-pipeline повний.

**⚠ Types-розбалансування (L-Err8):** CHECK дозволяє 7 типів, промоутер енк'ює лише `IncidentCreated`. `IncidentEscalated`/`IncidentClosed`/`KnowledgeVerified` ніколи не генеруються автоматично.

### 6.6. Висновки Завдання 6

- Error Pipeline (C#) — добре спроєктований, але з **5 сліпими зонами** (L-Err1...L-Err5).
- Incident Pipeline (SQL) — функціональний, з **3 копіями логіки** та **невідповідністю Mitigated** (F2).
- Notification Pipeline — повний (Worker + Provider), але з **незбалансованими типами** (L-Err8).

---

## ЗАВДАННЯ 7 — Control Center Impact Analysis

### 7.1. Поточна архітектура Control Center (верифіковано)

- **Blazor Server** у тому ж репозиторії: `SCLOCVerse.ControlCenter/`
- 7 сторінок: Home, Incidents, Knowledge, Releases, Settings, Traces, Error
- 2 репозиторії: `ControlCenterRepository`, `TraceRepository`
- Роль `cc_readonly`: `USAGE` + `SELECT` на схему `control_center` + `EXECUTE` на SECURITY DEFINER функції

### 7.2. Матриця Impact запропонованих оптимізацій

| # | Optimization | Control Center Impact | Breaking Change | Migration Needed |
|---|---|---|---|---|
| **OPT-1** | Створити `control_center.release_health_detail` (F1) | ✅ Вже читається (`Releases.razor`) — **полагодить краш сторінки** | НІ (additive) | ✅ Нова міграція |
| **OPT-2** | Розширити CHECK `telemetry_incidents.status` +Mitigated/Acknowledged (F2) | ✅ Дозволить `Mitigated` workflow (Knowledge Engine) | НІ (additive) | ✅ ALTER TABLE |
| **OPT-3** | Додати Terminal Flush у ApplicationUpdate-сервіси (F5) | НІ (не зачіпає CC) | НІ | НІ (тільки C#) |
| **OPT-4** | Розширити `PrivacySanitizer` на `Detail` (F4) | ⚠ Зменшить деталізацію forensic у `Incidents.razor` (cert_subject, appx_log будуть масковані) | ⚠ Можливе обурення розробників (менше діагностики) | НІ (тільки C#) |
| **OPT-5** | Додати global UnhandledException handler (F3) | ✅ Більше інцидентів у Dashboard | НІ | НІ (тільки C#) |
| **OPT-6** | Заповнити `telemetry_events.country` тригером (F8) | ✅ Гео-аналітика в Dashboard | НІ (additive) | ✅ Нова міграція (тригер) |
| **OPT-7** | Замапити 6 колонок `app_installations` в C# (F9) | ✅ Багатша аналітика в `Users.razor`/`Installations` | НІ | НІ (тільки C#) |
| **OPT-8** | Консолідувати `health` + `platform_stats` (deprecated view) | ⚠ Якщо є зовнішній споживач `health` — break | ⚠ Потенційно | ✅ DROP VIEW (sync з C#) |
| **OPT-9** | Видалити мертвий `control_center.errors` + `error_reports` | ✅ Менше плутанини в UI | ⚠ Якщо є зовнішній споживач — break | ✅ DROP |
| **OPT-10** | Видалити мертвий `LiaForensicParser.TryParseMinimal` (F7) | НІ (внутрішній C#) | НІ | НІ |
| **OPT-11** | Уніфікувати GitHubRelease моделі (E4) | НІ (внутрішній C#) | НІ | НІ |
| **OPT-12** | Усунути `Mitigated` enum невідповідність або додати в CHECK (F2) | ✅ Workflow знань працюватиме | НІ | ✅ ALTER |
| **OPT-13** | Додати `IncidentEscalated`/`IncidentClosed` auto-enqueue (L-Err8) | ✅ Більше сповіщень розробнику | НІ (additive) | ✅ Зміна промоутера |
| **OPT-14** | Додати `BackgroundUpdateMonitor` телеметрію (GAP) | ✅ Видимість збоїв фонового чеку | НІ | НІ (тільки C#) |
| **OPT-15** | Заповнювати `git_commit` через MSBuild | ✅ Фільтрація за build у Dashboard | НІ | НІ (тільки .csproj) |

### 7.3. Критичні зауваження щодо стабільності CC

1. **OPT-1 — обов'язкова перед релізом 1.0.0.1.** Сторінка Releases (`/releases`) крашиться `PostgresException 42P01 relation "control_center.release_health_detail" does not exist`. Без фіксу — реліз не можна випускати.
2. **OPT-2 — обов'язкова перед релізом** якщо планується використання `Mitigated` workflow (Knowledge Engine).
3. **OPT-8, OPT-9 — вимагають перевірки зовнішнього споживача** (можливо CC окремо від Blazor). Якщо Blazor = єдиний CC — безпечно.

### 7.4. Висновки Завдання 7

- 4 оптимізації **критичні** перед релізом 1.0.0.1 (OPT-1, OPT-2, OPT-6 — если потрібна гео, OPT-13 якщо потрібні повні сповіщення).
- 6 оптимізацій **additive-safe** (без breaking changes).
- 2 оптимізації **потенційно breaking** (OPT-8, OPT-9 — вимагають перевірки зовнішнього споживача).
- Жодна оптимізація не ламає Control Center, якщо виконана в порядку OPT-1 → OPT-2 → інші.

---

## ЗАВДАННЯ 8 — Production Value Analysis

### 8.1. Оцінка кожної TelemetryEvent за критерієм "Знайде Production Bug?"

| Category | Events | Production Value | Обґрунтування |
|---|---|---|---|
| **Application.Start.Started** | #1 | **Useful** | Session-start KPI, adoption metric |
| **Auth.SignIn.Started/Succeeded** | #2,#9 | **Useful** | Auth funnel metric |
| **Auth.SignIn.Cancelled** | #4,#5,#10 | **Low Value** | UX-метрика, не production bug |
| **Auth.SignIn.Failed (без контексту)** | #3,#6,#7,#8 | **Useful** ⚠ але без причини — OPT | Production bug auth, але без діагностики |
| **Auth.SignIn.Failed (catch з контекстом)** | #11 | **Critical** | Головна подія діагностики auth |
| **Auth.RestoreSession.*** | #12,#13,#14 | **Critical** | Відновлення сесії = повторний вхід користувача |
| **Installation.Sync.*** | #15,#16,#17 | **Critical** | Бізнес-стан інсталяції + adoption |
| **LIA.Install.Started (x2)** | #18,#25 | **Useful** ⚠ CASCADE | Дублювання funnel (див. §1.4) |
| **LIA.Download.*** | #19-#24 | **Critical** | 0x800B0109 фіксується тут (#20/#23) |
| **LIA.Install.Succeeded/Failed** | #26,#27 | **Critical** | Термінал LIA-операції |
| **LIA.RunInstallerScript.*** | #28-#32 | **Critical** | PowerShell-фаза, forensic-critical |
| **Updater.Download.*** | #33,#34,#35 | **Useful** | Self-update metric |
| **Updater.Install.*** | #36-#39 | **Useful** | Self-update metric ⚠ GAP фінального результату |
| **Updater.Verify.*** | #40-#44 | **Low Value** | Checksum verify рідко падає |

### 8.2. Категоризація за Production Value

| Категорія | К-ть events | % | Рекомендація |
|---|---:|---:|---|
| **Critical** (знайде prod bug однозначно) | 17 | 39% | KEEP |
| **Useful** (допоможе знайти prod bug) | 14 | 32% | KEEP |
| **Low Value** (UX-метрика, не bug) | 10 | 23% | OPTIONAL (вимкнути в production-strict) |
| **No Value** (немає споживача) | **0** | 0% | — |

> **Висновок:** Подій категорії "No Value" (які треба видалити) — **0**. Усі події мають споживача (Supabase → promotion/dashboard).

### 8.3. Події з lowered diagnostic value (рекомендація — НЕ видалення)

| Подія | Чому lowered | Рекомендація |
|---|---|---|
| `Auth.SignIn.Cancelled` (3 варіанти) | UX-факт, не bug | Залишити в Debug-build only |
| `Updater.Verify.Skipped (NoChecksum)` | Не відмова, штатний шлях | Залишити в Debug-build only |
| `LIA.Install.Started (x2)` | Дублює funnel | OPT: залишити лише один з двох Started |

### 8.4. Висновки Завдання 8

- **0 подій категорії "No Value"** — нема що видаляти.
- **10 подій Low Value** — кандидати на Debug-build gating (не видалення).
- **5 Failed-подій без контексту** (#3,#6,#7,#8,#11) — знижена діагностична цінність, OPT.
- **Висновок:** Система телеметрії не містить сміття; проблема не в надлишку, а в **неповноті** (GAP-и §1.3).

---

## ЗАВДАННЯ 9 — Event Reduction (підрахунок)

### 9.1. Поточний стан

| Метрика | Значення |
|---|---|
| Унікальних Track-точок у C# | **44** (поліморфних за outcome) |
| Унікальних `component.operation` пар | ~15 |
| Стандартних outcomes | 5 (Started/Succeeded/Failed/Cancelled/Skipped) |
| Стандартних severities | 4 (Info/Warning/Error/Critical) + `Crash` (не реалізовано) |
| DB retention (telemetry_events) | 14 днів |

### 9.2. Розрахунок потенційного скорочення

> ⚠ Скорочення **НЕ рекомендується** як мета. Зараз проблема не в надлишку, а в неповноті.

| Гіпотетичне скорочення | Можна? | Наслідок |
|---|---|---|
| Видалити 10 Low Value events | ⚠ Гіпотетично | -23% writes, але втрата UX-аналітики |
| Видалити CASCADE duplicates (1 з 2 LIA.Install.Started) | ✅ Безпечно | -2% writes |
| Видалити мертвий `LiaForensicParser.TryParseMinimal` | ✅ Безпечно (код) | 0% writes (не викликається) |
| Gate Debug/Trace через `BuildInfo.IsDebug` | ⚠ Можна | -30-50% writes у production |

### 9.3. Реальна економія ресурсів (якщо застосувати гіпотезу)

| Ресурс | Поточно (умовно) | Після скорочення (гіпотеза) | Економія |
|---|---|---|---:|
| **DB rows / day** (44 events × ~1000 sessions × ~5 events/session) | ~220K | ~150K (Debug-gating) | **-32%** |
| **Storage (14d retention)** | ~3 MB | ~2 MB | **-33%** |
| **Network (Supabase writes)** | ~220K INSERT/day | ~150K INSERT/day | **-32%** |
| **Supabase API calls** (Insert по одному) | ~220K | ~150K | **-32%** |

### 9.4. Реальна рекомендація (анти-скорочення)

> Замість скорочення — **розширення** з умовним gating:

| Дія | Ефект |
|---|---|
| Додати global UnhandledException handler | +1-5 events/crash (зараз 0) |
| Додати BackgroundUpdateMonitor телеметрію | +1 event/check (зараз 0) |
| Додати bridge-event з `UpdateHistoryService` (фінальний результат) | +1 event/update (зараз 0) |
| Додати `Application.Exit` термінальну подію | +1 event/session (зараз 0) |
| Додати `Localization.*` events | +3-5 events/install (зараз 0) |

**Висновок:** Реальна оптимізація — це **батч-інсерт** (зараз Insert по одному, див. `TelemetryUploader.cs:63-83`) замість скорочення подій. Це дасть **-90% мережевих round-trip** без втрати даних.

### 9.5. Висновки Завдання 9

- **Скорочення подій НЕ рекомендується** — 0 подій категорії "No Value".
- Реальна економія — через **батч-інсерт** (TelemetryUploader Phase 5, відкладено).
- Альтернатива — **Debug-build gating** для Low Value events (-32% writes у production).

---

## ЗАВДАННЯ 10 — Архітектурні рекомендації (фінальний документ)

### 10.1. Current Architecture

**Монорепо з 4 проєктами:**
- `SCLOCVerse` (WPF) — клієнт
- `SCLOCVerse.ControlCenter` (Blazor) — UI Dashboard
- `SCLOCVerse.Notifier` (Worker) — Notification Dispatcher
- `SCLOCVerse.Notifications` (Lib) — контракти

**БД:** 2 схеми (public, control_center), 15 таблиць, 18 VIEW + 1 MV, ~24 функції, 4 тригери, 4 ролі.

**Потоки:**
```
WPF Client → Supabase (telemetry_events) → Trigger → promote → incidents + notifications
                                                                  ↓
                                              Notifier Worker → Discord webhook
                                                                  ↓
                                              Control Center (Blazor) → Developer
```

### 10.2. Problems

| # | Тип | Опис |
|---|---|---|
| **P1** | Втрачена подія | Global UnhandledException handler відсутній (F3) |
| **P2** | Втрачена подія | Pre-auth events блокуються RLS, втрачаються при закритті |
| **P3** | Втрачений сигнал | `BackgroundUpdateMonitor.CheckFailed` без телеметрії |
| **P4** | Втрачений сигнал | Фінальний результат `UpdateInstaller` після restart — лише локально |
| **P5** | PII-ризик | `PrivacySanitizer` не покриває `Detail` (F4) |
| **P6** | Краш сторінки | `release_health_detail` не існує (F1) — `Releases.razor` крашиться |
| **P7** | Зламаний workflow | `Mitigated` enum відсутній у CHECK (F2) |
| **P8** | Мертвий код | 3 копії логіки promotion (F6) |
| **P9** | Мертвий код | `LiaForensicParser.TryParseMinimal` (F7) |
| **P10** | Мертвий код | `admin_audit_log`, `error_reports`, `user_discord_guilds` (F10) |
| **P11** | Мертвий код | `control_center.errors`, `control_center.health` VIEWs |
| **P12** | Дубль даних | `LiaInstallException` копіює `LiaForensic` 1:1 (E1) |
| **P13** | Дубль моделей | Вкладена `GitHubRelease` vs публічна (E4) |
| **P14** | Дубль JSON-бібліотеки | `System.Text.Json` + `Newtonsoft.Json` (E6) |
| **P15** | Дубль колонок | `created_at` vs `first_seen` в `app_installations` |
| **P16** | Мертва колонка | `telemetry_events.country` (F8) |
| **P17** | Мертві колонки | 6 з 19 в `app_installations` (F9) |
| **P18** | Асиметрія контракту | Terminal Flush відсутній у `ApplicationUpdate`-сервісах (F5) |
| **P19** | Тіньовий канал | `InputDiagnostics` поза Observability-конвеєром |
| **P20** | Types-розбаланс | Notification: лише `IncidentCreated` генерується (L-Err8) |
| **P21** | Stale doc | `ecosystem_stats()` referenced у docs, не існує |
| **P22** | Stale doc | `contract_info.product_version='1.8.0'` (актуально 1.0.0.1) |

### 10.3. Duplicate Data

| Де | Що дублюється | Тип |
|---|---|---|
| C# `LiaInstallException` ↔ `LiaForensic` | 8 полів копіюються 1:1 | Зайве перетворення |
| C# nested `GitHubRelease` ↔ `Models/ApplicationUpdate/GitHubRelease` | Схема GitHub API JSON | Дубль моделі |
| SQL міграції 13/16/05021100 | Логіка промоутингу | 3 копії |
| SQL `created_at` ↔ `first_seen` (`app_installations`) | Timestamps | Семантичний дубль |
| SQL `health` ↔ `platform_stats` ↔ `observability_health` | Метрики "active installations" | Functional overlap |
| SQL `error_reports` ↔ `telemetry_events` | Концепція error-tracking | Reserved (parallel channel) |
| `Debug.WriteLine` ↔ `TelemetryEvent` в `AuthService.cs:143-144` | Виняток фіксується двічі | Асиметричне (повний stack локально, санітизований удалено) |

### 10.4. Dead Code

| Об'єкт | Тип | Доказ |
|---|---|---|
| `LiaForensicParser.TryParseMinimal` | C# метод | 0 викликів |
| `promote_incident_candidates()` (batch, m13/m16) | SQL функція | 0 викликів (cron відсутній) |
| `admin_audit_log` | SQL таблиця | 0 продюсерів |
| `error_reports` | SQL таблиця | 0 продюсерів (reserved) |
| `user_discord_guilds` | SQL таблиця | Продюсер вимкнений |
| `control_center.errors` | SQL VIEW | Проєкція порожньої таблиці |
| `control_center.health` | SQL VIEW | Functional duplicate з `platform_stats` |
| `LiaInstallException.ForensicJson` | C# властивість | 0 споживачів |
| `contract_info/users/installations/statistics` views | SQL VIEW | Не споживаються C# напряму (через інші views) |

### 10.5. Redundant Telemetry

- **CASCADE Failed events** в LIA pipeline (2-3 Failed на 1 відмову) — свідома гранулярність, але потребує de-dup в Dashboard.
- **Подвійний `LIA.Install.Started`** (#18 + #25) — спотворює funnel-метрики.
- **`Debug.WriteLine` + `TelemetryEvent` в `AuthService.cs:143-144`** — асиметричне дублювання.

### 10.6. Database Optimization

| Пріоритет | Дія | Ефект |
|---|---|---|
| 🔴 P0 | Створити `release_health_detail` VIEW (OPT-1) | Polагодити Releases.razor |
| 🔴 P0 | Розширити CHECK `telemetry_incidents.status` +Mitigated/Acknowledged (OPT-2) | Workflow Knowledge Engine |
| 🟠 P1 | Додати тригер GeoIP на `telemetry_events` (OPT-6) | Гео-аналітика інцидентів |
| 🟡 P2 | ДепRECATE `control_center.health` (після перевірки зовнішнього споживача) | Менше superficies |
| 🟡 P2 | Документувати `error_reports`/`admin_audit_log`/`user_discord_guilds` як Future Capability | Ясність |
| 🟢 P3 | Batch-інсерт у `TelemetryUploader` (Phase 5) | -90% мережевих round-trip |

### 10.7. Control Center Changes

| Зміна | Файл CC | Складність |
|---|---|---|
| Полагодити Releases page (release_health_detail) | `ControlCenterRepository.cs:392` | НІ (вже читає) |
| Додати Mitigated workflow в Incidents | `Incidents.razor` (transition dropdown) | Малий |
| Geo-аналітика в Dashboard | Нова сторінка/віджет | Середній |
| Документувати Future Capability tables | — | — |

### 10.8. Migration Plan (рекомендація — НЕ виконання)

**Phase A — Критичні фікси перед релізом 1.0.0.1:**
1. Міграція: `CREATE VIEW control_center.release_health_detail AS ...` (F1)
2. Міграція: `ALTER TABLE telemetry_incidents DROP CONSTRAINT chk_incident_status, ADD CONSTRAINT ... CHECK (status IN ('Active','Confirmed','Investigating','Mitigated','Acknowledged','Resolved','Closed'))` (F2)
3. C#: Terminal Flush у `UpdateDownloader.cs:59`, `UpdateInstaller.cs:46,82`, `UpdateVerifier.cs:65` (F5)
4. C#: global `DispatcherUnhandledException` + `AppDomain.CurrentDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` (F3)

**Phase B — Privacy + діагностика:**
5. C#: Розширити `PrivacySanitizer` на `Detail` (F4)
6. C#: Контекст для Auth.SignIn.Failed #3,#6,#7,#8 (§1.2)
7. C#: `BackgroundUpdateMonitor` телеметрія (GAP)

**Phase C — Очищення мертвого коду (опційно):**
8. Видалити `LiaForensicParser.TryParseMinimal` (F7)
9. Документувати Future Capability таблиці (admin_audit_log, error_reports, user_discord_guilds)
10. Уніфікувати GitHubRelease моделі (E4)

**Phase D — Performance (Phase 5):**
11. `TelemetryUploader` батч-інсерт
12. JSONL-persistency черги (Slice 2)

### 10.9. Risk Analysis

| Ризик | Ймовірність | Наслідок | Міграція |
|---|---|---|---|
| Releases page крашиться на production | 100% (зараз) | Користувач не бачить метрик релізу | OPT-1 |
| Knowledge workflow не працює | 100% (зараз для Mitigated) | Автор знань не може перевести в Mitigated | OPT-2 |
| Pre-auth LIA forensic втрачається | 50% (користувач без логіну) | 0x800B0109 не фіксується | Bridge абоanon channel |
| PrivacySanitizer неповний | 30% (якщо appx_log містить ім'я) | PII-витік | OPT-4 |
| Зовнішній споживач `control_center.health` | Невідомо | Breaking change | Перевірити перед OPT-8 |

### 10.10. Zero Regression Strategy

1. **Forensic-first:** Кожна зміна починається з форензик-аналізу (AGENTS.md).
2. **Additive-only:** Усі SQL-міграції тільки додають (Стаття 13 Конституції).
3. **Backup commit:** Перед кожною зміною — резервний commit.
4. **Smoke-тести в міграціях:** Патерн `DO $$ ... ASSERT ... $$` (вже використовується в m05021100/05021200/05030000).
5. **Verification script:** `db-cleanup-verify.sql` розширити для нових міграцій.
6. **Rollback plan:** Кожна міграція Phase A (F1, F2) повинна мати DROP-аналог.
7. **Контроль Ukrainian text:** Усі commit-повідомлення та реліз-ноти — UTF-8 з `Content-Type: application/json; charset=utf-8` (P0 правило AGENTS.md).
8. **Staged rollout:** Phase A → реліз 1.0.0.1; Phase B/C/D — після стабілізації.

---

## ФІНАЛЬНИЙ ЗВІТ

### Виконано
Повний forensic-аудит Observability-архітектури SCLOC-Verse перед релізом 1.0.0.1:
- Прочитано 26 міграцій Supabase + ~85 .cs-файлів + 11 .md-файлів документації.
- Верифіковано 4 проєкти монорепо (SCLOCVerse, SCLOCVerse.ControlCenter, SCLOCVerse.Notifier, SCLOCVerse.Notifications).
- Побудовано повні схеми Forensic Pipeline, Error Pipeline, Incident State Machine.
- Виявлено 22 проблеми (P0-P3), 9 дублів, 9 одиниць мертвого коду.

### Змінені файли
**Лише один новий файл:** `docs/observability/Observability-Forensic-Audit-1.0.0.1.md` (цей звіт).
**Код не змінено. Комітів немає. Рефакторингу немає.**

### Ризики
Звіт суто описовий. Ризиків у продукт не внесено. Усі рекомендації вимагають окремого погодження перед реалізацією (згідно AGENTS.md).

### Commit
**Немає.** Дослідження без модифікацій коду.

### Ключове повідомлення
- **2 P0-дефекти** (F1, F2) — блокують реліз 1.0.0.1.
- **0 формально "мертвих" telemetry events** — видаляти нічого.
- **Реальна оптимізація — не скорочення, а батч-інсерт + розширення** (закриття GAP-ів).
- **Топ-пріоритет:** F1 (release_health_detail), F2 (Mitigated enum), F3 (UnhandledException), F4 (PrivacySanitizer Detail), F5 (Terminal Flush).
