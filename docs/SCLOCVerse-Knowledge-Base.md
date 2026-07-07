# SCLOC-Verse — Unified Knowledge Base

> **Single Source of Truth.** Цей документ — єдина точка входу для будь-якого AI-агента.
> Якщо інформація тут є — не перечитуй десятки forensic-документів.
> Якщо інформація тут суперечить сирому документу — сирий документ має пріоритет, але повідом про розбіжність (розділ 18.3).
>
> **Версія застосунку:** 1.0.0.1 (Observability Release, RC)
> **Supabase project:** `nrytczdbhehiotflaagl` (eu-west-1)
> **Живий документ:** постійно оновлюється при розвитку системи. Дозволено додавати, оновлювати, видаляти та переносити дані між розділами. Заборонено лише дублювання інформації та створення нових документів для вже описаних підсистем (див. AGENTS.md, «Knowledge Base — живий документ»).

---

## 0. Як користуватися цією базою знань

1. **Пошук по розділах** — розділи 1–18 покривають усю систему.
2. **Розділи 14 (Approved) та 15 (Rejected)** — першочергова перевірка перед тим, як пропонувати рішення. Більшість «очевидних» ідей уже прийнято або відхилено.
3. **Розділ 16 (Technical Debt) та 17 (Backlog)** — що вже відомо як борг і що заплановано. Не вигадувати нових задач, не перевіривши їх.
4. **Розділ 18 (Cross References)** — покажчик «який сирцевий документ підтверджує який факт».
5. **Джерельні forensic** — відкривати лише для верифікації першоджерела. Список у розділі 18.1.

> ⚠ **Конституція проєкту (AGENTS.md) має найвищий пріоритет** над цим документом у випадку конфлікту. Далі — Observability-Constitution, потім ця KB, потім окремі forensic.

---

# 1. Executive Summary

**SCLOC-Verse** — настільний WPF-клієнт (.NET 9) української локалізації Star Citizen з навісною observability-платформою (Supabase + Blazor Control Center + Worker Notifier + Knowledge Engine).

### 1.1. Підсистеми

| Підсистема | Призначення | Статус |
|---|---|---|
| **SCLOCVerse** (WPF) | Клієнт: локалізація, оновлення гри/L.I.A., hangar-timer, hotkeys, tray | ✅ RC |
| **Control Center** (Blazor Server) | Дашборд операційної команди: інциденти, трейси, release health, knowledge base | ✅ RC |
| **Notifier** (Worker) | Доставка сповіщень про інциденти (Discord) | ✅ RC |
| **Observability pipeline** (Supabase) | telemetry → incidents → notifications → knowledge | ✅ RC |
| **L.I.A.** | Голосовий асистент (MSIX/AppX) стороннього автора AlexLiberty — оркеструється клієнтом | ✅ (із відкладеними ризиками) |
| **Knowledge Engine** | Ручна база знань з auto-verify | ✅ Phase 6 завершено (API Freeze v1.0) |

### 1.2. Ключові факти одним рядком

- **4 процесоізольовані** проєкти монорепо; спілкуються **тільки через Postgres/Supabase** (2 схеми, 26 міграцій, ~24 SECURITY DEFINER функції).
- **Ручна композиція залежностей** (без IoC-контейнерів, без Generic Host).
- **Discord OAuth + Supabase GoTrue** (PKCE, scope `identify` only) — обов'язкова авторизація.
- **RLS скрізь**: `anon` deny-all, `authenticated` owner-only, `telemetry_events` append-only, `cc_readonly`/`cc_notifier` least-privilege.
- **Additive-only контракт** схеми (Стаття 13 Конституції Observability). DROP COLUMN заборонено.
- **Zero Regression** — стабільний код не чіпати без потреби (AGENTS.md).
- **PII-мінімізація**: збираються лише discord_user_id/username/avatar + технічні метадані; email, паролі, поведінкова телеметрія — ніколи.
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
- **Two-phase init** для розриву циклу auth↔telemetry: `TelemetryClient` конструюється ДО `AuthCompositionRoot` (без Supabase-клієнта), потім доін'єктується через `SetInstallId` + `AttachClientFactory` (`AppCompositionRoot.cs:78, 145-146`).
- **Без IoC-контейнерів** (ADR-001 ARCHITECTURE_DECISIONS). **Без Generic Host** (ADR-007).
- Структура папок: `Services/<Feature>/`, `Interfaces/`, `Models/`, `Controls/`; нові залежності — через `AppCompositionRoot`.

## 2.3. Життєвий цикл

`App.OnExit → AppCompositionRoot.Dispose()` → каскад reverse-order:
`TelemetryClient.Dispose` (Timer stop + best-effort flush + Uploader dispose) → `BackgroundUpdateMonitor.Dispose` → `HangarOverlayService.Dispose` → `HangarTimerService.Dispose` → `AuthCompositionRoot.Dispose` (`AuthService.Shutdown` + `ClientFactory.Shutdown`).

- **Жодного app-wide `CancellationTokenSource`.** Кожен сервіс зупиняє власні таймери; in-flight async не скасовується централізовано.
- Таймери: telemetry flush **30с**; background update **30 хв**; overlay countdown **200мс**; hangar card **250мс**; home smooth scroll **16мс**.

## 2.4. UI-координація

- `MainWindow` — WPF code-behind-координатор (ADR-004); бізнес-логіка в сервісах і presenter-ах. **Проте ~903 рядки, ~20 ctor-параметрів, ~31 field → God Class** (TD-6).
- Canvas-навігація через `CanvasManager` (ADR-006) — перемикає видимість Canvas-ів у межах одного `MainWindow`.
- Overlay — окреме вікно `HangarOverlayWindow` + `HangarOverlayService` (Win32 `WS_EX_TRANSPARENT`/`WS_EX_LAYERED`, ADR-005).

## 2.5. Гарячі клавіші

`IHotkeyBackend` + 2 реалізації: `RawInputBackend` (default, через `RIDEV_INPUTSINK`/`WM_INPUT`, key-up) і `RegisterHotkeyBackend` (fallback). Вибір через env `SCLOCVERSE_HOTKEY_BACKEND`. `IKeyStateBackend` (ISP) виділено для `KeyUp` — реалізує лише `RawInputBackend`.

## 2.6. Бекенд-абстракції (seam-и для розширення)

| Абстракція | Призначення | Найкращий seam для розширення |
|---|---|---|
| `ITelemetryService` | Єдиний санкціонований sink спостережуваності | — |
| `INotificationProvider` | Канал доставки сповіщень | Новий канал = 1 клас + 1 DI-рядок |
| `SupabaseClientFactory` | Supabase-клієнт (singleton, lazy, double-check lock) | — |
| `IHotkeyBackend` | Бекенд гарячих клавіш | Новий бекенд = 1 клас |
| Control Center схему `control_center` | Read-only views під роллю `cc_readonly` | — |

---

# 3. Database

> Деталі: `docs/observability/FORENSIC-DATA-PIPELINE-RAW.md` (повні CREATE), `FORENSIC-DATA-PIPELINE-DETAIL.md`.

## 3.1. Об'єкти

| Тип | Кількість | Примітка |
|---|---:|---|
| Схеми (SCLOC-Verse) | 3 | `public`, `control_center`, `backup_pre_1_0_0_1` (резерв до 1.0.0.1 — див. §3.5) |
| Базові таблиці | 15 (+12 backup) | 14 у `public` + 1 singleton у `control_center` + 12 у `backup_pre_1_0_0_1` |
| Звичайні VIEW | 25 | 24 у `control_center` + `public.user_analytics` |
| Materialized VIEW | 1 | `control_center.knowledge_coverage` |
| SECURITY DEFINER функції | 28 | promotion, incident workflow, knowledge lifecycle; лише 2 з 28 мають `SET search_path` (SEC-11) |
| Triggers | 4 | geoip, failed-promote, incident-refresh, knowledge-audit |
| БД-ролі | 4 | `anon`, `authenticated`, `cc_readonly`, `cc_notifier` |
| RLS policies (`public`) | 28 | deny-all RESTRICTIVE + owner-only permissive + cc_notifier |
| pg_cron | 0 | **НЕ встановлений** — план «Retention 14д» з Observability-Architecture не виконано |

## 3.2. Таблиці (призначення)

| Таблиця | Схема | Призначення | Продюсер | Статус |
|---|---|---|---|---|
| `app_installations` | public | Метадані інсталяції клієнта | C# `InstallationService` | ✅ жива (6 колонок dead/inactive — див. розділ 4) |
| `telemetry_events` | public | Append-only події | C# `TelemetryUploader` | ✅ ядро |
| `telemetry_incidents` | public | Згруповані інциденти | trigger `tg_promote_after_failed` | ✅ жива |
| `incident_policy` | public | Пороги детекції per-component | seed + адмін | ✅ config |
| `incident_status_log` | public | Історія переходів (immutable) | `transition_incident()` | ✅ жива |
| `incident_notes` | public | Примітки до інцидентів | `add_incident_note()` | ✅ жива |
| `notification_queue` | public | Черга сповіщень | trigger (при промоції) | ✅ жива |
| `notification_attempts` | public | Аудит спроб доставки | Notifier Worker | ✅ жива |
| `knowledge_entries` | public | База знань | CC workflow функції | ✅ жива |
| `knowledge_references` | public | Посилання 1:N | CC функції | ✅ жива |
| `knowledge_version_history` | public | Append-only audit | `knowledge_audit` trigger | ✅ жива |
| `error_reports` | public | Зарезервовано | **ніхто** | 🔴 reserved/future |
| `admin_audit_log` | public | Зарезервовано | **ніхто** | 🔴 reserved/future |
| `user_discord_guilds` | public | Синхронізація гільдій | вимкнений код | 🔴 reserved |
| `pipeline_health_meta` | control_center | Singleton health | `refresh_knowledge_coverage()` | ✅ singleton |

### 3.2.1. Резервна схема `backup_pre_1_0_0_1` (поза основним контрактом)

> Знахідка Phase 2 Database Cleanup Review (2026-07-07).

Схема створена перед міграцією 1.0.0.1 як ручний backup основних таблиць. Містить 12 таблиць-дублів (структура копія `public`):

| Таблиця | Рядків | Примітка |
|---|---:|---|
| `app_installations` | 36 | дублює public (55 заг.) |
| `telemetry_events` | 18 | |
| `telemetry_incidents` | 1 | |
| `incident_notes`, `incident_policy`, `incident_status_log`, `notification_queue`, `notification_attempts` | 0 | |
| `knowledge_*` | відсутні | створені пізніше 1.0.0.1 |

**Рішення (Phase 2):** `DEFER` — дослідити походження та погодити `DROP SCHEMA backup_pre_1_0_0_1` окремою міграцією. Жодних продюсерів, 0 зовнішніх залежностей, займає місце в бекапах Supabase.

### 3.2.2. Audit міграцій (Phase 2 forensic, 2026-07-07)

26 міграцій консистентні. Pattern audit:

| Pattern | Приклад | Статус |
|---|---|---|
| `CREATE OR REPLACE FUNCTION` | `promote_incident_candidates` (00013→00016), `refresh_knowledge_coverage` (00023→05021100), `tg_promote_after_failed` (05021100→05030000), `knowledge_audit_trigger` (00019→00022), `update_knowledge_entry`/`transition_knowledge`/`add_knowledge_reference`/`remove_knowledge_reference`/`get_knowledge_history` (00021→00022) | ✅ нормальні розвиткові оновлення |
| `DROP CONSTRAINT + ADD` | `chk_notif_status` (00016→00017, 3→5 станів), `chk_knowledge_ref_type` (00019→00022, +ReleaseNotes), `chk_notification_type` (00016→00023, 3→7 типів) | ✅ необхідні розширення enum |
| `DROP VIEW + CREATE` | `cc.notifications` (00016→00017, зміна порядку колонок) | ✅ необхідне |
| `CREATE FUNCTION → DROP FUNCTION` | `archive_knowledge_entry(bigint, text, text)` CREATE 00021 → DROP 00023 (замінено на `transition_knowledge(target=Archived)`) | ✅ refactor у межах релізу |
| Zombie функція (CREATE, не DROP) | `promote_incident_candidates()` (batch) — створена 00013, REPLACE 00016, продовжує жити (F6) | ⏸ DEFER |

