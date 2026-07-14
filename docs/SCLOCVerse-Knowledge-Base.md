# SCLOC-Verse — Unified Knowledge Base

> **Single Source of Truth.** Цей документ — єдина точка входу для будь-якого AI-агента.
> Якщо інформація тут є — не перечитуй десятки forensic-документів.
> Якщо інформація тут суперечить сирому документу — сирий документ має пріоритет, але повідом про розбіжність (розділ 18.3).
>
> **Версія застосунку:** 1.0.2.3 (Stable)
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
- Overlay — окреме вікно `HangarOverlayWindow` + `HangarOverlayService` (Win32 `WS_EX_TRANSPARENT`/`WS_EX_LAYERED`, ADR-005). `HangarTimerState` — SSOT для масштабу/прозорості (двостороння синхронізація: слайдери ↔ хоткеї ↔ overlay через `PropertyChanged`). `HangarOverlayService.PositionChanged` event — drag → Settings Hub.

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
| Схеми (SCLOC-Verse) | 2 | `public`, `control_center` (backup schemas DROPPED 2026-07-14) |
| Базові таблиці | 15 (+12 backup) | 14 у `public` + 1 singleton у `control_center` + 12 у `backup_pre_1_0_0_1` |
| Звичайні VIEW | 25 | 24 у `control_center` + `public.user_analytics` |
| Materialized VIEW | 1 | `control_center.knowledge_coverage` |
| SECURITY DEFINER функції | 30 | promotion, incident workflow, knowledge lifecycle, retention pipeline; 4 з 30 мають `SET search_path` (SEC-11: 2 legacy + 2 retention) |
| Triggers | 4 | geoip, failed-promote, incident-refresh, knowledge-audit |
| БД-ролі | 4 | `anon`, `authenticated`, `cc_readonly`, `cc_notifier` |
| RLS policies (`public`) | 28 | deny-all RESTRICTIVE + owner-only permissive + cc_notifier |
| pg_cron | 1 job | ✅ **Installed** (Phase 5.1, 2026-07-11) — `retention-pipeline-daily`, schedule `0 3 * * *` |

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

### 3.2.1. ~~Резервна схема `backup_pre_1_0_0_1`~~ — DROPPED (2026-07-14)

> ~~Знахідка Phase 2 Database Cleanup Review (2026-07-07).~~
> **DROPPED 2026-07-14.** Разом з `backup_pre_phase3a`. Обидві схеми мали 0 залежностей, 0 продюсерів, ~488 KB. Див. §14.36.

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

> ✅ **Retention Phase 5.1 Implemented (2026-07-11)** — `telemetry_events` (90d) через `pg_cron` + `run_retention_pipeline()`. Інші таблиці (notification_queue 30d, notification_attempts 90d, telemetry_incidents 1y) — Phase 5.2+, закоментовано в dispatcher.

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
| Retention 90d pg_cron purge | yes | ✅ **IMPL Phase 5.1** (2026-07-11): `run_retention_pipeline()` daily 03:00 UTC, telemetry_events 90d |
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

### 5.10.2. Field Registry — поля `TelemetryContext` за рівнями (Outcome-dependent, §5.11.3)

> **Оновлено forensic:** L1 розділено на **L1 Success** (мінімальний) та **L1 Failed** (розширений мінімальний — error context завжди, навіть без чекбокса). `error_message` повернуто в L1 (для Failed).

| Поле | L1 Success | L1 Failed | L2 Diagnostic | Reader (доведено §5.11.2) | Reason |
|---|---|---|---|---|---|
| `install_id` | ✅ | ✅ | — | FK + cc.release_health + incident_candidates | Identity |
| `user_id` | ✅ | ✅ | — | FK + cc.platform_stats + cc.release_health_detail | Identity |
| `session_id` | ✅ | ✅ | — | TraceRepository.cs:73,125 + cc.unfinished_started | Trace grouping |
| `correlation_id` | ✅ | ✅ | — | TraceRepository.cs:19,37,40 + cc.traces | Trace reconstruction |
| `step` | ✅ | ✅ | — | cc.traces (ORDER BY step) | Trace ordering |
| `component` | ✅ | ✅ | — | cc.release_health + incident_candidates + fingerprint | Classification |
| `operation` | ✅ | ✅ | — | cc.release_health + incident_candidates + fingerprint | Classification |
| `outcome` | ✅ | ✅ | — | cc.release_health (Succeeded/Failed count) + trg_telemetry_failed_promote | Status |
| `severity` | ✅ | ✅ | — | cc.telemetry_events | Status |
| `app_version` | ✅ | ✅ | — | cc.release_health (GROUP BY app_version) + fingerprint | Version |
| `telemetry_version` | ✅ | ✅ | — | cc.telemetry_events | Schema |
| `channel` | ✅ | ✅ | — | cc.telemetry_events | Config |
| `occurred_at` | ✅ | ✅ | — | cc.traces + cc.unfinished_started | Time |
| `received_at` | ✅ | ✅ | — | cc.release_health + incident_candidates + platform_stats + observability_health | Time |
| `os_version` | ✅ | ✅ | — | cc.telemetry_events | Environment |
| `country` | ✅ | ✅ | — | cc.telemetry_events | Geography |
| `error_message` | ❌ | ✅ | — | ControlCenterRepository.cs:540,570 + TraceRepository.cs:111,144 | **Human-readable** error (Exception.Message через PrivacySanitizer). Не дубль exception_type — дає пояснення ("Certificate chain invalid"), не тип. |
| `source` | ❌ | ✅ | — | ControlCenterRepository.cs:539,573 (COALESCE priority 1) | Incident fingerprint priority 1 (Supabase/Network/PowerShell/COM/CLR). Змінює grouping. |
| `hresult` | ❌ | ✅ | — | ControlCenterRepository.cs:539,573 (COALESCE priority 3) | Incident fingerprint priority 3. Без нього grouping деградує. |
| `exception_type` | ❌ | ✅ | — | ControlCenterRepository.cs:539,573 (COALESCE priority 5) | Incident fingerprint fallback. |
| `supabase_code` | ❌ | ❌ | ❌ (DEPRECATED) | — | 100% NULL (§5.6 F8a). CHECK вимагає колонку. |
| `http_status` | ❌ | ❌ | ❌ (DEPRECATED) | — | 100% NULL (§5.6 F8a). |
| `duration_ms` | — | — | ✅ | cc.traces + cc.telemetry_events | Performance diagnostics |
| `detail.phase` | — | — | ✅ | cc.telemetry_events (jsonb) | Cascade trace |
| `detail.retry_count` | — | — | ✅ | cc.telemetry_events (jsonb) | FUTURE (Retry Policy) |
| `detail.certificate_present` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.certificate_subject` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.certificate_thumbprint` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.installer_type` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.package_version` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.activity_id` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.powershell_exit_code` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.appx_log` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.signal_name` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic (0 у даних) |
| `detail` (jsonb container) | ❌ | ✅ (LIA forensic) | ✅ | cc.telemetry_events | Container для forensic keys |

### 5.10.3. Підсумок (оновлено після реалізації + Post-Impl Forensic)

- **21 L1 Mandatory емітерів** (Failed термінальні + lifecycle Succeeded/Started) — default, без `level:` параметра.
- **18 L2 Diagnostic емітерів** (проміжні Started/Succeeded/Skipped, Cascade trace, RunInstallerScript forensic) — `level: TelemetryLevel.Diagnostic`.
- **0 L3 Local** — 0 використань (лише enum визначення).
- **Усього: 39 .Track() емітерів** + 2 делегуючих (LiaEvents.cs:56, UpdateEvents.cs:43).

> **Post-Impl Forensic (2026-07-07):** KB §5.10.1 казав 21 L2, фактично 18 після реалізації. Різниця: #26 LIA/Install/Started — 2 виклики (Updater.cs:202,260) рахувались як 2, але #27-28 теж по 2. Фактичний підрахунок: 11 (Updater.cs) + 2 (Downloader) + 3 (Verifier) + 2 (Installer) = 18.

### 5.10.4. Post-Implementation Forensic (2026-07-07)

| # | Перевірка | Результат |
|---|---|---|
| 1 | L2 емітери з `TelemetryLevel.Diagnostic` | ✅ 18 (grep `level: TelemetryLevel.Diagnostic`) |
| 2 | `AdvancedDiagnostics` поза `TelemetryClient` | ✅ 0 у діловому коді (лише UI + SettingsService + TelemetryClient) |
| 3 | `Track()` без `level` → default Mandatory | ✅ 21 L1 виклик без level → Mandatory (ITelemetryService default) |
| 4 | `TelemetryLevel.Local` випадково | ✅ 0 використань (лише gate перевірка TelemetryClient.cs:100) |
| 5 | `AttachDiagnosticGate()` один раз | ✅ 1 виклик (AppCompositionRoot.cs:156) |
| 6 | `Category` не змінена | ✅ `context?.Category ?? "Operational"` — без прив'язки до Level |
| 7 | Incident Pipeline регресія | ✅ Failed = L1 Mandatory → trigger `trg_telemetry_failed_promote` не зачеплено. `RunInstallerScript/Failed` (L2 cascade) → фінальний `LIA/Install/Failed` (L1) створює інцидент завжди. |

## 5.11. Reader Validation — доказ споживача для L1 (Phase 3.5 forensic, 2026-07-07)

> **Forensic за вимогою користувача:** для кожного L1 емітера та поля — довести реального Reader (хто читає), а не «на око». Якщо Reader = ніхто → переглянути рівень.

### 5.11.1. Reader Validation для 18 L1 емітерів

> Production факт (705 рядків): Started=429 (61%), Succeeded=271 (38%), Failed=10 (1.4%). **Більшість L1 Failed-емітерів НІКОЛИ не траплялись у даних** (бо система працювала успішно). Але вони залишаються L1, бо при збій — потрібні для Incident Pipeline.

| # | Event | Reader (view/function/C#) | Mandatory because | Production факт |
|---|---|---|---|---|
| 1 | Application/Start/Started | `cc.platform_stats` (events_24h), `cc.telemetry_events` | adoption rate — скільки користувачів запустило | 130 рядків |
| 2 | Auth/RestoreSession/Success | `cc.release_health` (Succeeded count), `cc.platform_stats` | OAuth success rate — Release Health | 104 рядків |
| 3 | Auth/RestoreSession/Failed | `cc.incident_candidates_live/24h` (Failed filter), `cc.release_health` (Failed count), `trg_telemetry_failed_promote` | Auth failure rate — критичний інцидент | 0 рядків (не траплялось) |
| 4 | Auth/SignIn/Success | `cc.release_health`, `cc.platform_stats` | Auth success rate | 7 рядків |
| 5 | Installation/Sync/Success | `cc.release_health` (Succeeded), `cc.platform_stats` | Installation sync success rate | 111 рядків |
| 6 | Installation/Sync/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | 42501 або інша помилка (критичний) | 0 рядків (не траплялось) |
| 7 | Orchestrator/Cycle/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | Збій циклу оновлень | 0 рядків (не траплялось) |
| 8 | Orchestrator/AppCheck/UpdateFound | `cc.platform_stats`, `cc.telemetry_events` | Adoption нових релізів | 0 рядків (не траплялось) |
| 9 | Orchestrator/AppCheck/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | Не вдалося перевірити оновлення | 0 рядків |
| 10 | Orchestrator/LocalizationCheck/{outcome} | `cc.platform_stats`, `cc.telemetry_events` | Localization pipeline health | 0 рядків |
| 11 | Orchestrator/LocalizationCheck/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | Критичний збій локалізації | 0 рядків |
| 12 | Orchestrator/LiaCheck/UpdateFound | `cc.platform_stats`, `cc.telemetry_events` | LIA adoption | 0 рядків |
| 13 | Orchestrator/LiaCheck/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | Не вдалося перевірити LIA | 0 рядків |
| 14 | Updater/Download/Failed | `cc.incident_candidates_live/24h`, `cc.release_health` (Failed), `trg_telemetry_failed_promote` | Критичний збій завантаження | 0 рядків |
| 15 | Updater/Verify/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | Критичний збій перевірки | 0 рядків |
| 16 | Updater/Install/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | Критичний збій встановлення | 0 рядків |
| 17 | LIA/Install/Succeeded | `cc.release_health` (Succeeded), `cc.platform_stats` | LIA install success rate | 8 рядків |
| 18 | LIA/Install/Failed | `cc.incident_candidates_live/24h`, `cc.release_health` (Failed), `trg_telemetry_failed_promote` | Критичний збій LIA | 9 рядків |

> **Note:** `cc.incident_candidates_live/24h` читає `outcome='Failed'` + `received_at > now()-10min/24h`. `trg_telemetry_failed_promote` — тригер `AFTER INSERT WHEN outcome='Failed'`. `cc.release_health` — `count FILTER (outcome='Succeeded') / count FILTER (outcome='Failed')`. Усі L1 Failed-події мають **мінімум 2 Readers**.

### 5.11.2. Reader Validation для ключових полів

> Forensic за вимогою користувача: `correlation_id`, `session_id`, `hresult`, `exception_type`, `error_message`, `source` — довести Reader.

