# SCLOC-Verse — Unified Knowledge Base

> **Single Source of Truth.** Цей документ — єдина точка входу для будь-якого AI-агента.
> Якщо інформація тут є — не перечитуй десятки forensic-документів.
>
> **Версія застосунку:** 1.0.2.4 (Stable)
> **Supabase project:** `nrytczdbhehiotflaagl` (eu-west-1)
> **Живий документ:** відображає **поточний стан** системи. Історія релізів, forensic-деталі, phase-аудити винесені в дочірні документи.

---

## 0. Як користуватися цією базою знань

1. **Пошук по розділах** — розділи 1–17 покривають усю систему.
2. **§14 (Approved) та §15 (Rejected)** — першочергова перевірка перед тим, як пропонувати рішення.
3. **§16 (Technical Debt) та §17 (Backlog)** — що вже відомо як борг і що заплановано.
4. **Дочірні документи** — деталі в `docs/database/`, `docs/observability/`, `docs/release/`.
5. **Джерельні forensic** — відкривати лише для верифікації першоджерела (AGENTS.md §10).

> ⚠ **Конституція проєкту (AGENTS.md) має найвищий пріоритет** над цим документом у випадку конфлікту. Далі — Observability-Constitution, потім ця KB.

### Дочірні документи KB

| Документ | Зміст |
|---|---|
| [`docs/database/Data-Model.md`](database/Data-Model.md) | Повна схема БД: 15 таблиць по колонках (Classification/Ownership/Lifetime/Confidence), RLS, triggers, Phase 2 Cleanup Review. |
| [`docs/observability/Telemetry-Registry.md`](observability/Telemetry-Registry.md) | Telemetry Policy (3 рівні), Event Registry (39 емітерів), Field Registry, Reader Validation, Test Matrix. |
| [`docs/release/Release-History.md`](release/Release-History.md) | Журнал релізів v1.0.0.0 → v1.0.2.4, production incidents resolution log. |

---

# 1. Executive Summary

**SCLOC-Verse** — настільний WPF-клієнт (.NET 9) української локалізації Star Citizen з навісною observability-платформою (Supabase + Blazor Control Center + Worker Notifier + Knowledge Engine).

### 1.1. Підсистеми

| Підсистема | Призначення | Статус |
|---|---|---|
| **SCLOCVerse** (WPF) | Клієнт: локалізація, оновлення гри/L.I.A., hangar-timer, hotkeys, tray, anti-afk, auto-key, overlay | ✅ RC |
| **Control Center** (Blazor Server) | Дашборд операційної команди: інциденти, трейси, release health, knowledge base | ✅ RC |
| **Notifier** (Worker) | Доставка сповіщень про інциденти (Discord) | ✅ RC |
| **Observability pipeline** (Supabase) | telemetry → incidents → notifications → knowledge | ✅ RC |
| **L.I.A.** | Голосовий асистент (MSIX/AppX) стороннього автора AlexLiberty — оркеструється клієнтом | ✅ (із відкладеними ризиками) |
| **Knowledge Engine** | Ручна база знань з auto-verify (Phase 6 завершено, API Freeze v1.0) | ✅ RC |

### 1.2. Ключові факти

- **4 процесоізольовані** проєкти монорепо; спілкуються **тільки через Postgres/Supabase** (2 схеми, 26+ міграцій, 30 SECURITY DEFINER функцій).
- **Ручна композиція залежностей** (без IoC-контейнерів, без Generic Host).
- **Discord OAuth + Supabase GoTrue** (PKCE, scope `identify` only) — обов'язкова авторизація.
- **RLS скрізь**: `anon` deny-all, `authenticated` owner-only, `telemetry_events` append-only, `cc_readonly`/`cc_notifier` least-privilege.
- **Additive-only контракт** схеми (Стаття 13 Конституції Observability). DROP COLUMN заборонено.
- **Zero Regression** — стабільний код не чіпати без потреби (AGENTS.md).
- **PII-мінімізація**: лише discord_user_id/username/avatar + технічні метадані; email, паролі, поведінкова телеметрія — ніколи.
- **Code signing** через SignPath.io + SignPath Foundation.
- **Українська мова**: коментарі, документація, commit-повідомлення. UTF-8 як P0 для всіх текстових файлів.

---

# 2. Архітектура

> Деталі: `docs/architecture/Final-Architecture-Review.md`, `ARCHITECTURE_DECISIONS.md`, `README.md`, `AGENTS.md`.

## 2.1. Монорепо (4 проєкти)

| Проєкт | Технологія | Роль | БД-роль |
|---|---|---|---|
| `SCLOCVerse` | WPF .NET 9, Nullable | Клієнт — пише телеметрію + app_installations | `authenticated` |
| `SCLOCVerse.ControlCenter` | Blazor Server | UI Dashboard + Knowledge workflow | `cc_readonly` |
| `SCLOCVerse.Notifier` | Worker (BackgroundService) | Notification dispatcher | `cc_notifier` |
| `SCLOCVerse.Notifications` | Class Library | Контракт `INotificationProvider` | — |

## 2.2. Composition Root / DI (ручний)

- `AppCompositionRoot` (fan-out ~24 залежності) + `AuthCompositionRoot` (fan-out ~6) з ручним `new`-компонуванням.
- **Two-phase init** для розриву циклу auth↔telemetry: `TelemetryClient` конструюється ДО `AuthCompositionRoot` (без Supabase-клієнта), потім доін'єктується через `SetInstallId` + `AttachClientFactory`.
- **Без IoC-контейнерів** (ADR-001). **Без Generic Host** (ADR-007).
- Структура папок: `Services/<Feature>/`, `Interfaces/`, `Models/`, `Controls/`; нові залежності — через `AppCompositionRoot`.

## 2.3. Життєвий цикл

`App.OnExit → AppCompositionRoot.Dispose()` → каскад reverse-order: `TelemetryClient` → `BackgroundUpdateMonitor` → `HangarOverlayService` → `HangarTimerService` → `AuthCompositionRoot`.

- **Жодного app-wide `CancellationTokenSource`.** Кожен сервіс зупиняє власні таймери.
- Таймери: telemetry flush **30с**; background update **10 хв**; overlay countdown **200мс**; hangar card **250мс**; home smooth scroll **16мс**.