**Migration squash** (об'єднання 00001-00023 в одну) — відхилено (§15 #70): втратить аудит причин. Стандартна migration practice.

## 3.3. Triggers

| Trigger | Подія | Дія |
|---|---|---|
| `trg_app_installations_set_country` | BEFORE INSERT/UPDATE на `app_installations` | GeoIP з Cloudflare `cf-ipcountry` → `country` |
| `trg_telemetry_failed_promote` | AFTER INSERT `telemetry_events` WHEN `outcome='Failed'` | `promote_incident_candidates_for_event()` |
| `trg_incident_refresh_coverage` | AFTER INSERT/UPDATE `telemetry_incidents` | `refresh_knowledge_coverage()` |
| `knowledge_audit` | AFTER INSERT/UPDATE `knowledge_entries` | snapshot → `knowledge_version_history` |

> ⚠ Зауваження: `trg_*_set_country` існує лише на `app_installations`. На `telemetry_events` тригера GeoIP немає → `telemetry_events.country` завжди NULL (див. розділ 5.6).

## 3.4. RLS-модель

| Роль | Доступ |
|---|---|
| `anon` | deny-all скрізь |
| `authenticated` | `app_installations` SELECT+INSERT+UPDATE owner-only (`user_id = auth.uid()`); `telemetry_events` SELECT+INSERT owner-only (append-only); `user_discord_guilds` owner-only |
| `cc_readonly` | `USAGE`+`SELECT` лише на схему `control_center` + EXECUTE на workflow/knowledge функції |
| `cc_notifier` | ALL на `notification_queue`/`notification_attempts` + SELECT `telemetry_incidents` |

**Відома пастка:** `RETURNING`-вирази вимагають `GRANT SELECT` (спричинила історичний інцидент `42501`).

---

# 4. `app_installations` (детально, по колонках)

> Деталі: `docs/observability/app-installations-forensic-2026-07-05.md`, `app-installations-implementation-plan.md`, `post-cleanup-forensic-app-installations.md`.

## 4.1. Стан полів (19 колонок)

| Колонка | Тип | Nullable | Default | Хто пише | Хто читає | Стан | Категорія | Рішення |
|---|---|---|---|---|---|---|---|---|
| `id` | uuid | NO | `gen_random_uuid()` | DB | PK/FK | жива | 🟢 Critical | ✅ |
| `created_at` | timestamptz | NO | `now()` | DB | CC `installations` | жива | 🟢 Critical | ✅ |
| `install_id` | text | NO | — | C# `InstallationService` | FK, CC, telemetry | жива | 🟢 Critical | ✅ |
| `user_id` | uuid | YES | — | C# | RLS, CC `users` | жива | 🟢 Critical | ✅ |
| `app_version` | text | YES | — | C# | CC release health | жива | 🟢 Critical | ✅ |
| `platform` | text | YES | — | C# | CC platform stats | жива | 🟢 Critical | ✅ |
| `machine_id` | text | YES | — | C# | CC installations | жива | 🟡 Diagnostic | ✅ |
| `os_version` | text | YES | — | C# | CC installations | жива | 🟡 Diagnostic | ✅ |
| `first_seen` | timestamptz | YES | `now()` | DB | CC views | жива | 🟢 Critical | ✅ |
| `last_seen` | timestamptz | YES | — | C# | CC health, `ecosystem_stats()` | жива | 🟢 Critical | ✅ |
| `is_active` | boolean | YES | `true` | C# | CC health/statistics | жива | 🟢 Critical | ✅ |
| `updated_at` | timestamptz | YES | — | C# | CC installations | жива | 🟢 Critical | ✅ |
| `country` | text | YES | — | Cloudflare trigger | CC `users` | жива (через тригер) | 🟢 Critical | ✅ |
| `localization_version` | text | YES | — | **ніколи** | CC `installations` | завжди NULL | 🔴 Dead | 🟡 активувати |
| `game_folder_path` | text | YES | — | **ніколи** | CC `installations` | завжди NULL | 🔴 Dead | 🟡 активувати |
| `selected_environment` | text | YES | — | **ніколи** | CC `users`, `installations` | завжди NULL | 🔴 Dead | 🟡 активувати |
| `os_build` | text | YES | — | **ніколи** | CC `installations` | завжди NULL | 🔴 Dead | 🟡 опц. активувати |
| `update_channel` | text | YES | `'stable'` | DB default | CC views | завжди DEFAULT | 🔴 Dead | 🟡 активувати |
| `install_source` | text | YES | `'unknown'` | DB default | CC views | завжди DEFAULT | 🔴 Dead | 🟡 опц. активувати |

**Підсумок:** 11 живих від C#, 1 від тригера, 2 завжди DEFAULT, 5 завжди NULL. Усі 7 «мертвих» можна активувати (additive-only), нічого не видаляти.

## 4.2. Неактивовані можливості (детально)

| Поле | Що готово | Чого не вистачає | Складність |
|---|---|---|---|
| `localization_version` | Колонка + CC view читає | C# не фіксує `release.TagName` після Install/Update | низька |
| `game_folder_path` | Колонка + CC view читає | C# не прокидає `Settings.Default.GameFolder` | низька |
| `selected_environment` | Колонка + `cc.users/installations` читають | C# не зберігає вибір `EnvironmentSelector` (transient) | середня |
| `os_build` | Колонка + CC view читає | C# не читає `Environment.OSVersion.Version.Build` | низька |
| `update_channel` | DEFAULT в БД | C# не оновлює при зміні в SettingsCanvas | низька |
| `install_source` | DEFAULT в БД | InnoSetup не передає runtime-параметр | середня |

> **Архітектурне рішення (погоджено):** для `selected_environment` + `localization_version` + `game_folder_path` — ввести `IInstallationContextProvider` (read) + `IInstallationContextUpdater` (write), а НЕ дублювати в `Settings`. План: `docs/observability/app-installations-implementation-plan.md` (v2).

## 4.3. Cleanup-політика

- **НЕ повне очищення** (втрата `first_seen`/`created_at` — adoption-історія).
- Лише тестові записи: `DELETE FROM app_installations WHERE install_id !~ '^[0-9a-f]{32}$'`.
- Production UUID-записи зберігаються.

---

# 4.10. Data Model — Single Source of Truth (Phase 3 Freeze, 2026-07-07)

> **Data Model Freeze артефакт.** Цей розділ — фінальний довідник моделі даних SCLOC-Verse. Кожна колонка має Classification, Ownership, Lifetime, Confidence. Після цієї моделі будь-яка зміна БД проходить через зміну цієї моделі (а не через припущення).

## 4.10.1. Легенда

**Classification (Approved #99):**
- `CORE` — обов'язкове для роботи системи (NOT NULL або постійно заповнюється).
- `OPTIONAL` — необов'язкове, але корисне (NULL допустимий, заповнюється за обставин).
- `DIAGNOSTIC` — діагностична інформація (для форензики).
- `FUTURE` — заготовка під плановану фічу (зараз NULL/DEFAULT, активується пізніше).
- `DEPRECATED` — застаріле, не прибране через additive-only.

**Confidence (Approved #100):**
- `VER` 🟢 — доведено фактом (жива БД, код, EXPLAIN).
- `IMPL` ✅ — реалізовано в системі (Verified + активне).
- `HYP` 🔵 — припущення (не перевірялось або очікує рішення).
- `REJ` ❌ — спростовано.

**Lifetime (retention policy):**
- `FOREVER` — не видаляється (audit, reference data).
- `1y closed` — 1 рік після `closed_at` (потім archive/purge).
- `90d` — 90 днів (потім purge через service_role).
- `30d delivered` — 30 днів після фінального статусу.
- `RESERVED` — таблиця порожня, retention не визначено.

## 4.10.2. app_installations (19 cols, 51 rows, FOREVER)

| Колонка | Тип | NN | Default | Source (Writer) | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | uuid | ✓ | gen_random_uuid() | DB | all FK | Identification | CORE | IMPL |
| created_at | timestamptz | ✓ | now() | DB | cc.installations/users | Audit | CORE | IMPL |
| install_id | text | ✓ | — | C# InstallationService (stable machine id) | FK telemetry_events/error_reports/admin_audit_log; cc.installations | Identification | CORE | IMPL |
| app_version | text | ✗ | — | C# InstallationService | cc.installations/users | Version | OPTIONAL | IMPL |
| localization_version | text | ✗ | — | ❌ не пише (PLAN IInstallationContextProvider) | cc.installations/users | Version | **FUTURE** | HYP |
| country | text | ✗ | — | trigger `set_country_from_cf` (Cloudflare cf-ipcountry) | cc.installations/users, user_analytics.country | Geography | CORE | IMPL |
| platform | text | ✗ | — | C# InstallationService | cc.installations/users | Environment | OPTIONAL | IMPL |
| first_seen | timestamptz | ✗ | now() | DB | cc.installations/users | Audit | CORE | IMPL |
| last_seen | timestamptz | ✗ | — | C# InstallationService (UtcNow) | cc.installations/users | Activity | CORE | IMPL |
| user_id | uuid | ✗ | — | C# AuthService | FK auth.users; cc.installations/users | Identity | CORE | IMPL |
| machine_id | text | ✗ | — | C# InstallationService | cc.installations/users | Diagnostics | OPTIONAL | VER |
| os_version | text | ✗ | — | C# InstallationService | cc.installations/users | Environment | OPTIONAL | IMPL |
| os_build | text | ✗ | — | ❌ не пише | cc.installations | Environment | **FUTURE** | HYP |
| update_channel | text | ✗ | 'stable' | DB DEFAULT (PLAN: C# SettingsCanvas) | cc.installations/users | Config | **FUTURE** | HYP |
| install_source | text | ✗ | 'unknown' | DB DEFAULT (PLAN: InnoSetup) | cc.installations/users | Diagnostics | **FUTURE** | HYP |
| game_folder_path | text | ✗ | — | ❌ не пише (PLAN IInstallationContextProvider) | cc.installations/users | Diagnostics | **FUTURE** | HYP |
| selected_environment | text | ✗ | — | ❌ не пише (PLAN IInstallationContextProvider) | cc.installations/users | Config | **FUTURE** | HYP |
| is_active | boolean | ✗ | true | C# InstallationService | cc.installations/users | Status | CORE | IMPL |
| updated_at | timestamptz | ✗ | — | C# InstallationService (UtcNow) | cc.installations/users | Audit | CORE | IMPL |

**Trivia (VER #93):** `country` — per-installation; `user_countries` (computed у user_analytics) — per-user aggregate (string_agg DISTINCT). НЕ дубль.

## 4.10.3. telemetry_events (28 cols, 705 rows, 90d retention planned)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | uuid | ✓ | gen_random_uuid() | DB | cc.telemetry_events/traces; FK incidents root/last | Identification | CORE | IMPL |
| client_event_id | uuid | ✓ | — | C# TelemetryClient | uniq_telemetry_client_event_id (dedup) | Dedup | CORE | IMPL |
| session_id | uuid | ✓ | — | C# TelemetryClient | cc.telemetry_events/traces | Trace | CORE | IMPL |
| correlation_id | uuid | ✓ | — | C# TelemetryClient | cc.telemetry_events/traces | Trace | CORE | IMPL |
| step | int | ✓ | — | C# TelemetryClient | cc.telemetry_events/traces | Trace | CORE | IMPL |
| install_id | text | ✗ | — | C# TelemetryClient | FK app_installations | Identity | OPTIONAL | IMPL |
| user_id | uuid | ✗ | — | C# TelemetryClient | FK auth.users | Identity | OPTIONAL | IMPL |
| occurred_at | timestamptz | ✓ | — | C# TelemetryClient (UtcNow) | cc.telemetry_events/traces; incident detection windows | Time | CORE | IMPL |
| received_at | timestamptz | ✓ | now() | DB | cc.telemetry_events/traces; idx_telemetry_received | Time | CORE | IMPL |
| app_version | text | ✓ | — | C# TelemetryClient | cc.telemetry_events; release_health | Version | CORE | IMPL |
| git_commit | text | ✗ | — | ❌ завжди NULL (PLAN MSBuild target) | cc.telemetry_events | Diagnostics | **FUTURE** | HYP |
| channel | text | ✓ | 'stable' | C# TelemetryClient | cc.telemetry_events | Config | OPTIONAL | IMPL |
| telemetry_version | int | ✓ | 1 | C# TelemetryClient | cc.telemetry_events | Schema | CORE | IMPL |
| os_version | text | ✗ | — | C# TelemetryClient | cc.telemetry_events | Environment | OPTIONAL | IMPL |
| country | text | ✗ | — | ❌ ніколи (trigger лише на installations, Rejected #35/#38) | — | — | **DEPRECATED** | REJ |
| component | text | ✓ | — | C# TelemetryClient | cc.telemetry_events; incidents fingerprint | Classification | CORE | IMPL |
| operation | text | ✓ | — | C# TelemetryClient | cc.telemetry_events; incidents fingerprint | Classification | CORE | IMPL |
| outcome | text | ✓ | — | C# TelemetryClient (CHECK 5 значень) | cc.telemetry_events; signal COALESCE; trigger Failed | Status | CORE | IMPL |
| severity | text | ✓ | 'Info' | C# TelemetryClient (CHECK 5 значень) | cc.telemetry_events | Status | CORE | IMPL |
| category | text | ✓ | 'Operational' | C# (завжди Operational; PLAN Critical/Diagnostic/Analytics) | cc.telemetry_events | Classification | **FUTURE** | HYP |
| source | text | ✗ | — | C# ErrorContextExtractor | signal COALESCE (priority 1) | Diagnostics | OPTIONAL | IMPL |
| http_status | int | ✗ | — | ❌ ніколи з C# (100% NULL 705/705) | signal COALESCE (priority 4) | Diagnostics | **DEPRECATED** | VER |
| hresult | text | ✗ | — | C# ErrorContextExtractor (LIA) | signal COALESCE (priority 3) | Diagnostics | OPTIONAL | IMPL |
| supabase_code | text | ✗ | — | ❌ ніколи з C# (100% NULL 705/705) | signal COALESCE (priority 2) | Diagnostics | **DEPRECATED** | VER |
| exception_type | text | ✗ | — | C# ErrorContextExtractor | signal COALESCE (priority 5) | Diagnostics | OPTIONAL | IMPL |
| error_message | text | ✗ | — | C# TelemetryClient | cc.telemetry_events | Diagnostics | OPTIONAL | IMPL |
| duration_ms | int | ✗ | — | C# TelemetryClient (Started без duration) | cc.telemetry_events | Diagnostics | OPTIONAL | IMPL |
| detail | jsonb | ✗ | — | C# (UpdateEvents/LiaEvents/InstallationService) | cc.telemetry_events; JSON keys: phase, retry_count(0), certificate_*, installer_type, package_version, activity_id | Diagnostics | OPTIONAL | IMPL |

**Trivia:** `detail.retry_count` — `FUTURE` заготовка під Retry Policy (Slices 3-5, §14.9 #85). НЕ мертва. `detail.signal_name` — НЕ існує (Rejected #65).

## 4.10.4. telemetry_incidents (19 cols, 4 rows, 1y closed)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | cc.incidents; incident_code (trigger) | Identification | CORE | IMPL |
| fingerprint_key | text | ✓ | — | promote_incident_candidates_for_event | cc.incidents | Classification | CORE | IMPL |
| fingerprint_hash | text | ✓ | — | promote (md5(fingerprint_key)) | cc.incidents; idx_incidents_fp_open | Classification | CORE | IMPL |
| release | text | ✓ | — | promote (= app_version) | cc.incidents | Version | CORE | IMPL |
| component | text | ✓ | — | promote | cc.incidents; knowledge matching | Classification | CORE | IMPL |
| operation | text | ✓ | — | promote | cc.incidents; knowledge matching | Classification | CORE | IMPL |
| signal | text | ✓ | — | promote (COALESCE) | cc.incidents; knowledge matching | Classification | CORE | IMPL |
| root_event_id | uuid | ✗ | — | promote (ASC first Failed) | FK telemetry_events | Trace | OPTIONAL | IMPL |
| last_event_id | uuid | ✗ | — | promote (DESC last Failed) | FK telemetry_events | Trace | OPTIONAL | IMPL |
| opened_at | timestamptz | ✓ | now() | DB | cc.incidents; incident_code trigger | Time | CORE | IMPL |
| last_event_at | timestamptz | ✓ | now() | promote | cc.incidents; auto_close policy | Time | CORE | IMPL |
| closed_at | timestamptz | ✗ | — | transition_incident (when status=Closed) | cc.incidents | Status | OPTIONAL | IMPL |
| status | text | ✓ | 'Active' | promote/transition_incident (CHECK 5 станів) | cc.incidents | Status | CORE | IMPL |
| highest_severity | text | ✓ | 'Warning' | promote (CHECK Critical/Warning) | cc.incidents | Status | CORE | IMPL |
| peak_failure_pct | numeric(5,1) | ✗ | — | promote (GREATEST) | cc.incidents | Metrics | OPTIONAL | IMPL |
| affected_users | int | ✓ | 0 | promote (GREATEST) | cc.incidents | Metrics | CORE | IMPL |
| affected_installs | int | ✓ | 0 | promote (GREATEST) | cc.incidents; severity calc | Metrics | CORE | IMPL |
| event_count | bigint | ✓ | 0 | promote (increment) | cc.incidents | Metrics | CORE | IMPL |
| owner | text | ✗ | — | assign_incident_owner | cc.incidents; can_create_knowledge_for_incident | Workflow | OPTIONAL | IMPL |
| incident_code | text | ✗ | — | trigger set_incident_code (Post-Phase 3A) | cc.incidents AS incident_id; cc.incident_timeline/notes_view/notifications AS incident_code | Display | CORE | IMPL (Phase 3A) |

## 4.10.5. notification_queue (16 cols, 4 rows, 30d delivered)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | FK notification_attempts; cc.notifications | Identification | CORE | IMPL |
| incident_id | bigint | ✓ | — | promote (when incident created) | FK telemetry_incidents; cc.notifications | Identity | CORE | IMPL |
| notification_type | text | ✓ | — | promote (CHECK 7 типів) | cc.notifications | Classification | CORE | IMPL |
| provider | text | ✓ | 'Discord' | promote | cc.notifications | Config | CORE | IMPL |
| status | text | ✓ | 'Pending' | promote/Notifier (CHECK 5 станів) | cc.notifications; uniq_notification_dedup | Status | CORE | IMPL |
| payload | jsonb | ✗ | — | promote (10 keys: version, incident_id, component, operation, signal, severity, release, affected_installs, affected_users, failure_pct) | Notifier (claim) | Diagnostics | OPTIONAL | IMPL |
| retry_count | int | ✓ | 0 | Notifier | cc.notifications | Workflow | CORE | IMPL |
| last_attempt_at | timestamptz | ✗ | — | Notifier | cc.notifications | Time | OPTIONAL | IMPL |
| delivered_at | timestamptz | ✗ | — | Notifier (when Delivered) | cc.notifications | Status | OPTIONAL | IMPL |
| error_message | text | ✗ | — | ❌ Notifier не пише (де-факто deprecated з 00017) | cc.notifications | Diagnostics | **DEPRECATED** | VER |
| created_at | timestamptz | ✓ | now() | DB | cc.notifications; idx_notif_pending | Audit | CORE | IMPL |
| next_attempt_at | timestamptz | ✗ | — | Notifier (retry calc) | cc.notifications | Workflow | OPTIONAL | IMPL |
| max_retries | int | ✓ | 3 | DB DEFAULT | cc.notifications | Config | CORE | IMPL |
| claimed_at | timestamptz | ✗ | — | Notifier (claim) | cc.notifications | Workflow | OPTIONAL | IMPL |
| claimed_by | text | ✗ | — | Notifier (instance id) | cc.notifications | Workflow | OPTIONAL | IMPL |
| last_error | text | ✗ | — | Notifier (when Failed/RetryScheduled) | cc.notifications | Diagnostics | OPTIONAL | IMPL |

**Trivia (VER #84):** `error_message` ≠ `last_error` — різна семантика (фінал vs остання спроба retry).

## 4.10.6. notification_attempts (10 cols, 0 rows, 90d)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | FK; cc.notifications | Identification | CORE | IMPL |
| queue_id | bigint | ✓ | — | Notifier | FK notification_queue CASCADE | Identity | CORE | IMPL |
| attempt_no | int | ✓ | — | Notifier | cc.notifications | Workflow | CORE | IMPL |
| provider | text | ✓ | — | Notifier | cc.notifications | Config | CORE | IMPL |
| status | text | ✓ | — | Notifier (CHECK Sending/Delivered/Failed) | cc.notifications | Status | CORE | IMPL |
| http_status | int | ✗ | — | Notifier (provider response) | cc.notifications | Diagnostics | OPTIONAL | IMPL |
| provider_message_id | text | ✗ | — | Notifier (Discord message id) | cc.notifications | Trace | OPTIONAL | IMPL |
| error_message | text | ✗ | — | Notifier (when Failed) | cc.notifications | Diagnostics | OPTIONAL | IMPL |
| started_at | timestamptz | ✓ | now() | DB | cc.notifications | Time | CORE | IMPL |
| finished_at | timestamptz | ✗ | — | Notifier | cc.notifications | Time | OPTIONAL | IMPL |

## 4.10.7. incident_policy (8 cols, 5 rows, FOREVER)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| component | text | ✓ | — | seed (PK part) | promote_incident_candidates_for_event | Classification | CORE | IMPL |
| operation | text | ✓ | '_' | seed ('_' = будь-яка) | promote | Classification | CORE | IMPL |
| signal | text | ✓ | '_' | seed ('_' = будь-який) | promote | Classification | CORE | IMPL |
| min_sample | int | ✓ | 5 | seed | promote | Threshold | CORE | IMPL |
| failure_threshold_pct | numeric(5,1) | ✓ | 20.0 | seed | promote | Threshold | CORE | IMPL |
| critical_affected_threshold | int | ✓ | 10 | seed | promote (severity calc) | Threshold | CORE | IMPL |
| auto_close_after_minutes | int | ✓ | 60 | seed | auto_close_stale_incidents | Threshold | CORE | IMPL |
| enabled | boolean | ✓ | true | seed/manual | promote | Status | CORE | IMPL |

## 4.10.8. incident_status_log (7 cols, 0 rows, 1y)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | cc.incident_timeline | Identification | CORE | IMPL |
| incident_id | bigint | ✓ | — | transition_incident/assign_owner | FK CASCADE; cc.incident_timeline | Identity | CORE | IMPL |
| from_status | text | ✓ | — | transition_incident | cc.incident_timeline | Workflow | CORE | IMPL |
| to_status | text | ✓ | — | transition_incident | cc.incident_timeline | Workflow | CORE | IMPL |
| changed_by | text | ✓ | 'system' | transition_incident (admin/system) | cc.incident_timeline | Audit | CORE | IMPL |
| changed_at | timestamptz | ✓ | now() | DB | cc.incident_timeline | Time | CORE | IMPL |
| note | text | ✗ | — | transition_incident (optional) | cc.incident_timeline | Diagnostics | OPTIONAL | IMPL |

## 4.10.9. incident_notes (5 cols, 0 rows, 1y)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | cc.incident_notes_view | Identification | CORE | IMPL |
| incident_id | bigint | ✓ | — | add_incident_note | FK CASCADE; cc.incident_notes_view | Identity | CORE | IMPL |
| content | text | ✓ | — | add_incident_note | cc.incident_notes_view | Content | CORE | IMPL |
| created_by | text | ✓ | 'admin' | add_incident_note | cc.incident_notes_view | Audit | CORE | IMPL |
| created_at | timestamptz | ✓ | now() | DB | cc.incident_notes_view | Time | CORE | IMPL |

## 4.10.10. knowledge_entries (19 cols, 0 rows, FOREVER)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | cc.knowledge_*; FK | Identification | CORE | IMPL |
| fingerprint_key | text | ✓ | — | create_knowledge_from_incident | cc.knowledge_* | Classification | CORE | IMPL |
| fingerprint_hash | text | ✓ | — | create_knowledge_from_incident | idx_knowledge_fingerprint; match_knowledge_for_incident | Classification | CORE | IMPL |
| component | text | ✓ | — | create_knowledge_from_incident | cc.knowledge_*; match_knowledge_priority2 | Classification | CORE | IMPL |
| operation | text | ✓ | — | create_knowledge_from_incident | cc.knowledge_* | Classification | CORE | IMPL |
| signal | text | ✓ | — | create_knowledge_from_incident | cc.knowledge_*; match_knowledge_priority2 | Classification | CORE | IMPL |
| title | text | ✓ | — | create/update_knowledge_entry | cc.knowledge_* | Content | CORE | IMPL |
| symptoms | text | ✗ | — | update_knowledge_entry | cc.knowledge_* | Content | OPTIONAL | IMPL |
| known_cause | text | ✓ | — | create/update_knowledge_entry | cc.knowledge_* | Content | CORE | IMPL |
| workaround | text | ✗ | — | create/update_knowledge_entry | cc.knowledge_* | Content | OPTIONAL | IMPL |
| permanent_fix | text | ✗ | — | update_knowledge_entry | cc.knowledge_* | Content | OPTIONAL | IMPL |
| affected_versions | text[] | ✗ | — | update_knowledge_entry (parse_version_list) | cc.knowledge_list | Version | OPTIONAL | IMPL |
| fixed_version | text | ✗ | — | update_knowledge_entry | cc.knowledge_*; verify_knowledge_auto | Version | OPTIONAL | IMPL |
| confidence | text | ✓ | 'Low' | create/transition (CHECK Low/Medium/High/Verified) | cc.knowledge_*; verify_knowledge_auto | Status | CORE | IMPL |
| status | text | ✓ | 'Draft' | create/transition (CHECK Draft/Reviewed/Verified/Deprecated/Archived) | cc.knowledge_*; matching | Status | CORE | IMPL |
| created_by | text | ✓ | — | create_knowledge_from_incident | cc.knowledge_* | Audit | CORE | IMPL |
| created_at | timestamptz | ✓ | now() | DB | cc.knowledge_* | Audit | CORE | IMPL |
| updated_by | text | ✓ | — | update/transition_knowledge | cc.knowledge_*; knowledge_audit_trigger | Audit | CORE | IMPL |
| updated_at | timestamptz | ✓ | now() | DB/update | cc.knowledge_*; idx_knowledge_status_updated | Audit | CORE | IMPL |

## 4.10.11. knowledge_references (5 cols, 0 rows, FOREVER)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | FK | Identification | CORE | IMPL |
| knowledge_id | bigint | ✓ | — | add_knowledge_reference | FK RESTRICT; cc.knowledge_entry_detail (jsonb_agg) | Identity | CORE | IMPL |
| reference_type | text | ✓ | — | add_knowledge_reference (CHECK 5 типів) | cc.knowledge_entry_detail | Classification | CORE | IMPL |
| url | text | ✗ | — | add_knowledge_reference | cc.knowledge_entry_detail | Content | OPTIONAL | IMPL |
| label | text | ✗ | — | add_knowledge_reference | cc.knowledge_entry_detail | Content | OPTIONAL | IMPL |

## 4.10.12. knowledge_version_history (9 cols, 0 rows, FOREVER)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | get_knowledge_version_detail | Identification | CORE | IMPL |
| knowledge_id | bigint | ✓ | — | knowledge_audit_trigger | FK RESTRICT; get_knowledge_history | Identity | CORE | IMPL |
| version | int | ✓ | — | knowledge_audit_trigger (MAX+1) | get_knowledge_history; optimistic concurrency | Audit | CORE | IMPL |
| snapshot | jsonb | ✓ | — | knowledge_audit_trigger (to_jsonb NEW) | get_knowledge_version_detail | Audit | CORE | IMPL |
| changed_by | text | ✗ | — | knowledge_audit_trigger (NEW.updated_by/created_by) | get_knowledge_history | Audit | OPTIONAL | IMPL |
| db_user | text | ✓ | CURRENT_USER | DB | get_knowledge_history (dual-identity) | Audit | CORE | IMPL |
| changed_at | timestamptz | ✓ | now() | DB | get_knowledge_history | Time | CORE | IMPL |
| change_reason | text | ✗ | — | set_knowledge_change_context | get_knowledge_history | Audit | OPTIONAL | IMPL |
| change_type | text | ✓ | 'Updated' | set_knowledge_change_context (Created/Updated/Archived/WorkflowTransition/ReferenceAdded/ReferenceRemoved) | get_knowledge_history | Audit | CORE | IMPL |

## 4.10.13. error_reports (13 cols, 0 rows, RESERVED)

> **Зарезервована таблиця (міграція 00003).** Жодного продюсера, 0 рядків. KEEP через Rejected #39. Усі колонки — RESERVED lifetime, Class=FUTURE (до появи клієнтського "Report bug" або Edge Function).

| Колонка | Тип | NN | Default | Source (Writer) — план | Reader (план) | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | uuid | ✓ | gen_random_uuid() | DB | FK; cc.errors | Identification | FUTURE | HYP |
| user_id | uuid | ✗ | — | (PLAN) client Report bug | FK auth.users ON DELETE SET NULL; cc.errors | Identity | FUTURE | HYP |
| install_id | text | ✗ | — | (PLAN) client Report bug | FK app_installations(install_id) ON DELETE SET NULL; cc.errors | Identity | FUTURE | HYP |
| error_type | text | ✓ | — | (PLAN) client ( ApplicationException.GetType().Name) | cc.errors | Classification | FUTURE | HYP |
| message | text | ✗ | — | (PLAN) client (Exception.Message) | cc.errors | Content | FUTURE | HYP |
| stack_trace | text | ✗ | — | (PLAN) client (Exception.StackTrace) | cc.errors | Diagnostics | FUTURE | HYP |
| app_version | text | ✗ | — | (PLAN) client | cc.errors | Version | FUTURE | HYP |
| localization_version | text | ✗ | — | (PLAN) client (IInstallationContextProvider) | cc.errors | Version | FUTURE | HYP |
| game_folder_path | text | ✗ | — | (PLAN) client (IInstallationContextProvider) | cc.errors | Diagnostics | FUTURE | HYP |
| selected_environment | text | ✗ | — | (PLAN) client (IInstallationContextProvider) | cc.errors | Config | FUTURE | HYP |
| context | jsonb | ✗ | — | (PLAN) client (additional context) | cc.errors | Diagnostics | FUTURE | HYP |
| is_resolved | boolean | ✗ | false | (PLAN) admin via Dashboard/CC | cc.errors | Status | FUTURE | HYP |
| created_at | timestamptz | ✓ | now() | DB | cc.errors | Audit | FUTURE | HYP |

**Рішення:** RESERVED/KEEP (до фічі client-side error reporting).

## 4.10.14. admin_audit_log (7 cols, 0 rows, RESERVED)

> **Зарезервована для майбутньої адмін-панелі (міграція 00004).** 0 продюсерів, 0 рядків. KEEP через Rejected #39.

| Колонка | Тип | NN | Default | Source (Writer) — план | Reader (план) | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | uuid | ✓ | gen_random_uuid() | DB | (TBD admin panel) | Identification | FUTURE | HYP |
| admin_discord_id | text | ✓ | — | (PLAN) admin action via SECURITY DEFINER function | (TBD admin panel) | Identity | FUTURE | HYP |
| action | text | ✓ | — | (PLAN) admin action (e.g. 'close_incident', 'transition_knowledge') | (TBD admin panel) | Classification | FUTURE | HYP |
| target_user_id | uuid | ✗ | — | (PLAN) admin function arg | FK auth.users ON DELETE SET NULL | Identity | FUTURE | HYP |
| target_install_id | text | ✗ | — | (PLAN) admin function arg | FK app_installations(install_id) ON DELETE SET NULL | Identity | FUTURE | HYP |
| details | jsonb | ✗ | '{}' | (PLAN) admin function arg | (TBD admin panel) | Diagnostics | FUTURE | HYP |
| created_at | timestamptz | ✓ | now() | DB | (TBD admin panel) | Audit | FUTURE | HYP |

**Рішення:** RESERVED/KEEP (до фічі admin panel + audit через SECURITY DEFINER).

## 4.10.15. user_discord_guilds (5 cols, 0 rows, RESERVED)

> **Код `DiscordGuildSyncService` мертвий** (identify scope only — без guilds.members.read). 0 рядків.

| Колонка | Тип | NN | Default | Source (Writer) | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | uuid | ✓ | gen_random_uuid() | DB | — (TBD) | Identification | FUTURE | HYP |
| user_id | uuid | ✓ | — | (DEAD) DiscordGuildSyncService →复兴 when scope extended | FK auth.users ON DELETE CASCADE | Identity | FUTURE | HYP |
| discord_guild_id | text | ✓ | — | (DEAD) | UNIQUE (user_id, discord_guild_id) | Identity | FUTURE | HYP |
| guild_name | text | ✗ | — | (DEAD) | — | Content | FUTURE | HYP |
| synced_at | timestamptz | ✓ | now() | DB | — | Audit | FUTURE | HYP |

**Рішення:** RESERVED/KEEP (до розширення OAuth scope + Community Center фічі).

## 4.10.16. pipeline_health_meta (2 cols, 1 row singleton, FOREVER)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| singleton | boolean | ✓ | true (CHECK singleton=true) | DB | cc.observability_health | Config | CORE | IMPL |
| last_knowledge_refresh | timestamptz | ✗ | — | refresh_knowledge_coverage | cc.observability_health | Time | CORE | IMPL |

## 4.10.17. auth.users (35 cols, 54 rows, Supabase-managed)

> **Системна таблиця Supabase GoTrue.** Не керується міграціями SCLOC-Verse. Структура 35 колонок ідентична в production та replica (verified Schema Verification). Залежить від неї: `user_analytics`, `cc.users`, `cc.statistics` (dependency graph §16.8).

**Використовувані в SCLOC-Verse колонки:** `id`, `email`, `raw_user_meta_data` (provider_id, full_name, custom_claims.global_name, avatar_url), `created_at`, `last_sign_in_at`, `email_confirmed_at`, `is_anonymous`, `banned_until`, `deleted_at`.

## 4.10.18. Data Lifetime — зведена таблиця

| Таблиця | Lifetime | Reason | Tool (future) |
|---|---|---|---|
| app_installations | FOREVER (поки user_id існує) | історія установок | — |
| telemetry_events | 90d (план) | append-only, retention | service_role purge (Phase 5) |
| telemetry_incidents | 1y після closed_at | інциденти — коротка пам'ять | service_role archive (Phase 5) |
| incident_policy | FOREVER | reference data | — |
| incident_status_log | 1y | audit journal | service_role archive |
| incident_notes | 1y | audit journal | service_role archive |
| notification_queue | 30d після delivered_at | транзиція | service_role purge |
| notification_attempts | 90d | audit | service_role purge |
| knowledge_entries | FOREVER | knowledge base | — |
| knowledge_references | FOREVER | audit | — |
| knowledge_version_history | FOREVER | audit | — |
| error_reports | RESERVED | не визначено (таблиця порожня) | — |
| admin_audit_log | RESERVED | не визначено | — |
| user_discord_guilds | RESERVED | не визначено (код мертвий) | — |
| pipeline_health_meta | FOREVER | singleton | — |
| auth.users | Supabase-managed | GoTrue lifecycle | auth.admin API |

> ⚠ **Retention (90d/30d) НЕ реалізовано** — `pg_cron` не встановлений (KB §3.1, Rejected TBD). Backlog: Phase 5 Retention Pipeline.

---

# 5. Telemetry



> Деталі: `docs/observability/FORENSIC-DATA-PIPELINE-DETAIL.md` (всі `.Track()` з рядками), `Observability-Constitution.md`, `Optimization-Matrix.md`, `Observability-Database-Optimization-Plan.md`.

## 5.1. Конституція (29 статей) — цільові інваріанти

1. Absolute Isolation — телеметрія не кидає винятки в бізнес-код.
2. Never Block the UI — sync O(1), I/O у фоновому потоці.
3. Never Break Startup — конструювання дешеве й синхронне.
4. Zero PII — `PrivacySanitizer` як єдине вузьке горло.
5. Append-Only — `telemetry_events` лише INSERT.
6. Idempotent Retries — `UNIQUE(client_event_id)` + `ON CONFLICT DO NOTHING`.
7. Single Sanctioned Sink — лише `ITelemetryService`.
8. Critical Failures Auto-Captured — глобальні exception handlers.
9. Incidents Forensable From Platform Alone.
10. Kill-Switch Transparent — вимкнення не ламає додаток.
11. Trace Mandatory — `correlation_id` + `step` NOT NULL.
12. Single Ingestion Contract.
13. Additive-Only Schema.
14. Offline First — bounded JSONL-черга + flush-on-connect.
15. Observable by Default — Start/Success/Failure/Duration.
16. Backward Compatibility.
17. Production Database Verification — runtime під роллю `authenticated`.
18. Production Pending — статус для непридушених сценаріїв.
19. Promotion Immutability — promotion лише SELECT events.
20. Dashboard Purity — `cc_readonly` лише SELECT VIEWs.
21. Incident Identity — інцидент = `incident_id`, не fingerprint.
22. Incident History Is Immutable.
23. Incident Workflow Access.
24. Notification Independence — Incident Engine не знає про канали.
25. Notification Idempotency — `UNIQUE(incident_id, notification_type)`.
26. Delivery Audit — append-only `notification_attempts` + Zombie Recovery.
27. Provider Independence — `INotificationProvider` у окремій бібліотеці.
28. Knowledge Preservation — Knowledge Engine ручного походження.
29. Secret Independence — тришарова Git/DB/App модель.

## 5.2. Реалізовано vs Цільовий дизайн (ВАЖЛИВО)

> Багато артефактів у Конституції/Architecture — **цільовий дизайн**. Реальний стан станом на 1.0.0.1 (за forensic):

| Компонент | Цільовий дизайн | Реалізовано |
|---|---|---|
| `ITelemetryService` (single sink) | ✅ | ✅ `TelemetryClient` — єдина impl |
| In-memory bounded queue | cap 5000 | ✅ `TelemetryEventQueue` cap 5000 drop-oldest |
| Background flush Timer | 30с | ✅ `TelemetryClient.FlushInterval=30s` |
| `TelemetryUploader` batch+backoff | ≤100, 2с/8с/30с | ✅ SemaphoreSlim-серіалізований |
| Idempotency по `client_event_id` | UNIQUE + ON CONFLICT | ✅ |
| `PrivacySanitizer` | рекурсивно по всьому payload | ⚠ **лише `ErrorMessage`** (F4) — `Detail` не санітарити |
| Offline JSONL-черга (Стаття 14) | bounded 10K, flush-on-connect | ❌ **не реалізовано** (тільки in-memory) |
| `SamplingGate` (duplicate-suppression 60с) | yes | ❌ не реалізовано |
| `FeatureFlagService` (remote→env→setting→default, reload 10 хв) | yes | ❌ не реалізовано — лише env kill-switch |
| Конфігурований endpoint → Edge Function ingest (Phase 5) | yes | ❌ PostgREST напряму |
| `telemetry_rollup_mv` MATVIEW (2 роки) | yes | ❌ не реалізовано |
| Retention 14д pg_cron purge | yes | ⚠ план у release-runbook; перевірити деплой |
| Global `UnhandledException` handler (Стаття 8) | yes | ❌ **GAP** — WPF-краш минає спостережуваність (F3) |
| `git_commit` на кожній події | MSBuild target | ❌ завжди NULL (target відкладено) |
| `category` диференційована | Critical/Operational/Diagnostic/Analytics | ⚠ завжди `'Operational'` |
| `country` через тригер | yes | ❌ тригер лише на `app_installations` |
| Terminal Flush на всіх Failed (Стаття 16) | yes | ⚠ 5 Failed-емітерів без `FlushAsync` |

## 5.3. Identity та Build info

- **`install_id`** — machine identity, файл `%LOCALAPPDATA%\SCLOCVerse\install-id` + registry `HKCU\Software\VALDEUS\SCLOCVerse\InstallId` (dual-store). Ніколи `MachineName`.
- **`user_id`** — nullable (null для pre-auth подій), встановлюється `TelemetryUploader` перед INSERT.
- **Build info** на кожній події: `app_version`, `channel`, `telemetry_version=1`, `os_version`. `git_commit` — завжди NULL.

## 5.4. Kill-switch

- **Єдиний реалізований:** env `SCLOCVERSE_TELEMETRY_DISABLED=1|true` (`AppCompositionRoot.cs:282`).
- Вимикає **всю** телеметрію бінарно. Усі `Track()` повертаються, Flush не запускається.

## 5.5. Карта всіх 44 `.Track()` емітерів

> Точні file:line у `FORENSIC-DATA-PIPELINE-DETAIL.md`. Тут — зведення.

| Компонент | К-ть подій | Файли | Примітка |
|---|---:|---|---|
| `Application` | 1 | `App.xaml.cs:81` | `Start.Started` |
| `Auth` | 13 | `AuthService.cs:66-229` | SignIn + RestoreSession |
| `Installation` | 3 | `InstallationService.cs:46-118` | Sync |
| `Orchestrator` | 4 | `BackgroundUpdateMonitor.cs:131-229` | Cycle/AppCheck/LiaCheck (Failed) |
| `Updater` (self-update) | 9 | `UpdateDownloader.cs`, `UpdateInstaller.cs`, `UpdateVerifier.cs` | Download/Install/Verify |
| `LIA` | 18 | `Updater.cs:79-323` | Install/Download/RunInstallerScript |
| `Localization` | **0** | — | **GAP** — сліпа зона |

**Severity/Outcome/Categoria модель:**
- `outcome ∈ {Started, Succeeded, Failed, Cancelled, Skipped}` (CHECK).
- `severity ∈ {Info, Warning, Error, Critical, Crash}` (CHECK).
- `category ∈ {Critical, Operational, Diagnostic, Analytics}` (CHECK) — **завжди `'Operational'`** у реальності.
- Default severity: `Failed → Error`, інакше `Info`.

## 5.6. Відомі проблеми телеметрії

| # | Проблема | Де |
|---|---|---|
| F3 | Відсутній global `UnhandledException` handler | `App.xaml.cs` |
| F4 | `PrivacySanitizer` покриває лише `ErrorMessage`; `Detail` (appx_log, cert-поля) проходить неочищеним | `TelemetryClient.cs:142` |
| F5 | Terminal `FlushAsync` пропущено у 5 `ApplicationUpdate` Failed-емітерів | `UpdateDownloader.cs:59`, `UpdateInstaller.cs:46,76,82`, `UpdateVerifier.cs:65` |
| F7 | `LiaForensicParser.TryParseMinimal` — мертвий код (0 викликів) | `LiaForensicParser.cs:67` |
| F8 | `telemetry_events.country` — мертва (тригер лише на installations) | міграції 2 + 9 |
| F8a | `telemetry_events.http_status` + `supabase_code` — **100% NULL** (705/705 рядків); ніколи не пишуться з C# (TelemetryContext має поля, але ErrorContextExtractor їх не заповнює) | Phase 2 forensic (2026-07-07) |
| F9 | 6 з 19 колонок `app_installations` завжди NULL/DEFAULT | див. розділ 4 |
| — | LIA cascade: 1 фізична відмова → 3-4 Failed-події | `Updater.cs` |
| — | `Localization.*` телеметрія відсутня (сліпа зона встановлення локалізації) | `LocalizationInstaller.cs` |

## 5.7. Дублікати всередині телеметрії

| Що дублюється | Де | Рішення |
|---|---|---|
| ~~`detail.signal_name` ↔ computed `signal` у views~~ | ~~`telemetry_events.detail` JSON~~ | ❌ **ПЕРЕВІРЕНО Phase 2:** у живих даних (705 рядків) ключ `signal_name` **НЕ існує** (0 зустрічей). Твердження застаріле — прибрати з плану. |
| `detail.retry_count` | `0` у 351 записі; 4 місця в C# (`ErrorContextExtractor.cs:181`, `InstallationService.cs:149`, `LiaEvents.cs:46`, `UpdateEvents.cs:39`) — додано в Observability Slices 3-5 (коміти `a4f6d39`, `3bb5a3c`, `4db4448`) | ⏸ **НЕ дубль, НЕ мертва.** Заготовка під плановану Retry Policy (коментарі в коді: `// схема готова для майбутньої Retry Policy`). Рішення відкладено до **Retry Policy architectural decision** (§17.4). |
| LIA cascade 3-4 Failed на 1 відмову | `Updater.cs` | 🔄 об'єднати до 1 термінальної |
| App self-update Started/Succeeded по фазах | `UpdateDownloader/Installer/Verifier` | 🔄 об'єднати до 1 результату |

---

## 5.8. Telemetry Policy (Phase 3.5, 2026-07-07)

> **Single Source of Truth для збору даних.** Відповідає на 3 питання: що надсилається завжди? Що лише при Developer Diagnostics? Що ніколи не виходить із комп'ютера користувача?

### 5.8.1. Три рівні

| Рівень | Назва | Вимикається? | Опис |
|---|---|---|---|
| **L1** | **Mandatory** | ❌ Ні | Статистика життя продукту. Без неї неможливо оцінити release health, success rate, інциденти. Анонімізована (install_id, app_version) + технічні результати. |
| **L2** | **Diagnostic** | ✅ Так (Developer Diagnostics) | Розширена діагностика для розробника: проміжні кроки, трейси, forensic payload (cert, hresult, stack), duration_ms, detail.phase. |
| **L3** | **Local Only** | ❌ Ніколи не відправляється | Hotkeys, debug log, performance, FPS, input traces, verbose. Лише локальний журнал (якщо буде). |

### 5.8.2. Архітектура Policy — варіант E (Enum level, обраний після forensic)

> **Forensic (2026-07-07, після критики користувача):** `TelemetryPolicy.Classify(component, operation)` занадто крихке — перейменування `LocalizationInstall`→`LocalizationInstaller` ламає політику. Розглянуто 5 варіантів (Attribute/StronglyTyped/Registry/ContextTag/EnumLevel). **Обрано Enum level**.

```csharp
public enum TelemetryLevel { Mandatory, Diagnostic, Local }

public interface ITelemetryService {
    Task Track(string component, string operation, string outcome,
               TelemetryContext? ctx = null,
               TelemetryLevel level = TelemetryLevel.Mandatory);  // default для backward compat
}
```

**Переваги:**
- Не крихке до перейменувань `component/operation` (Level вшитий у виклик).
- Існуючі 39 викликів `.Track()` за замовчуванням стають L1 (default параметр) — не ламає код.
- L2 виклики додають `level: TelemetryLevel.Diagnostic` явно.
- Читається одразу в коді емітера.

**Перевірка `IsDiagnosticEnabled`** всередині `Track`:
```
if (level == TelemetryLevel.Diagnostic && !settings.DeveloperDiagnostics) return;
```

### 5.8.3. Чекбокс — БЕЗ перейменування (forensic correction)

| Аспект | Станом |
|---|---|
| UI id | `AdvancedDiagnosticsCheckBox` — **НЕ чіпати** (частина UI, релізований контракт) |
| Label | `Content="Розширена діагностика"` — **НЕ чіпати** |
| Settings key | `Settings.Default.AdvancedDiagnostics` — лишити (внутрішнє ім'я) |
| Читання | `MainWindow.xaml.cs:403` |
| Запис | `MainWindow.xaml.cs:450-456` |
| Вплив на телеметрію | зараз ❌ відсутній; після Phase 3.5 — `ITelemetryService` перевіряє `settings.AdvancedDiagnostics` для L2 |
| Default | `false` (opt-in) |

**Changes тільки внутрішньо:** `TelemetryClient.Track()` перевіряє `settings.AdvancedDiagnostics` перед відправкою L2. UI не змінюється.

### 5.8.4. Очікуваний ефект (без точних цифр)

Зараз усі 39 викликів L1+L2 відправляються завжди. Після Phase 3.5 реалізації: L2 (Diagnostic) відправляється лише при ON. **Очікується суттєве зменшення навантаження на БД для користувачів з OFF** (попередня оцінка, не підтверджена вимірюваннями — реальний ефект буде виміряно post-implementation).

## 5.8b. Прив'язка `app_installations` FUTURE колонок до Policy (оновлено після forensic)

> Forensic (2026-07-07): `game_folder_path` і `install_source` перенесені з L1 на L2 (немає доказу, що потрібні для Release Health/Incident Pipeline/Statistics/Dashboard).

| Колонка | Policy Level | Reason |
|---|---|---|
| `app_version`, `os_version`, `machine_id`, `country`, `platform` | **L1 Mandatory** | базова статистика релізів + Release Health |
| `localization_version`, `selected_environment`, `update_channel` | **L1 Mandatory** | контекст установки, потрібен для статистики релізів |
| `game_folder_path` | **L2 Diagnostic** | немає доведеного споживача в Release Health/Incidents/Statistics → Diagnostic (для пошуку конкретної машини) |
| `install_source` | **L2 Diagnostic** | не доведено, що потрібен для дашборду → Diagnostic |
| `os_build` | **L2 Diagnostic** | деталізація OS, корисна для debug |



## 5.9. Аудит .Track() емітерів за рівнями Policy (39 викликів у коді)

> Verified через grep `.Track(` у SCLOCVerse/**/*.cs (2026-07-07). Кожен емітер класифіковано.

### 5.9.1. Level 1 — Mandatory (завжди відправляється)

| Емітер | Файл:рядок | Component/Operation | Outcome | Category |
|---|---|---|---|---|
| App Start | `App.xaml.cs:81` | Application/Start | Started | Operational |
| Auth Success/Failed | `AuthService.cs:297` | Auth/{operation} | Success/Failed | Critical (Failed) / Operational |
| Installation Sync | `InstallationService.cs:152` | Installation/Sync | Success/Failed | Critical (Failed) / Operational |
| Orchestrator Cycle Failed | `BackgroundUpdateMonitor.cs:131` | Orchestrator/Cycle | Failed | Critical |
| App Update Found | `BackgroundUpdateMonitor.cs:148` | Orchestrator/AppCheck | UpdateFound | Operational |
| App Check Failed | `BackgroundUpdateMonitor.cs:153` | Orchestrator/AppCheck | Failed | Critical |
| Localization Check outcome | `BackgroundUpdateMonitor.cs:198` | Orchestrator/LocalizationCheck | Success/Failed/UpToDate | Operational/Critical |
| Localization Check Failed | `BackgroundUpdateMonitor.cs:207` | Orchestrator/LocalizationCheck | Failed | Critical |
| LIA Update Found | `BackgroundUpdateMonitor.cs:224` | Orchestrator/LiaCheck | UpdateFound | Operational |
| LIA Check Failed | `BackgroundUpdateMonitor.cs:229` | Orchestrator/LiaCheck | Failed | Critical |
| App Update Download **Failed** | `UpdateDownloader.cs:69` | Updater/Download | Failed | Critical |
| App Update Verify **Failed** | `UpdateVerifier.cs:46,59,65` | Updater/Verify | Failed | Critical |
| App Update Install **Failed** | `UpdateInstaller.cs:46,76,82` | Updater/Install | Failed | Critical |
| LIA Install **Succeeded** | `Updater.cs:265` | LIA/Install | Succeeded | Operational |
| LIA Install **Failed** | `Updater.cs:273` | LIA/Install | Failed | Critical |
| LIA Download **Failed (terminal)** | `Updater.cs:226,246` | LIA/Download | Failed | Critical |

**16 емітерів.** Усі Failed + Succeeded (фінальні) + lifecycle (Start, UpdateFound, Sync).

### 5.9.2. Level 2 — Diagnostic (лише при Developer Diagnostics ON)

| Емітер | Файл:рядок | Component/Operation | Outcome | Category |
|---|---|---|---|---|
| App Update Download **Started/Succeeded** | `UpdateDownloader.cs:46,64` | Updater/Download | Started/Succeeded | Diagnostic |
| App Update Verify **Started/Skipped/Succeeded** | `UpdateVerifier.cs:34,40,59` | Updater/Verify | Started/Skipped/Succeeded | Diagnostic |
| App Update Install **Started/Succeeded** | `UpdateInstaller.cs:40,76` | Updater/Install | Started/Succeeded | Diagnostic |
| LIA Install **Started** | `Updater.cs:201,259` | LIA/Install | Started | Diagnostic |
| LIA Download **Started/Succeeded (cascade)** | `Updater.cs:217,231,238,251` | LIA/Download | Started/Succeeded | Diagnostic |
| LIA RunInstallerScript (Started/Succeeded/Failed ×3) | `Updater.cs:555,574,589,597,604` | LIA/RunInstallerScript | * | Diagnostic |
| `detail.phase` (orchestrationPhase) | UpdateEvents/LiaEvents | — | — | Diagnostic |
| `detail.retry_count` (FUTURE) | — | — | — | Diagnostic |
| `duration_ms` (на всіх L2) | UpdateEvents/LiaEvents | — | — | Diagnostic |
| `hresult`, `exception_type`, `error_message` (stack) | ErrorContextExtractor | — | — | Diagnostic |
| LIA forensic payload (`certificate_*`, `installer_type`, `package_version`, `activity_id`, `powershell_exit_code`, `appx_log`) | LiaForensicParser → detail | — | — | Diagnostic |
| `machine_id`, `os_version`, `os_build` (install context) | InstallationService | — | — | Diagnostic |

**~23 емітера + fields.** Проміжні кроки, forensic payload, detailed diagnostics.

### 5.9.3. Level 3 — Local Only (ніколи не відправляється)

| Дані | Де | Зараз відправляється? | План |
|---|---|---|---|
| Hotkey events (key pressed, key-up) | `HotkeyService.cs`, `RawInputBackend` | ❌ ні (вже локально) | Local journal (future) |
| Debug.WriteLine traces | `AuthService.cs:381`, `HotkeyService.cs:268`, `LiaEvents` debug | ❌ ні | Local journal |
| InputDiagnostics (HWND, window title, key sequence) | `InputDiagnostics.cs:37` | ❌ ні (file log) | Stay local |
| HangarTimer cycle ms, FPS | `HangarTimerService.cs`, `HomeCanvas.xaml.cs` | ❌ ні | Local journal |
| Performance counters | (future) | ❌ ні | Local journal |

**0 .Track() викликів.** Усі Level 3 дані вже локальні — це правильно. Архітектурне правило: нова перформанс/вхідна телеметрія — лише локально.

### 5.9.4. Підсумок аудиту

| Рівень | .Track() викликів | Очікуваний обсяг даних (за місяць на 1000 користувачів) |
|---|---:|---|
| **L1 Mandatory** | 16 | ~50 000 подій (5/користувач/місяць × 10 результатів) |
| **L2 Diagnostic** | ~23 | ~500 000 подій при ON, 0 при OFF (opt-in) |
| **L3 Local** | 0 .Track() | локально (file journal) |

**Зараз у production:** усі 39 викликів L1+L2 відправляються завжди (чекбокс мертвий). Після Phase 3.5: L2 відправляється лише при ON → **очікується суттєве зменшення** навантаження на БД для користувачів з OFF (попередня оцінка, не підтверджена вимірюваннями).

---

## 5.10. Telemetry Event Registry (Phase 3.5 forensic, 2026-07-07)

> **Single Source of Truth для кожного `.Track()` емітера.** Кожен має Level + Reason. Після цього рішення ніхто не гадатиме, чому саме ця подія Mandatory.

### 5.10.1. Event Registry — 39 емітерів

| # | Component/Operation/Outcome | Level | Файл:рядок | Reason (чому цей рівень) |
|---|---|---|---|---|
| 1 | Application/Start/Started | **L1** | `App.xaml.cs:81` | Release adoption rate — скільки користувачів запустило застосунок |
| 2 | Auth/Login (або /Callback)/Success | **L1** | `AuthService.cs:297` | OAuth success rate, без нього не працює Release Health |
| 3 | Auth/Login (або /Callback)/Failed | **L1** | `AuthService.cs:297` | Auth failure rate — критичний інцидент |
| 4 | Installation/Sync/Success | **L1** | `InstallationService.cs:152` | Installation sync success rate |
| 5 | Installation/Sync/Failed | **L1** | `InstallationService.cs:152` | 42501 або інша помилка синхронізації (критичний інцидент) |
| 6 | Orchestrator/Cycle/Failed | **L1** | `BackgroundUpdateMonitor.cs:131` | Збій циклу оновлень — інфраструктурна проблема |
| 7 | Orchestrator/AppCheck/UpdateFound | **L1** | `BackgroundUpdateMonitor.cs:148` | Adoption нових релізів |
| 8 | Orchestrator/AppCheck/Failed | **L1** | `BackgroundUpdateMonitor.cs:153` | Не вдалося перевірити оновлення |
| 9 | Orchestrator/LocalizationCheck/{outcome} | **L1** | `BackgroundUpdateMonitor.cs:198` | Localization pipeline health |
| 10 | Orchestrator/LocalizationCheck/Failed | **L1** | `BackgroundUpdateMonitor.cs:207` | Критичний збій локалізації |
| 11 | Orchestrator/LiaCheck/UpdateFound | **L1** | `BackgroundUpdateMonitor.cs:224` | LIA adoption |
| 12 | Orchestrator/LiaCheck/Failed | **L1** | `BackgroundUpdateMonitor.cs:229` | Не вдалося перевірити LIA оновлення |
| 13 | Updater/Download/Failed (terminal) | **L1** | `UpdateDownloader.cs:69` | Критичний збій завантаження оновлення застосунку |
| 14 | Updater/Verify/Failed (terminal) | **L1** | `UpdateVerifier.cs:46,59,65` | Критичний збій перевірки checksum/Authenticode |
| 15 | Updater/Install/Failed (terminal) | **L1** | `UpdateInstaller.cs:46,76,82` | Критичний збій встановлення оновлення |
| 16 | LIA/Install/Succeeded | **L1** | `Updater.cs:265` | LIA install success rate (фінальний) |
| 17 | LIA/Install/Failed (terminal) | **L1** | `Updater.cs:273` | Критичний збій LIA встановлення (фінальний) |
| 18 | LIA/Download/Failed (terminal) | **L1** | `Updater.cs:226,246` | Не вдалося завантажити LIA package або cert |
| 19 | Updater/Download/Started | **L2** | `UpdateDownloader.cs:46` | Проміжний крок, корисний лише для trace діагностики |
| 20 | Updater/Download/Succeeded | **L2** | `UpdateDownloader.cs:64` | Не потрібно для release health (маємо Failed фінальний) |
| 21 | Updater/Verify/Started | **L2** | `UpdateVerifier.cs:34` | Проміжний крок verify |
| 22 | Updater/Verify/Skipped | **L2** | `UpdateVerifier.cs:40` | NoChecksum — діагностична інформація |
| 23 | Updater/Verify/Succeeded | **L2** | `UpdateVerifier.cs:59` | Проміжний крок verify |
| 24 | Updater/Install/Started | **L2** | `UpdateInstaller.cs:40` | Проміжний крок install |
| 25 | Updater/Install/Succeeded | **L2** | `UpdateInstaller.cs:76` | Проміжний (для cascade trace) |
| 26 | LIA/Install/Started (orchestration) | **L2** | `Updater.cs:201,259` | Проміжний крок LIA install |
| 27 | LIA/Download/Started | **L2** | `Updater.cs:217,238` | Проміжний крок LIA download (InstallerAsset, CertificateAsset) |
| 28 | LIA/Download/Succeeded | **L2** | `Updater.cs:231,251` | Проміжний крок LIA download |
| 29 | LIA/RunInstallerScript/Started | **L2** | `Updater.cs:555` | Детальний trace PowerShell script execution |
| 30 | LIA/RunInstallerScript/Succeeded | **L2** | `Updater.cs:604` | Детальний trace |
| 31 | LIA/RunInstallerScript/Failed ×3 (cascade) | **L2** | `Updater.cs:574,589,597` | Детальний trace з different fallback exception |

**Всього: 18 L1 + 21 L2 = 39 емітерів** (уточнено: раніше 16/23, фактично 18/21 після перегляду Succeeded/Skipped).

### 5.10.2. Field Registry — поля `TelemetryContext` за рівнями

> Подія може бути L1, але окремі її поля L2. Наприклад `LIA/Install/Failed` (L1) несе `detail.certificate_thumbprint` (L2 forensic).

| Поле | Level | Reason |
|---|---|---|
| `error_message` (text) | **L1** (для Failed) | Обов'язкове для аналізу інциденту |
| `source` (text) | **L1** | Класифікація джерела помилки (Supabase/Network/PowerShell/COM/CLR) — для signal COALESCE |
| `hresult` (text) | **L1** (для LIA Failed) | Signal для incident fingerprint (priority 3 в COALESCE) |
| `exception_type` (text) | **L1** (для Failed) | Тип винятку для класифікації |
| `supabase_code` (text) | **L2** (DEPRECATED, завжди NULL) | Зараз ніколи не пишеться, але CHECK вимагає колонку (див. §5.6) |
| `http_status` (int) | **L2** (DEPRECATED, завжди NULL) | Те саме |
| `duration_ms` (int) | **L2** | Тривалість операції — оптимізація performance, не release health |
| `detail.phase` | **L2** | orchestrationPhase для cascade trace |
| `detail.retry_count` | **L2** | FUTURE (Retry Policy заготовка, §5.7) |
| `detail.installer_type` | **L2** | LIA forensic (MSIX/AppX/Zip) |
| `detail.package_version` | **L2** | LIA forensic — конкретна версія package |
| `detail.certificate_present` | **L2** | LIA forensic — чи був cert |
| `detail.certificate_subject` | **L2** | LIA forensic — суб'єкт cert (publisher) |
| `detail.certificate_thumbprint` | **L2** | LIA forensic — thumbprint cert |
| `detail.powershell_exit_code` | **L2** | LIA forensic — exit code PowerShell script |
| `detail.activity_id` | **L2** | LIA forensic — correlation ActivityId |
| `detail.appx_log` | **L2** | LIA forensic — Event Viewer AppX dump |
| `detail.signal_name` | **L2** | LIA forensic — HResultCatalog.ResolveSymbol (в коді ErrorContextExtractor:204, але 0 зустрічей у даних — LiaInstallException з Hresult рідкісний) |

### 5.10.3. Підсумок

- **18 L1 подій** (Failed термінальні + lifecycle Succeeded) — завжди.
- **21 L2 подій** (проміжні Started/Succeeded/Skipped, Cascade trace).
- **9 L1 полів** у context (error_message, source, hresult, exception_type, + БД-колонки install_id/user_id/occurred_at/received_at/session_id/correlation_id).
- **10 L2 полів** (duration_ms, detail.phase, detail.retry_count, cert-поля, appx_log, signal_name).
- **0 L3 полів** у .Track() (всі L3 — debug, hotkeys, perf — вже локальні).

**Реалізація:** кожен з 21 L2 емітерів додасть `level: TelemetryLevel.Diagnostic` у виклик `.Track()`. Інші 18 — дефолтний L1 (без параметра).




> Observability = Telemetry (розділ 5) + Incident Pipeline + Notification System (розділ 12) + Knowledge Engine (розділ 13) + Control Center (розділ 11).

## 6.1. Incident Pipeline (детально)

> Деталі: `Observability-RC1-Release.md`, `FORENSIC-DATA-PIPELINE-DETAIL.md`.

**Потік:**
```
telemetry_events (Failed) ─trigger─▶ promote_incident_candidates_for_event()
                                              │
                                              ▼
                                  telemetry_incidents (INSERT/UPDATE)
                                              │
                          ┌───────────────────┼───────────────────┐
                          ▼                   ▼                   ▼
                  notification_queue    match_knowledge_*    CC views
                          │
                          ▼
                  Notifier Worker ──▶ notification_attempts + Discord
```

**Детекція:**
- Trigger `trg_telemetry_failed_promote` AFTER INSERT WHEN `outcome='Failed'` → `promote_incident_candidates_for_event(event_id)`.
- Signal-групування: `signal = COALESCE(source, hresult, supabase_code, http_status::text, exception_type, '-')`.
- Поріг per-component з `incident_policy`: failure_pct + sample-guard.
- Severity: Critical (≥10 affected OR failure_pct>50%) / Warning.

**Lifecycle:** `Detected → Confirmed (≥15 хв анти-флап) → Monitoring → Resolved (rate<поріг ≥30 хв) → Closed`.
- Усі переходи через `transition_incident()` (SECURITY DEFINER, forward-only валідація) → UPDATE status + INSERT в `incident_status_log` (immutable).
- Додатково: `add_incident_note()` (append-only), `assign_incident_owner()`, `auto_close_stale_incidents()`.

> ⚠ **F2 (P0):** enum `telemetry_incidents.status` (CHECK) НЕ містить `Mitigated`/`Acknowledged`, які використовують `transition_incident` та `create_knowledge_from_incident`. Потрібно розширити CHECK.

## 6.2. Release Health (цільовий vs реальний)

| Артефакт | Стан |
|---|---|
| `control_center.release_health` VIEW | ✅ жива (`app_version, succeeded, failed, active_installs`) |
| `control_center.release_health_detail` VIEW | ✅ створена (міграція 05021200) — раніше була P0-прогалина (F1) |
| `control_center.component_health`, `platform_stats` | ✅ живі |
| Матеріалізація 3 views (Phase 3) | ❌ відкладено (medium risk, потребує тестування) |

---

# 7. Authentication

> Деталі: `SCLOCVerse/docs/Auth-StateMachine.md`, `.kilo/adr/ADR-001`, `.kilo/adr/ADR-002`, `PRIVACY.md`.

## 7.1. Модель

- **Обов'язкова Discord-авторизація.** Без неї користувач не потрапляє в Main UI.
- **Єдине джерело істини:** enum `AuthState = { Unknown, Checking, SignedOut, SigningIn, SignedIn, Error }` через `IAuthStatusProvider.State` + `StatusChanged`. **Без прапорців** `IsAuthenticated`.
- 2 режими: Auth Gate Mode / Main UI Mode (перемикання лише за `AuthState`).

## 7.2. Провайдер

Supabase GoTrue + Discord OAuth, **PKCE**, scope `identify` only.
- Discord `client_id=1519140665940770946`.
- Redirect: Supabase-проксі `https://nrytczdbhehiotflaagl.supabase.co/auth/v1/callback` + локальний loopback у клієнті.

## 7.3. Redirect-механізм

**Loopback Redirect** (ADR-001 .kilo): `LoopbackCallbackListener` через `HttpListener` на `http://127.0.0.1:<випадковий порт>/auth/callback`. Supabase allow-list `http://localhost:*/auth/callback`. Custom Protocol Handler відхилено.

## 7.4. Сесія

- `SecureSessionStorage`, файл `.auth` у `%LocalAppData%\SCLOCVerse`, **DPAPI CurrentUser**.
- SignOut = global token revoke.
- `access_denied` → SignedOut без діалогу помилки.

## 7.5. Installation Sync

`InstallationService.SyncCurrentInstallationAsync` викликається при SignIn/RestoreSession — SELECT (filter `install_id`) + INSERT (нова) / UPDATE (існуюча). Мапить 11 з 19 колонок `app_installations` (див. розділ 4).

## 7.6. Відомий борг

- OAuth provider **hardcoded to Discord** (`AuthService.cs:69-76`) — config-driven у roadmap Року 2.
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
| `FolderSearchService` | `Services/Common/FolderSearchService.cs` | Валідація `StarCitizen` root (потрібна підтримувана підпапка середовища) |
| `GitHubReleaseClient` | `Services/.../GitHubReleaseClient.cs` | Fetch релізів локалізації |
| `LocalizationMetadata` | (record) | Per-environment meta.json: `assetId, etag, sha256, fileSize, lastModified` |

## 8.2. Дані

- Зберігається у `%LocalAppData%\SCLOCVerse\<envName>.meta.json` (per-environment).
- **`tagName` (semver версія локалізації) НЕ зберігається** в meta.json — лише `assetId/etag/sha256`. Відомо лише під час Install/Update з `release.TagName`.
- Після перезапуску програма не знає встановленої версії локалізації (див. розділ 4: план `IInstallationContextProvider`).

## 8.3. Game Folder

- Джерело: `Settings.Default.GameFolder` (string, User scope) через `ISettingsService.GetGameFolder()`.
- Валідація `FolderSearchService.IsValidGameRoot`: лише папка `StarCitizen` з підтримуваною підпапкою середовища.
- **PII-ризик відсутній** — шлях не містить імені користувача Windows (RSI Launcher → `C:\Program Files\Roberts Space Industries\StarCitizen\` або окремий диск).

## 8.4. Телеметрія

**0 телеметричних подій** у `LocalizationInstaller` — сліпа зона. Встановлення/оновлення локалізації не фіксується.

---

# 9. L.I.A.

> Деталі: `docs/LIA_INSTALLATION.md`, `FORENSIC-DATA-PIPELINE-DETAIL.md`.

## 9.1. Продукт

Голосовий асистент, MSIX/AppX-пакет; автор — AlexLiberty (Alexuß). Встановлюється через `Add-AppxPackage` (PowerShell). Оркеструє оновлення `Updater` (інтерфейс `IUpdater`, `static readonly HttpClient` без timeout).

## 9.2. Сертифікат (self-signed)

- `CN=Alexuß`, thumbprint `33DD2416B9CC3DA94A84A479AD63D07C4B322833`.
- Імпорт у **`Cert:\LocalMachine\Root` ТА `Cert:\LocalMachine\TrustedPeople`** через `Import-Certificate` (CryptoAPI `CertAddCertificateContextToStore`). `CurrentUser` відкинуто як недостатній для AppX/MSIX deployment trust (перевіряє лише `HKLM`).

## 9.3. Elevation

- Лише під час Install L.I.A. через `Process.Start` з `Verb="runas"` (принцип найменших привілеїв). **НЕ** `requireAdministrator` app.manifest.
- `RunPowerShellAsync(script, ct, requireElevation)`:
  - `requireElevation=false`: `UseShellExecute=false` + `RedirectStandardOutput=true`.
  - `requireElevation=true`: `UseShellExecute=true` + `Verb="runas"`; транспорт stdout через `wrapper.ps1` + тимчасові файли **UTF-8 без BOM**.
- Контракт `PowerShellResult = (int ExitCode, string Output, string Error)` — однаковий в обох режимах.

## 9.4. Forensic-контракт

Маркер `##SCLOC_FORENSIC##` + JSON у stdout (hresult, phase, message, activityId, appxLog, cert context). Парсинг через `LiaForensicParser.TryParse` → `LiaInstallException` → `ErrorContextExtractor.ApplyLiaForensic` → `TelemetryContext.Detail`.

## 9.5. Кодування

`EncodingPreamble` + `[Console]::OutputEncoding=UTF8` + `StandardOutputEncoding=UTF8` — коректна обробка OEM/UTF-8 пастки PowerShell 5.1.

## 9.6. Відкриті ризики (additive-only)

| # | Ризик | Стан |
|---|---|---|
| SEC-2 | Інсталятор без integrity check (тільки розмір) + elevation | відкрито |
| SEC-3 | Довільний `.cer` → `LocalMachine\Root`+`TrustedPeople` **без pin** | відкрито |
| — | `CERT_E_UNTRUSTEDROOT` (0x800B0109) на свіжих Windows | навмисно відкладено до 1.0.0.2 |
| TD-10 | PowerShell без timeout (`ct=None` з UI) | відкрито |
| L-A6 | Orphaned elevated PowerShell-процес при cancellation | відкрито |

---

# 10. Security

> Деталі: `PRIVACY.md`, `SCLOCVerse/docs/Privacy-Design.md`, `CODE_SIGNING_POLICY.md`, `docs/architecture/Final-Architecture-Review.md`.

## 10.1. PII-політика (мінімізація)

- **Identity Layer:** `discord_user_id`, `username`/`global_name`, `avatar_url`.
- **Technical Metadata:** `install_id`, `machine_id`, `platform`, `os_version`, `app_version`, UTC-мітки.
- **НЕ збираються:** email (ігнорується), паролі, платіжні, адреси, біометрія, guild list, поведінкова телеметрія.
- `EXTERNAL_DISCORD_EMAIL_OPTIONAL` увімкнено.

## 10.2. RLS

Скрізь. `anon` deny-all; `authenticated` owner-only; `telemetry_events` append-only. Контроль через `Database-Verification.md` (DO-блок під реальною роллю `authenticated`).

## 10.3. OAuth-захист

PKCE, HTML-encoding на callback, SignOut = global revoke, **без `service_role` у клієнті**, SQL parameterized, PowerShell single-quote escaping.

## 10.4. Code signing (supply chain)

**SignPath.io** + сертифікат **SignPath Foundation**. Підписуються `SCLOCVerse.exe` та `SCLOC-Verse_Setup.exe` у верифікованому автоматичному білді з вихідного коду GitHub. Без DLL injection / зміни пам'яті / античиту.

## 10.5. Цілісність/ланцюг постачання — прогалини

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
| SEC-11 | `SECURITY DEFINER` без `SET search_path` (~20 функцій) | 🟠 |
| SEC-12 | `cc_readonly` фактично write-capable через definer-функції | 🟠 |

## 10.6. Права користувача

Інформація, виправлення, видалення акаунта, відкликання OAuth. Повернення додаткових scope (email/guilds) — gated на Product Review.

---

# 11. Control Center

> Деталі: `docs/contracts/control_center.md`, `FORENSIC-DATA-PIPELINE-RAW.md` (розділ 5).

## 11.1. Структура

Blazor Server. 6 сторінок: `Home.razor`, `Incidents.razor`, `Traces.razor`, `Releases.razor`, `Knowledge.razor`, `Settings.razor` (placeholder).
2 репозиторії: `ControlCenterRepository` (Npgsql + `control_center` схему + SECURITY DEFINER функції), `TraceRepository` (`control_center.telemetry_events`).
Сервіс: `PiiSanitizer`.

## 11.2. Колонки, що реально використовуються по сторінках

| Сторінка | Джерело | Критичні колонки |
|---|---|---|
| `Home` | `observability_health`, `component_health`, `release_health`, `platform_stats`, `knowledge_coverage`, `top_missing_knowledge` | `active_installations_last_7d`, `component`, `health`, `active_incidents`, `app_version`, `success_rate`, `failed`, `events_24h`, `active_users_24h`, `open_incidents`, `coverage_pct` |
| `Incidents` | `incidents`, `incident_timeline`, `incident_notes_view`, knowledge функції | `incident_id`, `status`, `highest_severity`, `component`, `signal`, `event_count`, `affected_users`, `affected_installs`, `peak_failure_pct`, `opened_at`, `last_event_at`, `owner` |
| `Traces` | `telemetry_events` | `correlation_id`, `session_id`, `step`, `install_id`, `occurred_at`, `component`, `operation`, `outcome`, `severity`, `error_message`, `duration_ms`, `detail` |
| `Releases` | `release_health_detail`, `knowledge_coverage` | `app_version`, `success_rate`, `succeeded`, `failed`, `active_installs`, `new_incidents`, `critical_incidents`, `top_fingerprint` |
| `Knowledge` | `knowledge_coverage`, `top_missing_knowledge`, `knowledge_list`, knowledge функції | `coverage_pct`, `component`, `signal`, `title`, `status`, `confidence`, `fixed_version` |
| `Settings` | — | placeholder, без запитів |

## 11.3. Контракт

- Supabase = SSOT; read-only View-контракт `control_center`; versioning через `contract_info`.
- `cc_readonly` — `USAGE`+`SELECT` лише на `control_center` + EXECUTE на workflow/knowledge функції (Стаття 20+23).
- Бізнес-логіка в SQL VIEWs/функціях, не в Blazor (Стаття 20).

---

# 12. Notification System

> Деталі: `Observability-RC1-Release.md`, `FORENSIC-DATA-PIPELINE-DETAIL.md` (розділ 6).

## 12.1. Архітектура

Incident Engine НЕ відправляє повідомлення напряму (Стаття 24). Пише в `notification_queue` → `SCLOCVerse.Notifier` Worker → `INotificationProvider` (контракт у `SCLOCVerse.Notifications`) → `DiscordNotificationProvider`.

## 12.2. `notification_queue` (16 колонок)

Стани: `Pending → Sending → Delivered | RetryScheduled | Failed`. Zombie Recovery `Sending>10хв → RetryScheduled`. `UNIQUE(incident_id, notification_type) WHERE status != 'Failed'`. Worker poll 30с, `FOR UPDATE SKIP LOCKED` (безпечно для 2+ інстансів).

## 12.3. `notification_attempts` (10 колонок)

Append-only аудит кожної спроби: `queue_id, attempt_no, provider, status, http_status, provider_message_id, error_message, started_at, finished_at`.

## 12.4. Retry/backoff

`next_attempt_at = now + 2^attemptNo секунд`. `max_retries` default 3.

## 12.5. Поля, що реально потрібні Notifier

| Таблиця | Читання | Запис |
|---|---|---|
| `notification_queue` | `id, incident_id, notification_type, provider, payload, retry_count, max_retries, status, next_attempt_at, claimed_at, claimed_by, created_at` | `status, retry_count, next_attempt_at, claimed_at, claimed_by, last_attempt_at, delivered_at, last_error, error_message` |
| `telemetry_incidents` | `id, component, operation, signal, highest_severity, release` | — |
| `notification_attempts` | — | `queue_id, attempt_no, provider, status, http_status, provider_message_id, error_message, started_at, finished_at` |

> ~~**Дублікат:** `notification_queue.error_message` ↔ `last_error` — обидва містять помилку (рішення: об'єднати).~~
>
> ❌ **ПЕРЕВІРЕНО Phase 2 forensic (2026-07-07): це НЕ дубль, різна семантика.** Див. §12.6.

## 12.6. Семантика `error_message` vs `last_error` (Phase 2 forensic, 2026-07-07)

| Колонка | Створена | Коміт | Первісне призначення | Хто пише |
|---|---|---|---|---|
| `error_message` | міграція `00016` (RC6) | `378fc47` Phase 5b/RC6 | фінальна помилка черги (legacy інтенція до retry pipeline) | ❌ ніхто (Notifier не пише) |
| `last_error` | міграція `00017` (ALTER, audit+retry) | Phase 5b retry pipeline | помилка **останньої спроби** (Стаття 26, оновлюється кожен retry) | ✅ Notifier (фінальний UPDATE queue) |

**Різниця семантик:**
- `error_message` — фінал (після `max_retries`, остаточний стан черги).
- `last_error` — остання спроба (проміжна в retry-циклі).

**Статус після 00017:** Notifier перейшов на `last_error`. `error_message` залишилась як **де-факто deprecated**, але не прибрана через additive-only.

**Рішення (Phase 2):** НЕ MERGE (різна семантика). Варіанти:
- (a) **NOTHING** — лишити статус-кво (поточний стан).
- (b) **SEMANTIC SPLIT** — додати в Notifier запис `error_message` лише при фінальному `status='Failed'` після `max_retries` (окрема архітектурна задача, поза Database Cleanup).
- (c) **DEprecate contract** — позначити в SQL-коментарі + KB як deprecated (без зміни коду).

Погоджено варіант **(c)** — фіксація статус-кво в KB без зміни коду.

---

# 13. Knowledge Engine

> Деталі: `docs/observability/Knowledge-Engine-Design.md`, `Knowledge-Engine-Design-Review.md`. **Phase 6 завершено** (commit `e1357dc`), **API Freeze v1.0**.

## 13.1. Таблиці (схема `public`)

1. **`knowledge_entries`** (18 колонок): PK, `FingerprintKey`/`FingerprintHash`, `Title`/`Symptoms`/`KnownCause`/`Workaround`/`PermanentFix`, `AffectedVersions text[]`, `FixedVersion`, `Confidence`/`Status` enums, `CreatedBy`/`UpdatedBy`.
2. **`knowledge_references`** (5 колонок): 1:N, `ReferenceType ∈ {GitCommit, GitHubIssue, Documentation, ReleaseNotes, External}`, `ON DELETE RESTRICT`.
3. **`knowledge_version_history`** (8+1 колонок): append-only audit, `Version, Snapshot jsonb, ChangedBy (клієнт) + SessionUser (БД current_user), ChangeType`.

## 13.2. Інваріант (CHECK на рівні схеми)

- `status='Verified' → confidence ∈ {High, Verified}`.
- `status ∈ {Draft, Reviewed} → confidence ∈ {Low, Medium, High}`.
- `status ∈ {Deprecated, Archived}` — будь-яка.

## 13.3. Workflow

`Draft → Reviewed → Verified → Deprecated → Archived`.
- Publish (Draft→Reviewed), Verify (Reviewed→Verified, вимагає Confidence ≥ High), ReturnForRevision, Deprecate (обов'язковий ChangeReason), Reopen (Deprecated→Reviewed, Verified→Reviewed), Archive (фінальний).
- ❌ `Archived → *` заборонено.
- Усі переходи через `transition_knowledge()` (SECURITY DEFINER, optimistic concurrency через `expected_version`).

## 13.4. Confidence

`Low → Medium → High → Verified`.
- Low автоматично (початкове).
- Low → Medium вручну.
- Medium → High: додано `PermanentFix` + ≥1 `GitCommit` reference.
- High → Verified: **автоматично** через `verify_knowledge_auto()`.

## 13.5. Auto-verify (5 детермінованих умов)

1. `FixedVersion IS NOT NULL`.
2. Реліз R з `install_count ≥ 50` за 14 днів.
3. `success_rate(component, operation, FixedVersion) ≥ 0.95`.
4. 0 інцидентів з тим самим `fingerprint_key` з `opened_at >= release_date(FixedVersion)`.
5. Confidence = High.

## 13.6. Matching (3 рівні)

- **Priority 1 (Exact Fingerprint):** `fingerprint_hash` + `status='Verified'` LIMIT 1.
- **Priority 2 (Component + Signal):** Verified + AffectedVersions match + FixedVersion > release; ORDER BY confidence DESC, updated_at DESC.
- **Priority 3 (Manual):** оператор через `search_knowledge`.
- UI: 🟢 Priority 1 / 🟡 Priority 2 / ⚪ кнопка Search.

## 13.7. Coverage

Materialized VIEW `control_center.knowledge_coverage` (per-release). REFRESH через `refresh_knowledge_coverage()` після Workflow + trigger на incidents. `top_missing_knowledge` VIEW для пріоритезації досліджень.

## 13.8. API Freeze v1.0 (Phase 7 межі)

- **Дозволено:** нові таблиці (embeddings, ai_suggestions), нові функції, нові VIEW, адитивні nullable-колонки з DEFAULT, окремі extensions.
- **Заборонено:** `ALTER TABLE NOT NULL`, зміна сигнатур існуючих функцій, DROP функцій/VIEW, зміна CHECK-інваріантів, зміна workflow-правил/алгоритму matching/auto-verify.

---

# 14. Approved Decisions (майстер-список)

> Об'єднано архітектурні (38) + observability (66) рішення, дедупліковано. Джерела в розділі 18.

## 14.1. Архітектура / DI / Проєкт

1. Ручний Composition Root (`AppCompositionRoot` + `AuthCompositionRoot`), без IoC.
2. 4 процесоізольовані проєкти; спілкування лише через Postgres.
3. Без `Microsoft.Extensions.Hosting` (Generic Host); lifetime через WPF Application.
4. `MainWindow` як UI-координатор (code-behind); бізнес-логіка в сервісах/presenter-ах.
5. Overlay як окреме вікно + `HangarOverlayService` (Win32 click-through).
6. Canvas-навігація через `CanvasManager` у межах одного `MainWindow`.
7. 2 бекенди гарячих клавіш; `RawInputBackend` — default; env `SCLOCVERSE_HOTKEY_BACKEND`.
8. Виділення `IKeyStateBackend` (ISP) для `KeyUp`.
9. .NET 9, Nullable Enabled; код англійською; коментарі/документація українською.
10. UTF-8 як P0 для всіх текстових файлів; явний `Encoding.UTF8`.
11. Async-суфікс; `CancellationToken`; `ConfigureAwait(false)` вибірково; без `async void` окрім WPF-обробників.
12. Zero Regression — стабільний код не чіпати без потреби.

## 14.2. Auth / Privacy / Supply Chain

13. Loopback Redirect для OAuth (allow-list `http://localhost:*/auth/callback`).
14. Кнопка акаунта у верхньому тайтлбарі (`BtnAccount`, `AccountDialog`, `AuthStatusPresenter`).
15. Обов'язкова Discord-авторизація; єдиний `AuthState` (6 станів); 2 режими; `MainWindow` stateless re auth.
16. OAuth scope лише `identify`; мінімізація даних.
17. DPAPI CurrentUser для локальних сесійних токенів.
18. RLS everywhere.
19. Least-privilege `cc_readonly`.
20. Supabase = SSOT; read-only View-контракт `control_center`; versioning через `contract_info`.
21. Code signing через SignPath.io + SignPath Foundation.
22. MIT-ліцензія застосунку.
23. Без DLL injection / зміни пам'яті / античиту.
24. `EXTERNAL_DISCORD_EMAIL_OPTIONAL`; email не зберігається/відображається.
25. `access_denied` → SignedOut без діалогу помилки.

## 14.3. L.I.A.

26. Cert → `LocalMachine\Root` + `LocalMachine\TrustedPeople`.
27. Elevation лише під час Install через `Verb="runas"` (не `requireAdministrator`).
28. Elevated-транспорт через `wrapper.ps1` + UTF-8-no-BOM файли.
29. Forensic-контракт `##SCLOC_FORENSIC##` + JSON; `LiaForensicParser.TryParse`.
30. Єдиний контракт `PowerShellResult (ExitCode, Output, Error)`.

## 14.4. Observability / Telemetry / Інциденти

31. `ITelemetryService` — єдиний санкціонований канал (Стаття 7/12).
32. Additive-only контракт схеми (Стаття 13).
33. Append-only `telemetry_events` з RLS authenticated-INSERT-only (Стаття 5).
34. `promote_incident_candidates()` SECURITY DEFINER, лише SELECT events + INSERT incidents (Стаття 19).
35. Dashboard Purity: `cc_readonly` лише SELECT VIEWs (Стаття 20).
36. Workflow через SECURITY DEFINER функції + GRANT EXECUTE (Стаття 23).
37. `INotificationProvider` контракт у окремій бібліотеці (Стаття 27).
38. Knowledge Engine виключно ручного походження (Стаття 28).
39. Three-tier secret model Git/DB/App, P0 для порушень (Стаття 29).
40. Runtime DB verification під роллю `authenticated` (Стаття 17).
41. Ambient `TraceContext` з `Application.Start` (Стаття 11).
42. Глобальні exception handlers у `App.OnStartup` (Стаття 8).
43. `PrivacySanitizer` як єдине вузьке горло (Стаття 4).
44. Idempotent retries через `UNIQUE(client_event_id)` + `ON CONFLICT DO NOTHING` (Стаття 6).
45. Sync O(1) hot path; усе I/O у фоновому потоці з `ConfigureAwait(false)` (Стаття 2).
46. `install_id` (file+registry) як machine identity; ніколи `MachineName`.
47. Bounded in-memory черга (cap 5000, drop-oldest).
48. Pre-auth події буферуються локально (in-memory), флешаються після логіну.
49. Інцидент = `incident_id`, не fingerprint; рецидив = новий інцидент (Стаття 21).
50. Forward-only workflow transitions в append-only `incident_status_log` (Стаття 22).
51. Per-component пороги в `incident_policy`.
52. Анти-флап ≥15 хв (Confirmed); ≥30 хв (Resolved).
53. Incident Engine не відправляє напряму; пише в `notification_queue` (Стаття 24).
54. `UNIQUE(incident_id, notification_type) WHERE status != 'Failed'` (Стаття 25).
55. Кожна спроба → append-only `notification_attempts` (Стаття 26).
56. Zombie Recovery `Sending>10хв → RetryScheduled`.
57. Worker `FOR UPDATE SKIP LOCKED`.
58. Retry backoff `2^attemptNo секунд`.
59. `cc_notifier` роль (least privilege).
60. `NotificationPayload` версіонований.

## 14.5. Knowledge Engine

61. Human Verified; автогенерація заборонена.
62. Append-Only History; DELETE заборонено RLS.
63. Dual-identity audit: `ChangedBy` + `SessionUser`.
64. CHECK-інваріант Status ↔ Confidence на рівні схеми.
65. Materialized VIEW `knowledge_coverage` з REFRESH.
66. 3 рівні matching.
67. Auto-verify `verify_knowledge_auto()` (5 умов).
68. Optimistic concurrency через `expected_version`.
69. API Freeze v1.0 (Phase 7 лише адитивні розширення).

## 14.6. Оптимізація БД (затверджені Phases)

70. Phase 0: 3 індекси (`idx_telemetry_failed` partial WHERE outcome='Failed', `idx_telemetry_version_window`, `idx_telemetry_occurred`).
71. Phase 1: видалити 11 проміжних `.Started`/`.Succeeded` емітів + перетворити `Verify.Skipped`→`Verify.Failed(Warning)`.
72. Phase 2: прибрати `detail.signal_name`, `detail.retry_count`, `Country` з C# `TelemetryEvent`.
73. Phase 3: single-scan candidates + матеріалізувати `release_health`/`platform_stats`/`component_health`.
74. Cleanup script ідемпотентний, в одній транзакції.
75. **app_installations cleanup:** лише не-UUID тестові (`install_id !~ '^[0-9a-f]{32}$'`).

## 14.7. app_installations

76. Активація `localization_version`, `game_folder_path`, `selected_environment` через `IInstallationContextProvider` (НЕ дублювати в Settings).
77. `game_folder_path` — діагностичне поле, не PII (валідація `IsValidGameRoot`).

## 14.8. Phase 2 Database Cleanup Review (2026-07-07)

78. **Phase 2 Database Cleanup Review виконано** — повний аудит схеми без реалізації. Усі 16 пунктів завдання покриті доказами з живої БД. Matrix (поновлено після forensic §14.9): 40 KEEP / 10 ACTIVATE / **0 MERGE** (обидва кандидати зняті) / 3 REMOVE (індекси) / 28 DEFER. Деталі в §16.5.
79. **`DROP INDEX` — additive-only** (не порушує схематичний контракт): дозволено для 3 доведених дублів/невикористань (`idx_app_installations_install_id`, `idx_app_installations_machine_id`, `idx_user_discord_guilds_user_id`).
80. **Generated column PostgreSQL 12** — additive спосіб кешувати `incident_code` замість формування в 4 views + Notifier. Zero Regression.
81. **`SET search_path = public, pg_catalog` у SECURITY DEFINER функціях** — additive виправлення SEC-11 (26 функцій), не змінює сигнатур (дозволено API Freeze §13.8).

## 14.9. Phase 2 forensic-аудит (2026-07-07, post-Review)

82. **26 міграцій Supabase консистентні** — pattern audit (CREATE OR REPLACE / DROP+CREATE / CREATE→DROP) підтвердив, що всі зміни необхідні для розвитку схеми. **Migration squash відхилено** (§15 #70).
83. **Dependency graph 4 VIEW** (`incidents`, `incident_timeline`, `incident_notes_view`, `notifications`): **0 inbound залежностей** в БД (ані views, ані функцій, ані тригерів). OR REPLACE безпечний — єдині залежні C# SQL-запити з явним списком колонок.
84. **`error_message` ≠ `last_error`** — різна семантика (фінал черги vs остання спроба retry). НЕ MERGE. `error_message` де-факто deprecated з міграції 00017. Деталі в §12.6.
85. **`detail.retry_count` ≠ мертва** — заготовка під плановану Retry Policy (додана в Observability Slices 3-5, коментарі в коді: `// схема готова для майбутньої Retry Policy`). НЕ REMOVE. Рішення відкладено до Retry Policy architectural decision (§17.4).
86. **`archive_knowledge_entry(bigint, text, text)`** — створена 00021, DROP 00023 (замінено на `transition_knowledge(target=Archived)`). Коректний lifecycle, не борг.
87. **`promote_incident_candidates()` (batch)** — zombie з міграції 00013 (REPLACE 00016, але не DROP). Підтверджено на рівні БД через `pg_depend=[]`. DEFER (DROP заборонено API Freeze §13.8).

## 14.10. Phase 3A Production Replica + Post-Impl Forensic (2026-07-07)

88. **Phase 3A успішно перевірено на Production Replica** (`zhdtcxvnzlvbgxariyww`, SCLOC-Verse-Test, free tier). Усі 3 міграції застосовано. Post-Implementation Forensic повністю пройшов.
89. **Generated column → trigger заміна (Post-Impl виявив):** PostgreSQL generated column вимагає IMMUTABLE expression; `EXTRACT(YEAR FROM opened_at)` для `timestamptz` — STABLE, не проходить («generation expression is not immutable»). Жодна функція timestamptz→int не може бути IMMUTABLE (залежить від session TimeZone). Замінено на звичайну text-колонку + `BEFORE INSERT/UPDATE` тригер `set_incident_code()`. Семантика ідентична.
90. **Schema Verification PASSED:** 9/9 показників production співпадають з replica (14 public tables, 1 cc table, 23 cc views, 1 matview, 1 public view, 29 public functions, 4 triggers, 47 indexes, 29 RLS policies).
91. **3 об'єкти НЕ описані в міграціях** (створені вручну в production через Dashboard, виявлено через Schema Verification diff): `control_center.release_health_detail` (view), `control_center.unfinished_started` (view), `public.ecosystem_stats()` (function). Всі додані в replica окремо. **TD: створити окрему міграцію для опису цих об'єктів** (див. §16.6).

## 14.11. Нове правило Migration Review (AGENTS.md)

92. **Generated column IMMUTABLE вимога** — занотовано в Migration Review чеклист AGENTS.md: `to_char(ts,…)` → STABLE, не підходить; `EXTRACT` для timestamptz → також STABLE; єдине рішення для timestamptz-derived computed — звичайна колонка + тригер.

## 14.12. Control Center Presentation Forensic (2026-07-07)

93. **`country` ≠ `user_countries` — НЕ дубль (forensic).** `country` = країна конкретної інсталяції (per-installation, з `app_installations.country` через тригер `set_country_from_cf`/Cloudflare cf-ipcountry). `user_countries` = агрегат `string_agg(DISTINCT country, ', ')` per-user. Production факт: 0 з 50 користувачів мають розходження (3 з мульти-інсталяціями, але всі в 1 країні). Сценарій розходження доведено: користувач з установками в UA + PL → 2 рядки в `user_analytics` (UA/UA,PL і PL/UA,PL). KEEP обидві.
94. **`control_center.users` view НЕ має споживача в C#/Blazor** (grep: 0 згадок `country`/`user_countries`/`cc.users`). Доступний лише через SQL/Dashboard. TD: вирішити долю view (використати в Phase 4 UX або визнати застарілим).
95. **Production deployment Phase 3A відкладено** до завершення Phase 4 Control Center UX & Data Presentation Optimization. Причина: за останні дні ≥5 forensic змінили матрицю рішень (error_message, retry_count, generated column, machine_id, country) → процес forensic ще активний, міграції незрілі для production. Сигнал до стабілізації: ≥3 forensic поспіль без зміни матриці.

## 14.13. Phase 3 завершення — Data Model Freeze (2026-07-07)

96. **Phase 3 не завершується міграціями, а Data Model Freeze.** Модель даних вважається оптимізованою лише після повного Data Dictionary (Source/Reader/Nullable/Category/Classification/Lifetime/Confidence для кожної колонки). Див. §4.10.
97. **Phase 4 (UX/Blazor) починається лише після Phase 3 Freeze.** НЕ змішувати оптимізацію БД з оптимізацією UI — різні фази, різні ризики.
98. **Time Zone реалізація через `ITimeZoneService`** (НЕ хардкодити `Europe/Kyiv`). БД лишається canonical UTC `timestamptz`. UI — конфігурований через інтерфейс (сьогодні Kyiv, завтра UTC або Local User Time).
99. **Classification 5 станів для кожної колонки:** `CORE` (обов'язкове для роботи), `OPTIONAL` (корисне), `DIAGNOSTIC` (діагностика), `FUTURE` (заготовка під майбутнє, напр. `retry_count`), `DEPRECATED` (застаріле, напр. `notification_queue.error_message` після 00017).
100. **Confidence (HYP/VER/IMPL/REJ) поширюється на ВСІ рішення** в KB, не лише forensic-знахідки. Кожне Approved/Rejected Decision має маркер ступеня доведеності.

## 14.14. Phase 3 Freeze Validation + Closure (2026-07-07)

101. **Freeze Validation PASSED.** Усі 172 колонки 15 таблиць мають чіткі відповіді на 6 атрибутів: Writer, Reader, Classification (CORE/OPTIONAL/DIAGNOSTIC/FUTURE/DEPRECATED), Confidence (HYP/VER/IMPL/REJ), Lifetime (FOREVER/1y/90d/30d/RESERVED), Рішення (KEEP/ACTIVATE/DEPRECATED/RESERVED). Прогалини в error_reports/admin_audit_log/user_discord_guilds усунуто (розгорнуто per-column у §4.10.13-15).
102. **Phase 3 OFFICIALLY CLOSED.** Data Model заморожено як Single Source of Truth. Подальші зміни БД — лише через зміну цієї моделі (спочатку модель → потім міграція). Phase 3A міграції (idx DROP/ADD, generated→trigger) залишаються в git, готові до production deployment **після** Phase 4.

## 14.15. Phase 4 — Data Presentation Layer (2026-07-07)

> **Перейменовано з "Control Center UX" на "Data Presentation Layer"** — точніше відображає scope (НЕ лише UX, а окремий Presentation Layer).

103. **Phase 4 scope:** Time Zone, формат дат, порядок колонок, badges, icons, NULL presentation, UUID presentation, filters, sorting, grouping. БД НЕ змінюється (canonical UTC). Лише presentation layer.
104. **`ITimeZoneService`** (abstract) — відповідає **який** часовий пояс використовувати (сьогодні `Europe/Kyiv`, завтра UTC або Local User Time). Реєстрація в `AppCompositionRoot`/Blazor DI.
105. **`IUserDateTimeFormatter`** (abstract) — відповідає **як** показувати дату (формат `dd.MM.yyyy HH:mm:ss`, `yyyy-MM-dd`, відносний час "5 хв тому"). Залежить від `ITimeZoneService`.
106. **Дворівнева архітектура presentation:**
```
ITimeZoneService (which TZ)
        ↓
IUserDateTimeFormatter (how to format)
        ↓
Blazor Components (.razor)
```
107. **Forensic baseline поточного Blazor UI** — перший крок Phase 4: які сторінки існують, які колонки відображаються, в якому порядку, які формати. Без baseline — не формувати цільову модель.

## 14.16. Phase 3.5 — Telemetry Policy & Diagnostic Levels (2026-07-07)

> **Пріоритет над Phase 4.** Без політики чекбокс мертвий, FUTURE колонки не активуються, тестовий Supabase заллється "неправильними" даними. Phase 3.5 визначає правила збору для всього SCLOC-Verse.

108. **Telemetry Policy 3 рівні** (застосовуємо замість 2 Category A/B):
    - **L1 Mandatory** — не вимикається (статистика життя продукту).
    - **L2 Diagnostic** — лише при `DeveloperDiagnostics` ON (розширена діагностика).
    - **L3 Local Only** — ніколи не відправляється (hotkeys, debug, perf, input).
109. **`TelemetryPolicy` архітектура — варіант E (Enum level)** (обрано після forensic #5.8.2). Замість крихкого `Classify(component, operation)` — enum-параметр `TelemetryLevel` у `ITelemetryService.Track()`. Default = Mandatory → існуючі 39 викликів не ламаються. L2 виклики додають `level: TelemetryLevel.Diagnostic` явно. Не крихке до перейменувань.
110. **Чекбокс — БЕЗ перейменування.** `AdvancedDiagnosticsCheckBox` + `Content="Розширена діагностика"` — НЕ чіпати (частина UI). Тільки внутрішня логіка: `TelemetryClient.Track()` перевіряє `settings.AdvancedDiagnostics` для L2 (§5.8.3).
111. **Audit 39 .Track() емітерів — Event Registry (§5.10):** 18 L1 + 21 L2 + 0 L3. Кожен емітер має Reason (чому цей рівень). Кожне поле TelemetryContext має Level (Field Registry §5.10.2).
112. **Активація `category` поля** — `TelemetryClient.BuildEvent()` встановлює Critical/Operational/Diagnostic/Analytics (CHECK вже дозволяє, міграція 00009). Additive.
113. **Активація `app_installations` FUTURE колонок прив'язана до Policy (оновлено forensic §5.8b):**
    - L1 Mandatory: `app_version`, `os_version`, `machine_id`, `country`, `platform`, `localization_version`, `selected_environment`, `update_channel`.
    - L2 Diagnostic: `game_folder_path` (немає доведеного споживача в Release Health/Incidents), `install_source` (не доведено), `os_build` (деталізація OS).

## 14.17. Порядок фаз (оновлено)

```
Phase 1 (Telemetry Cleanup) — DEFERRED до Phase 3.5 (має стати основою)
Phase 2 (Database Cleanup Review) — ✅ Closed
Phase 3 (Database Model + Freeze) — ✅ Closed
Phase 3.5 (Telemetry Policy) — 🔄 Active
Phase 3A (3 міграції) — ⏸ Pending production deployment (після Phase 3.5 + Phase 4)
Phase 4 (Data Presentation Layer) — ⏸ після Phase 3.5
Phase 5 (Retention Pipeline) — Backlog (pg_cron)
```

---

# 15. Rejected Decisions (майстер-список)

> Щоб більше ніхто не пропонував. Об'єднано архітектурні (18) + observability (54).

## 15.1. Архітектурні

1. Сторонні IoC-контейнери.
2. MVVM-фреймворки.
3. `Microsoft.Extensions.Hosting` (Generic Host).
4. `RegisterHotKey` як default бекенд.
5. Custom Protocol Handler (`sclocverse://`) для OAuth.
6. `requireAdministrator` app.manifest.
7. `CurrentUser` store для cert L.I.A.
8. Pipe-based stdout для elevated PowerShell.
9. Нова плитка «ПРОФІЛЬ» у лівій панелі.
10. Інтеграція акаунта в діалог Налаштування.
11. Додаткові прапорці `IsAuthenticated`.
12. Рішення `AuthGateCanvas` про видимість Main UI (це робить `MainWindow`).
13. Scope `email`.
14. Scope `guilds` (future, gated на Product Review).
15. Повне очищення `app_installations`.
16. Повне очищення `auth.users`.
17. Поведінкова телеметрія.
18. Глобальний рефакторинг/переписування.

## 15.2. Observability

19. ILogger / `Microsoft.Extensions.Logging`.
20. AppInsights / Sentry / OpenTelemetry.
21. Сторінкові таблиці статистики замість VIEWs.
22. Ad-hoc логери (Стаття 7).
23. Прямі вставки в БД поза `ITelemetryService` (Стаття 12).
24. Власні HTTP-клієнти телеметрії.
25. `throw` у публічній поверхні телеметрії (Стаття 1).
26. `await` на UI-потоку для телеметрії (Стаття 2).
27. Телеметрія у критичному шляху `MainWindow_Loaded` (Стаття 3).
28. Бізнес-логіка розгалужується на результат телеметрії (Стаття 10).
29. Токени/JWT/email/IP/шляхи/MachineName/MAC у payload (Стаття 4).
30. Будь-який секрет у репозиторії (Стаття 29).
31. Міграції з PASSWORD.
32. Реальні секрети в `appsettings*.json`.
33. Зміна семантики колонок, NOT NULL без backfill (Стаття 13).
34. Зміна PK/RLS-контракту (Стаття 13).
35. DROP COLUMN `telemetry_events.country` (additive-only).
36. Нормалізація signal → signal_id.
37. Перепис promotion engine.
38. Перенос country-тригера на events.
39. DROP порожніх таблиць (error_reports, admin_audit_log, user_discord_guilds).
40. `UPDATE` на `telemetry_events` (Стаття 5).
41. `Archived → *` переходи.
42. Пряме UPDATE `telemetry_incidents.status` мимо функції.
43. Прямі writes в `incident_status_log`/`incident_notes`.
44. Будь-які переходи `notification_queue` поза дозволеними.
45. Створення фейкових релізів/аварій заради тесту (Стаття 18).
46. Бізнес-логіка в Blazor/C# (усі рішення у SQL VIEWs).
47. Side-effect виклики з Dashboard (Стаття 20).
48. `cc_readonly` INSERT/UPDATE/DELETE напряму.
49. Автогенерація знань з телеметрії/LLM (Стаття 28).
50. Knowledge Engine як джерело правди.
51. Семантичний/LLM matching у v1.
52. Роль `cc_knowledge_editor`.
53. Варіант A (друге підключення cc_knowledge_editor у Blazor).
54. Варіант C (окремий застосунок Knowledge Management).
55. Об'єднати Status+Confidence в одну шкалу.
56. Confidence як похідне від Status.
57. Окремого Evidence поля в KnowledgeEntry.
58. Phase 7: LLM-розширення (порушує Free Tier).
59. Phase 7: embeddings (pgvector складність).
60. Phase 7: Auto-Draft.
61. Phase 7: DROP FUNCTION / ALTER NOT NULL / зміна сигнатур / зміна CHECK-інваріантів.
62. Варіант A «ничого не відновлювати» (cleanup).
63. DROP `idx_telemetry_source_signal` без перевірки `pg_stat_user_indexes`.
64. **Signal normalization** (вигода 16 MB/1M не виправдовує перепис 6 views + 33 CC-запитів).
65. **Phase 2: твердження «`detail.signal_name` дублює computed signal» відхилено** — у живих даних (705 рядків) ключ `signal_name` взагалі не існує (0 зустрічей). Було планованим, але не реалізованим у C# `ErrorContextExtractor`.
66. **Phase 2: DROP COLUMN заборонено і для `http_status`/`supabase_code`** — хоча 100% NULL (705/705), CHECK `chk_telemetry_failed_has_signal` посилається на обидві. Additive-only (§13).
67. **Phase 2: DROP зарезервованих таблиць (`error_reports`, `admin_audit_log`, `user_discord_guilds`)** — підтверджує Rejected #39; 0 продюсерів не є підставою для DROP (майбутнє використання).
68. **Phase 2 forensic: MERGE `notification_queue.error_message` ↔ `last_error` відхилено** — різна семантика (фінал черги vs остання спроба retry). Деталі в §12.6.
69. **Phase 2 forensic: REMOVE `detail.retry_count` з C# відхилено** — заготовка під плановану Retry Policy (коментарі в коді: `// схема готова для майбутньої Retry Policy`). Рішення відкладено до Retry Policy architectural decision (§17.4).
70. **Phase 2: Migration squash відхилено** — об'єднання 26 міграцій втратить аудит причин. Стандартна migration practice (див. §3.2.2).

---

# 16. Known Technical Debt

> Деталі: `docs/architecture/Final-Architecture-Review.md` (TD-1…TD-60: 6 P0 + 18 P1 + 26 P2 + 8 P3; ZR-1…ZR-24 phased).

## 16.1. P0 (6)

| ID | Опис | Де |
|---|---|---|
| TD-1 / SEC-1 | Control Center без auth | `Program.cs` |
| TD-2 / SEC-3 | Cert L.I.A. без pin → `LocalMachine\Root`+`TrustedPeople` | L.I.A. installer |
| TD-3 / SEC-2 | Інсталятор L.I.A. без integrity check | L.I.A. installer |
| TD-4 / SEC-4 | Checksum-bypass при порожньому checksum | `UpdateVerifier.cs` |
| TD-5 | HomeCanvas mojibake (UTF-8) | `HomeCanvas.xaml` |
| F1 (закрито) | `release_health_detail` не існувала | ✅ створена міграцією 05021200 |

## 16.2. Forensic-знахідки (F2–F10)

| ID | Опис |
|---|---|
| F2 | enum `telemetry_incidents.status` не містить `Mitigated`/`Acknowledged` (CHECK vs функції) |
| F3 | Відсутній global `UnhandledException` handler — WPF-краш минає спостережуваність |
| F4 | `PrivacySanitizer` покриває лише `ErrorMessage`; `Detail` неочищений |
| F5 | Terminal `FlushAsync` пропущено у 5 Failed-емітерів ApplicationUpdate |
| F6 | Три копії promotion-engine (m13 batch, m16 batch+notify, m05021100 per-event); batch — мертвий |
| F7 | `LiaForensicParser.TryParseMinimal` — мертвий код |
| F8 | `telemetry_events.country` — мертва (тригер лише на installations) |
| F9 | 6 з 19 колонок `app_installations` завжди NULL/DEFAULT |
| F10 | Мертві таблиці без продюсера: `admin_audit_log`, `error_reports`, `user_discord_guilds` |

## 16.3. Додатковий борг

- `MainWindow` God Class (~903 рядки, ~20 ctor-параметрів, ~31 field) — TD-6.
- Жодного app-wide `CancellationTokenSource`.
- OAuth `state` не валідується (SEC-8 / TD-36).
- `AuthService.State`/`Profile` non-atomic з background thread (R-5 / TD-30).
- `DiscordGuildSyncService` — dead код.
- UI чекбокс `AdvancedDiagnostics` — **не підключений до телеметрії** (див. розділ 16.4).
- PowerShell без timeout у L.I.A. (TD-10).
- Orphaned elevated PowerShell-процес при cancellation (L-A6).
- `TECHDEBT-001`: `FolderBrowserDialog` → `OpenFolderDialog` (SCLOC-Verse 2.0).
- `SECURITY DEFINER` без `SET search_path` (~20 функцій) — SEC-11.

## 16.4. Чекбокс «Розширена діагностика»

- UI: `SettingsCanvas.xaml:475` `AdvancedDiagnosticsCheckBox`; Settings: `Settings.Default.AdvancedDiagnostics`; читання `MainWindow.xaml.cs:403`; запис `:450-456`.
- **Вплив на телеметрію: відсутній.** `TelemetryClient` залежить лише від env `SCLOCVERSE_TELEMETRY_DISABLED`.
- Цільова схема (Категорія A завжди / Категорія B лише при ввімкненому) — див. розділ 17.1.

## 16.5. Phase 2 Database Cleanup Review (2026-07-07)

> Повний аналітичний аудит схеми Supabase без реалізації. Усі цифри — з живої БД через `pg_catalog`/`pg_stat_user_indexes`/`pg_stat_*`.

### 16.5.1. Нові знахідки (10 пунктів)

| # | Знахідка | Доказ (жива БД) |
|---|---|---|
| C-1 | Резервна схема `backup_pre_1_0_0_1` (12 таблиць-дублів, 55 рядків) | `pg_class` по схемі; жодних продюсерів, 0 залежностей |
| C-2 | `telemetry_events.http_status` + `supabase_code` 100% NULL (705/705) | `count(*) FILTER (WHERE … IS NULL)`; ErrorContextExtractor не заповнює |
| C-3 | `detail.signal_name` НЕ існує в живих даних (0 зустрічей) | `jsonb_object_keys` частотний аналіз; KB §5.7 було неточне |
| C-4 | VIEWs у KB §3.1 занижено: 19 → 25 фактично (24 cc + 1 public) | `pg_class WHERE relkind IN ('v','m')` |
| C-5 | SECURITY DEFINER функцій: ~24 → 28 фактично; лише 2 з 28 мають `SET search_path` | `pg_proc WHERE prosecdef=true` + `proconfig` |
| C-6 | `pg_cron` НЕ встановлений — план «Retention 14д» з Observability-Architecture не виконано | `relation cron.jobs does not exist` |
| C-7 | Дубль індексу `app_installations.install_id`: NON-UNIQUE (1266 scans) + UNIQUE constraint (18 scans) | `pg_stat_user_indexes` — планувальник обходить UNIQUE |
| C-8 | `ecosystem_stats()` — мертва (`.Rpc(` в C# не знайдено; `pg_depend=[]`) | лише docs як RPC-контракт (KB §181, app-installations-forensic) |
| C-9 | `promote_incident_candidates()` (batch) — мертва (`pg_depend=[]`, F6 на рівні БД) | тригер викликає лише `_for_event` |
| C-10 | 11 з 24 control_center views НЕ викликаються з C#/Blazor | grep `.razor`+`.cs`: contract_info (контракт), errors, health, installations, incident_candidates_24h, knowledge_list, observability_health, statistics, traces, unfinished_started, users, notifications |

### 16.5.2. Database Cleanup Matrix

**🟢 KEEP (40):** `public`, `control_center`; 13 живих таблиць; усі 13 FK; 4 triggers; 13 активних views; 24 активні функції; усі 28 RLS; 29 структурних/використовуваних індексів.

**🟡 ACTIVATE (10):** 4 активації `app_installations.*` через `IInstallationContextProvider`; `git_commit` (MSBuild); `category` диференційована; Phase 0 (3 partial-індекси KB #70); Phase 3 (3 матв'юхи KB #73); generated column `incident_code` (замість формування в 4 views + Notifier); чекбокс AdvancedDiagnostics → Category A/B.

**~~🔄 MERGE (2)~~** → ❌ **MERGE (0)** — обидва кандидати зняті після forensic:
- ~~`notification_queue.error_message` ↔ `last_error`~~ — **різна семантика** (фінал vs остання спроба retry), НЕ дубль. Див. §12.6, Rejected #68.
- ~~`telemetry_events.detail.retry_count`~~ — **заготовка під Retry Policy**, НЕ мертва. Див. §14.9 #85, Rejected #69.

**🔴 REMOVE (3 індекси — доведено дубль/невикористання):**
- `idx_app_installations_install_id` — дублює UNIQUE `app_installations_install_id_key`.
- `idx_app_installations_machine_id` — `idx_scan=0`, machine_id не шукається.
- `idx_user_discord_guilds_user_id` — дублює провідний стовпець UNIQUE `(user_id, discord_guild_id)`.

> Усі 3 — `DROP INDEX` (additive, не порушує схематичний контракт).

**📊 Trivia — dependency graph 4 VIEW** (що змінюватимуться через generated column): `incidents`, `incident_timeline`, `incident_notes_view`, `notifications` — **0 inbound залежностей** у БД (ані views, ані функцій, ані тригерів на них не посилаються). OR REPLACE безпечний. Залежні лише C# SQL-запити з явним списком колонок (`ControlCenterRepository`). Див. §14.9 #83.

**⏸ DEFER (28):** схема `backup_pre_1_0_0_1`; таблиці `error_reports`/`admin_audit_log`/`user_discord_guilds` (Rejected #39); 11 невикористовуваних views (additive view-контракт); функції `promote_incident_candidates`/`ecosystem_stats`/`get_knowledge_version_detail` (DROP заборонено API Freeze §13.8); `app_installations.os_build`/`install_source`; `telemetry_events.country` (Rejected #35).

### 16.5.3. SEC-11 — підтверджено на рівні БД

Лише `ecosystem_stats` та `set_country_from_cf` мають `SET search_path=public`. **26 з 28 SECURITY DEFINER функцій вразливі до schema-poisoning.** Виправлення: `add SET search_path = public, pg_catalog` — additive-only, не змінює сигнатур (дозволено API Freeze §13.8).

## 16.6. Незадокументовані об'єкти БД (Phase 3A Post-Impl, 2026-07-07)

> Schema Verification на Production Replica виявила 3 об'єкти, які існують у production, але НЕ описані в жодній з 26 міграцій. Створені вручну через Supabase Dashboard поза міграційним конвеєром.

| Об'єкт | Тип | Стан | Доведено | Trivia |
|---|---|---|---|---|
| `control_center.release_health_detail` | VIEW | 🟢 використовується (CCRepository.GetReleaseHealthAsync) | Schema Verification diff | Посилається на нього `verify_knowledge_auto()` (міграція 23) |
| `control_center.unfinished_started` | VIEW | 🔴 не використовується C#/Blazor (Phase 2 finding) | Schema Verification diff | Started-події без термінальної |
| `public.ecosystem_stats()` | FUNCTION | 🔴 не викликається з C# (лише docs-контракт) | Schema Verification diff | Повертає json totalInstallations/activeTotal/active30d |

**TD-NEW:** Створити окрему міграцію `20260708000000_describe_unschema_objects.sql` що описує ці 3 об'єкти як частину міграційної історії (ідемпотентно — `CREATE OR REPLACE` / `CREATE OR REPLACE FUNCTION`). Відновлює audit trail схеми.

## 16.7. Security Drift: DEFAULT PRIVILEGES (Phase 3A Replica Verification, 2026-07-07)

> Schema Verification на Production Replica виявила розбіжність grants між production та replica.

| Grantee | PROD | REPL | Δ |
|---|---:|---:|---|
| `postgres` | 273 | 273 | ✅ |
| `cc_readonly` | 27 | 27 | ✅ |
| `cc_notifier` | 7 | 7 | ✅ |
| `authenticated` | 49 | 93 | +44 |
| `anon` | 39 | 91 | +52 |
| `service_role` | 45 | 105 | +60 |

**Кастомні ролі (`cc_readonly`, `cc_notifier`) — ідентичні.** Різниця в anon/authenticated/service_role — через `ALTER DEFAULT PRIVILEGES` у міграції 00008 (control_center_readonly_role). У production міграції застосовувались історично в іншому порядку/часі, тож DEFAULT PRIVILEGES НЕ активувались на вже створених об'єктах. У replica — застосувались коректно до всіх об'єктів.

**Два можливі діагнози:**
- (a) Production історично не застосували DEFAULT PRIVILEGES → replica має правильніший стан.
- (b) Replica права видані ширше, ніж потрібно → можливе over-granting.

**Рішення:** НЕ змішувати з Phase 3A (оптимізація БД). Окремий **Security Forensic** після завершення оптимізації БД.

## 16.8. Dependency graph `control_center.users` (Phase 3A Verification, 2026-07-07)

Доведено через `pg_rewrite`:

```
control_center.users (VIEW)
    ↓
public.user_analytics (VIEW)
    ↓
auth.users (TABLE, 35 columns)  +  public.app_installations (TABLE)
```

- Ланцюг ідентичний в обох проєктах (PROD + REPL).
- Жодної власної `public.users` таблиці немає — лише системна `auth.users`.
- `user_analytics` — плоский LEFT JOIN `auth.users × app_installations × user_country_agg` CTE (при мульти-інсталяціях → декартів добуток, кожен рядок з різним `country`, однаковим `user_countries`).

---

# 17. Backlog

> Лише підтверджені задачі з існуючих документів. Не вигадувати нових без перевірки.

## 17.1. Підтверджені задачі

| Задача | Джерело | Складність |
|---|---|---|
| Активація `localization_version` + `game_folder_path` + `selected_environment` через `IInstallationContextProvider` | `app-installations-implementation-plan.md` | низька–середня |
| Підключити чекбокс `AdvancedDiagnostics` до телеметрії (Категорія A/B) | forensic + ця KB | середня |
| Фікс `CERT_E_UNTRUSTEDROOT` (0x800B0109) у L.I.A. — реліз 1.0.0.2 | `LIA_INSTALLATION.md` | середня |
| Enrichment deployment HRESULT з message у L.I.A. | backlog | низька |
| Cleanup cert trust chain L.I.A. (orphaned root cert) | backlog | середня |
| Global `UnhandledException` handler (F3) | forensic | низька |
| Розширити `PrivacySanitizer` на `Detail` (F4) | forensic | низька |
| Terminal `FlushAsync` у 5 ApplicationUpdate Failed (F5) | forensic | низька |
| Розширити enum `telemetry_incidents.status` (F2) | forensic | низька |
| Видалити мертвий `LiaForensicParser.TryParseMinimal` (F7) | forensic | низька |
| MSBuild target для `git_commit` у BuildInfo | forensic | низька |
| Phase 0: 3 індекси (partial Failed, version_window, occurred) | Optimization-Plan | низька |
| Phase 1: скоротити LIA chain 9→2, app-update 6→1 | Optimization-Matrix | середня |
| Phase 2: видалити `detail.retry_count` з C# `UpdateEvents.Track`/`LiaEvents.Track` (завжди 0) | Optimization-Plan + Phase 2 Review | низька |
| ~~Phase 2: MERGE `notification_queue.error_message` ↔ `last_error` → `last_error`~~ | ~~Phase 2 Review (2026-07-07)~~ | ~~низька~~ — **ВІДХИЛЕНО forensic (різна семантика, див. §12.6, Rejected #68)** |
| ~~Phase 2: REMOVE 3 індекси-дублікати~~ (`idx_app_installations_install_id`, `idx_app_installations_machine_id`, `idx_user_discord_guilds_user_id`) | ~~Phase 2 Review — DROP INDEX additive~~ | низька — **2/3 виконано на replica Phase 3A (install_id + user_discord_guilds_user_id); machine_id → DEFER (Pre-Impl Forensic виявив ризик regression)** |
| Phase 2: дослідити та погодити DROP SCHEMA `backup_pre_1_0_0_1` (12 дублів, 55 рядків, 0 залежностей) | Phase 2 Review | низька |
| Phase 3A: описати незадокументовані об'єкти в міграції `20260708000000_describe_unschema_objects.sql` (release_health_detail, unfinished_started, ecosystem_stats) | Post-Impl Forensic Phase 3A — §16.6 TD-NEW | низька |
| **Phase 3.5: `TelemetryLevel` enum в `ITelemetryService.Track()`** — додати enum-параметр `level` (default Mandatory). Існуючі 39 викликів не ламаються. Реєстрація в `AppCompositionRoot`. | архітектурна основа Policy (варіант E, §5.8.2) | низька |
| **Phase 3.5: wire `AdvancedDiagnostics` чекбокс** — `TelemetryClient.Track()` перевіряє `settings.AdvancedDiagnostics` для L2. **БЕЗ перейменування UI** (§5.8.3). | увімкнути мертвий UI-контракт §16.4 | низька |
| **Phase 3.5: `category` активація** — `TelemetryClient.BuildEvent()` встановлює `category` (Critical/Operational/Diagnostic/Analytics) через Level. Additive. | диференціація подій у БД | низька |
| **Phase 3.5: `IInstallationContextProvider`** — активувати L1 FUTURE колонки (`localization_version`, `game_folder_path`→L2, `selected_environment`, `update_channel`). | наповнити test Supabase "правильними" даними | середня |
| **Phase 3.5: filter L2 при OFF** — TelemetryClient skip L2 events when `AdvancedDiagnostics=false`. Очікується суттєве зменшення навантаження. | скоротити telemetry_events volume | низька |
| **Phase 3.5: позначити 21 L2 емітерів** — додати `level: TelemetryLevel.Diagnostic` до 21 викликів з §5.10.1 (Updater Started/Succeeded, LIA cascade, RunInstallerScript). | реалізувати Policy в коді | низька |
| Phase 2: ~~generated column~~ **trigger-based `incident_code`** (`INC-YYYY-NNNNN`) замість формування в 4 views + Notifier | Phase 2 Review → Phase 3A Post-Impl: generated column неможливий для timestamptz (STABLE), замінено на trigger | низька |
| Phase 3: single-scan candidates + матеріалізувати 3 views | Optimization-Plan | середня |
| Control Center auth (SEC-1) | Final-Review | середня |
| Cert pin L.I.A. (SEC-3) | Final-Review | середня |
| L.I.A. installer integrity check (SEC-2) | Final-Review | середня |
| Checksum-fail замість Verify.Skipped (SEC-4) | Final-Review | низька |
| `SET search_path` у SECURITY DEFINER функціях (SEC-11) | Final-Review | низька |

## 17.2. Цільова політика збору даних (для чекбокса)

**Категорія A — передається ЗАВЖДИ** (навіть при вимкненому чекбоксі):
- `app_installations`: `install_id, user_id, app_version, platform, first_seen, last_seen, is_active`.
- `telemetry_events` з `outcome='Failed'` або `severity ∈ {Error, Critical}`.
- `telemetry_events`: `component, operation, outcome, severity, app_version`.
- `telemetry_incidents` + `notification_queue`.

**Категорія B — лише при увімкненому чекбоксі**:
- `app_installations`: `machine_id, os_version, os_build, game_folder_path, selected_environment, localization_version`.
- `telemetry_events` з `outcome ∈ {Started, Succeeded, Cancelled, Skipped}`.
- `telemetry_events`: `duration_ms, detail, http_status, hresult, supabase_code, exception_type, error_message`.
- LIA forensic payload (`appx_log`, cert-поля, `activity_id`, `phase`).
- Update Started/Succeeded/Failed detail.
- `telemetry_events.git_commit`.

## 17.3. Roadmap (Роки 1–3)

- **Рік 1 (стабілізація):** P0-борг, оптимізація БД, app_installations, чекбокс.
- **Рік 2 (масштабування):** config-driven OAuth, Email/Telegram providers, Edge Function ingest, CC auth + Supabase Auth, rollup tables/MATVIEW, партиціювання при >50M рядків/рік.
- **Рік 3 (еволюція):** `ITelemetryBackend`, plugin-system notification, localization integrity, Authenticode enforcement, multi-tenant CC.

## 17.4. Phase 2 forensic-задачі (відкриті питання після аудиту 2026-07-07)

| Задача | Призначення | Складність |
|---|---|---|
| **Retry Policy architectural decision** — прийняти або відхилити Retry Policy для `UpdateEvents`/`LiaEvents`/`InstallationService`. Визначити долю `detail.retry_count` (зараз завжди 0, схема готова). Якщо відхилити — прибрати ключ з 4 місць C# + зафіксувати в KB §15. Якщо прийняти — спроєктувати retry pipeline (бекенд-логіка). | усуває неоднозначність: «мертва чи заготовка?» | середня (рішення) / висока (реалізація) |
| **`error_message` semantic activation (варіант b з §12.6)** — додати в Notifier SQL запис `error_message` лише при фінальному `status='Failed'` після `max_retries` (фінал проміжного `last_error`). | розділити семантику фінальної помилки черги від проміжної retry | низька |
| **Phase 2: позначити `error_message` deprecated у SQL-коментарі** (варіант c з §12.6) — фіксація статус-кво без зміни коду. | предупредити наступних агентів | низька |

## 17.5. Phase 4 — Data Presentation Layer (після Phase 3 Freeze, перед production deployment)

> **Перейменовано з "Control Center UX"** (Approved #103). Phase 3 CLOSED (Approved #102). БД НЕ змінюється — лише presentation.

| Задача | Призначення | Складність | Залежність |
|---|---|---|---|
| **Forensic baseline Blazor UI** — Inventory сторінок + колонок + форматів (що зараз є, в якому порядку). Без baseline — не формувати цільову модель. | розуміння стартової точки | низька | перший крок Phase 4 |
| **`ITimeZoneService` інтерфейс + реалізація** — який TZ (Kyiv/UTC/Local). Реєстрація в DI. | абстракція TZ | низька | Approved #104 |
| **`IUserDateTimeFormatter` інтерфейс + реалізація** — формат (`dd.MM.yyyy HH:mm:ss`, relative time). Залежить від `ITimeZoneService`. | абстракція формату | низька | Approved #105 |
| **Blazor component `<DateTimeDisplay>`** — використовує `IUserDateTimeFormatter`. Універсальний. | DRY presentation | низька | після форматера |
| **Display Order** — логічні блоки в сторінках CC: Installation → User → Activity → Diagnostics. | читабельність | середня | після baseline |
| **Display Formatting** — boolean → `✔ Active`/`✖ Disabled`; status → color-coded; severity → badge. | читабельність | середня | після baseline |
| **NULL Audit** — замість `NULL` показувати `—`/`Not collected`/`Unknown` залежно від семантики. | читабельність | низька | після baseline |
| **Human-readable IDs** — UUIDs скорочені `b4a7d7f7…` у списках; повний лише в деталях. | читабельність | низька | після baseline |
| **Sorting & Filtering** — клікабельні headers + search для основних сторінок. | UX | середня | після Display Order |
| **`control_center.users` доле** — використати view у Phase 4 Users page, АБО визнати застарілим (§16.8 + Approved #94). | усунути мертвий контракт | низька | після baseline |

**Принцип:** спочатку baseline + абстракції (`ITimeZoneService`/`IUserDateTimeFormatter`), потім конкретні сторінки. Уникнути хардкоду TZ у розетках.

---

# 18. Cross References

## 18.1. Джерельні документи (що підтверджує що)

| Документ | Що підтверджує |
|---|---|
| `AGENTS.md` | Конституція проєкту (Zero Regression, UTF-8 P0, українські commits, additive-only, структура) |
| `ARCHITECTURE_DECISIONS.md` | ADR-001…008 (DI, hotkeys, MainWindow, overlay, canvas, lifetime, ISP) |
| `.kilo/adr/ADR-001-oauth-redirect-loopback.md` | Loopback Redirect для OAuth |
| `.kilo/adr/ADR-002-oauth-ux-integration.md` | Кнопка акаунта у тайтлбарі |
| `docs/architecture/Final-Architecture-Review.md` | Повна архітектура, TD-1…60, ZR-1…24, SEC-1…12 |
| `docs/contracts/control_center.md` | Контракт Control Center, role `cc_readonly` |
| `docs/LIA_INSTALLATION.md` | L.I.A. installer/cert/elevation/forensic |
| `docs/checklists/Database-Verification.md` | Чеклист RLS/таблиць (Стаття 17) |
| `docs/release/release-runbook-1.0.0.1.md` | Ранбук, cleanup-класифікація |
| `docs/release/db-cleanup-forensic-analysis.md` | Початкова cleanup-політика (частково застаріла) |
| `docs/release/post-cleanup-forensic-app-installations.md` | Актуальна cleanup-політика (B+C) |
| `PRIVACY.md` + `SCLOCVerse/docs/Privacy-Design.md` | PII-політика, scopes |
| `SCLOCVerse/docs/Auth-StateMachine.md` | Auth state machine |
| `CODE_SIGNING_POLICY.md` | SignPath code signing |
| `docs/observability/Observability-Constitution.md` | 29 статей |
| `docs/observability/Observability-Architecture.md` | Цільова архітектура телеметрії |
| `docs/observability/Observability-Roadmap.md` | Roadmap (увага: згадує застарілі «21 статтю») |
| `docs/observability/Observability-RC1-Release.md` | Incident/Notification engine |
| `docs/observability/Observability-Database-Optimization-Plan.md` | Phase 0–4 оптимізації |
| `docs/observability/Optimization-Matrix.md` | 30 сигналів → 18 |
| `docs/observability/Knowledge-Engine-Design.md` + `Knowledge-Engine-Design-Review.md` | Knowledge Engine |
| `docs/observability/app-installations-forensic-2026-07-05.md` | Колонки app_installations |
| `docs/observability/app-installations-implementation-plan.md` | План `IInstallationContextProvider` (v2) |
| `docs/observability/FORENSIC-DATA-PIPELINE-RAW.md` | Сирі SQL/C# факти (25 міграцій, всі views) |
| `docs/observability/FORENSIC-DATA-PIPELINE-DETAIL.md` | Повні CREATE/ALTER + всі `.Track()` з рядками |

## 18.2. Карта залежностей розділів

```
1 Executive Summary
   ├─ 2 Архітектура ──── 7 Auth ──── 8 Localization ──── 9 L.I.A.
   ├─ 3 Database ─────── 4 app_installations
   ├─ 5 Telemetry ────── 6 Observability ─┬─ 11 Control Center
   │                                      ├─ 12 Notification System
   │                                      └─ 13 Knowledge Engine
   ├─ 10 Security (cross-cutting)
   ├─ 14 Approved ←── 15 Rejected (перевіряти перед пропозиціями)
   ├─ 16 Technical Debt ── 17 Backlog
   └─ 18 Cross References
```

## 18.3. Відомі неузгодженості (вирішити окремо)

1. **Roadmap** згадує «Constitution (21 стаття)» — фактично **29** (Конституція розширена).
2. **`db-cleanup-forensic-analysis`** — політика повного очищення `app_installations` частково застаріла; актуальна в `post-cleanup-forensic-app-installations.md` (B+C).
3. **Optimization-Matrix vs Database-Optimization-Plan** — цифри економії дещо різняться (Matrix ~−68%; Plan ~−70%). **Plan авторитетніший** (детальніший).
4. **Колізія нумерації ADR** — `ARCHITECTURE_DECISIONS.md` (ADR-001…008) і `.kilo/adr/` (ADR-001, ADR-002) описують різні рішення під однаковими номерами. Джерело вказувати явно.
5. **Версія** — `control_center.md` декларує `product_version=1.8.0`; застосунок `1.0.0.1`.

---

> **Підтримка:** ця KB — живий документ. При нових погоджених рішеннях — додавати в розділ 14; при нових відхиленнях — в розділ 15; при нових forensic — спочатку перевірити, чи факт вже тут, і лише потім доповнити цю KB + за потреби сирцевий документ.