| Поле | Reader (view) | Reader (C#) | Висновок |
|---|---|---|---|
| **`correlation_id`** | `cc.traces` (ORDER BY correlation_id, step), `cc.telemetry_events` | `TraceRepository.cs:19,37,40` (GetTraceAsync WHERE correlation_id=$1), `:37,40` (SearchTracesAsync filter), `ControlCenterRepository.cs:44,538,555,568` | ✅ **L1** (Traces page — trace reconstruction) |
| **`session_id`** | `cc.traces`, `cc.unfinished_started` (JOIN), `cc.telemetry_events` | `TraceRepository.cs:73,91,107,125` (SearchTracesAsync + GetTraceAsync), `TraceModels.cs:8,54` | ✅ **L1** (session grouping + unfinished detection) |
| **`hresult`** | `cc.incident_candidates_live/24h` (signal COALESCE priority 3), `cc.release_health_detail` (top_fingerprint), `cc.traces` (signal) | `ControlCenterRepository.cs:539,540,553,569,573` (COALESCE signal), `TraceRepository.cs:110,141` (trace detail), `TraceModels.cs:24` | ✅ **L1 Failed** (Incident fingerprint priority 3 — без нього grouping деградує) |
| **`exception_type`** | `cc.incident_candidates_live/24h` (signal COALESCE priority 5), `cc.release_health_detail`, `cc.traces` | `ControlCenterRepository.cs:539,553,569,573` (COALESCE), `TraceRepository.cs:110,143` | ✅ **L1 Failed** (Incident fingerprint priority 5 fallback) |
| **`source`** | `cc.incident_candidates_live/24h` (signal COALESCE priority 1), `cc.release_health_detail`, `cc.traces` | `ControlCenterRepository.cs:539,540,553,569,573`, `TraceRepository.cs:110` | ✅ **L1 Failed** (Incident fingerprint priority 1 — Supabase/Network/PowerShell/COM/CLR) |
| **`error_message`** | `cc.traces`, `cc.telemetry_events` | `ControlCenterRepository.cs:540,554,570` (trace detail SELECT), `TraceRepository.cs:111,144` | ✅ **L1 Failed** (Traces page — людяне пояснення помилки) |

> **Writer forensic:** `error_message` = `Exception.Message` через `PrivacySanitizer.Sanitize()`. Це НЕ дубль `exception_type` — `exception_type` дає тип винятку (напр. "LiaInstallException"), а `error_message` дає **людяне пояснення** ("Certificate chain invalid", "Access denied", "Package not found"). Без `error_message` при Failed — розробник бачить лише тип, без причини. Користувач правий: не можна змушувати "перезапустити з діагностикою" для критичної помилки.

### 5.11.3. Outcome-dependent Field Policy (нова модель)

> **Forensic (2026-07-07, за пропозицією користувача):** розділити L1 не лише за рівнем, а й за **Outcome**. Failed-події збирають розширений мінімальний набір (навіть без чекбокса). Mature systems так і працюють: критична інформація про збої — завжди, глибока діагностика — лише за згодою.

```
L1 Success (мінімальний набір):
    install_id, user_id, session_id, correlation_id, step
    component, operation, outcome, severity, category
    app_version, telemetry_version, channel
    occurred_at, received_at
    os_version, country (trigger)
    → без error-context (немає exception)

L1 Failed (розширений мінімальний набір — навіть без чекбокса):
    + error_message (Exception.Message через PrivacySanitizer)
    + source (ErrorContextExtractor.ClassifySource: Supabase/Network/PowerShell/COM/CLR)
    + hresult (priority 3 в signal COALESCE)
    + exception_type (priority 5 fallback в signal COALESCE)
    → ErrorContextExtractor.Extract(exception) — завжди для Failed

L2 Diagnostic (лише при AdvancedDiagnostics ON — будь-який outcome):
    + duration_ms
    + detail.phase (orchestrationPhase)
    + detail.retry_count (FUTURE — Retry Policy)
    + detail.certificate_present / certificate_subject / certificate_thumbprint
    + detail.installer_type, package_version
    + detail.activity_id, powershell_exit_code, appx_log
    + detail.signal_name (HResultCatalog.ResolveSymbol)
    + game_folder_path, install_source, os_build (app_installations L2)

L3 Local Only (ніколи не відправляється):
    hotkeys, debug.WriteLine, performance, FPS, input traces
    → file journal (future), 0 .Track() викликів
```

**Реалізація:** `ErrorContextExtractor.Extract(exception)` вже викликається лише при `exception != null` (Failed). Для Success — `ctx = null` або мінімальний. Тобто **нова модель не вимагає переписування коду** — лише чітке документування, що:
- L1 Success ≠ L1 Failed (поля різні, бо ErrorContextExtractor додає error-context лише для Failed).
- L2 — додаткові поля поверх L1 (через `level: TelemetryLevel.Diagnostic` параметр).

**Перевірка `TelemetryClient.Track()`:**
```
if (level == TelemetryLevel.Diagnostic && !settings.AdvancedDiagnostics) return;
// L1 events проходять завжди (Succeeded + Failed)
// L1 Failed events несуть error_message + source + hresult + exception_type (через ErrorContextExtractor)
// L2 events несуть + duration_ms + detail.* (через LiaEvents/UpdateEvents параметри)
```






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
| SEC-11 | `SECURITY DEFINER` без `SET search_path` (~20 функцій) | ✅ COMPLETED (2026-07-08 + 2026-07-14: 28 + 2 функцій) |
| SEC-12 | `cc_readonly` фактично write-capable через definer-функції | ✅ COMPLETED (2026-07-14, REVOKE EXECUTE FROM PUBLIC) |

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
95. ~~**Production deployment Phase 3A відкладено** до завершення Phase 4~~ — **SUPERSEDED (#133, 2026-07-07):** Phase 3A розгорнуто на production ДО Phase 4. Рішення змінено після Phase 3.5 (Telemetry Policy implemented + verified) + Phase 3.6 (Replica structural audit PASSED). Міграції additive-only, rollback-готові, verified на replica.

## 14.13. Phase 3 завершення — Data Model Freeze (2026-07-07)

96. **Phase 3 не завершується міграціями, а Data Model Freeze.** Модель даних вважається оптимізованою лише після повного Data Dictionary (Source/Reader/Nullable/Category/Classification/Lifetime/Confidence для кожної колонки). Див. §4.10.
97. **Phase 4 (UX/Blazor) починається лише після Phase 3 Freeze.** НЕ змішувати оптимізацію БД з оптимізацією UI — різні фази, різні ризики.
98. **Time Zone реалізація через `ITimeZoneService`** (НЕ хардкодити `Europe/Kyiv`). БД лишається canonical UTC `timestamptz`. UI — конфігурований через інтерфейс (сьогодні Kyiv, завтра UTC або Local User Time).
99. **Classification 5 станів для кожної колонки:** `CORE` (обов'язкове для роботи), `OPTIONAL` (корисне), `DIAGNOSTIC` (діагностика), `FUTURE` (заготовка під майбутнє, напр. `retry_count`), `DEPRECATED` (застаріле, напр. `notification_queue.error_message` після 00017).
100. **Confidence (HYP/VER/IMPL/REJ) поширюється на ВСІ рішення** в KB, не лише forensic-знахідки. Кожне Approved/Rejected Decision має маркер ступеня доведеності.

## 14.14. Phase 3 Freeze Validation + Closure (2026-07-07)

101. **Freeze Validation PASSED.** Усі 172 колонки 15 таблиць мають чіткі відповіді на 6 атрибутів: Writer, Reader, Classification (CORE/OPTIONAL/DIAGNOSTIC/FUTURE/DEPRECATED), Confidence (HYP/VER/IMPL/REJ), Lifetime (FOREVER/1y/90d/30d/RESERVED), Рішення (KEEP/ACTIVATE/DEPRECATED/RESERVED). Прогалини в error_reports/admin_audit_log/user_discord_guilds усунуто (розгорнуто per-column у §4.10.13-15).
102. **Phase 3 OFFICIALLY CLOSED.** Data Model заморожено як Single Source of Truth. Подальші зміни БД — лише через зміну цієї моделі (спочатку модель → потім міграція). ~~Phase 3A міграції (idx DROP/ADD, generated→trigger) залишаються в git, готові до production deployment **після** Phase 4.~~ — **DEPLOYED 2026-07-07 (#133).**

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
114. **Outcome-dependent Field Policy** — L1 розділено на **L1 Success** (мінімальний набір, 16 полів) та **L1 Failed** (розширений мінімальний, 21 поле). Failed-події збирають error_context (error_message, source, hresult, exception_type) **завжди**, навіть без чекбокса. Mature systems так працюють: критична інформація про збої — завжди, глибока діагностика — за згодою. Деталі в §5.11.3.
115. **`error_message` повернуто в L1 Failed** (forensic: Writer = `Exception.Message` через PrivacySanitizer — людяне пояснення "Certificate chain invalid", не дубль exception_type). Без нього розробник бачить лише тип винятку без причини. Не можна змушувати "перезапустити з діагностикою" для критичної помилки.

## 14.18. Phase 3.5 CLOSED (2026-07-07)

116. **Phase 3.5 Telemetry Policy OFFICIALLY CLOSED.** Затверджено користувачем після: Event Registry (39 емітерів), Field Registry (Outcome-dependent: L1 Success 16 / L1 Failed 21 / L2 12 / DEPRECATED 2 / L3 0), Reader Validation (усі L1 доведено через C# код), Writer Validation (error_message = Exception.Message через PrivacySanitizer). UI не ламається (чекбокс без перейменування). Data Model не ламається (additive). Політика базується на коді, а не на припущеннях. Подальші зміни — реалізація затвердженого контракту (TelemetryLevel enum + wire AdvancedDiagnostics + 21 L2 емітери).

## 14.19. Phase 3.5 реалізація — архітектурні правила (forensic, 2026-07-07)

117. **`TelemetryLevel` ≠ `Category` — дві різні осі класифікації.** Forensic: `Category` зараз завжди `"Operational"` (`TelemetryClient.cs:136`: `context?.Category ?? "Operational"`, ніхто з 39 емітерів не встановлює `context.Category`). `TelemetryLevel` відповідає на «коли відправляти?» (gate), `Category` — на «що це за подія?» (бізнес-семантика). **НЕ змішувати**: `TelemetryLevel` не визначає `Category` автоматично. `Category` залишається незалежною — встановлюється явно через `TelemetryContext.Category` або default `"Operational"`.
    - Приклад: `LIA/Install/Failed` → `TelemetryLevel=Mandatory` (відправляється завжди), `Category="Critical"` (бізнес-семантика — критична подія). Це **два незалежні атрибути**.
    - Приклад: `Updater/Download/Started` → `TelemetryLevel=Diagnostic` (відправляється лише при ON), `Category="Operational"` (бізнес-семантика — нормальна операція). Level ≠ Category.

118. **Єдина точка прийняття рішення** — `TelemetryClient.Track()` є **єдиним місцем**, де перевіряється `AdvancedDiagnostics`. НЕ 21 місце з `if (AdvancedDiagnostics)`. Архітектура:
    ```
    .Track(component, operation, outcome, ctx, level)
        ↓
    TelemetryClient.Track()  ← ЄДИНА ТОЧКА
        ↓
    if (level == Diagnostic && !IsDiagnosticEnabled()) return;
        ↓
    BuildEvent() → _queue.Enqueue()
        ↓
    TelemetryUploader → Supabase
    ```
    `IsDiagnosticEnabled` = `_diagnosticGate?.Invoke() ?? false` (two-phase `AttachDiagnosticGate(Func<bool>)`).

119. **`TelemetryLevel.Local` залишити в enum** — 0 використань зараз, але документує архітектуру (L3 — ніколи не відправляється). Місце для майбутніх локальних подій.

120. **Реалізацій `ITelemetryService` — одна** (`TelemetryClient`). Жодних mock/fake/test. Новий параметр додається лише в 1 інтерфейс + 1 реалізацію. Backward compatible (default = Mandatory).
121. **`TelemetryLevel` застосовується лише через `.Track()` параметр.** ЗАБОРОНЕНО: `if (settings.AdvancedDiagnostics) { _telemetry.Track(...) }` у 21 місці. Дозволено лише: `_telemetry.Track(..., level: TelemetryLevel.Diagnostic)`. Рішення відправляти чи ні приймає **виключно `TelemetryClient.Track()`**.
122. **Заборонити пряме читання `AdvancedDiagnostics` поза `TelemetryClient`.** Жоден код, окрім `TelemetryClient` (через `AttachDiagnosticGate(Func<bool>)`), не має права читати `_preferencesService.GetAdvancedDiagnostics()` або `Settings.Default.AdvancedDiagnostics`. Єдиний споживач прапорця — `TelemetryClient`. Виняток: `MainWindow.xaml.cs:403,450-455` (UI читання/запис чекбокса → SettingsService).
123. **Gate null не блокує Mandatory.** Якщо `AttachDiagnosticGate()` не викликано (`_diagnosticGate == null`): Mandatory події ✅ відправляються, Diagnostic події ❌ блокуються (`gate null → false`). Это забезпечує безпеку: gate not attached = conservative (не відправляємо L2), але L1 працює завжди.

## 14.21. Phase 3.5 реалізація завершена + Post-Impl Forensic (2026-07-07)

124. **Phase 3.5 Telemetry Policy реалізовано.** `TelemetryLevel` enum + `ITelemetryService.Track()` параметр + `TelemetryClient` gate + `AttachDiagnosticGate` + 18 L2 емітерів позначено. Build: 0 warnings, 0 errors. UTF-8 коректний.
125. **Post-Implementation Forensic PASSED (7/7 перевірок):** 18 L2 емітерів (фактично, KB казав 21 — уточнено), 0 `AdvancedDiagnostics` у діловому коді, 21 L1 через default Mandatory, 0 Local використань, 1 AttachDiagnosticGate виклик, Category незмінний, Incident Pipeline без регресії. Деталі в §5.10.4.

## 14.22. Phase 3.6 — Replica Synchronization (2026-07-07)

126. **Replica = одноразовий полігон.** Test-проєкт `zhdtcxvnzlvbgxariyww` — НЕ довгострокова replica. Після тестування Phase 3.5/3A + Production Deployment + Post-Impl Forensic — буде повністю видалений. Не будувати політику довгострокової еквівалентності.
127. **Структурний аудит: 0 несподіваного drift.** 47/47 таблиць, 24/24 views, 28/28 SECURITY DEFINER, 29/29 RLS — ідентично. 3 розбіжності (function/index/trigger) — артефакти Phase 3A. Деталі §16.9.1.
128. **Data Import: 3 таблиці + 1 skip.** auth.users (55→56), auth.identities (generated from users), app_installations (54→55), incident_policy (skip — вже з міграцій). 14 таблиць + 25 views skip (historical/reserved/derived/0-rows). Деталі §16.9.2.
129. **Replica Validation PASSED.** 0 FK orphan, country preserved (6 distinct), views повертають дані, Discord-метадані коректні. Деталі §16.9.3.
130. **DEFAULT PRIVILEGES Drift — корінь VER (§16.7 оновлено).** Production = `Dxtm` (manually hardened, не в міграціях). Replica = `arwdDxtm` (Supabase baseline). RLS однаково блокує. Винесено в Phase 3.7 (§16.10).
131. **3-критеріальний Mandatory-фреймворк:** Writer + Reader + Empty Consequence. pipeline_health_meta → SKIP (cache, auto-recover). incident_policy → IMPORT (empty = silent incident death). auth.users → IMPORT (empty = FK violation).
132. **auth.identities генерується на самій replica** з auth.users (identity_data = raw_user_meta_data; provider_id з JSON). Не потрібен transfer з production.

## 14.23. Phase 3A Production Deployment + Release Cleanup (2026-07-07)

133. **Phase 3A DEPLOYED to production.** 3 міграції застосовані послідовно з верифікацією після кожної: (1) DROP 2 дубль-індексів, (2) ADD 3 partial індексів, (3) `incident_code` column + trigger + 4 views OR REPLACE. Post-Impl Forensic PASSED: +1 function, +1 index net (−2+3), +1 trigger, 0 RLS/policy/grant changes. Trigger functional test: `INC-2026-00022` ✅. Деталі §16.11.

134. **Release Cleanup — одноразова підготовка production до релізу.** НЕ Retention Pipeline (Phase 5). Видалено 717 telemetry_events + 4 telemetry_incidents + 4 notification_queue (усі — тестові дані періоду розробки Jul 4-7, версії 0.9.0.0–1.0.0.1). Збережено: auth.users (56), app_installations (55), incident_policy (5). In-database backup `backup_pre_phase3a` (11 таблиць) створено до cleanup.

135. **Backup `backup_pre_phase3a`** — in-database snapshot (аналог `backup_pre_1_0_0_1`). 11/11 таблиць верифіковано (row counts ідентичні public). Локальний `pg_dump` неможливий (Docker не встановлено, org plan=FREE). In-database backup + rollback scripts = повне покриття.

136. **Mandatory Event Audit (forensic).** 5 L1 подій на запуск проаналізовано. Висновок: `Auth/RestoreSession/Started` та `Installation/Sync/Started` — **кандидати на L2** (VER: 0 автоматичних споживачів, інформація повністю виводима з Succeeded/Failed outcomes). `Application/Start/Started` — KEEP L1 (єдина pre-auth подія, термінальний outcome). `RestoreSession/Succeeded` + `Sync/Succeeded` — KEEP L1 (читаються `release_health_detail.success_rate`). Економія: 5→3 подій/запуск (−40%), ~120K рядків/місяць при 1000 юзерів. Перенесено в Phase 3.5.1.

137. **Phase 3.5.1 (Mandatory Event Optimization) відкладено до після стабільного релізу.** Причина: ще одна поведінкова зміна перед релізом ускладнює аналіз при регресії. Мінімальний рефакторинг: додати `level` параметр у `TrackAuth`/`TrackSync` (default Mandatory), позначити 2 Started-події Diagnostic.

138. **Replica functional testing пропущено (архітектурне рішення).** Replica `zhdtcxvnzlvbgxariyww` пройшла структурний аудит (47/47 таблиць, 0 drift) + data import validation, але клієнтське тестування не виконувалось (OAuth redirect URL потребував би втручання в Discord Developer Portal). Phase 3A міграції additive-only з rollback — ризик оцінено як низький. Production functional testing виконується безпосередньо на production після backup.

## 14.24. Zero Noise Telemetry Policy (2026-07-07)

> **Новий архітектурний принцип.** `telemetry_events` не є журналом роботи застосунку. Це **журнал відхилень від нормальної роботи** (exception-driven log). Нормальний стан системи відображається в `auth.users` (`created_at`, `last_sign_in_at`) та `app_installations` (`last_seen`, `app_version`, `country`, `platform`).

139. **Zero Noise Policy затверджено.** L1 Mandatory = **тільки Failed події**. Усі non-Failed (Started, Succeeded, Cancelled, UpdateFound, Updated, UpdateAvailable) переведені в L2 Diagnostic. При AdvancedDiagnostics OFF `telemetry_events` росте виключно при реальних помилках.

140. **Mandatory Event Audit — повний (26 L1 викликів, 62 total).** 13 Failed типів → KEEP L1 (trigger + release_health). 13 non-Failed типів → L2 Diagnostic (0 автоматичних споживачів). Build: 0 warnings, 0 errors. `TrackAuth`/`TrackSync` отримали `level` параметр (default Mandatory → backward compatible).

141. **Вплив на обсяг:** успішний запуск → **0 L1 подій** (було 5). 1000 юзерів × 2 запуски/день: 10K/день → **~0/день**. За місяць: ~300K → **<100** (тільки реальні Failed). Free Tier telemetry_events практично не росте.

142. **Відоме обмеження `release_health_detail`:** `success_rate` показує 0% (0 Succeeded / N Failed). `active_installs` рахує лише юзерів з помилками. Це прийнятно — точні active install counts доступні з `app_installations` таблиці. Фікс presentation layer — Phase 4 scope.

143. **Контракт поведінки (оновлено):** `telemetry_events` при AdvancedDiagnostics OFF містить **тільки Failed події**. При ON — Failed + усі diagnostic деталі (Started, Succeeded, duration_ms, phases). Це не втрата даних — це розділення сигнал/шум.

144. **Zero Noise Policy — VERIFIED on production (2026-07-07):** OFF: успішний запуск → **0 подій** ✅. ON: успішний запуск → **9 L2 diagnostic** ✅. Failed pipeline (DB-side): `LIA/Install/Failed` → trigger → `INC-2026-00023` → notification_queue ✅. Усі поля Failed заповнені: `error_message`, `exception_type`, `hresult`, `source` ✅.

145. **Fire-and-forget dispose — TD (виявлено під час тестування).** `TelemetryClient.Dispose()` (рядок 227): `_ = _uploader.FlushAsync()` запускає upload, але **не await'ить** — процес завершується до відправки. Події відправляються лише фоновим таймером (30с tick). Якщо додаток закрито <30с після помилки → подія втрачена. Автор свідомо прибрав `Wait(5s)` (блокував UI при shutdown). TD: disk-based queue persistence або deferred upload на наступний запуск. Pre-existing, не регресія Zero Noise.

## 14.20. Telemetry Policy Test Matrix (контракт поведінки)

> Контракт для тестування реалізації по чек-листу. Не форензик, а поведінка системи.

### 14.20.1. Матриця сценаріїв

| Scenario | AdvancedDiagnostics | TelemetryLevel | Очікування |
|---|---|---|---|
| Success OFF | false | Mandatory | ✅ L1 Success (16 полів) відправляється |
| Success ON | true | Mandatory | ✅ L1 Success + L2 (якщо є) |
| Failed OFF | false | Mandatory | ✅ L1 Failed (21 поле: + error_message, source, hresult, exception_type) |
| Failed ON | true | Mandatory | ✅ L1 Failed + L2 (detail forensic) |
| Diagnostic OFF | false | Diagnostic | ❌ подія НЕ відправляється |
| Diagnostic ON | true | Diagnostic | ✅ L2 відправляється (duration_ms, detail.*) |
| Telemetry Disabled | будь-який | будь-який | ❌ нічого (env SCLOCVERSE_TELEMETRY_DISABLED) |
| Gate not attached | — | Diagnostic | ❌ подія НЕ відправляється (`_diagnosticGate == null → false`) |
| **Gate null + Mandatory** | **null** | **Mandatory** | **✅ Відправляється** (gate null не блокує L1; Approved #123) |

### 14.20.2. Конкретні події — що піде

| Подія | Scenario | Поля, що відправляються |
|---|---|---|
| **Auth/SignIn/Success** | OFF | install_id, user_id, session_id, correlation_id, step, component, operation, outcome, severity, app_version, telemetry_version, channel, occurred_at, received_at, os_version, country (16 = L1 Success) |
| **Auth/SignIn/Failed** | OFF | L1 Success (16) + error_message, source, hresult, exception_type, detail (21 = L1 Failed) |
| **Localization/Install/Failed** | OFF | L1 Failed (21) — error_message = "Certificate chain invalid" (PrivacySanitizer), source = "PowerShell", hresult = "0x800B0109", exception_type = "LiaInstallException" |
| **LIA/Install/Failed** | OFF | L1 Failed (21) — error_message, source, hresult, exception_type, detail (phase, retry_count=0, installer_type, certificate_present) |
| **LIA/Install/Failed** | ON | L1 Failed (21) + L2 (duration_ms, detail.certificate_subject, certificate_thumbprint, package_version, activity_id, powershell_exit_code, appx_log, signal_name) |
| **Updater/Download/Started** | OFF | ❌ НЕ відправляється (L2 Diagnostic, gate OFF) |
| **Updater/Download/Started** | ON | ✅ L2: install_id, session_id, correlation_id, step, component, operation, outcome, severity, app_version, ..., duration_ms, detail.phase, detail.retry_count |
| **Updater/Download/Failed** | OFF | ✅ L1 Failed (21) — error_message, source, hresult, exception_type |
| **Application/Start/Started** | OFF | ❌ НЕ відправляється (L2 Diagnostic, gate OFF) — Zero Noise Policy #139 |
| **Application/Start/Started** | ON | ✅ L2: diagnostic detail (pre-auth launch signal) |


## 14.17. Порядок фаз (оновлено)

```
Phase 1 (Telemetry Cleanup) — ✅ Closed (замінено Release Cleanup 2026-07-07)
Phase 2 (Database Cleanup Review) — ✅ Closed
Phase 3 (Database Model + Freeze) — ✅ Closed
Phase 3.5 (Telemetry Policy) — ✅ Closed + Implemented
Phase 3.6 (Replica Synchronization) — ✅ Closed (replica deleted 2026-07-07)
Phase 3A (3 міграції) — ✅ **DEPLOYED to production** (2026-07-07, Post-Impl Forensic PASSED)
Phase 3.5.1 (Mandatory Event Optimization) — ✅ **Implemented as Zero Noise Policy** (2026-07-07)
Phase 4 (Data Presentation Layer) — ⏸
Phase 5.1 (Retention Pipeline — telemetry_events) — ✅ **Implemented** (2026-07-11, pg_cron + SECURITY DEFINER)
Phase 5.2+ (Retention — notification_queue, notification_attempts, incidents) — Backlog (додати рядки у dispatcher)
Phase 3.7 (Security Hardening) — ✅ SEC-12 + SEC-11 completed (REVOKE EXECUTE + search_path); DEFAULT PRIVILEGES — Backlog
```

## 14.18. UI: Кастомний ToolTip для CheckBox (2026-07-07) — ✅ IMPL

146. **Кастомний ToolTip-інфраструктура реалізована.** Замість стандартного Windows ToolTip створено єдиний implicit `Style TargetType=ToolTip` у `Resources/Styles.xaml` (DRY/KISS — будь-який елемент з `ToolTip="..."` у Canvas, що підключає `Styles.xaml`, автоматично отримує стиль). Палітра повторно використовує існуючі кольори додатку: картка `#102A3A` (= ComboBox/CheckBox bg), рамка `#2478A9` (= accent), текст `#E8F3FF` (= ComboBoxItem foreground), тінь `DropShadowEffect` як у кнопок, `CornerRadius=8` як у карток. Шрифт `Segoe UI` 14 (як у SettingCheckBox).
147. **Поведінка ToolTip.** Позиція `Right` + `HorizontalOffset=8` (не перекриває елемент). `MaxWidth=300` + `TextWrapping=Wrap` (автоперенос). `InitialShowDelay=250` (поява ~250 мс), `ShowDuration=60000` (тримається довго — контрольно-налаштункова підказка). Плавний fade забезпечується OS-рівневим `ToolTipPopupAnimation` (за замовчуванням Fade). `UseFading`/`PopupAnimation` — НЕ властивості ToolTip (це attached `ToolTipService.*`, але таких attached немає; WPF делегує OS). Спроба додати `ToolTipService.UseFading`/`PopupAnimation` у Style викликає MC4005 — прибрано.
148. **Застосування.** `SettingCheckBox` стиль у `SettingsCanvas.xaml` розширено 5 сеттерами `ToolTipService.*`. 4 чекбокси (`RunAtStartupCheckBox`, `MinimizeToTrayCheckBox`, `AutoUpdateLocalizationCheckBox`, `AdvancedDiagnosticsCheckBox`) отримали `ToolTip="..."` з українськими текстами. Новий CheckBox автоматично отримує підказку через властивість `ToolTip` (інфраструктура готова).
149. **Scope.** Implicit ToolTip-стиль у `Styles.xaml` застосовується лише в Canvas, що підключають `Styles.xaml` (Settings, ScTools, Assistant, Localization). MainWindow та діалоги НЕ підключають `Styles.xaml` → їхні існуючі ToolTip (`ToolTip="Обліковий запис"`, `ToolTip="Закрити"`) не змінюються (zero side-effect). Зміна не зачіпає C#, схему, телеметрію. Лише XAML-ресурси (+51 рядок Styles.xaml, +12 рядків SettingsCanvas.xaml).

## 14.19. Release v1.0.1.0 Stable (2026-07-07) — ✅ IMPL

150. **Version Audit проведено.** У проєкті існує єдине джерело версії — `AssemblyVersion`/`Version`/`FileVersion` у `SCLOCVerse.csproj`. Усі 7 шляхів споживання версії читають її з assembly через reflection: `BuildInfo.ReadAppVersion` (telemetry `app_version`), `ApplicationVersionProvider.GetCurrentVersion` (HomeCanvas/About/Updater), `InstallationService.GetCurrentAppVersion` (`app_installations.app_version`), `App.xaml.cs.GetCurrentVersionString` (Settings migration `LastAppVersion`), `HttpRetryHelper.BuildUserAgent` (User-Agent `SCLOC-Verse/<version>`), `Installer/SCLOC-Verse.iss` `GetFileVersion` (інсталятор читає з білда). Hardcoded "1.0.0.0"/"1.0.0.1" в сирцях відсутні — архітектура single-source-of-truth.
151. **Version Update: 1.0.0.1 → 1.0.1.0.** Змінено 3 рядки у `SCLOCVerse.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`). Release-білд перевірено: `FileVersion=1.0.1.0`, `ProductVersion=1.0.1.0+<commit>`, `AssemblyVersion=1.0.1.0`. `AssemblyInformationalVersion` не заданий явно — MSBuild використовує `<Version>` (1.0.1.0) → User-Agent коректний. Інсталятор `.iss` читає версію з білда → автоматично синхронізується. Build: 0 warnings, 0 errors.
152. **Production Readiness підтверджено.** Build без warning/error ✅. Zero Noise Telemetry Policy (Phase 3.5) — ✅ implemented + verified on production (#144). Phase 3A міграції — ✅ deployed to production (#95). Production DB актуальна — ✅ Post-Impl Forensic PASSED (#90). Auto Update працює — ✅ `ApplicationUpdateService` використовує `IApplicationVersionProvider` (версія з assembly). Release Notes згенеровано: `Installer/Release-Notes-v1.0.1.0.md` (переписано після forensic — лише фічі після v1.0.0.0, 160 комітів v1.0.0.0..HEAD, 92 файли). Git Tag `v1.0.1.0` — готовий до створення. v1.0.0.1-pre1 не враховується (видалений з GitHub Releases).
153. **Release scope verified.** Тег `v1.0.0.0` → коміт `404bd85` («Реліз 1.0.0.0: фінальні URLs та секція сайту»). У v1.0.0.0 НЕ БУЛО: системного трею, автозапуску, чекбоксів налаштувань, кастомних ToolTip, toast-сповіщень, BackgroundUpdateOrchestrator/NotificationRouter, single-instance (Mutex+Named Pipe), AppUpdateProgressWindow, брендованої OAuth callback сторінки, всього Observability стеку (TelemetryClient, ErrorContextExtractor, PrivacySanitizer, TraceContext, BuildInfo, інцидент-менеджмент, Knowledge Engine), GitHub API Hardening (HttpRetryHelper, retry/backoff, Conditional GET ETag, єдиний User-Agent), LIA cert LocalMachine fix (0x800B0109), LIA elevation (RunPowerShellAsync.requireElevation). БУЛО у v1.0.0.0: UpdateHistoryWindow.
154. **Release v1.0.1.0 PUBLISHED.** Git tag `v1.0.1.0` → коміт `8a6a989`. GitHub Release створено: https://github.com/Vova-Bob/SCLOC-Verse/releases/tag/v1.0.1.0. Інсталятор `SCLOC-Verse_Setup.exe` (68.3 МБ, ProductVersion=1.0.1.0) + SHA256 прикріплені. Release notes українською без mojibake (перевірено браузером).
155. **Блокер релізу виявлено та усунуто під час публікації.** `.iss` та `build-installer.ps1` посилались на застарілий TFM-шлях `net9.0-windows\win-x64\publish` (з v1.0.0.0), тоді як csproj змінив TFM на `net9.0-windows10.0.18362.0` ще в коміті `aa6c5d8` (Етап C — Toast). Стара publish-директорія містила SCLOCVerse.exe версії 1.0.0.1 (від 5 липня) — ISCC взяв би застарілу версію. Фікс: `.iss` рядки 2+6 та `build-installer.ps1` рядок 13 оновлено на `net9.0-windows10.0.18362.0`. Стара директорія видалена. Після фіксу publish+ISCC зібрав інсталятор ProductVersion=1.0.1.0 коректно.

## 14.20. SEW — SCLOC-Verse Engineering Workflow (2026-07-09) — ✅ IMPL

> **Власна методологія процесу розробки.** Базується на практиках OpenSpec, Spec Kit, BMAD, але **не є їх копією** — адаптована під одноосібну розробку, WPF/.NET, ручну композицію та Knowledge Base. Заборонено зовнішні CLI (OpenSpec/Specify/BMAD CLI).

156. **SEW інтегровано в AGENTS.md як розширення** 8-крокового циклу (Форензик → План → Погодження → Резервний commit → Реалізація → Звіт → Фінальний commit), а НЕ як окремий документ-методологія. Це узгоджується з правилом KB «не створювати нових документів для вже описаних підсистем». Повний цикл: Forensic → Evidence → Proposal → Design → AEC Review → Approval → Backup Commit → Implementation → Verification → Quality Gates → Security Review → Acceptance → KB Synchronization → ADR → Commit → Retro.
157. **Ієрархія пріоритетів згорнута у 3 рівні** (замість плоского списку 1–11): Рівень 1 (Безпека: Безпека→Докази→Root Cause) > Рівень 2 (Стабільність: Zero Regression→Reuse First→Minimal Change) > Рівень 3 (Зручність: KISS→DRY→Автономність→Продуктивність→Естетика). Жоден нижчий рівень не порушує вищий.
158. **Quality Gates — дворівнева модель** (`docs/checklists/Quality-Gates.md`):
    - **Core Gates** (обов'язкові для всіх змін): Root Cause, Zero Regression, Evidence, UTF-8.
    - **Extended Gates** (лише для критичних доменів БД/Auth/Installer/Network/Supabase/API): Simplicity/Anti-Abstraction, Integration First, Security Review.
    - Прецедент: `docs/checklists/Database-Verification.md` вже реалізує Extended для БД-домену.
159. **Forensability замість «Observability First»** — принцип перейменовано для узгодження з Конституцією Observability Стаття 1 (Absolute Isolation) та Стаття 10 (Kill-Switch). SEW вимагає НЕ observability у кожній фічі, а **можливості форензику інциденту з платформи** (Стаття 9).
160. **Security Review — за тригером домену** (`docs/checklists/Security-Review.md`): виконується лише якщо зміна зачіпає OAuth/Auth/Installer/Auto Update/Network/Registry/FS/SQL/Supabase/API/RLS/Crypto. Для звичайних UI/локалізації — НЕ потрібне.
161. **Acceptance Criteria (AC)** — кожна нетривіальна задача містить явні AC1/AC2/..., що формуються на етапі Proposal/Design ДО реалізації. Тривіальні правки (опечатка, UI-твік) — одне AC.
162. **ADR — канонічний файл `ARCHITECTURE_DECISIONS.md`** (формат «Контекст→Рішення→Наслідки»). SEW розширює до «Проблема→Контекст→Варіанти→Рішення→Наслідки» для складних рішень. Створюється лише для важливих архітектурних рішень.
163. **Exit Criteria** — задача не завершена, доки не виконано: реалізація, перевірка, всі AC, Core Quality Gates (Extended за доменом), Security Review (за тригером), KB Synchronization, Self-Critique, ADR (за потреби), фінальний commit.
164. **Retro** — раз на місяць або після значущої функції. НЕ створює окремий артефакт — фіксується через оновлення KB §16 (Technical Debt) та §17 (Backlog).
165. **`docs/backlog/` — деталізація великих задач** (конвенція `docs/backlog/README.md`). KB §17 лишається індексом (короткі картки з посиланнями), деталі живуть у `docs/backlog/<task-slug>.md`. Аналогія: forensic-документи в `docs/observability/` + KB як індекс.
166. **Автономність агента — з чіткими межами.** Дозволено самостійно: досліджувати кодову базу, знаходити точки інтеграції, аналізувати залежності, шукати першопричину, перевіряти KB/AGENTS.md/Constitution, проводити форензик, формувати варіанти рішень. НЕ дозволено без погодження: змінювати архітектуру, глобальні рефакторинги, публічний API, схему БД, правила безпеки, незворотні дії. У таких випадках — зупинка після форензик-аналізу та очікування погодження.
167. **Правило невизначеності** — агент чітко розрізняє Підтверджений факт (VER/IMPL), Висновок (з маркером доведеності), Припущення (HYP), Особисту рекомендацію. Заборонено подавати припущення як підтверджений факт.
168. **Self-Critique** — перед завершенням роботи агент критично оцінює власне рішення (простіше/безпечніше рішення? більше reuse? зайва складність? неперевірені припущення?). Негативна відповідь хоча б на один пункт — задача не завершена.

### 14.20.1. SEW vs зовнішні методології (forensic-дослідження 2026-07-09)

169. **Досліджено 4 методології** (Spec Kit, BMAD, OpenSpec, SDD). Висновок: впроваджувати жодну цілком заборонено — конфлікт з архітектурними заборонами AGENTS.md (WPF-клієнт vs CLI-Mandate Spec Kit; Claude Code plugins BMAD; Node CLI OpenSpec). SEW запозичує лише концепції: Quality Gates (Spec Kit), AC-driven flow + Security-домени (BMAD), delta/brownfield-first (OpenSpec).
170. **Заборонено паралельні структури** `openspec/`, `.specify/`, `.bmad/`, окремі `docs/specs/` — вони дублюють Knowledge Base. SEW-артефакти інтегровані в існуючу структуру: `docs/checklists/` (Gates, Security), `docs/backlog/` (деталі задач), `ARCHITECTURE_DECISIONS.md` (ADR), KB §14/§15/§16/§17 (рішення/борг/беклог).

### 14.20.2. SEW Core — формалізація PRACTICED-механізмів (аудит 2026-07-09, раунд 2)

> Аудит поточної поведінки SEW (а не лише тексту AGENTS.md) виявив 10 механізмів, які вже багаторазово практикуються, але не були формалізовані в Core. За принципом «Methodology Gate НЕ застосовується до документування PRACTICED поведінки» вони перенесені до Core без окремого пілоту. Доказ: рефлексія власної поведінки агентом у сесії аудиту + Git-історія (40+ forensic-комітів) + 9 зовнішніх платформ (Anthropic, Claude Code, Cursor, OpenHands, Devin, AutoGen, CrewAI, OpenSpec, Spec Kit, BMAD).

171. **Adaptive Resource Optimization** (принцип) — «Maximum Result → Minimum Resources»: мінімально необхідна кількість агентів/субагентів/MCP/Skills/Workflow/інструментів. Природно доповнює Reuse First + Minimal Change. Зовнішній доказ: Anthropic multi-agent 15× токенів; CrewAI Cost-Efficient principle. PRACTICED: у сесії аудиту агент не викликав task/explore/agent_manager без потреби.
172. **Capability Detection** (чехліст перед нетривіальною задачею) — визначення можливостей середовища (git/terminal/build/internet/MCP/subagents/skills/recall/parallel). PRACTICED: у сесії аудиту агент неявно виконав (4 паралельні webfetch, kilo_local_recall, glob+read+bash паралельно). Описує **ролі**, не продукти — переживає зміну IDE.
173. **Evidence Discovery** (пріоритет джерел: KB → AGENTS → код → forensic → зовнішні) — IMPLEMENTED через AGENTS.md §«Єдина база знань» (спочатку KB, потім першоджерела). PRACTICED: у сесії аудиту пройдено всі 5 рівнів.
174. **Resource Discovery adaptive** (5 рівнів: Project → Global → Marketplace → Dynamic → Manual) — замість 13-крокової моделі. PRACTICED: kilo_local_recall → AGENTS → KB → checklists → forensic → webfetch, без створення нових ресурсів.
175. **Adaptive Workflow** (матриця тип→цикл: Trivial/Bug/Feature/Refactoring/Architecture/Migration/Security/Incident) — PRACTICED: `docs/checklists/Database-Verification.md` § «Коли чеклист НЕ потрібне» + «trivial AC».
176. **Decision Engine** (≥2 варіантів для significant, ≥3 для architectural) — IMPLEMENTED через ADR-формат AGENTS.md «Проблема → Контекст → Варіанти → Рішення → Наслідки». PRACTICED: Phase 3A «generated → trigger» серед альтернатив; Phase 3.5 Outcome-dependent Field Policy; у сесії аудиту — Кластер A/B/C/D.
177. **Continuous Learning Loop** (Knowledge → Resource → Process → Project) — PRACTICED у фрагментах: KB Sync (15+ комітів) + Retro + SEW виник через вивчення зовнішніх методологій. Повний конвеєр `Knowledge → Rule → Checklist → Prompt → Pattern → Template → Automation` — HYPOTHESIS (див. §17).
178. **Multi-Agent principle** (інтелектуальне використання екосистеми лише коли покращує результат) — PRACTICED: kilo_local_recall у сесії аудиту; обґрунтована відмова від 30+ Skills/Agent Manager/MCP без потреби. Multi-Agent як обовʼязковий крок для всіх задач — HYPOTHESIS (див. §17).
179. **Methodology Gate** (формальний розділ з 6 питаннями) — IMPLEMENTED неявно через AGENTS.md «не створювати нових документів». Ключове розширення: **НЕ застосовується до документування PRACTICED поведінки** — інакше парадокс «робить 100 разів, але не може описати». Pilot потрібен лише для EXPERIMENTAL/HYPOTHESIS.
180. **Methodology Evolution Workflow** (Meta-Evolution режим) — коли предмет аналізу — сама SEW. Будь-яке правило може бути підтверджене/змінене/обʼєднане/спрощене/вилучене за Evidence. Запобігає самозахисту SEW від власної критики. Повний Constitutional Review як окремий формальний процес — HYPOTHESIS (див. §17).

### 14.20.3. SEW — Engineering Orchestrator (визначення)

181. **SEW = AI Engineering Operating System** (Engineering Orchestrator), не окремий виконавець. SEW визначає правила/межі/якість/ресурси/цикли. Рівень автономії визначає виконавець (модель + середовище + Agent Framework). Метафора «головний інженер» відхилена (див. Rejected #76) як персоніфікація системи.

## 14.25. Settings Hub — Центр керування налаштуваннями (2026-07-09) — ✅ IMPL (Phase 0 + Phase 0.5 + Overlay)

> **Архітектурне рішення ADR-009.** Детальна специфікація: [`docs/backlog/settings-hub.md`](backlog/settings-hub.md). Phase 0 + Phase 0.5 (hotkey editor) + Overlay (live-preview, bidirectional sync, position) реалізовано. Build 0 warnings.

182. **Settings Hub замінює `SettingsCanvas`.** Замість єдиного плоского `SettingsCanvas.xaml` (514 рядків) — нова оболонка: ліва панель-навігатор категорій + права панель вмісту. Варіант B серед 3 (див. ADR-009). Причина: плоский список не масштабується під overlay- та hotkey-налаштування; UX First — пошук параметра <30с.
183. **Категорії за функцією, не за інструментом.** `Загальне`, `Гарячі клавіші`, `Overlay` (Phase 0) + зарезервовані `Головна`, `Локалізація`, `Інтерфейс`, `Профіль`, `Про програму`. Не «налаштування Hangar Timer» / «Anti-AFK».
184. **Phase 0 категорії:** `Загальне` (Star Citizen шлях, автозапуск, трей, автооновлення локалізації, канал, менеджер версій, очистити кеш, розширена діагностика), `Гарячі клавіші` (14 дій через `HotkeyService`), `Overlay` (масштаб/прозорість/позиція через `HangarSettingsService`).
185. **Контент лише для реалізованого функціоналу (no fabricated fields).** Anti-AFK без оверлея → плейсхолдер «інструмент не активний», а не вигадані повзунки. Зарезервовані категорії показують плейсхолдер, не порожні поля.
186. **Миттєве збереження без Apply; мітка «•»** біля зміненого пункту. Reset лише **per-category** (унизу категорії) та **per-control** (↺). **Глобального «Скинути все» немає** (див. Rejected #80).
187. **`F1` відкриває Hub лише при активному вікні SCLOC-Verse** (не глобально). Причина: уникнення конфлікту з ігровою/системною допомогою F1. Реалізація: Window-фокус-чек перед відкриттям.
188. **Шлях до гри = `...\StarCitizen`** (не `\LIVE`); середовища (LIVE/PTU/EPTU/TECH-PREVIEW) — **read-only індикатори** автовизначення, не перемикачі. Опис шляху: «Використовується для встановлення локалізації та конфігурації.»
189. **Reuse First — без нових сервісів у Phase 0.** Hub лише споживає існуючі контракти: `ISettingsService`/`IUpdateChannelService`/`IPreferencesService` (Загальне), `HotkeyService` + `HotkeyConflictPolicy` (Гарячі клавіші), `HangarSettingsService` (Overlay). Категорії реалізуються як Canvas (ADR-006).
190. **Профіль зарезервований** для майбутнього `profile.json` (локальний контракт/схема-версія, Phase 1) та опціональної синхронізації Supabase (Phase 2). Hotkey-**bindings** — user preferences (можуть синхронізуватись); hotkey-**події** — L3 Local Only (ніколи).
191. **Схема БД не зачіпається** (Phase 0 — суто UI-шар). Security Review не потрібне (UI/локалізація, не Auth/Installer/Network/SQL).
192. **Phase 0 реалізовано через SettingsCanvas-фаçade (Zero Regression).** SettingsCanvas залишається Canvas (сумісність із CanvasManager); мігровані контролі «Загальне» живуть у `GeneralSettingsPane`, а SettingsCanvas зберігає фасадну property-поверхню → MainWindow.xaml.cs працює без змін.
193. **Гарячі клавіші — Варіант A+ (read-only), ✅ VER+IMPL.** Forensic: `HotkeyService` не мав API переліку, а `CurrentGesture` ніколи не персистувався → повний редактор порушив би «контент лише для реалізованого». Phase 0 показує 13 реальних комбінацій read-only. Additive API: `IHotkeyService.GetDefinitions()`. Повний редактор (persistence/rebind/capture/conflict/reset/sync) → Phase 0.5.
194. **Overlay — реальний редактор з live-preview + двосторонньою синхронізацією, ✅ VER+IMPL.** `IHangarSettingsService` персистує scale/opacity/X-Y. `HangarTimerState` — SSOT: слайдер пише в settings + state; хоткеї пишуть в state; `state.PropertyChanged` синхронізує UI. Зміни застосовуються негайно до відкритого overlay. `IHangarTimerService.OverlayService` (additive) надає доступ. Drag оверлея → `PositionChanged` event → поля X/Y оновлюються. Повзунок: jump-to-click (PreviewMouseLeftButtonDown обчислює точне значення). Build оптимізація: Debug = framework-dependent (8× швидше, 283→40 файлів).

## 14.26. Settings Hub — Дизайн-система (2026-07-10) — ✅ APPROVED

> **Доктрина.** Повна специфікація: [`docs/backlog/settings-hub-design-system.md`](backlog/settings-hub-design-system.md).
> Settings Hub — це **дизайн-система**, а не окремий екран: правила, за якими будується будь-яка сторінка налаштувань. «Центр керування» — інтерфейс, що показує стан продукту й дозволяє ним керувати, а не лише змінює параметри.

195. **Коренева причина design-drift (✅ VER, UX Forensic 2026-07-10).** Реалізація Phase 0 виглядає як стара сторінка налаштувань (`SettingsCanvas`) із доданим меню, а не як окремий центр керування. Наслідки: чекбокси замість тоглів; README-блок (відсутній у концепції); прихований стан продукту (немає бейджів середовищ, статус-крапок); скидання виглядає як видалення; стиснутість; злиття тонів; плаваючі контролі; «Головна» як категорія-пустушка; невидимий індикатор активної категорії. Архітектура/функціональність — 10/10; візуальна мова/відповідність концепції — 6–7/10.
196. **P0 Identity First — головне правило.** Settings Hub має сприйматись як окремий центр керування продуктом, а не як стара сторінка налаштувань із меню. Усі принципи нижче — наслідки P0; рішення, що суперечить P0, хибне.
197. **Принципи дизайн-системи (наслідки P0):** P1 Reset≠Delete (скидання — оборотна дія, нейтральний стиль, не небезпечне); P2 Простір як ієрархія (щедрі відступи, спокій/делікатність); P3 Контраст і відокремлення (три шари глибини + м'які межі); P4 Єдина колонка контролов (усі контролі дії на одній правій вертикалі); P5 Progressive Disclosure (лише реалізований функціонал — див. §14.25 #185, #193).
198. **Design Kit — спільний набір для всіх сторінок.** Каркас (заголовок+лічильник+опис+зона груп+рядок скидання категорії); групи з uppercase-заголовком; рядок «назва+опис ліворуч, контрол праворуч»; тогл для булевих; ↺ скидання per-control + per-category; видимі статуси продукту; єдина шкала відступів і палітра; «Головна» — навігація повернення. Нова категорія береться з набору, а не дизайнується з нуля.
199. **Критерій завершення UX Polishing.** Не «список пунктів закрито», а сліпий перегляд впізнає Hub як окремий центр керування, а не як стару сторінку з меню (P0). Еталон відчуття — HTML-макети `.kilo/settings-mockups/` (та сама якість сприйняття, не піксель-в-піксель).

## 14.27. Anti-AFK — модуль анти-AFK (2026-07-10) — ✅ IMPL

> Міграція функціональності з SCLOCUA (WinForms .NET 4.8) → SCLOC-Verse (WPF .NET 9).
> Форензик: 5 ревізій (R1–R5). Джерело: `F:\C\SCLOCUA\AntiAFK.cs`.

200. **GetLastInputInfo замість глобальних hooks (✅ VER+IMPL).** Старий код використовував `WH_KEYBOARD_LL` + `WH_MOUSE_LL` (72 рядки `HookManager`, always-on keylogger). Замінено на `GetLastInputInfo()` — 1 PInvoke, 0 hooks, privacy-safe. Root Cause First: коренева причина hooks — виявлення бездіяльності; GetLastInputInfo вирішує це в один виклик system-wide. Zero Regression: `RawInputBackend` не зачеплено (жодних hooks).

201. **AntiAfkService — координатор (✅ IMPL).** 2 залежності: `IHotkeyService` + `IPreferencesService`. `System.Threading.Timer` (1с poll) → idle check → `SendInput(±1px, MOUSEEVENTF_MOVE)` + random threshold (1–60с, анти-детект). `IDisposable`. Auto-start зі збереженого стану. Не залежить від Hangar Overlay. **Foreground Gate (2026-07-11, ✅ IMPL):** `TimerCallback` пропускає дію, коли активне вікно ≠ Star Citizen — спільний `StarCitizenForeground.IsStarCitizenForeground()` (див. §14.30). Alt+Tab → тиша (без SendInput), повернення у SC → відновлення. **Видимість індикатора за гейтом (2026-07-11, ✅ IMPL):** не-SC → індикатор приховано (`EnsureIndicatorHidden`, idempotent через `_isIndicatorVisible`), SC + Running-режим → показано (`EnsureIndicatorVisible`). IdleOnly-спалах лише при SC-foreground. UI/налаштування незмінні (без Paused-стану).

202. **AntiAfkIndicatorWindow — окремий overlay (✅ IMPL).** Пульсуюча точка (Ellipse). Win32 `WS_EX_TRANSPARENT | WS_EX_LAYERED` — click-through. 2 анімації: Pulse (0.6с, різкий) / Breathing (2.5с, SineEase). GPU-accelerated Opacity animation. 5 позицій (TopLeft/TopRight/BottomLeft/BottomRight/Center). Не залежить від Hangar Overlay — повністю незалежний життєвий цикл.

203. **Налаштування через IPreferencesService (✅ IMPL).** 6 additive пар: enabled (bool), color (hex string, `"Hidden"` = приховати), position (enum, string у Settings), size (8–32px double), animation (enum), mode (enum). Enum у коді, string у Settings.Designer — конверсія `Enum.TryParse` на межі `SettingsService`.

204. **Гаряча клавіша End (✅ IMPL).** `HotkeyIds.AntiAfkToggle` = `"AntiAfk.Toggle"`, `HotkeyGesture(None, End)`. Rebind-able через Phase 0.5 editor. Автоматично з'являється у «Гарячі клавіші» через `GetDefinitions()`.

205. **Indicator Mode: Running / IdleOnly (✅ IMPL).** Running — індикатор видимий безперервно при on. IdleOnly — спалах (~1.5с) при кожному `SendInput` (зворотний зв'язок: бачити момент дії).

206. **Settings Hub — блок у Overlay (✅ IMPL).** Плейсхолдер (OverlaySettingsPane.xaml:163–174) замінено на реальний `HubGroupCard`: toggle + 4 ComboBox (колір/позиція/анімація/режим) + slider (розмір). Live Preview через `IAntiAfkService.ApplyIndicatorSettings()`. `_isAntiAfkSyncing` flag запобігає зацикленню (аналог `_isSyncing` для Hangar).

207. **FollowOverlay відхилено (✅ REJ).** Ревізія R4 пропонувала позицію «Follow Overlay» (індикатор слідкує за Hangar Overlay). Прибрано в R5: крос-модульна залежність задля 1 опції суперечить KISS. Anti-AFK повністю незалежний від Hangar Overlay.

208. **Телеметрія відкладена.** `anti_afk.toggle` event не входить у першу реалізацію. Спочатку стабільність функції + UX, потім observability.

209. **Схема БД не зачеплена.** Anti-AFK — суто локальний модуль (user.config persistence). Security Review не потрібне.

## 14.28. Auto Key — модуль авто-натискання клавіші (2026-07-11) — ✅ IMPL

> Незалежний модуль: автоматичне натискання заданої клавіші через заданий інтервал,
> лише коли активне вікно (foreground) — Star Citizen. Перша задача — авто-прийняття
> місій/запрошень. Повністю незалежний від Anti-AFK та Hangar Timer.
> Форензик: 3 ітерації Decision Engine (Caption → кеш PID → stateless PID).

210. **Stateless foreground-гейт за PID процесу (✅ VER+IMPL).** Кожен send-цикл: `GetForegroundWindow()` → `GetWindowThreadProcessId()` → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `QueryFullProcessImageName()` → `Path.GetFileName` → `string.Equals(name, "StarCitizen.exe", OrdinalIgnoreCase)`. **Жодного кешу PID, жодної логіки відновлення** — сервіс завжди бачить поточний стан системи. Переживає краш/перезапуск/Alt+Tab гри та запуск гри після SCLOC-Verse. Доведено Spy++: процес `StarCitizen.exe`, вікно Class=`CryENGINE`, Caption=`Star Citizen`. Перевірка за PID процесу, а не за Caption (не залежить від локалізації/редакції заголовка).

211. **Відхилено: кешування PID (✅ REJ).** Кеш PID на весь час життя сервісу ламається при краші/перезапуску гри (нова PID → сервіс «помирає»). Також відхилено Caption-Contains (залежність від заголовка) та `Process.GetProcessesByName` як gate на циклі (заборонено ТЗ, дорожче). Лінива re-resolve + Auto Recover (throttle) також відхилені як зайва складність — stateless-підхід усуває весь клас ризиків відсутністю стану.

212. **OpenProcess == NULL → тихо PAUSED (✅ IMPL).** Якщо `OpenProcess` не відкрився (доступ/перехід вікна) — `TryGetProcessName` повертає `null` без логів і без винятків → стан PAUSED, **0 SendInput**. SendInput виконується лише у стані RUNNING (активне вікно Star Citizen), після встановлення стану (спочатку стан, потім дія).

213. **AutoKeyService — координатор (✅ IMPL).** 2 залежності: `IHotkeyService` + `IPreferencesService`. `System.Threading.Timer` (100–2000мс, default 1000). `SendInput(KEYBDINPUT, key-down + key-up)`. `IDisposable`. Не авто-стартує при конструюванні (патерн Anti-AFK: toggle/хоткей керує). Не залежить від Anti-AFK та Hangar Timer.

214. **AutoKeyIndicatorWindow — окремий overlay (✅ IMPL).** Компактний badge «● Auto Key», click-through (`WS_EX_TRANSPARENT | WS_EX_LAYERED`), фіксована позиція **TopLeft** (не перетинається з Anti-AFK у TopRight). 3 стани: Off (сірий, приховано) / Running (зелений) / Paused (жовтий, «Auto Key · Paused»). Повністю незалежний життєвий цикл.

215. **Налаштування через IPreferencesService (✅ IMPL).** 3 additive пари: enabled (bool), actionKey (`HotkeyKey` enum, string у Settings, default `Oem4`='['), intervalMs (int 100–2000, default 1000). Enum у коді, string у Settings.Designer — конверсія `Enum.TryParse` на межі `SettingsService`.

216. **Гаряча клавіша Home (✅ IMPL).** `HotkeyIds.AutoKeyToggle` = `"AutoKey.Toggle"`, `HotkeyGesture(None, Home)`. Home вільний (Hangar: F6–F9, Anti-AFK: End). Rebind-able через Phase 0.5 editor. Автоматично з'являється у «Гарячі клавіші» через `ResolveGroup` → група «Auto Key» (`GroupOrder`=5).

217. **Settings Hub — блок у Overlay (✅ IMPL).** `HubGroupCard`: toggle (Enable) + Action Key keycap (capture через `KeyInterop.VirtualKeyFromKey`) + slider (інтервал 100–2000мс, snap 50). Live Preview через `IAutoKeyService.ApplySettings()` (перезапуск таймера з новим інтервалом). `_isAutoKeySyncing` flag запобігає зацикленню. Статус-крапка + текст («працює»/«очікує Star Citizen»/«вимкнено») синхронізуються через `StateChanged`.

218. **Action Key capture без зміни InputSystem (✅ IMPL).** Capture використовує WPF `KeyInterop.VirtualKeyFromKey(Key)` → VK → cast `(HotkeyKey)vk`. Не зачіпає `HotkeyCaptureMapper` (DRY — окремий шлях для однієї клавіші без модифікаторів). Display через `AutoKeyFormats.FormatKey` (OEM-символи: `[`, `]`, `\` тощо).

219. **Телеметрія відкладена.** `auto_key.toggle` event не входить у першу реалізацію (патерн Anti-AFK). Спочатку стабільність + UX, потім observability.

220. **Схема БД не зачеплена.** Auto Key — суто локальний модуль (user.config persistence). Security Review не потрібне (SendInput локальний, PID-гейт deterministic).

## 14.29. Динамічні підказки гарячих клавіш — SSOT (2026-07-11) — ✅ IMPL

> UX-регресія: підказки Hangar Timer містили жорстко прописані комбінації в XAML
> → брехали після rebind у «Гарячих клавішах». Прибрано дублювання даних.

221. **Коренева причина (✅ VER).** `HangarTimerCard.xaml` popup (4 групи) та `HangarOverlayWindow.xaml` (рядок-підказка) містили статичні `TextBlock`-и з комбінаціями. Жодного зв'язку з `IHotkeyService` → після rebind `CurrentGesture` оновлювався, але підказки показували заводські жести довічно.

222. **SSOT — GetDefinitions (✅ IMPL).** Підказки читають `IHotkeyService.GetDefinitions()` (то ж джерело, що й «Гарячі клавіші»). `HangarTimerCard.RebuildHotkeyPopup` — при кожному відкритті popup (recompute-on-open, KISS: події зміни визначень у `IHotkeyService` немає). `HangarOverlayService.BuildHotkeyHint` — при показі overlay. Жодного жорстко прописаного жесту в XAML підказок.

223. **Спільний форматувальник (✅ IMPL, DRY).** `HotkeyGestureFormat.Format(HotkeyGesture)` — єдине місце форматування gesture→«Ctrl+Shift+F7». Споживачі: `HotkeysSettingsPane` (делегує), `HangarTimerCard`, `HangarOverlayService`. Прибрано приватні дублікати `FormatKey`/`FormatGesture` з `HotkeysSettingsPane`.

224. **Дизайн незмінний (Zero Regression).** Popup зберіг стилі (`PopupKeyStyle`/`PopupHintTextStyle`/`PopupGroupHeaderStyle`), 2-колонковий Grid на групу, ⌨-хедер, 💡-тіп. Описи тепер = `Definition.Description` (SSOT), формат жестів уніфікований з «Гарячими клавішами».

225. **Presentation-структура груп popup (✅ IMPL).** Явне групування `group → [HotkeyId]` у `HangarTimerCard` (Основні/Цикл/Масштаб/Прозорість). Це **не дублювання комбінацій** (жести/описи читаються live) — лише те, які id показати разом. Overlay-підказка фільтрує `Id.Value.StartsWith("HangarTimer.")`; `Esc: закрити` — статичний суфікс (локальна клавіша вікна, не HotkeyService).

226. **Інʼєкція (additive).** `IHotkeyService` → `HangarTimerCard.SetHotkeyService` (через `ScToolsCanvas.SetHotkeyService` ← `MainWindow`) та у `HangarOverlayService` (новий ctor-параметр; у `AppCompositionRoot` конструкція `HotkeyService` перенесена ДО `HangarOverlayService`). `IHangarTimerService` не розширювався.

## 14.30. StarCitizenForeground — спільний Foreground Gate (2026-07-11) — ✅ IMPL

> DRY: stateless-перевірка активного вікна Star Citizen винесена з AutoKeyService у
> спільний helper; підключена до Anti-AFK (див. §14.27 item 201).

227. **StarCitizenForeground (✅ IMPL).** `Helpers/StarCitizenForeground.IsStarCitizenForeground()` — stateless: `GetForegroundWindow` → `GetWindowThreadProcessId` → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `QueryFullProcessImageName` → `Path.GetFileName` → `string.Equals(name, "StarCitizen.exe", OrdinalIgnoreCase)`. `OpenProcess==NULL` → тихо `false` (без логів/винятків). Перевірка за PID процесу, а не за Caption.

228. **AutoKeyService — прибрано дубль + видимість за гейтом (✅ IMPL, Zero Regression).** Приватний foreground PInvoke-блок + `StarCitizenProcessName` видалено; `TimerCallback` делегує `StarCitizenForeground.IsStarCitizenForeground()`. **Видимість індикатора (2026-07-11, ✅ IMPL):** `SetState` показує індикатор лише у `Running` (SC-foreground), приховує у `Paused`/`Off`. Guard `_state==newState` захищає від churn. `StartInternal` — миттєвий фідбек видимості (без SendInput). Стани Off/Running/Paused + SendInput незмінні.

229. **AntiAfkService — підключено гейт (✅ IMPL).** Один рядок у `TimerCallback` (після disposed/running): `if (!StarCitizenForeground.IsStarCitizenForeground()) return;`. При SC-foreground — байт-в-байт як раніше; при не-SC — skip `SimulateMouseMove` + skip `FlashIndicator`. Детекція бездіяльності/SendInput/індикатор/хоткей не зачеплені. UI незмінний.

---

## 14.31. Release v1.0.2.0 Stable (2026-07-11) — ✅ IMPL

> Version bump після Release Readiness Forensic. 68 комітів після v1.0.1.0 (`e30fb886`): Auto Key, Anti-AFK міграція, Foreground Gate, Overlay Settings, динамічні підказки хоткеїв, десятки UI-fixes.

230. **Version Update: 1.0.1.0 → 1.0.2.0.** Колізія версії виявлена під час Release Readiness Forensic: csproj=1.0.1.0 вже було PUBLISHED (tag `v1.0.1.0` на `e30fb886`, 68 комітів тому). Змінено 3 рядки у `SCLOCVerse.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>` → `1.0.2.0`). `AssemblyInformationalVersion` не заданий явно — MSBuild використовує `<Version>` (single-source-of-truth, #150). Інсталятор `.iss` читає версію з exe через `GetFileVersion` → автоматично синхронізується. Release-білд: 0 warnings, 0 errors.
231. **Release scope v1.0.2.0.** 68 комітів `v1.0.1.0..HEAD` (`f25123d`). Нові модулі: Auto Key (#14.28, SendInput з кореневим фіксом INPUT=40 байт), Anti-AFK міграція з WinForms (#14.27, GetLastInputInfo замість hooks), StarCitizenForeground Foreground Gate (#14.30), динамічні підказки хоткеїв SSOT (#14.29). Settings Hub Phase 0.5 + Overlay live-preview (#14.25). Жодних breaking changes (additive-only, Settings мігрують автоматично). Release Notes: `Installer/Release-Notes-v1.0.2.0.md`.
232. **Release v1.0.2.0 PUBLISHED.** Git tag `v1.0.2.0` → коміт `c473e63`. GitHub Release створено: https://github.com/Vova-Bob/SCLOC-Verse/releases/tag/v1.0.2.0 (Latest, не Draft/Pre-release). Інсталятор `SCLOC-Verse_Setup.exe` (68.4 МБ, ProductVersion=1.0.2.0, SHA256 `eaba2204…`) прикріплений. Release notes українською без mojibake. Build: 0 warnings, 0 errors. MojibakeScanner: 8 false-positives (патерн `\u0420\u0456` = «Рі» — легітимний український, верифіковано byte-level; 1 — коментар-документація в `Updater.cs:764`).

233. **Forensic: Supabase Production Audit (2026-07-11).** Повний аудит production (`nrytczdbhehiotflaagl`): API, Postgres, Auth, Realtime, Storage, Edge Functions, Database Health. Знайдено: (1) **RC-1** ✅ VER — `BackgroundUpdateMonitor` відправляв 3 невалідних outcome (`Updated`, `UpdateAvailable`, `UpdateFound`), яких немає в `chk_telemetry_outcome` (дозволені: Started/Succeeded/Failed/Cancelled/Skipped). Кожна така подія відхилялась БД → requeue → нескінченний retry loop (~2880 ERROR/добу). (2) **RC-0** ✅ VER — Retention Pipeline відсутній повністю: pg_cron не встановлений, cleanup-функцій не існуло, GRANT DELETE відсутній. Таблиця `telemetry_events` росла без обмеження. (3) **LIA failures** 🔵 HYP — 23 Failed (17× GitHub API `InvalidOperationException` + 6× PowerShell `UnknownExitCode`), але HTTP status/PowerShell output втрачені в exception wrapping — root cause не доведено. (4) **Notifier Worker offline** ✅ VER — 2 Pending notifications, 0 attempts.

234. **RC-1 Fix: BackgroundUpdateMonitor invalid outcomes (2026-07-11).** ✅ IMPL — Виправлено 3 рядки в `BackgroundUpdateMonitor.cs`: `UpdateFound`→`Skipped` (AppCheck, LiaCheck), `Updated`→`Succeeded` + `UpdateAvailable`→`Skipped` (LocalizationCheck). Commit `a84ab59`. Build 0/0. Семантика: `Skipped` не входить у success_rate формулу. Статус: реалізовано, **очікує production deploy** для верифікації зникнення `chk_telemetry_outcome`.

235. **Phase 5.1: Retention Pipeline — telemetry_events 90d (2026-07-11).** ✅ IMPL — Міграція `20260711180000_retention_pipeline_phase5_1.sql`: встановлено `pg_cron`, створено `purge_old_telemetry_events(int)` + `run_retention_pipeline()` (SECURITY DEFINER, owner postgres, `SET search_path = public, pg_catalog`). Dispatcher pattern: єдина точка входу, майбутні таблиці додаються одним `RETURN QUERY`. Cron: daily 03:00 UTC. FK `ON DELETE SET NULL` — безпечно для incidents. RAISE LOG на старті/завершенні. Guard: `retention_days <= 0 → EXCEPTION`. Verification V1-V7 пройдено (113 rows незмінно, 0 purged, логи підтверджені). Commit `37a5a2a`.

236. **RC-2: BackgroundUpdateMonitor repeated Diagnostic telemetry (2026-07-11).** 🟢 VER — При `AdvancedDiagnostics=true` та `AutoUpdateLocalization=false`, `CheckLocalizationAsync` генерує Diagnostic event кожен цикл (1 год) для того ж `HasUpdate=true` стану. Toast layer має version-based dedup (`NotificationRouter.BuildLocalizationCandidates`, `LastLocalizationToast`), телеметрія — ні. Побічний ефект, не задумана поведінка. Обсяг: 2-5 events/годину на Diagnostic-ON користувача. Статус: HYP→VER, **очікує план після закриття RC-1**.

237. **Hotfix v1.0.2.1 — RC-1 Production Release (2026-07-11).** ✅ IMPL + VER — Hotfix для v1.0.2.0: version bump 1.0.2.0→1.0.2.1 (csproj), build з HEAD (`37a5a2a`). Стратегія: Варіант A (build з HEAD, не cherry-pick) — безпечно, бо лише 1 compiled-файл з runtime-зміною (`BackgroundUpdateMonitor.cs`, 3 рядки). Release notes: `Installer/Release-Notes-v1.0.2.1.md`. GitHub Release: https://github.com/Vova-Bob/SCLOC-Verse/releases/tag/v1.0.2.1, asset `SCLOC-Verse_Setup.exe` (60.7 МБ, SHA256 `2472ca2962285ae72c83b78aceaa64203cef81bbe06bf221093baadb75821ee6`).

## 14.32. Post-Release Production Forensic v1.0.2.1 (2026-07-12)

> Повний аудит production стану після релізу v1.0.2.1. Evidence First. Жодних припущень без даних.

238. **RC-1 (`chk_telemetry_outcome`) — ✅ ЗАКРИТО.** Жодної невалідної події в `telemetry_events` за весь час (0 invalid outcomes). Postgres logs — 0 продакшн-помилок `chk_telemetry_outcome`. Джерело: SQL `COUNT(*) FILTER (WHERE outcome NOT IN (...))` = 0; source — `BackgroundUpdateMonitor.cs` fix (`a84ab59`). Версії: історичні порушення були виключно від `<1.0.2.1`; від `1.0.2.1` — 0 випадків.

239. **v1.0.2.1 adoption — ✅ VER.** `app_installations.app_version='1.0.2.1'` = 17 active installs (last_seen 2026-07-11 17:46 … 2026-07-12 17:45 UTC). v1.0.2.0 — 5 installs (останній last_seen 2026-07-11 16:09). v1.0.1.0 — 14 installs. v1.0.0.1 — 2 installs. v1.0.0.0 — 31 installs. Є телеметрія, позначена `app_version='1.0.2.1'` — 1 подія.

240. **Telemetry silence у v1.0.2.1 — 🔵 HYP (нова аномалія).** 17 installs оновили `app_installations.last_seen`, але лише 1 подія `telemetry_events` має `app_version='1.0.2.1'` (LIA|Install|Failed, install `edd8372f...`). 0 подій `Application/Start/Started` від v1.0.2.1. Порівняння: v1.0.1.0 за 24h — 18 events; v1.0.0.1 — 11 events; v1.0.2.1 — 1 event. Два інсталяції (0148b502…, dc627da2…) мали багату історію телеметрії у старих версіях, але після переходу `app_installations.app_version='1.0.2.1'` жодної нової події від них не надійшло. Гіпотези (не доведено): (a) Zero Noise Policy — після оновлення немає Failed-подій; (b) v1.0.2.1 процес не відправляє телеметрію успішного запуску; (c) користувачі запускали updater, але не перезапускали основний застосунок. **Потребує подальшого розслідування.**

241. **LIA Install failure — ⚠ ПРОДОВЖУЄТЬСЯ (pre-existing, не регресія v1.0.2.1).** Новий інцидент `INC-2026-00031` (`LIA|Install|InvalidOperationException|1.0.2.1`, Active, 1 user/install, peak_failure_pct=100%). Подія: 2026-07-11 17:47:38 UTC, install `edd8372f...`, user `7927f83f...`. Root cause — той самий InvalidOperationException, що й `INC-2026-00029/30` (v1.0.1.0, зараз Closed). Git diff v1.0.2.0→v1.0.2.1 у `LiaServices/Updater.cs` — лише коментар (UTF-8 fix), функціональних змін немає. Статус: не нова помилка, а той самий LIA pipeline на новій інсталяції. **Потребує окремого фіксу LIA (Backlog §17.1).**

242. **Notifier Worker offline — ❌ НОВА ПРОБЛЕМА (операційна).** `notification_queue` — 3 Pending записи (INC-29, INC-30, INC-31), 0 attempts за останні 24h, `notification_attempts` порожня. Сповіщення про інциденти не доставляються. Trigger promotion працює (INC-31 створився автоматично), але downstream Worker не обробляє чергу. **Потребує перевірки стану Notifier Worker / Discord webhook / host.**

243. **Phase 5.1 Retention Pipeline — ✅ VER.** `pg_cron` job `retention-pipeline-daily` active, schedule `0 3 * * *`. Перший запуск: 2026-07-12 03:00:00 UTC, status `succeeded`, duration ~51 мс. `telemetry_events` = 143 live rows (retention 90d). Ніяких purge ще не відбулось (дані молодші 90d).

244. **RC-401 Hotfix v1.0.2.2 — ✅ IMPL.** Production-інцидент: безперервний 401 storm на `POST /rest/v1/telemetry_events` (~14 req/сек, 4+ години). Root Cause: `TelemetryUploader.FlushAsync:86` — `_queue.Requeue(failed)` безумовно повертав відхилені події в чергу. При expired JWT (після failed refresh у SDK `TokenRefresh.HandleRefreshTimerTick`) події нескінченно циркулювали: Drain → Insert → 401 → Requeue → 30с → ∞. Hotfix: `TelemetryUploader.cs:85` — при `PostgrestException { StatusCode: 401 }` event не додається в `failed` (drop замість requeue). Черга спорожніє, timer знаходить порожню чергу, return — storm зупинено. Транзитні помилки (500, network) — як і раніше requeue. Build: 0 warnings, 0 errors. Git: `729e951` (dev), tag `v1.0.2.2`. GitHub Release: https://github.com/Vova-Bob/SCLOC-Verse/releases/tag/v1.0.2.2, asset `SCLOC-Verse_Setup.exe` (60.7 МБ, SHA256 `2a47d982dc3ebcdf8f21851931c72dc0561beaa890182402af321ea9dcbc625c`). Release notes: `Installer/Release-Notes-v1.0.2.2.md` (українською).

245. **RC-401 SDK Root Cause — 🟡 PROBABLE (не остаточно доведено).** `Supabase.Gotrue 6.0.3` — `TokenRefresh.HandleRefreshTimerTick` ковтає exception від `RefreshToken()` без очищення сесії. `GetInterval()` не клампує negative values → `new Timer(negative)` → `ArgumentOutOfRangeException` → caught/swallowed → timer помирає. Доказ: декомпільований SDK код + `auth.refresh_tokens` data (13 успішних refreshes 15:29–17:45 UTC, потім 0 — timer зупинився). НЕ доведено: чи був виклик `RefreshToken()` о ~18:33 UTC (Scenario A) чи timer не спрацював (Scenario B). Backlog: `docs/backlog/rc-401-sdk-refresh-bug.md`. Для остаточного підтвердження: Supabase Dashboard → Log Explorer → Auth → пошук `POST /auth/v1/token` о ~18:33 UTC 2026-07-12.

246. **RC-401 Audit: інших джерел 401 storm не існує — ✅ VER.** Повний аудит кодобази: єдиний timer-driven Supabase API caller — `TelemetryClient._flushTimer` (30с) → `TelemetryUploader` (FIXED). Інші Supabase API callers (`InstallationService`, `DiscordGuildSyncService`, `AuthService`) — event-driven, без timer, без retry, без loop. SDK `TokenRefresh` — 1 запит/48хв (не storm). Всі інші timers (`BackgroundUpdateMonitor`, `AntiAfkService`, `AutoKeyService`, `HangarOverlayService`) — не звертаються до Supabase API.

## 14.33. RC-401 Root Cause Investigation + Defense-in-Depth (2026-07-14)

> Форензик 401 storm: джерела, версійний розподіл, SDK audit, BackgroundUpdateMonitor investigation.

247. **Storm активний, створюють СТАРІ версії — ✅ VER.** API логи: ~100× `POST /rest/v1/telemetry_events → 401` за 5 сек = ~20 req/sec. 0 успішних інсертів telemetry за 20+ год. Патерн 100 запитів/batch = один flush-цикл одного користувача (BatchSize=100, foreach Insert). Storm від старих версій (v1.0.0.0–v1.0.2.1, 61 з 73 інсталяцій = 84%) з requeue loop (без RC-401 фіксу). Топ-9 підозрюваних (сесії мертві 19–27 год): romanshevtsov, acnedark, skorskiy.d.i, akva_tor, fargusriba, bohdan_58551, olegkuper., baro_ua, brv87.

248. **v1.0.2.2 НЕ доведено як джерело storm — 🔵 HYP.** Поточні 12 v1.0.2.2 юзерів мають свіжі `app_installations.last_seen` (під час останньої sync JWT був валідний — НЕ доведено що валідний зараз). Фікс v1.0.2.2 верифікований для andriu86 (KB #244). Storm rate (100/batch) відповідає requeue loop (старий код), не drop pattern (v1.0.2.2). Коректне формулювання: «під час останніх sync ці клієнти успішно автентифікувалися», а не «мають валідний JWT зараз».

249. **SDK audit (E): Supabase.Gotrue 6.0.3 — остання версія, баг не виправлений upstream — ✅ VER.** NuGet: 6.0.3 (26 липня 2024) — остання. Новіших релізів немає. GitHub tags зупинились на v4.0.2 (5.x/6.x не мають тегів). Master TokenRefresh.cs відрізняється від декомпільованого 6.0.3 (має DestroySession у RefreshToken catch), але це невипущений код. SDK upgrade неможливий. Fork — надто інвазивно (порушення KISS/Minimal Change).

250. **BackgroundUpdateMonitor (B): повністю незалежний від auth — ✅ VER.** `BackgroundUpdateMonitor` (DispatcherTimer 1 год) → `ApplicationUpdateService.CheckForUpdatesAsync` → GitHub REST API (не Supabase). `UpdateDownloader`/`UpdateVerifier`/`InstallUpdateAsync` — GitHub + локально. Pipeline не падає при втраті сесії. Проте оновлення **потребує ручної дії користувача** (toast → клік "Оновити"). Немає auto-download, auto-install, forced gate, або повторного нагадування (toast dedup по версії).

251. **Парадокс оновлення: неможливо доставити фікс на старі версії без ручного оновлення — ✅ VER.** Будь-яке покращення update UX (repeat reminders, forced gate, auto-download) потребує реалізації в НОВІЙ версії. Стара версія цей код не має і ніколи не матиме. Для stuck-юзерів (stale CurrentSession у працюючому процесі) — технічного шляху з сервера немає. Storm — тимчасовий (~4% Free Tier ліміту, 0 даних у БД), згасне природньо при перезапусках.

252. **A (auth.sessions cleanup) — знято.** Видалення рядків з `auth.sessions` не впливає на stale `CurrentSession` у працюючому процесі (SDK тримає стан in-memory). Експеримент без гарантії.

## 14.34. RC-401 Defense-in-Depth: C+D+F (2026-07-14) — ✅ IMPL

> Превентивні фікси для наступного релізу. Не вирішують поточний storm від старих версій,
> але запобігають майбутнім storm-ам коли v1.0.2.2+ юзер зіткнеться з протухлим JWT.
> Build: 0 warnings, 0 errors.

253. **C — JWT expiry check у TelemetryUploader (✅ IMPL).** `TelemetryUploader.FlushAsync` перевіряє не лише `CurrentSession != null`, а й валідність JWT через `JwtSecurityTokenHandler.ReadJwtToken(accessToken).ValidTo <= UtcNow+30s`. Якщо токен protух — return ДО відправки запитів. Запобігає 401 на корені, не покладається на exception matching після факту. +30с tolerance на розинхронізацію годинника.

254. **D — Stop/Resume телеметрії за auth-статом (✅ IMPL).** `ITelemetryService` розширено additive методами `Stop()`/`Resume()`. `TelemetryClient` має `_authStopped` flag — `OnFlushTick` пропускає відправку. `AuthService.OnAuthStateChanged`: `SignedOut` → `_telemetry?.Stop()`, `SignedIn` → `_telemetry?.Resume()`. Track() продовжує працювати (кладе в чергу), але flush-timer мовчить до повторної авторизації. Defense-in-depth: навіть якщо SDK bug залишає stale CurrentSession, `SignedOut` event зупиняє відправку. **D+ (додано):** `_telemetry?.Stop()` також викликається в `catch`-блоках `TryRestoreSessionAsync` (рядок 224) та `SignInAsync` (рядок 148) — бо SDK не завжди генерує `SignedOut` event при `SetSession` failure (напр. `refresh_token_already_used`). Без D+ фікс D покладався б на event, який може не надійти → телеметрія продовжувала б працювати з stale сесією.

255. **F — Batch insert замість foreach (✅ IMPL).** `TelemetryUploader.FlushAsync` відправляє до 100 подій одним HTTP запитом через `Insert(ICollection<TelemetryEvent>)` з `QueryOptions { OnConflict = "client_event_id", DuplicateResolution = IgnoreDuplicates, Returning = Minimal }`. 1 запит замість N (до 100× менше навантаження). Idempotent через `resolution=ignore-duplicates` (Стаття 6). Прибрано старий `IsDuplicate` helper (більше не потрібен — дублікати обробляються на рівні PostgREST). 401 drop (RC-401 fix) збережено як safety net.

256. **BackgroundUpdateMonitor (B) — ✅ VER: GitHub API, незалежно від auth.** `ApplicationUpdateService.CheckForUpdatesAsync` → `IGitHubReleaseClient.GetReleasesAsync` (GitHub REST). `UpdateDownloader`/`Verifier`/`Installer` — GitHub + локально. Жодного звернення до Supabase у pipeline оновлення. Timer (DispatcherTimer 1 год) продовжує працювати при втраті сесії. Оновлення потребує ручної дії (toast → клік).

## 14.35. SEC-12 Production Fix: REVOKE EXECUTE (2026-07-14) — ✅ IMPL

> Критичний security fix, застосований напряму на production. Security Advisor: 63 warnings → 1.

257. **SEC-12 FIXED — ✅ IMPL (production).** `REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA public FROM PUBLIC;` + `GRANT EXECUTE ... TO cc_readonly, cc_notifier;`. Будь-який `authenticated` користувач більше не може викликати admin-функції (`purge_old_telemetry_events`, `transition_incident`, `create_knowledge_from_incident` тощо) через `POST /rest/v1/rpc/...`. Раніше 56 залогінених Discord-юзерів мали неявний доступ до ВСІХ SECURITY DEFINER функцій через PostgreSQL дефолтний `GRANT EXECUTE TO PUBLIC`. Клієнт SCLOC-Verse не викликає ЖОДНОЇ rpc-функції (grep `.Rpc(` = 0) — використовує лише table INSERT/SELECT/UPDATE під RLS.

258. **REVOKE EXECUTE не ламає клієнт — ✅ VER (production).** Через 10 хв після REVOKE, v1.0.2.2 юзер успішно синхронізував `app_installations` (last_seen оновлено, country=UA — тригер `set_country_from_cf` спрацював). Причина безпеки: (1) table INSERT/SELECT/UPDATE регулюються RLS, не EXECUTE; (2) тригери викликаються системою PostgreSQL (EXECUTE не перевіряється при fire); (3) всі тригер-функції SECURITY DEFINER (виконуються як postgres). `has_function_privilege('authenticated', ...) = false` для всіх public-функцій.

259. **Security Advisor audit: 63 → 1 warning — ✅ VER.** До фіксу: 60 × "SECURITY DEFINER Function callable by authenticated" + 2 × "Function Search Path Mutable" + 1 × "Leaked Password Protection". Після: 0 × SECURITY DEFINER + 0 × search_path + 1 × Leaked Password (косметичне — SCLOC-Verse OAuth-only, паролів немає).

260. **SEC-11 доповнено: 2 пропущені функції — ✅ IMPL (production).** `set_knowledge_change_context(p_change_type text, p_change_reason text)` та `set_incident_code()` додано `SET search_path = public, pg_catalog`. SEC-11 (KB §16.5.3) був позначений COMPLETED для 28 функцій, але ці 2 були пропущені (обидві SECURITY INVOKER, додані пізніше).

## 14.36. Performance Advisor Fix + Backup Schema Cleanup (2026-07-14) — ✅ IMPL

> Performance Advisor audit після SEC-12 fix. 10 warnings + 43 info → виправлено 3 категорії.

261. **RLS Init Plan Fix (10 policies) — ✅ IMPL (production).** Всі 10 RLS policies на `app_installations`, `telemetry_events`, `user_discord_guilds` переписані: `auth.uid()` → `(SELECT auth.uid())`. PostgreSQL тепер обчислює `auth.uid()` один раз на запит замість per-row. Верифіковано: `pg_policies` показує `( SELECT auth.uid() AS uid)`.

262. **FK Indexes (7) — ✅ IMPL (production).** CREATE INDEX на 7 неіндексованих FK колонок: `incident_notes(incident_id)`, `incident_status_log(incident_id)`, `knowledge_version_history(knowledge_id)`, `telemetry_events(install_id)`, `telemetry_events(user_id)`, `telemetry_incidents(root_event_id)`, `telemetry_incidents(last_event_id)`. Прискорює DELETE CASCADE/SET NULL операції.

263. **Backup Schemas DROPPED — ✅ IMPL (production).** `DROP SCHEMA backup_pre_1_0_0_1 CASCADE` + `DROP SCHEMA backup_pre_phase3a CASCADE`. Разом: 23 таблиці, ~488 KB, 0 залежностей, 0 продюсерів, 0 споживачів. Усунуло ~36 "No Primary Key" warnings з Performance Advisor. KB §3.2.1 оновлено.

## 14.37. Release v1.0.2.3 Stable (2026-07-14) — ✅ IMPL

264. **Version Update: 1.0.2.2 → 1.0.2.3.** Змінено 3 рядки у `SCLOCVerse.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>` → `1.0.2.3`). Release-білд: 0 warnings, 0 errors. Інсталятор `SCLOC-Verse_Setup.exe` (10.4 МБ, SHA256 `2b8bc9a83d3168b780c775846b7a53d4b269cdc0ee586fc24a45bcc7cb1eaa1e`). Git tag `v1.0.2.3` → коміт `6011648`. GitHub Release: https://github.com/Vova-Bob/SCLOC-Verse/releases/tag/v1.0.2.3.
265. **Release scope v1.0.2.3.** 3 коміти після v1.0.2.2 (`2a72268`): RC-401 defense-in-depth (C+D+D++F), SEC-12 fix (REVOKE EXECUTE), SEC-11 (2 функції search_path), Performance Advisor (RLS init plan + FK indexes + backup schema DROP), KB Synchronization. Жодних breaking changes (additive-only, Settings мігрують автоматично). Release Notes: `Installer/Release-Notes-v1.0.2.3.md`.
266. **Release v1.0.2.3 PUBLISHED.** GitHub Release: https://github.com/Vova-Bob/SCLOC-Verse/releases/tag/v1.0.2.3 (Latest, не Draft/Pre-release). Asset `SCLOC-Verse_Setup.exe` (10.4 МБ, SHA256 `2b8bc9a83d3168b780c775846b7a53d4b269cdc0ee586fc24a45bcc7cb1eaa1e`) прикріплений. Release notes українською.

## 14.38. Спрощений скін overlay Hangar Timer (2026-07-14) — ✅ IMPL

> **Additive feature.** Компактний горизонтальний бейдж як альтернативний вигляд накладання Hangar Timer.
> Перемикач у Settings Hub → Overlay. Концепт: `docs/images/concept-ex-hangar-overley.png`.
> Прев'ю реалізованого бейджа: `docs/images/ex-hangar-compact-preview.png`.

267. **`HangarOverlayMode` enum** (`Classic=0` / `Simplified=1`) — нова модель у `Models/HangarTimer/HangarOverlayMode.cs`. Default `Classic` (Zero Regression). Зберігається через `IHangarSettingsService.GetOverlayMode`/`SetOverlayMode` → `Settings.HangarOverlayMode` (int, default 0).

268. **`IHangarOverlayWindow` інтерфейс** — абстракція специфічних методів overlay-вікна (`SetHotkeyHint`, `ToggleClickThrough`, `BeginTemporaryDragMode`, `EndTemporaryDragMode`). Реалізується класичним (`HangarOverlayWindow`) та компактним (`HangarCompactOverlayWindow`) вікнами. Загальні Window-методи (Show/Hide/Close/Left/Top/LocationChanged) викликаються через базовий `Window`. `HangarOverlayService` працює через `Window? _window` + `IHangarOverlayWindow? _overlayWindow`.

269. **`HangarCompactOverlayWindow` — окреме компактне вікно** (не модифікація класичного). `SizeToContent="WidthAndHeight"`, `Background="Transparent"`, `Topmost`, `ShowActivated="False"`. Містить лише тонкий `Border`-бейдж (`Background=#CC0A1D29`, `BorderBrush=#2A5A78`, `BorderThickness=1.5`, `CornerRadius=16`, `Padding=16,8`) з `StackPanel Orientation=Horizontal`: 5 `Ellipse` 14×14 (LED, `LightStateToBrushConverter`, margin 10) + `TextBlock` з `TimerText` (Consolas 18px Bold, `#E0E8F0`). Без card-контейнера, без статусу, без підказок. Масштаб через `LayoutTransform` (ScaleTransform) — `SizeToContent` лишається коректним.

270. **`HangarOverlayService.ApplyOverlayMode(HangarOverlayMode)`** — перемикає режим. Якщо overlay відкритий — закриває старе вікно, створює нове (Classic або Compact), відкриває на тій самій позиції. `CreateWindow()` вибирає тип за `_settingsService.GetOverlayMode()`. Якщо закритий — режим застосується при наступному `Show()`.

> **Bug fix (2026-07-14):** `ApplyOverlayMode` після рекреації вікна не викликав `_timer.Start()` + `UpdateModel()`. `_window.Close()` → `OnWindowClosed()` → `_timer.Stop()`, а після створення нового вікна timer залишався зупиненим → таймер «завмирав» після перемикання. Фікс: додано `_timer.Start()` + `UpdateModel()` після `_window.Show()` у `ApplyOverlayMode` (аналогічно `Show()`).

271. **Settings Hub → Overlay — ComboBox «Вигляд»** (`HangarModeBox`) з варіантами «Класичний» / «Спрощений». `HangarMode_Changed` персистить + викликає `_overlay.ApplyOverlayMode(mode)` (рекреація вікна, live-apply). Reset-кнопка скидає до Classic.

272. **Zero Regression.** Класичне вікно `HangarOverlayWindow` відновлено до оригіналу (без змін XAML/layout). `HangarCycleCalculator`, `HangarTimerState`, хоткеї — без змін. Build: 0 warnings, 0 errors.

273. **Схема БД не зачеплена.** Суто UI-фічa (user.config persistence). Security Review не потрібне (UI/локалізація, не Auth/Installer/Network/SQL).

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

## 15.3. SEW / Методології (2026-07-09)

71. **SEW як окремий документ-методологія відхилено** — SEW впроваджено як розширення `AGENTS.md`, а не окремий `docs/SEW.md`. Порушення правила KB «не створювати нових документів для вже описаних підсистем» (Approved #156).
72. **Впровадження Spec Kit / BMAD / OpenSpec цілком відхилено** — конфлікт з архітектурними заборонами AGENTS.md (WPF-клієнт vs CLI-Mandate; Claude Code plugins; Node CLI залежності). SEW запозичує лише концепції (Approved #169).
73. **Паралельні структури `openspec/`, `.specify/`, `.bmad/`, окремі `docs/specs/` відхилено** — дублюють Knowledge Base. SEW-артефакти інтегровані в існуючу структуру (Approved #170).
74. **Принцип «Observability First» (SEW) відхилено у первісній формі** — конфлікт з Конституцією Observability Стаття 1 (Absolute Isolation) та Стаття 10 (Kill-Switch). Замінено на «Forensability» (Approved #159).
75. **Зовнішні CLI (OpenSpec CLI, Specify CLI, BMAD CLI) відхилено** — лише Markdown та існуюча інфраструктура SCLOC-Verse.
76. **Метафора «SEW як головний інженер» відхилена у формі персоніфікації** — SEW — система (Engineering Orchestrator / AI Engineering Operating System), не особа. Прийнято нейтральне формулювання (Approved #181). Персоніфікація створює хибне враження агентності у SEW як властивості (тоді як агентність — у виконавця).
77. **Resource Discovery як 13-крокова жорстка ієрархія відхилена** — порушує KISS та Minimal Change для trivial задач. Замінено на адаптивний 5-рівневий механізм (Approved #174).
78. **Task Classification як обовʼязковий жорсткий крок (Крок 1) відхилена** — багато задач SCLOC-Verse міждоменні (Phase 3.5 = БД+Telemetry+Arch+Security). Замінено на Adaptive Workflow матрицю як heuristic (Approved #175). Зовнішній доказ: Anthropic «heuristics rather than rigid rules».
79. **«Expert AI» як окремий рівень Capability Escalation відхилена** — у платформах немає «Expert AI» як класу; є лише різні моделі (Haiku/Sonnet/Opus). Capability Escalation переформульована як «Specialized Agent → Human» (Approved #178).

### 15.3.1. Settings Hub (2026-07-09)

80. **Глобальна кнопка «Скинути все» відхилена** — надто руйнівна; порушує Minimal Change. Замінено на reset per-category + per-control (↺). Лише шлях до гри має власний reset (Approved #186).
81. **Категоризація за інструментом відхилена** («налаштування Hangar Timer»/«Anti-AFK») — дублює логіку, ламається при нових інструментах. Замінено на категорії за функцією (Approved #183).
82. **Передчасні категорії з вигаданими полями відхилені** — категорія існує лише з критичною масою реалізованих налаштувань; зарезервовані показують плейсхолдер (Approved #185).
83. **`Діагностика` як окрема категорія відхилена** — згорнута в чекбокс «Розширена діагностика» у Загальне (один перемикач — не категорія).
84. **`Поведінка` та `Оновлення` як окремі категорії відхилені** — замало контенту; згорнуті у Загальне.
85. **`F1` як глобальна гаряча клавіша відхилена** — конфлікт з ігровою/системною допомогою. Обмежено активним вікном застосунку (Approved #187).

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
| C-6 | ~~`pg_cron` НЕ встановлений — план «Retention 14д» не виконано~~ → ✅ **RESOLVED**: Phase 5.1 (2026-07-11) — pg_cron installed, `run_retention_pipeline()` daily 03:00 UTC | ~~`relation cron.jobs does not exist`~~ → `cron.job` 1 row active |
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

✅ COMPLETED (2026-07-08, Phase 2.2 C2). Усі 28 public SECURITY DEFINER функцій тепер мають `SET search_path = public, pg_catalog`. Міграція: `supabase/migrations/20260708235000_security_definer_search_path.sql`. Verification: `supabase/verification/verify_security_definer_search_path.sql`.

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

**🟢 VER (Phase 3.6, 2026-07-07) — корінь розкрито:** DEFAULT PRIVILEGES для `public`/`postgres`/`TABLES` **різні**:

| Grantee | PROD DEFAULT PRIV | REPLICA DEFAULT PRIV |
|---|---|---|
| `anon` | `Dxtm` (TRUNCATE+REF+TRIG+MAINT) | `arwdDxtm` (FULL DML) |
| `authenticated` | `Dxtm` | `arwdDxtm` |
| `service_role` | `Dxtm` | `arwdDxtm` |

**Діагноз (b) підтверджено частково:** Replica over-grants. АЛЕ причина — НЕ помилка replica. **Production був ручно захищений** (REVOKE DML з DEFAULT PRIVILEGES для `postgres` у `public`). Цей hardening **НЕ в жодній міграції** SCLOC-Verse. Replica має Supabase baseline (insecure default для fresh projects). Кастомні ролі (`cc_readonly`=27, `cc_notifier`=7) — ідентичні ✅. RLS однаково блокує доступ в обох.

**Рішення:** Винесено в **Phase 3.7 Security Hardening** (Backlog §17) — окремо від імпорту даних. Production hardening задокументувати в міграції (audit trail). Replica REVOKE — окремо.

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

## 16.9. Phase 3.6 — Production Replica Synchronization (2026-07-07)

> **Одноразовий полігон** для перевірки Phase 3.5, Phase 3A та Production Deployment. Після завершення тестів проєкт буде видалено. НЕ довгострокова replica.

### 16.9.1. Replica Synchronization Audit

Структурний аудит Replica (`zhdtcxvnzlvbgxariyww`) ↔ Production (`nrytczdbhehiotflaagl`):

| Тип | PROD | REPL | Δ |
|---|---:|---:|---|
| Таблиці (public+cc+auth+storage+vault) | 47 | 47 | ✅ ідентично |
| VIEW | 24 | 24 | ✅ |
| Materialized VIEW | 1 | 1 | ✅ |
| SECURITY DEFINER функцій | 28 | 28 | ✅ |
| Triggers | 4 | 5 | +1 REPL (`trg_set_incident_code` — Phase 3A) |
| Indexes | 47 | 48 | +1 REPL (Phase 3A: −2 drop +3 add) |
| RLS policies | 29 | 29 | ✅ |
| Extensions | 5 | 5 | ✅ |
| Publications | 1 | 1 | ✅ |

**Усі 3 розбіжності — артефакти виключно Phase 3A** (3 міграції на replica). 0 несподіваного drift.

### 16.9.2. Data Import — завершено

| Таблиця | Рядків (PROD) | Імпортовано (REPL) | Метод |
|---|---:|---:|---|
| `auth.users` | 55 | 56 | SQL INSERT (13 essential колонок; 22 nullable GoTree-колонки отримали DEFAULT/NULL) |
| `auth.identities` | 55 | 56 | **GENERATED FROM auth.users** на самій replica (identity_data = raw_user_meta_data; provider_id з JSON) |
| `app_installations` | 54 | 55 | SQL INSERT (19 колонок, повний клон) |
| `incident_policy` | 5 | 5 | SKIP (вже з міграцій, ідентично) |

**SKIP (14 таблиць + 25 views + 1 matview + 1 function):** telemetry_events/incidents, notification_queue/attempts, incident_status_log/notes, knowledge_*, error_reports, admin_audit_log, user_discord_guilds, storage, vault — історичні runtime / reserved / 0 рядків / derived.

### 16.9.3. Replica Validation — PASSED

| Перевірка | Результат |
|---|---|
| Row counts | ✅ ~відповідають production (+1 можливий фоновий тест-юзер) |
| FK integrity (app_installations → auth.users) | ✅ 0 orphan |
| FK integrity (identities → auth.users) | ✅ 56/56 valid |
| identities.email generated column | ✅ 54/56 (2 без email = discord без email scope) |
| country preserved (trigger no-op) | ✅ 6 distinct (matches PROD) |
| cc.users / cc.installations / cc.statistics | ✅ повертають дані |
| Discord-метадані (display_name, avatar_url) | ✅ 59/59 rows parsed |

### 16.9.4. Forensic знахідки

1. **`set_country_from_cf` trigger безпечний для прямого SQL**: `headers_json IS NULL → RETURN NEW` → country з production зберігається. Тригер спрацьовує лише через PostgREST (з Cloudflare header).
2. **`incident_policy` порожня = silent death**: `promote()` → `IF NOT FOUND THEN RETURN` → 0 інцидентів. SOFT failure, без помилок.
3. **`pipeline_health_meta` = CACHE** (не config): `refresh_knowledge_coverage()` перезаписує `last_knowledge_refresh`. SKIP імпорту — авто-відновлюється.
4. **auth.identities.email — GENERATED column** у Supabase PG17 (з `identity_data->>'email'`). Не можна INSERT напряму.
5. **DEFAULT PRIVILEGES drift** — §16.7 оновлено (VER: production manually hardened, replica = Supabase baseline).

## 16.10. Phase 3.7 — Security Hardening

> ~~Окрема фаза, відокремлена від імпорту даних (Phase 3.6).~~ SEC-11 + SEC-12 completed 2026-07-14.

- **~~SEC-11~~:** ✅ COMPLETED (2026-07-08 + 2026-07-14: 28 + 2 функцій мають `SET search_path`).
- **~~SEC-12~~:** ✅ COMPLETED (2026-07-14: `REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA public FROM PUBLIC`).
- **DEFAULT PRIVILEGES:** ⏸ Backlog — Production = `Dxtm` (manually hardened), не в міграціях. RLS блокує доступ в обох БД; over-granting — defense-in-depth gap. Див. §16.7.

## 16.11. Phase 3A Production Deployment Report (2026-07-07)

> Production `nrytczdbhehiotflaagl`, PostgreSQL 17.6, UTC. 3 міграції застосовані послідовно з верифікацією після кожної.

### 16.11.1. Pre-Deployment

- **Backup:** `backup_pre_phase3a` schema (11 таблиць, 11/11 row counts match public).
- **Release Cleanup:** 717 telemetry_events + 4 telemetry_incidents + 4 notification_queue видалено (тестові дані Jul 4-7, pre-release). app_installations (55), auth.users (56), incident_policy (5) — preserved.
- **Pre-migration state verified:** 2 DROP targets exist, 3 CREATE targets not found, incident_code/fn/trigger not found.

### 16.11.2. Migration Results

| Міграція | Зміна | Verification |
|---|---|---|
| phase3a_drop_dup_indexes | DROP `idx_app_installations_install_id` + `idx_user_discord_guilds_user_id` | ✅ DROPPED, UNIQUE constraints KEPT, query by install_id OK |
| phase3a_add_partial_indexes | CREATE `idx_telemetry_failed` (partial) + `idx_telemetry_version_window` (partial) + `idx_telemetry_occurred` | ✅ CREATED з коректними definitions |
| phase3a_incident_code_generated | ADD `incident_code` column + `set_incident_code()` fn + `trg_set_incident_code` trigger + OR REPLACE 4 views + backfill | ✅ EXISTS, trigger test `INC-2026-00022` OK, views return without error, grants cc_readonly OK |

### 16.11.3. Post-Impl Object Count Diff

| Метрика | Before | After | Δ | Expected |
|---|---|---|---|---|
| TABLES public | 14 | 14 | 0 | 0 ✅ |
| VIEWS cc | 23 | 23 | 0 | 0 ✅ |
| INDEXES public | 45 | 46 | +1 | −2+3=+1 ✅ |
| FUNCTIONS public | 29 | 30 | +1 | +1 ✅ |
| TRIGGERS public | 4 | 5 | +1 | +1 ✅ |
| RLS policies | 29 | 29 | 0 | 0 ✅ |
| SECURITY DEFINER | 28 | 28 | 0 | 0 ✅ |

### 16.11.4. Mandatory Event Audit (forensic)

5 L1 подій на запуск проаналізовано. Readers identified via `pg_get_viewdef`:

| Подія | Reader (automated) | Reader (human) | Вердикт |
|---|---|---|---|
| Application/Start/Started | None | cc.traces | **KEEP L1** (єдина pre-auth подія) |
| Auth/RestoreSession/Started | **None** | cc.traces | **→ L2** (кандидат, Phase 3.5.1) |
| Auth/RestoreSession/Succeeded | release_health_detail | cc.traces | **KEEP L1** (success_rate) |
| Installation/Sync/Started | **None** | cc.traces | **→ L2** (кандидат, Phase 3.5.1) |
| Installation/Sync/Succeeded | release_health_detail | cc.traces | **KEEP L1** (success_rate) |

**Факт:** `release_health_detail` фільтрує `outcome IN ('Succeeded', 'Failed')`. `trg_telemetry_failed_promote` fires on `outcome = 'Failed'` only. `cc.statistics` не читає telemetry_events. → "Started" події не мають автоматичних споживачів.

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
| Розслідувати telemetry silence у v1.0.2.1 | §14.32 #240 | середня |
| Перевірити Notifier Worker / Discord webhook (3 Pending notifications) | §14.32 #242 | низька |
| Розширити enum `telemetry_incidents.status` (F2) | forensic | низька |
| Видалити мертвий `LiaForensicParser.TryParseMinimal` (F7) | forensic | низька |
| MSBuild target для `git_commit` у BuildInfo | forensic | низька |
| ~~Phase 0: 3 індекси (partial Failed, version_window, occurred)~~ | ~~Optimization-Plan~~ | ~~низька~~ — **✅ DONE: Phase 3A production 2026-07-07** |
| Phase 1: скоротити LIA chain 9→2, app-update 6→1 | Optimization-Matrix | середня |
| Phase 2: видалити `detail.retry_count` з C# `UpdateEvents.Track`/`LiaEvents.Track` (завжди 0) | Optimization-Plan + Phase 2 Review | низька |
| ~~Phase 2: MERGE `notification_queue.error_message` ↔ `last_error` → `last_error`~~ | ~~Phase 2 Review (2026-07-07)~~ | ~~низька~~ — **ВІДХИЛЕНО forensic (різна семантика, див. §12.6, Rejected #68)** |
| ~~Phase 2: REMOVE 3 індекси-дублікати~~ (`idx_app_installations_install_id`, `idx_app_installations_machine_id`, `idx_user_discord_guilds_user_id`) | ~~Phase 2 Review — DROP INDEX additive~~ | низька — **2/3 ✅ DONE: Phase 3A production 2026-07-07 (install_id + user_discord_guilds_user_id); machine_id → DEFER (Pre-Impl Forensic виявив ризик regression)** |
| Phase 2: дослідити та погодити DROP SCHEMA `backup_pre_1_0_0_1` (12 дублів, 55 рядків, 0 залежностей) | Phase 2 Review | низька |
| Phase 3A: описати незадокументовані об'єкти в міграції `20260708000000_describe_unschema_objects.sql` (release_health_detail, unfinished_started, ecosystem_stats) | Post-Impl Forensic Phase 3A — §16.6 TD-NEW | низька |
| **Phase 3.5: `TelemetryLevel` enum в `ITelemetryService.Track()`** — додати enum-параметр `level` (default Mandatory). Існуючі 39 викликів не ламаються. Реєстрація в `AppCompositionRoot`. | архітектурна основа Policy (варіант E, §5.8.2) | низька |
| **Phase 3.5: wire `AdvancedDiagnostics` чекбокс** — `TelemetryClient.Track()` перевіряє `settings.AdvancedDiagnostics` для L2. **БЕЗ перейменування UI** (§5.8.3). | увімкнути мертвий UI-контракт §16.4 | низька |
| **Phase 3.5: `category` активація** — `TelemetryClient.BuildEvent()` встановлює `category` (Critical/Operational/Diagnostic/Analytics) через Level. Additive. | диференціація подій у БД | низька |
| **Phase 3.5: `IInstallationContextProvider`** — активувати L1 FUTURE колонки (`localization_version`, `game_folder_path`→L2, `selected_environment`, `update_channel`). | наповнити test Supabase "правильними" даними | середня |
| **Phase 3.5: filter L2 при OFF** — TelemetryClient skip L2 events when `AdvancedDiagnostics=false`. Очікується суттєве зменшення навантаження. | скоротити telemetry_events volume | низька |
| **Phase 3.5: позначити 21 L2 емітерів** — додати `level: TelemetryLevel.Diagnostic` до 21 викликів з §5.10.1 (Updater Started/Succeeded, LIA cascade, RunInstallerScript). | реалізувати Policy в коді | низька |
| **Phase 3.5.1: Mandatory Event Optimization** — перевести `Auth/RestoreSession/Started` + `Installation/Sync/Started` з L1→L2 (forensic #136: 0 автоматичних споживачів, −40% подій/запуск). Додати `level` параметр у `TrackAuth`/`TrackSync` (default Mandatory). | §16.11.4 Mandatory Event Audit | низька — **✅ DONE 2026-07-07: розширено до Zero Noise Policy (13 non-Failed → L2)** |
| ~~Phase 2: generated column~~ **trigger-based `incident_code`** (`INC-YYYY-NNNNN`) замість формування в 4 views + Notifier | Phase 2 Review → Phase 3A Post-Impl: generated column неможливий для timestamptz (STABLE), замінено на trigger | низька — **✅ DONE: Phase 3A production 2026-07-07** |
| Phase 3: single-scan candidates + матеріалізувати 3 views | Optimization-Plan | середня |
| Control Center auth (SEC-1) | Final-Review | середня |
| Cert pin L.I.A. (SEC-3) | Final-Review | середня |
| L.I.A. installer integrity check (SEC-2) | Final-Review | середня |
| Checksum-fail замість Verify.Skipped (SEC-4) | Final-Review | низька |
| `SET search_path` у SECURITY DEFINER функціях (SEC-11) | Final-Review | низька |

## 17.2. Цільова політика збору даних — Zero Noise Policy (Verified #144)

> **Оновлено 2026-07-07.** Замінює попередню Категорію A/B. Принцип: `telemetry_events` — журнал відхилень (Failed only). Нормальний стан — в `auth.users` + `app_installations`.

**L1 Mandatory — відправляється ЗАВЖДИ (навіть при вимкненому чекбоксі):**
- `telemetry_events` з `outcome='Failed'` (усі 13 Failed типів: Auth, Installation, Orchestrator, Updater, LIA).
- `telemetry_incidents` + `notification_queue` (авто-створюються trigger/promote).

**L2 Diagnostic — лише при AdvancedDiagnostics ON:**
- Усі non-Failed: Started, Succeeded, Cancelled, UpdateFound, Updated, UpdateAvailable (13 типів).
- `duration_ms, detail, http_status, hresult, supabase_code, exception_type, error_message` (у Failed завжди L1, у non-Failed — L2).
- LIA forensic payload (`appx_log`, cert-поля, `activity_id`, `phase`).
- `telemetry_events.git_commit`.

**НЕ в `telemetry_events` (живе в інших таблицях):**
- User identity → `auth.users` (`created_at`, `last_sign_in_at`, `email`).
- Installation activity → `app_installations` (`last_seen`, `app_version`, `country`, `platform`).
- Adoption metrics → `app_installations.first_seen` / `created_at`.

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

## 17.6. SEW Advanced (HYPOTHESIS — потрібен пілот)

> За принципом Methodology Gate (Approved #179), EXPERIMENTAL/HYPOTHESIS-механізми не йдуть у Core без пілоту. Кожен пункт нижче — кандидат на пілотну перевірку на реальній задачі SCLOC-Verse. Після перевірки → або IMPL в Core (§14.20.2), або REJ (§15.3).

| Механізм | Гіпотеза | Умова переходу в Core |
|---|---|---|
| **Pattern Library** (`docs/patterns/`) — каталог перевірених рішень (Canvas Pattern, OAuth Pattern, Retry Pattern, Migration Pattern тощо) | Reuse First з зубами: перед новим рішенням шукати в Pattern Library | Пілот: 1 цикл Phase 3.6/3.7, де Pattern реально зекономить час. Без наповнення = порожній розділ. |
| **Experience Database** — пошуковий шар над forensic-документами (пошук минулих forensic/incidents/rollback) | Повторне використання досвіду | Пілот: 1 complex incident, де search по ~10 forensic-документам зекономив би час. |
| **AI Performance Metrics** — самооцінка агента (Forensic Accuracy, False Proposal Rate, Rollback Count, KB Growth, Reuse %) | Вимірювання ефективності методології | Пілот: 5 завершених задач, де метрики реально зібрані та змінили процес. |
| **Capability Escalation 4-рівнева** (AI → Sub AI → Expert AI → Human) | Делегування як принцип | Ні: «Expert AI» як клас не існує (Rejected #79). Альтернатива: формальний pipeline «Specialized Agent → Human». |
| **Engineering Confidence** (ступінь впевненості 97% з підставами, не лише HYP/VER/IMPL/REJ) | Кальбрація рекомендацій | Пілот: 5 рекомендацій, де кількісна довіра реально змінила рішення користувача. Ризик: «97%» суб'єктивне → шум. |
| **Continuous Learning Loop повний pipeline** (`Knowledge → Rule → Checklist → Prompt → Pattern → Template → Automation`) | Автоматизоване покращення екосистеми | Пілот: 1 задача, де агент самостійно пройшов повний конвеєр від Knowledge до Automation. |
| **Multi-Agent як обовʼязковий крок** для parallelizable задач | Координація кількох субагентів одночасно | Пілот: 1 parallelizable задача (великий forensic з паралельним аудитом схеми+коду+логів через agent_manager). Більшість фаз SCLOC-Verse послідовні — не виправдано. |
| **SEW Meta-Evolution / Constitutional Review** як окремий формальний процес | Регулярний ревʼю власної методології (за аналогією з Code Review) | Пілот: 1 цикл (3 місяці), де Meta-Evolution реально змінив ≥1 правило SEW через Evidence. Зараз коротка форма Meta-Evolution в AGENTS.md — достатня. |

**Критерії пілоту (загальні):**
- S1: механізм реально змінив вибір інструменту/рішення/процесу? (ні → не йде в Core)
- S2: механізм не додав бюрократії? (так → REJ)
- S3: метрика успіху — реальне повторне використання, не «галочка пройдена».

## 17.7. Settings Hub — Центр керування налаштуваннями (2026-07-09)

> **ADR-009.** Деталі: [`docs/backlog/settings-hub.md`](backlog/settings-hub.md). Затверджені/відхилені рішення: §14.25, §15.3.1.

| Phase | Задача | Складність | Статус |
|---|---|---|---|
| 0 | **Hub-оболонка** (ліва панель + CanvasManager-інтеграція, ⚙/F1-вхід) | середня | ✅ DONE 2026-07-09 |
| 0 | **Загальне** — міграція контенту SettingsCanvas (шлях/автозапуск/трей/локалізація/канал/менеджер/кеш/діагностика) | низька–середня | ✅ DONE 2026-07-09 |
| 0 | **Гарячі клавіші** — **read-only** список реальних комбінацій (Варіант A+; інтерактивний редактор → Phase 0.5) | низька | ✅ DONE 2026-07-09 |
| 0 | **Overlay** — Hangar Timer (масштаб/прозорість слайдерами); Anti-AFK — плейсхолдер | низька | ✅ DONE 2026-07-09 |
| 0 | Зарезервовані категорії-плейсхолдери (Головна/Локалізація/Інтерфейс/Профіль/Про програму) | низька | ✅ DONE 2026-07-09 |
| UX | **UX Polishing** — візуальна ідентичність «центр керування» за дизайн-системою (§14.26); виправлення design-drift | середня | ✅ DONE 2026-07-10 (зауважень немає; арх/композ/атмосфера 9.5–10/10) |
| 0.5 | **Повна система користувацьких гарячих клавіш** — persistence (`CurrentGesture` per id), runtime rebind, capture, conflict resolution, reset, cloud sync через Профіль | висока | ✅ DONE 2026-07-10 (JSON persistence + Rebind API + capture + conflict + reset) |
| 0.5+ | **Overlay — повна функціональність** — live-preview (слайдер → state → overlay), bidirectional sync (хоткеї → слайдери), jump-to-click, drag→позиція синхронізація, reset позиції | середня | ✅ DONE 2026-07-10 (state.PropertyChanged + PositionChanged + PreviewMouseLeftButtonDown) |
| — | **Оптимізація збірки** — Debug = framework-dependent (SelfContained=false) | низька | ✅ DONE 2026-07-10 (8× швидше: 26s → 3s) |
| — | **Anti-AFK** — міграція з SCLOCUA: GetLastInputInfo замість hooks, AntiAfkService, AntiAfkIndicatorWindow (пульсуюча точка, 5 позицій, 2 анімації), хоткей End, 6 налаштувань через IPreferencesService, Settings Hub блок, Indicator Mode (Running/IdleOnly) | висока | ✅ DONE 2026-07-10 (§14.27; build 0 warnings) |
| — | **Auto Key** — незалежний модуль авто-натискання клавіші: stateless foreground-гейт за PID процесу (StarCitizen.exe), AutoKeyService (SendInput keyboard), AutoKeyIndicatorWindow (3 стани), хоткей Home, Action Key (default `[`), інтервал 100–2000мс, Settings Hub блок | висока | ✅ DONE 2026-07-11 (§14.28; build 0 warnings) |
| 1 | **Профіль — `profile.json`** (локальний контракт/схема-версія, експорт/імпорт) | середня | 🔵 PLANNED |
| 2 | **Профіль — синхронізація Supabase** (hotkey-bindings, overlay; НЕ hotkey-події — L3 Local Only) | висока | 🔵 PLANNED |

---

# 18. Cross References

## 18.1. Джерельні документи (що підтверджує що)

| Документ | Що підтверджує |
|---|---|
| `AGENTS.md` | Конституція проєкту (Zero Regression, UTF-8 P0, українські commits, additive-only, структура, SEW Workflow) |
| `ARCHITECTURE_DECISIONS.md` | ADR-001…009 (DI, hotkeys, MainWindow, overlay, canvas, lifetime, ISP, **Settings Hub**) — **канонічний файл ADR** |
| `docs/architecture/Final-Architecture-Review.md` | Повна архітектура, TD-1…60, ZR-1…24, SEC-1…12 |
| `docs/contracts/control_center.md` | Контракт Control Center, role `cc_readonly` |
| `docs/LIA_INSTALLATION.md` | L.I.A. installer/cert/elevation/forensic |
| `docs/checklists/Quality-Gates.md` | SEW Core + Extended Quality Gates (обов'язкові для всіх змін) |
| `docs/checklists/Security-Review.md` | SEW Security Review за доменами (OAuth/Auth/Installer/Network/SQL/RLS/Crypto) |
| `docs/checklists/Database-Verification.md` | Чеклист RLS/таблиць (Стаття 17) — реалізує Extended Gates для БД-домену |
| `docs/backlog/README.md` | SEW конвенція деталізації великих задач (картка в KB §17 + файл у `docs/backlog/`) |
| `docs/backlog/settings-hub.md` | Settings Hub — повна специфікація (AC, варіанти A/B/C, зона впливу). Картка: KB §17.7, ADR-009 |
| `docs/backlog/settings-hub-design-system.md` | Settings Hub — дизайн-система (P0 Identity First + P1–P5, Design Kit). Доктрина: KB §14.26 |
| `docs/backlog/hotkey-editor.md` | Phase 0.5 — редактор гарячих клавіш (JSON persistence, Rebind, capture, conflict, reset). Картка: KB §17.7 |
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
   │      └─ 14.20 SEW (AGENTS.md + docs/checklists/ + docs/backlog/)
   ├─ 16 Technical Debt ── 17 Backlog (картки → docs/backlog/<task>.md)
   └─ 18 Cross References
```

## 18.3. Відомі неузгодженості (вирішити окремо)

1. **Roadmap** згадує «Constitution (21 стаття)» — фактично **29** (Конституція розширена).
2. **`db-cleanup-forensic-analysis`** — політика повного очищення `app_installations` частково застаріла; актуальна в `post-cleanup-forensic-app-installations.md` (B+C).
3. **Optimization-Matrix vs Database-Optimization-Plan** — цифри економії дещо різняться (Matrix ~−68%; Plan ~−70%). **Plan авторитетніший** (детальніший).
4. **Колізія нумерації ADR** — ~~`ARCHITECTURE_DECISIONS.md` (ADR-001…008) і `.kilo/adr/` (ADR-001, ADR-002) описують різні рішення під однаковими номерами~~. **RESOLVED (2026-07-09):** `.kilo/adr/`-файли відсутні в репозиторії (виключено `.gitignore`-правилом для `.kilo/` — локальні артефакти). Канонічний файл ADR — `ARCHITECTURE_DECISIONS.md`. Рішення про OAuth (Loopback Redirect, кнопка акаунта у тайтлбарі) живуть у §14.2 (#13, #14) та `docs/architecture/Final-Architecture-Review.md`.
5. **Версія** — `control_center.md` декларує `product_version=1.8.0`; застосунок `1.0.0.1`.

---

> **Підтримка:** ця KB — живий документ. При нових погоджених рішеннях — додавати в розділ 14; при нових відхиленнях — в розділ 15; при нових forensic — спочатку перевірити, чи факт вже тут, і лише потім доповнити цю KB + за потреби сирцевий документ.