## 2.4. UI-координація

- `MainWindow` — WPF code-behind-координатор (ADR-004). **~903 рядки, ~20 ctor-параметрів → God Class** (TD-6).
- Canvas-навігація через `CanvasManager` (ADR-006) — перемикає видимість Canvas-ів у межах одного `MainWindow`.
- Overlay — окремі вікна `HangarOverlayWindow` / `HangarCompactOverlayWindow` + `HangarOverlayService` (Win32 `WS_EX_TRANSPARENT`/`WS_EX_LAYERED`, ADR-005).
- `HangarTimerState` — SSOT для масштабу/прозорості (двостороння синхронізація: слайдери ↔ хоткеї ↔ overlay через `PropertyChanged`).

## 2.5. Гарячі клавіші

`IHotkeyBackend` + 2 реалізації: `RawInputBackend` (default) і `RegisterHotkeyBackend` (fallback). Вибір через env `SCLOCVERSE_HOTKEY_BACKEND`. Persistence через `IHotkeyService.GetDefinitions()` (JSON). Повний редактор — Settings Hub → «Гарячі клавіші».

## 2.6. Бекенд-абстракції (seam-и для розширення)

| Абстракція | Призначення | Seam для розширення |
|---|---|---|
| `ITelemetryService` | Єдиний санкціонований sink спостережуваності | — |
| `INotificationProvider` | Канал доставки сповіщень | Новий канал = 1 клас + 1 DI-рядок |
| `SupabaseClientFactory` | Supabase-клієнт (singleton, lazy, double-check lock) | — |
| `IHotkeyBackend` | Бекенд гарячих клавіш | Новий бекенд = 1 клас |
| `IInstallationContextProvider/Updater` | Активація FUTURE колонок `app_installations` | — |
| Control Center схему `control_center` | Read-only views під роллю `cc_readonly` | — |

---

# 3. Database (огляд)

> **Повна схема, модель даних, RLS, triggers, lifetime, Phase 2 Cleanup Review:** [`docs/database/Data-Model.md`](database/Data-Model.md).

| Тип | К-сть | Примітка |
|---|---:|---|
| Схеми (SCLOC-Verse) | 2 | `public`, `control_center` |
| Базові таблиці | 15 | 14 у `public` + 1 singleton у `control_center` |
| Звичайні VIEW | 25 | 24 у `control_center` + `public.user_analytics` |
| Materialized VIEW | 1 | `control_center.knowledge_coverage` |
| SECURITY DEFINER функції | 30 | усі з `SET search_path` (SEC-11 ✅) |
| Triggers | 5 | geoip, failed-promote, incident-refresh, knowledge-audit, set_incident_code |
| БД-ролі | 4 | `anon`, `authenticated`, `cc_readonly`, `cc_notifier` |
| RLS policies (`public`) | 28 | deny-all RESTRICTIVE + owner-only permissive + cc_notifier |
| pg_cron | 1 job | `retention-pipeline-daily` (Phase 5.1 ✅) — daily 03:00 UTC |

### 3.1. Triggers (5)

| Trigger | Подія | Дія |
|---|---|---|
| `trg_app_installations_set_country` | BEFORE INSERT/UPDATE на `app_installations` | GeoIP з Cloudflare `cf-ipcountry` → `country` |
| `trg_telemetry_failed_promote` | AFTER INSERT `telemetry_events` WHEN `outcome='Failed'` | `promote_incident_candidates_for_event()` |
| `trg_incident_refresh_coverage` | AFTER INSERT/UPDATE `telemetry_incidents` | `refresh_knowledge_coverage()` |
| `knowledge_audit` | AFTER INSERT/UPDATE `knowledge_entries` | snapshot → `knowledge_version_history` |
| `trg_set_incident_code` | BEFORE INSERT/UPDATE `telemetry_incidents` | `set_incident_code()` (Phase 3A) |

> ⚠ `trg_*_set_country` існує лише на `app_installations`. На `telemetry_events` тригера GeoIP немає → `telemetry_events.country` завжди NULL.

### 3.2. RLS-модель

| Роль | Доступ |
|---|---|
| `anon` | deny-all скрізь |
| `authenticated` | owner-only: `app_installations` SELECT+INSERT+UPDATE; `telemetry_events` SELECT+INSERT (append-only); `user_discord_guilds` |
| `cc_readonly` | `USAGE`+`SELECT` лише на `control_center` + EXECUTE на workflow/knowledge функції |
| `cc_notifier` | ALL на `notification_queue`/`notification_attempts` + SELECT `telemetry_incidents` |

**RLS Init Plan Fix (IMPL):** усі 10 RLS policies переписані `auth.uid()` → `(SELECT auth.uid())` (одне обчислення на запит).
**SEC-12 (IMPL):** `REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA public FROM PUBLIC` — `authenticated` більше не може викликати admin-функції через RPC.

---

# 4. `app_installations` (огляд)

> **Повна модель 19 колонок — у [`docs/database/Data-Model.md`](database/Data-Model.md) §6.**

**Стан:** 11 живих колонок (C# `InstallationService`), 1 від тригера (`country`), 2 DEFAULT (`update_channel='stable'`, `install_source='unknown'`), 5 завжди NULL. Усі 7 «мертвих» — FUTURE, активуються (additive-only).

**Активація FUTURE колонок** через `IInstallationContextProvider` (read) + `IInstallationContextUpdater` (write) — НЕ дублювати в `Settings`. Деталі: `docs/observability/app-installations-implementation-plan.md`.

**Cleanup-політика:** лише тестові записи (`install_id !~ '^[0-9a-f]{32}$'`). Production UUID — зберігаються (adoption-історія).

---

# 5. Telemetry

> **Повна Policy (3 рівні L1/L2/L3), Event Registry (39 емітерів), Field Registry, Reader Validation, Test Matrix — у [`docs/observability/Telemetry-Registry.md`](observability/Telemetry-Registry.md).**

## 5.1. Конституція (29 статей — цільові інваріанти)

Деталі: `docs/observability/Observability-Constitution.md`. Ключові статті:
- **Ст. 1** Absolute Isolation — телеметрія не кидає винятки в бізнес-код.
- **Ст. 2** Never Block UI — sync O(1), I/O у фоновому потоці.
- **Ст. 4** Zero PII — `PrivacySanitizer` як єдине вузьке горло.
- **Ст. 5** Append-Only — `telemetry_events` лише INSERT.
- **Ст. 7** Single Sanctioned Sink — лише `ITelemetryService`.
- **Ст. 10** Kill-Switch Transparent — вимкнення не ламає додаток.
- **Ст. 13** Additive-Only Schema.

## 5.2. Identity та Build info

- **`install_id`** — machine identity, файл `%LOCALAPPDATA%\SCLOCVerse\install-id` + registry `HKCU\Software\VALDEUS\SCLOCVerse\InstallId` (dual-store). Ніколи `MachineName`.
- **`user_id`** — nullable (null для pre-auth подій).
- **Build info:** `app_version`, `channel`, `telemetry_version=1`, `os_version`. `git_commit` — завжди NULL (MSBuild target відкладено).

## 5.3. Kill-switch

Єдиний реалізований: env `SCLOCVERSE_TELEMETRY_DISABLED=1|true`. Вимикає всю телеметрію бінарно.

## 5.4. Відомі проблеми (поточний борг)

| # | Проблема | Де |
|---|---|---|
| F3 | Відсутній global `UnhandledException` handler — WPF-краш минає спостережуваність | `App.xaml.cs` |
| F4 | `PrivacySanitizer` покриває лише `ErrorMessage`; `Detail` неочищений | `TelemetryClient.cs:142` |
| F5 | Terminal `FlushAsync` пропущено у 5 Failed-емітерів ApplicationUpdate | `UpdateDownloader/Installer/Verifier.cs` |
| F8 | `telemetry_events.country` — мертва (тригер лише на installations) | міграції 2 + 9 |
| — | LIA cascade: 1 фізична відмова → 3-4 Failed-події | `Updater.cs` |
| — | `Localization.*` телеметрія відсутня (сліпа зона) | `LocalizationInstaller.cs` |

> **Вирішені (для історії — див. Release-History.md):** F1 `release_health_detail`, F2 `chk_telemetry_outcome`, RC-401 requeue storm, chk_telemetry_failed_has_signal constraint.

---

# 6. Incident Pipeline + Release Health

## 6.1. Incident Pipeline

> Деталі: `docs/observability/Observability-RC1-Release.md`.

```
telemetry_events (Failed) ─trigger─▶ promote_incident_candidates_for_event()
                                              │
                                              ▼
                                  telemetry_incidents (INSERT/UPDATE)
                                              │
                           ┌───────────────────┼───────────────────┐
                           ▼                   ▼                   ▼
                   notification_queue    match_knowledge_*    CC views
```

- **Signal:** `signal = COALESCE(source, hresult, supabase_code, http_status::text, exception_type, '-')`.
- **Поріг:** per-component з `incident_policy`: failure_pct + sample-guard.
- **Severity:** Critical (≥10 affected OR failure_pct>50%) / Warning.
- **Lifecycle:** `Detected → Confirmed (≥15 хв) → Monitoring → Resolved (≥30 хв) → Closed`. Усі переходи через `transition_incident()` → UPDATE + INSERT в `incident_status_log` (immutable).

## 6.2. Release Health

- `control_center.release_health` VIEW — ✅ жива.
- `control_center.release_health_detail` VIEW — ✅ жива (top_fingerprint).
- `control_center.component_health`, `platform_stats` — ✅ живі.

---

# 7. Authentication

> Деталі: `SCLOCVerse/docs/Auth-StateMachine.md`, `PRIVACY.md`.

- **Обов'язкова Discord-авторизація.** Без неї користувач не потрапляє в Main UI.
- **Єдине джерело істини:** enum `AuthState = { Unknown, Checking, SignedOut, SigningIn, SignedIn, Error }` через `IAuthStatusProvider.State` + `StatusChanged`. **Без прапорців** `IsAuthenticated`.
- 2 режими: Auth Gate Mode / Main UI Mode (перемикання лише за `AuthState`).

## 7.1. Провайдер

Supabase GoTrue + Discord OAuth, **PKCE**, scope `identify` only. Discord `client_id=1519140665940770946`. Redirect: Supabase-проксі + локальний loopback.

## 7.2. Redirect-механізм

**Loopback Redirect** (ADR-001): `LoopbackCallbackListener` через `HttpListener` на `http://127.0.0.1:<випадковий порт>/auth/callback`. Supabase allow-list `http://localhost:*/auth/callback`. Custom Protocol Handler відхилено.

## 7.3. Сесія

`SecureSessionStorage`, файл `.auth` у `%LocalAppData%\SCLOCVerse`, **DPAPI CurrentUser**. SignOut = global token revoke. `access_denied` → SignedOut без діалогу помилки.

## 7.4. Відомий борг

- OAuth provider **hardcoded to Discord** (`AuthService.cs:69-76`).
- OAuth `state` **не валідується** (PKCE-only) — SEC-8.
- `AuthService.State`/`Profile` non-atomic read-modify-write з background thread — TD-30.
- `DiscordGuildSyncService` — dead (`SyncGuildsAsync` never called).

---

# 8. Localization

> Деталі: `LocalizationInstaller.cs`, `EnvironmentSelector.xaml.cs`, `FolderSearchService.cs`.

## 8.1. Компоненти

| Компонент | Файл | Призначення |
|---|---|---|
| `LocalizationInstaller` | `Services/LocalizationServices/LocalizationInstaller.cs` | Install/Update `global.ini` з GitHub releases (ETag-умовний download) |
| `EnvironmentSelector` | `Controls/EnvironmentSelector.xaml.cs` | UI вибір середовища: `LIVE`/`PTU`/`EPTU`/`HOTFIX` |
| `FolderSearchService` | `Services/Common/FolderSearchService.cs` | Валідація `StarCitizen` root |
| `GitHubReleaseClient` | `Services/.../GitHubReleaseClient.cs` | Fetch релізів локалізації |

## 8.2. Дані

- Зберігається у `%LocalAppDATA%\SCLOCVerse\<envName>.meta.json` (per-environment).
- **`tagName` (semver версія локалізації) НЕ зберігається** в meta.json — лише `assetId/etag/sha256`. Відомо лише під час Install/Update.
- Після перезапуску програма не знає встановленої версії локалізації (план: `IInstallationContextProvider`).

## 8.3. Game Folder

Джерело: `Settings.Default.GameFolder` через `ISettingsService.GetGameFolder()`. Валідація `FolderSearchService.IsValidGameRoot`: лише папка `StarCitizen` з підтримуваною підпапкою середовища. **PII-ризик відсутній** — шлях не містить імені користувача.

---

# 9. L.I.A.

> Деталі: `docs/LIA_INSTALLATION.md`.

Голосовий асистент, MSIX/AppX-пакет; автор — AlexLiberty (Alexuß). Встановлюється через `Add-AppxPackage` (PowerShell). Оркеструє оновлення `Updater` (інтерфейс `IUpdater`).

## 9.1. Сертифікат (self-signed)

`CN=Alexuß`, thumbprint `33DD2416B9CC3DA94A84A479AD63D07C4B322833`. Імпорт у **`Cert:\LocalMachine\Root` ТА `Cert:\LocalMachine\TrustedPeople`** через `Import-Certificate`. `CurrentUser` відкинуто.

## 9.2. Elevation

Лише під час Install L.I.A. через `Process.Start` з `Verb="runas"` (принцип найменших привілеїв). **НЕ** `requireAdministrator` app.manifest. Контракт `PowerShellResult = (int ExitCode, string Output, string Error)`.

## 9.3. Forensic-контракт

Маркер `##SCLOC_FORENSIC##` + JSON у stdout (hresult, phase, message, activityId, appxLog, cert context). Парсинг через `LiaForensicParser.TryParse` → `LiaInstallException` → `ErrorContextExtractor.ApplyLiaForensic` → `TelemetryContext.Detail`.

## 9.4. Відкриті ризики

| # | Ризик |
|---|---|
| SEC-2 | Інсталятор без integrity check (тільки розмір) + elevation |
| SEC-3 | Довільний `.cer` → `LocalMachine\Root`+`TrustedPeople` **без pin** |
| TD-10 | PowerShell без timeout (`ct=None` з UI) |
| L-A6 | Orphaned elevated PowerShell-процес при cancellation |

---

# 10. Security

> Деталі: `PRIVACY.md`, `SCLOCVerse/docs/Privacy-Design.md`, `CODE_SIGNING_POLICY.md`.

## 10.1. PII-політика (мінімізація)

- **Identity Layer:** `discord_user_id`, `username`/`global_name`, `avatar_url`.
- **Technical Metadata:** `install_id`, `machine_id`, `platform`, `os_version`, `app_version`, UTC-мітки.
- **НЕ збираються:** email, паролі, платіжні, адреси, біометрія, guild list, поведінкова телеметрія.
- `EXTERNAL_DISCORD_EMAIL_OPTIONAL` увімкнено.

## 10.2. OAuth-захист

PKCE, HTML-encoding на callback, SignOut = global revoke, **без `service_role` у клієнті**, SQL parameterized, PowerShell single-quote escaping.

## 10.3. Code signing (supply chain)

**SignPath.io** + сертифікат **SignPath Foundation**. Підписуються `SCLOCVerse.exe` та `SCLOC-Verse_Setup.exe` у верифікованому автоматичному білді з вихідного коду GitHub. Без DLL injection / зміни пам'яті / античиту.

## 10.4. Відкриті прогалини цілісності

| # | Ризик | Severity |
|---|---|---|
| SEC-1 | Control Center без auth (`Program.cs`, 0 `[Authorize]`) | 🔴 CRITICAL |
| SEC-2 | L.I.A. інсталятор без integrity check + elevation | 🔴 CRITICAL |
| SEC-3 | Довільний `.cer` → `LocalMachine\Root`+`TrustedPeople` без pin | 🔴 CRITICAL |
| SEC-4 | Checksum-bypass при порожньому checksum (`Verify.Skipped` замість `Verify.Failed`) | 🔴 CRITICAL |
| SEC-5/6/7 | `MachineName`/`Detail` не sanitized; `PrivacySanitizer` regex занадто вузький | 🟠 |
| SEC-8 | OAuth `state` не валідується (PKCE-only) | 🟠 |
| SEC-9 | TOCTOU verify→install | 🟠 |
| SEC-10 | SHA256 = byte-equality, не Authenticode publisher identity | 🟠 |

> ✅ **SEC-11 + SEC-12 COMPLETED** (2026-07-14): усі 30 SECURITY DEFINER функцій мають `SET search_path`; `REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA public FROM PUBLIC`.

---

# 11. Control Center

> Деталі: `docs/contracts/control_center.md`.

Blazor Server. 6 сторінок: `Home.razor`, `Incidents.razor`, `Traces.razor`, `Releases.razor`, `Knowledge.razor`, `Settings.razor` (placeholder).
2 репозиторії: `ControlCenterRepository` (Npgsql + `control_center` схему + SECURITY DEFINER функції), `TraceRepository`.
Сервіс: `PiiSanitizer`.

## 11.1. Колонки, що реально використовуються по сторінках

| Сторінка | Джерело | Критичні колонки |
|---|---|---|
| `Home` | `observability_health`, `component_health`, `release_health`, `platform_stats`, `knowledge_coverage`, `top_missing_knowledge` | `active_installations_last_7d`, `health`, `active_incidents`, `app_version`, `success_rate`, `failed`, `events_24h`, `active_users_24h`, `open_incidents`, `coverage_pct` |
| `Incidents` | `incidents`, `incident_timeline`, `incident_notes_view`, knowledge функції | `incident_id`, `status`, `highest_severity`, `component`, `signal`, `event_count`, `affected_users`, `peak_failure_pct`, `opened_at`, `last_event_at`, `owner` |
| `Traces` | `telemetry_events` | `correlation_id`, `session_id`, `step`, `install_id`, `occurred_at`, `component`, `operation`, `outcome`, `severity`, `error_message`, `duration_ms`, `detail` |
| `Releases` | `release_health_detail`, `knowledge_coverage` | `app_version`, `success_rate`, `succeeded`, `failed`, `active_installs`, `new_incidents`, `critical_incidents`, `top_fingerprint` |
| `Knowledge` | `knowledge_coverage`, `top_missing_knowledge`, `knowledge_list` | `coverage_pct`, `component`, `signal`, `title`, `status`, `confidence`, `fixed_version` |

## 11.2. Контракт

- Supabase = SSOT; read-only View-контракт `control_center`; versioning через `contract_info`.
- `cc_readonly` — `USAGE`+`SELECT` лише на `control_center` + EXECUTE на workflow/knowledge функції.
- Бізнес-логіка в SQL VIEWs/функціях, не в Blazor.

---

# 12. Notification System

> Деталі: `docs/observability/Observability-RC1-Release.md`.

## 12.1. Архітектура

Incident Engine НЕ відправляє повідомлення напряму (Стаття 24). Пише в `notification_queue` → `SCLOCVerse.Notifier` Worker → `INotificationProvider` (контракт у `SCLOCVerse.Notifications`) → `DiscordNotificationProvider`.

## 12.2. `notification_queue` (16 колонок)

Стани: `Pending → Sending → Delivered | RetryScheduled | Failed`. Zombie Recovery `Sending>10хв → RetryScheduled`. `UNIQUE(incident_id, notification_type) WHERE status != 'Failed'`. Worker poll 30с, `FOR UPDATE SKIP LOCKED`.

## 12.3. Retry/backoff

`next_attempt_at = now + 2^attemptNo секунд`. `max_retries` default 3.

## 12.4. Семантика `error_message` vs `last_error`

НЕ дубль (різна семантика):
- `error_message` — фінал черги (після `max_retries`, де-факто deprecated з міграції 00017, ніхто не пише).
- `last_error` — остання спроба retry (Notifier оновлює кожен retry).

---

# 13. Knowledge Engine

> Деталі: `docs/observability/Knowledge-Engine-Design.md`. **Phase 6 завершено**, **API Freeze v1.0**.

## 13.1. Таблиці

1. **`knowledge_entries`** (19 колонок): `FingerprintKey`/`FingerprintHash`, `Title`/`Symptoms`/`KnownCause`/`Workaround`/`PermanentFix`, `AffectedVersions text[]`, `FixedVersion`, `Confidence`/`Status` enums.
2. **`knowledge_references`** (5 колонок): 1:N, `ReferenceType ∈ {GitCommit, GitHubIssue, Documentation, ReleaseNotes, External}`, `ON DELETE RESTRICT`.
3. **`knowledge_version_history`** (9 колонок): append-only audit, dual-identity (`ChangedBy` клієнт + `db_user` БД).

## 13.2. Інваріант (CHECK)

- `status='Verified' → confidence ∈ {High, Verified}`.
- `status ∈ {Draft, Reviewed} → confidence ∈ {Low, Medium, High}`.
- `status ∈ {Deprecated, Archived}` — будь-яка.

## 13.3. Workflow

`Draft → Reviewed → Verified → Deprecated → Archived`.
- Publish, Verify (вимагає Confidence ≥ High), ReturnForRevision, Deprecate (обов'язковий ChangeReason), Reopen, Archive (фінальний).
- ❌ `Archived → *` заборонено.
- Усі переходи через `transition_knowledge()` (SECURITY DEFINER, optimistic concurrency через `expected_version`).

## 13.4. Confidence та Auto-verify

`Low → Medium → High → Verified`.
- Medium → High: додано `PermanentFix` + ≥1 `GitCommit` reference.
- High → Verified: **автоматично** через `verify_knowledge_auto()` (5 умов: FixedVersion IS NOT NULL, реліз з install_count ≥ 50 за 14 днів, success_rate ≥ 0.95, 0 інцидентів з тим fingerprint_key з FixedVersion, Confidence = High).

## 13.5. Matching (3 рівні)

- **Priority 1 (Exact Fingerprint):** `fingerprint_hash` + `status='Verified'` LIMIT 1.
- **Priority 2 (Component + Signal):** Verified + AffectedVersions match + FixedVersion > release; ORDER BY confidence DESC, updated_at DESC.
- **Priority 3 (Manual):** оператор через `search_knowledge`.

## 13.6. API Freeze v1.0 (Phase 7 межі)

- **Дозволено:** нові таблиці (embeddings, ai_suggestions), нові функції, нові VIEW, адитивні nullable-колонки з DEFAULT, окремі extensions.
- **Заборонено:** `ALTER TABLE NOT NULL`, зміна сигнатур існуючих функцій, DROP функцій/VIEW, зміна CHECK-інваріантів, зміна workflow-правил/алгоритму matching/auto-verify.

---

# 14. Approved Decisions (поточні архітектурні рішення)

> Лише поточні рішення. Історія прийняття — у forensic-документах та git.

## 14.1. Архітектура / DI / Проєкт

1. Ручний Composition Root (`AppCompositionRoot` + `AuthCompositionRoot`), без IoC.
2. 4 процесоізольовані проєкти; спілкування лише через Postgres.
3. Без `Microsoft.Extensions.Hosting` (Generic Host); lifetime через WPF Application.
4. `MainWindow` як UI-координатор (code-behind); бізнес-логіка в сервісах/presenter-ах.
5. Overlay як окремі вікна + `HangarOverlayService` (Win32 click-through).
6. Canvas-навігація через `CanvasManager` у межах одного `MainWindow`.
7. 2 бекенди гарячих клавіш; `RawInputBackend` — default; env `SCLOCVERSE_HOTKEY_BACKEND`.
8. .NET 9, Nullable Enabled; код англійською; коментарі/документація українською.
9. UTF-8 як P0 для всіх текстових файлів; явний `Encoding.UTF8`.
10. Zero Regression — стабільний код не чіпати без потреби.

## 14.2. Auth / Privacy / Supply Chain

11. Loopback Redirect для OAuth (allow-list `http://localhost:*/auth/callback`).
12. Обов'язкова Discord-авторизація; єдиний `AuthState` (6 станів).
13. OAuth scope лише `identify`; мінімізація даних.
14. DPAPI CurrentUser для локальних сесійних токенів.
15. RLS everywhere; least-privilege `cc_readonly`.
16. Supabase = SSOT; read-only View-контракт `control_center`.
17. Code signing через SignPath.io + SignPath Foundation.
18. `EXTERNAL_DISCORD_EMAIL_OPTIONAL`; email не зберігається.
19. `access_denied` → SignedOut без діалогу помилки.

## 14.3. L.I.A.

20. Cert → `LocalMachine\Root` + `LocalMachine\TrustedPeople`.
21. Elevation лише під час Install через `Verb="runas"` (не `requireAdministrator`).
22. Elevated-транспорт через `wrapper.ps1` + UTF-8-no-BOM файли.
23. Forensic-контракт `##SCLOC_FORENSIC##` + JSON; `LiaForensicParser.TryParse`.

## 14.4. Observability / Telemetry / Інциденти

24. `ITelemetryService` — єдиний санкціонований канал (Стаття 7/12).
25. Additive-only контракт схеми (Стаття 13).
26. Append-only `telemetry_events` з RLS authenticated-INSERT-only (Стаття 5).
27. `promote_incident_candidates()` SECURITY DEFINER, лише SELECT events + INSERT incidents.
28. Dashboard Purity: `cc_readonly` лише SELECT VIEWs (Стаття 20).
29. Workflow через SECURITY DEFINER функції + GRANT EXECUTE.
30. `INotificationProvider` контракт у окремій бібліотеці.
31. Knowledge Engine виключно ручного походження (Стаття 28).
32. Three-tier secret model Git/DB/App, P0 для порушень (Стаття 29).
33. `install_id` (file+registry) як machine identity; ніколи `MachineName`.
34. Bounded in-memory черга (cap 5000, drop-oldest).
35. Інцидент = `incident_id`, не fingerprint; рецидив = новий інцидент (Стаття 21).
36. Per-component пороги в `incident_policy`. Анти-флап ≥15 хв (Confirmed); ≥30 хв (Resolved).
37. `UNIQUE(incident_id, notification_type) WHERE status != 'Failed'`.
38. Zombie Recovery `Sending>10хв → RetryScheduled`. Worker `FOR UPDATE SKIP LOCKED`.
39. `cc_notifier` роль (least privilege).

## 14.5. Telemetry Policy (Phase 3.5)

40. **Zero Noise Policy:** L1 Mandatory = **тільки Failed події**. Усі non-Failed (Started, Succeeded, Cancelled, UpdateFound) → L2 Diagnostic. При `AdvancedDiagnostics=false` успішний запуск → 0 L1 подій.
41. **L2 Diagnostic** — лише при `AdvancedDiagnostics` ON. L3 Local Only — ніколи.
42. **`TelemetryLevel` ≠ `Category`** — дві різні осі (коли відправляти / що за подія).
43. **Єдина точка прийняття рішення** — `TelemetryClient.Track()`. Gate null не блокує Mandatory.
44. **Outcome-dependent Field Policy:** L1 Failed несе `error_message`, `source`, `hresult`, `exception_type` завжди; L1 Success — 16 полів без error-context; L2 — додаткові `duration_ms`, `detail.*`.
45. **3-шарова захист** від Failed-without-signal: Layer 1 (ErrorContextExtractor.Create на 13 шляхів) + Layer 2 (BuildEvent валідація) + Layer 3 (Uploader binary split + poison eviction + 401 drop).

## 14.6. Knowledge Engine

46. Human Verified; автогенерація заборонена.
47. Append-Only History; DELETE заборонено RLS.
48. CHECK-інваріант Status ↔ Confidence на рівні схеми.
49. Materialized VIEW `knowledge_coverage` з REFRESH.
50. Auto-verify `verify_knowledge_auto()` (5 умов).
51. API Freeze v1.0.

## 14.7. Settings Hub (ADR-009)

52. Hub замінює `SettingsCanvas`: ліва панель-навігатор + права панель вмісту (Варіант B).
53. Категорії за функцією, не за інструментом: `Загальне`, `Гарячі клавіші`, `Overlay`.
54. Контент лише для реалізованого функціоналу (no fabricated fields).
55. Миттєве збереження без Apply; мітка «•». Reset per-category + per-control (↺). **Без глобального «Скинути все».**
56. `F1` відкриває Hub лише при активному вікні SCLOC-Verse.
57. Шлях до гри = `...\StarCitizen`; середовища — read-only індикатори.
58. Reuse First — без нових сервісів у Phase 0.

## 14.8. Anti-AFK / Auto Key / Foreground Gate

59. **Anti-AFK:** `GetLastInputInfo` замість глобальних hooks (privacy-safe). `AntiAfkService` + `AntiAfkIndicatorWindow` (5 позицій, 2 анімації). Хоткей `End`. Налаштування через `IPreferencesService`.
60. **Auto Key:** stateless foreground-гейт за PID процесу (`StarCitizen.exe`). `AutoKeyService` + `AutoKeyIndicatorWindow` (3 стани: Off/Running/Paused). Хоткей `Home`. Action Key (default `[`), інтервал 100–2000мс.
61. **StarCitizenForeground** — спільний helper `IsStarCitizenForeground()`. Stateless: перевірка за PID процесу, не за Caption. `OpenProcess==NULL` → тихо PAUSED (без SendInput).
62. **Динамічні підказки SSOT:** підказки гарячих клавіш читають `IHotkeyService.GetDefinitions()` (то ж джерело, що й Settings Hub). Спільний `HotkeyGestureFormat.Format()`.

## 14.9. Overlay

63. **`HangarOverlayMode`** enum: `Classic=0` (default) / `Simplified=1` (compact badge). `IHangarOverlayWindow` абстракція. `HangarOverlayService.ApplyOverlayMode()` перемикає.
64. **Live-preview + bidirectional sync:** слайдер пише в settings + state; хоткеї пишуть в state; `state.PropertyChanged` синхронізує UI. Drag оверлея → `PositionChanged` → поля X/Y.

---

# 15. Rejected Decisions

> Щоб більше ніхто не пропонував.

## 15.1. Архітектура / Observability / Методології

1. Сторонні IoC-контейнери. MVVM-фреймворки. `Microsoft.Extensions.Hosting`. `RegisterHotKey` як default.
2. Custom Protocol Handler (`sclocverse://`) для OAuth. `requireAdministrator` app.manifest. `CurrentUser` store для cert L.I.A.
3. Повне очищення `app_installations` / `auth.users`. Поведінкова телеметрія. Глобальний рефакторинг.
4. ILogger / `Microsoft.Extensions.Logging`. AppInsights / Sentry / OpenTelemetry. Ad-hoc логери. Прямі вставки в БД поза `ITelemetryService`.
5. Токени/JWT/email/IP/шляхи/MachineName/MAC у payload. Будь-який секрет у репозиторії.
6. Зміна семантики колонок, NOT NULL без backfill. DROP COLUMN `telemetry_events.country`. Нормалізація signal → signal_id.
7. DROP порожніх таблиць (`error_reports`, `admin_audit_log`, `user_discord_guilds`). `UPDATE` на `telemetry_events`.
8. Пряме UPDATE `telemetry_incidents.status` мимо функції. Прямі writes в `incident_status_log`/`incident_notes`.
9. `cc_readonly` INSERT/UPDATE/DELETE напряму. Автогенерація знань з телеметрії/LLM. Knowledge Engine як джерело правди.
10. Семантичний/LLM matching у v1. Роль `cc_knowledge_editor`. Об'єднати Status+Confidence. Confidence як похідне від Status.
11. Phase 7: LLM-розширення. embeddings (pgvector). Auto-Draft. DROP FUNCTION / ALTER NOT NULL / зміна сигнатур.
12. Migration squash (втратить аудит причин).
13. **MERGE `notification_queue.error_message` ↔ `last_error`** — різна семантика.
14. **REMOVE `detail.retry_count`** — заготовка під Retry Policy, не мертва.
15. **Signal normalization** (вигода 16 MB/1M не виправдовує перепис 6 views + 33 CC-запитів).
16. **`detail.signal_name` дублює computed signal** — у живих даних ключ `signal_name` не існує (0 зустрічей).
17. **Зовнішні CLI** (OpenSpec CLI, Specify CLI, BMAD CLI) — лише Markdown.
18. **Паралельні структури** `openspec/`, `.specify/`, `.bmad/`, окремі `docs/specs/`.
19. **Глобальна кнопка «Скинути все»** в Settings Hub. Категоризація за інструментом.
20. Передчасні категорії з вигаданими полями. `F1` як глобальна гаряча клавіша.

---

# 16. Known Technical Debt

> Деталі: `docs/architecture/Final-Architecture-Review.md` (TD-1…TD-60, ZR-1…ZR-24).

## 16.1. P0 — Security (4)

| ID | Опис |
|---|---|
| SEC-1 | Control Center без auth (`Program.cs`) |
| SEC-2 | L.I.A. cert без pin → `LocalMachine\Root`+`TrustedPeople` + integrity check |
| SEC-3 | Довільний `.cer` без pin |
| SEC-4 | Checksum-bypass при порожньому checksum (`Verify.Skipped` замість `Verify.Failed`) |

## 16.2. Forensic-знахідки (відкриті)

| ID | Опис |
|---|---|
| F3 | Відсутній global `UnhandledException` handler — WPF-краш минає спостережуваність |
| F4 | `PrivacySanitizer` покриває лише `ErrorMessage`; `Detail` неочищений |
| F5 | Terminal `FlushAsync` пропущено у 5 Failed-емітерів ApplicationUpdate |
| F8 | `telemetry_events.country` — мертва (тригер лише на installations) |
| — | LIA cascade: 1 фізична відмова → 3-4 Failed-події |
| — | `Localization.*` телеметрія відсутня (сліпа зона встановлення локалізації) |

## 16.3. Додатковий борг

- `MainWindow` God Class (~903 рядки, ~20 ctor-параметрів, ~31 field) — TD-6.
- Жодного app-wide `CancellationTokenSource`.
- OAuth `state` не валідується (SEC-8 / TD-36).
- `AuthService.State`/`Profile` non-atomic з background thread (R-5 / TD-30).
- `DiscordGuildSyncService` — dead код.
- PowerShell без timeout у L.I.A. (TD-10).
- Orphaned elevated PowerShell-процес при cancellation (L-A6).
- 6 з 19 колонок `app_installations` завжди NULL/DEFAULT.
- `category` завжди `'Operational'` (FUTURE: Critical/Diagnostic/Analytics диференціація).
- `telemetry_events.http_status` + `supabase_code` — 100% NULL (DEPRECATED, але CHECK вимагає колонку).
- `promote_incident_candidates()` (batch) — мертвий (CREATE 00013, не DROP). DEFER (API Freeze).
- `ecosystem_stats()` — мертва (`.Rpc(` в C# не знайдено).
- DEFAULT PRIVILEGES Drift: production manually hardened, не в міграціях (RLS однаково блокує).
- enum `telemetry_incidents.status` (CHECK) не містить `Mitigated`/`Acknowledged` (F2).

---

# 17. Backlog

> Лише підтверджені задачі. Не вигадувати нових без перевірки.

## 17.1. Підтверджені задачі

| Задача | Складність |
|---|---|
| Активація `localization_version` + `game_folder_path` + `selected_environment` через `IInstallationContextProvider` | низька–середня |
| Фікс `CERT_E_UNTRUSTEDROOT` (0x800B0109) у L.I.A. | середня |
| Enrichment deployment HRESULT з message у L.I.A. | низька |
| Cleanup cert trust chain L.I.A. (orphaned root cert) | середня |
| Global `UnhandledException` handler (F3) | низька |
| Розширити `PrivacySanitizer` на `Detail` (F4) | низька |
| Terminal `FlushAsync` у 5 ApplicationUpdate Failed (F5) | низька |
| Розширити enum `telemetry_incidents.status` (F2) | низька |
| Видалити мертвий `LiaForensicParser.TryParseMinimal` (F7) | низька |
| MSBuild target для `git_commit` у BuildInfo | низька |
| Phase 1: скоротити LIA chain 9→2, app-update 6→1 | середня |
| Retry Policy architectural decision (`detail.retry_count`) | середня |
| Control Center auth (SEC-1) | середня |
| Cert pin L.I.A. (SEC-3) | середня |
| L.I.A. installer integrity check (SEC-2) | середня |
| Checksum-fail замість Verify.Skipped (SEC-4) | низька |
| Phase 3: single-scan candidates + матеріалізувати 3 views | середня |
| Phase 5.2+: Retention для notification_queue (30d), notification_attempts (90d), incidents (1y) | низька |
| Phase 4: Data Presentation Layer — `ITimeZoneService` + `IUserDateTimeFormatter` + Blazor presentation | середня |
| DEFAULT PRIVILEGES hardening у міграції (Phase 3.7 backlog) | низька |
| `error_message` semantic activation (варіант b з §12.6) — Notifier пише лише при фінальному Failed | низька |

## 17.2. Roadmap (Роки 1–3)

- **Рік 1 (стабілізація):** P0-борг, оптимізація БД, app_installations, чекбокс.
- **Рік 2 (масштабування):** config-driven OAuth, Email/Telegram providers, Edge Function ingest, CC auth + Supabase Auth, rollup tables/MATVIEW, партиціювання при >50M рядків/рік.
- **Рік 3 (еволюція):** `ITelemetryBackend`, plugin-system notification, localization integrity, Authenticode enforcement, multi-tenant CC.

---

# 18. Cross References

## 18.1. Дочірні документи KB (deep details)

| Документ | Що містить |
|---|---|
| [`docs/database/Data-Model.md`](database/Data-Model.md) | Повна схема БД: 15 таблиць по колонках, RLS, triggers, lifetime, Phase 2 Cleanup Review |
| [`docs/observability/Telemetry-Registry.md`](observability/Telemetry-Registry.md) | Telemetry Policy (3 рівні), Event Registry (39 емітерів), Field Registry, Reader Validation, Test Matrix |
| [`docs/release/Release-History.md`](release/Release-History.md) | Журнал релізів v1.0.0.0 → v1.0.2.4, production incidents resolution log |

## 18.2. Інші документи (що підтверджує що)

| Документ | Що підтверджує |
|---|---|
| `AGENTS.md` | Конституція проєкту (Zero Regression, UTF-8 P0, українські commits, additive-only, структура, методологія-пікер) |
| `ARCHITECTURE_DECISIONS.md` | ADR-001…009 (DI, hotkeys, MainWindow, overlay, canvas, lifetime, ISP, Settings Hub) |
| `docs/architecture/Final-Architecture-Review.md` | Повна архітектура, TD-1…60, ZR-1…24, SEC-1…12 |
| `docs/contracts/control_center.md` | Контракт Control Center, role `cc_readonly` |
| `docs/LIA_INSTALLATION.md` | L.I.A. installer/cert/elevation/forensic |
| `docs/checklists/Quality-Gates.md` | Core + Extended Quality Gates |
| `docs/checklists/Security-Review.md` | Security Review за доменами (OAuth/Auth/Installer/Network/SQL/RLS/Crypto) |
| `docs/checklists/Database-Verification.md` | Чеклист RLS/таблиць (Стаття 17) |
| `docs/backlog/README.md` | Конвенція деталізації великих задач |
| `docs/backlog/settings-hub.md` | Settings Hub — повна специфікація (AC, варіанти A/B/C). ADR-009 |
| `docs/backlog/settings-hub-design-system.md` | Settings Hub — дизайн-система (P0 Identity First + P1–P5) |
| `docs/backlog/hotkey-editor.md` | Phase 0.5 — редактор гарячих клавіш |
| `docs/release/release-runbook-1.0.0.1.md` | Ранбук, cleanup-класифікація |
| `docs/release/post-cleanup-forensic-app-installations.md` | Актуальна cleanup-політика (B+C) |
| `PRIVACY.md` + `SCLOCVerse/docs/Privacy-Design.md` | PII-політика, scopes |
| `SCLOCVerse/docs/Auth-StateMachine.md` | Auth state machine |
| `CODE_SIGNING_POLICY.md` | SignPath code signing |
| `docs/observability/Observability-Constitution.md` | 29 статей |
| `docs/observability/Observability-Architecture.md` | Цільова архітектура телеметрії |
| `docs/observability/Observability-RC1-Release.md` | Incident/Notification engine |
| `docs/observability/Knowledge-Engine-Design.md` | Knowledge Engine |
| `docs/observability/app-installations-forensic-2026-07-05.md` | Колонки app_installations |
| `docs/observability/app-installations-implementation-plan.md` | План `IInstallationContextProvider` (v2) |
| `docs/observability/FORENSIC-DATA-PIPELINE-RAW.md` | Сирі SQL/C# факти |
| `docs/observability/FORENSIC-DATA-PIPELINE-DETAIL.md` | Повні CREATE/ALTER + всі `.Track()` з рядками |
| `docs/observability/Observability-Forensic-Audit-1.0.0.1.md` | Forensic аудит |
| `docs/backlog/rc-401-sdk-refresh-bug.md` | RC-401 SDK root cause analysis |
| `docs/backlog/ocr-platform.md`, `ocr-hardening-sprint.md`, `ocr-profiling-report.md`, `ocr-ab-test-report.md` | OCR платформа |

## 18.3. Карта залежностей розділів

```
1 Executive Summary
   ├─ 2 Архітектура ──── 7 Auth ──── 8 Localization ──── 9 L.I.A.
   ├─ 3 Database ─────── 4 app_installations → [Data-Model.md]
   ├─ 5 Telemetry → [Telemetry-Registry.md] ── 6 Observability ─┬─ 11 Control Center
   │                                                             ├─ 12 Notification System
   │                                                             └─ 13 Knowledge Engine
   ├─ 10 Security (cross-cutting)
   ├─ 14 Approved ←── 15 Rejected (перевіряти перед пропозиціями)
   ├─ 16 Technical Debt ── 17 Backlog
   └─ 18 Cross References (покажчик на дочірні документи)
```
