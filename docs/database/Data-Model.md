# SCLOC-Verse Data Model — Single Source of Truth

> **Дочірній документ Knowledge Base (KB §3 + §4 + §16.5).**
> Містить: схему БД, об'єкти, всі 15 таблиць по колонках (Classification/Ownership/Lifetime/Confidence), RLS, triggers, Phase 2 Database Cleanup Review.
> **Data Model Freeze артефакт (Phase 3, 2026-07-07).** Після цієї моделі будь-яка зміна БД проходить через зміну цієї моделі (а не через припущення).
> Джерело: KB §3 + §4 + §4.10 + §16.5 (винесено 2026-07-20 при KB optimization Variant B).

---

## 1. Огляд об'єктів

| Тип | Кількість | Примітка |
|---|---:|---|
| Схеми (SCLOC-Verse) | 2 | `public`, `control_center` (backup schemas DROPPED 2026-07-14) |
| Базові таблиці | 15 | 14 у `public` + 1 singleton у `control_center` |
| Звичайні VIEW | 25 | 24 у `control_center` + `public.user_analytics` |
| Materialized VIEW | 1 | `control_center.knowledge_coverage` |
| SECURITY DEFINER функції | 30 | promotion, incident workflow, knowledge lifecycle, retention pipeline |
| Triggers | 4+1 | geoip, failed-promote, incident-refresh, knowledge-audit, set_incident_code |
| БД-ролі | 4 | `anon`, `authenticated`, `cc_readonly`, `cc_notifier` |
| RLS policies (`public`) | 28 | deny-all RESTRICTIVE + owner-only permissive + cc_notifier |
| pg_cron | 1 job | ✅ **Installed** (Phase 5.1, 2026-07-11) — `retention-pipeline-daily`, schedule `0 3 * * *` |

---

## 2. Таблиці (призначення)

| Таблиця | Схема | Призначення | Продюсер | Статус |
|---|---|---|---|---|
| `app_installations` | public | Метадані інсталяції клієнта | C# `InstallationService` | ✅ жива |
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

### 2.1. Резервна схема `backup_pre_1_0_0_1` — DROPPED (2026-07-14)

Разом з `backup_pre_phase3a`. Обидві схеми мали 0 залежностей, 0 продюсерів, ~488 KB. Див. KB §14.36.

### 2.2. Audit міграцій (26 міграцій консистентні)

| Pattern | Приклад | Статус |
|---|---|---|
| `CREATE OR REPLACE FUNCTION` | `promote_incident_candidates` (00013→00016), `refresh_knowledge_coverage` (00023→05021100), `tg_promote_after_failed` (05021100→05030000), `knowledge_audit_trigger` (00019→00022), `update_knowledge_entry`/`transition_knowledge` (00021→00022) | ✅ розвиткові оновлення |
| `DROP CONSTRAINT + ADD` | `chk_notif_status` (00016→00017, 3→5 станів), `chk_knowledge_ref_type` (00019→00022, +ReleaseNotes), `chk_notification_type` (00016→00023, 3→7 типів) | ✅ розширення enum |
| `DROP VIEW + CREATE` | `cc.notifications` (00016→00017, зміна порядку колонок) | ✅ необхідне |
| `CREATE FUNCTION → DROP FUNCTION` | `archive_knowledge_entry(bigint, text, text)` CREATE 00021 → DROP 00023 | ✅ refactor у межах релізу |
| Zombie функція | `promote_incident_candidates()` (batch) — створена 00013, REPLACE 00016, продовжує жити | ⏸ DEFER |

**Migration squash** (об'єднання 00001-00023 в одну) — відхилено (KB Rejected #70): втратить аудит причин.

---

## 3. Triggers

| Trigger | Подія | Дія |
|---|---|---|
| `trg_app_installations_set_country` | BEFORE INSERT/UPDATE на `app_installations` | GeoIP з Cloudflare `cf-ipcountry` → `country` |
| `trg_telemetry_failed_promote` | AFTER INSERT `telemetry_events` WHEN `outcome='Failed'` | `promote_incident_candidates_for_event()` |
| `trg_incident_refresh_coverage` | AFTER INSERT/UPDATE `telemetry_incidents` | `refresh_knowledge_coverage()` |
| `knowledge_audit` | AFTER INSERT/UPDATE `knowledge_entries` | snapshot → `knowledge_version_history` |
| `trg_set_incident_code` | BEFORE INSERT/UPDATE `telemetry_incidents` | `set_incident_code()` (Phase 3A, IMPL) |

> ⚠ `trg_*_set_country` існує лише на `app_installations`. На `telemetry_events` тригера GeoIP немає → `telemetry_events.country` завжди NULL.

---

## 4. RLS-модель

| Роль | Доступ |
|---|---|
| `anon` | deny-all скрізь |
| `authenticated` | `app_installations` SELECT+INSERT+UPDATE owner-only (`user_id = auth.uid()`); `telemetry_events` SELECT+INSERT owner-only (append-only); `user_discord_guilds` owner-only |
| `cc_readonly` | `USAGE`+`SELECT` лише на схему `control_center` + EXECUTE на workflow/knowledge функції |
| `cc_notifier` | ALL на `notification_queue`/`notification_attempts` + SELECT `telemetry_incidents` |

**Відома пастка:** `RETURNING`-вирази вимагають `GRANT SELECT` (спричинила історичний інцидент `42501`).

**RLS Init Plan Fix (2026-07-14, IMPL):** усі 10 RLS policies на `app_installations`, `telemetry_events`, `user_discord_guilds` переписані: `auth.uid()` → `(SELECT auth.uid())`. PostgreSQL обчислює один раз на запит замість per-row.

---

## 5. Легенда (Classification / Confidence / Lifetime)

**Classification (KB Approved #99):**
- `CORE` — обов'язкове для роботи системи (NOT NULL або постійно заповнюється).
- `OPTIONAL` — необов'язкове, але корисне (NULL допустимий).
- `DIAGNOSTIC` — діагностична інформація (для форензики).
- `FUTURE` — заготовка під плановану фічу (зараз NULL/DEFAULT, активується пізніше).
- `DEPRECATED` — застаріле, не прибране через additive-only.

**Confidence (KB Approved #100):**
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

---

## 6. `app_installations` (19 cols, FOREVER)

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

**Trivia (VER):** `country` — per-installation; `user_countries` (computed у user_analytics) — per-user aggregate (string_agg DISTINCT). НЕ дубль.

### 6.1. Неактивовані можливості (FUTURE колонки)

| Поле | Що готово | Чого не вистачає | Складність |
|---|---|---|---|
| `localization_version` | Колонка + CC view читає | C# не фіксує `release.TagName` після Install/Update | низька |
| `game_folder_path` | Колонка + CC view читає | C# не прокидає `Settings.Default.GameFolder` | низька |
| `selected_environment` | Колонка + `cc.users/installations` читають | C# не зберігає вибір `EnvironmentSelector` (transient) | середня |
| `os_build` | Колонка + CC view читає | C# не читає `Environment.OSVersion.Version.Build` | низька |
| `update_channel` | DEFAULT в БД | C# не оновлює при зміні в SettingsCanvas | низька |
| `install_source` | DEFAULT в БД | InnoSetup не передає runtime-параметр | середня |

> **Архітектурне рішення (погоджено):** для `selected_environment` + `localization_version` + `game_folder_path` — ввести `IInstallationContextProvider` (read) + `IInstallationContextUpdater` (write), а НЕ дублювати в `Settings`. План: `docs/observability/app-installations-implementation-plan.md`.

### 6.2. Cleanup-політика

- **НЕ повне очищення** (втрата `first_seen`/`created_at` — adoption-історія).
- Лише тестові записи: `DELETE FROM app_installations WHERE install_id !~ '^[0-9a-f]{32}$'`.
- Production UUID-записи зберігаються.

---

## 7. `telemetry_events` (28 cols, 90d retention)

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
| country | text | ✗ | — | ❌ ніколи (trigger лише на installations) | — | — | **DEPRECATED** | REJ |
| component | text | ✓ | — | C# TelemetryClient | cc.telemetry_events; incidents fingerprint | Classification | CORE | IMPL |
| operation | text | ✓ | — | C# TelemetryClient | cc.telemetry_events; incidents fingerprint | Classification | CORE | IMPL |
| outcome | text | ✓ | — | C# TelemetryClient (CHECK 5 значень) | cc.telemetry_events; signal COALESCE; trigger Failed | Status | CORE | IMPL |
| severity | text | ✓ | 'Info' | C# TelemetryClient (CHECK 5 значень) | cc.telemetry_events | Status | CORE | IMPL |
| category | text | ✓ | 'Operational' | C# (завжди Operational) | cc.telemetry_events | Classification | **FUTURE** | HYP |
| source | text | ✗ | — | C# ErrorContextExtractor | signal COALESCE (priority 1) | Diagnostics | OPTIONAL | IMPL |
| http_status | int | ✗ | — | ❌ ніколи з C# (100% NULL) | signal COALESCE (priority 4) | Diagnostics | **DEPRECATED** | VER |
| hresult | text | ✗ | — | C# ErrorContextExtractor (LIA) | signal COALESCE (priority 3) | Diagnostics | OPTIONAL | IMPL |
| supabase_code | text | ✗ | — | ❌ ніколи з C# (100% NULL) | signal COALESCE (priority 2) | Diagnostics | **DEPRECATED** | VER |
| exception_type | text | ✗ | — | C# ErrorContextExtractor | signal COALESCE (priority 5) | Diagnostics | OPTIONAL | IMPL |
| error_message | text | ✗ | — | C# TelemetryClient | cc.telemetry_events | Diagnostics | OPTIONAL | IMPL |
| duration_ms | int | ✗ | — | C# TelemetryClient | cc.telemetry_events | Diagnostics | OPTIONAL | IMPL |
| detail | jsonb | ✗ | — | C# (UpdateEvents/LiaEvents/InstallationService) | cc.telemetry_events (JSON keys: phase, retry_count, certificate_*, installer_type, package_version, activity_id) | Diagnostics | OPTIONAL | IMPL |

**Trivia:** `detail.retry_count` — `FUTURE` заготовка під Retry Policy. НЕ мертва. `detail.signal_name` — НЕ існує в даних.

---

## 8. `telemetry_incidents` (19 cols, 1y closed)

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
| incident_code | text | ✗ | — | trigger set_incident_code (Phase 3A) | cc.incidents AS incident_id; cc.incident_timeline/notes_view/notifications AS incident_code | Display | CORE | IMPL |

---

## 9. `notification_queue` (16 cols, 30d delivered) + `notification_attempts` (10 cols, 90d)

### 9.1. notification_queue

| Колонка | Тип | NN | Default | Source | Reader | Category | Class | Conf |
|---|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | FK notification_attempts; cc.notifications | Identification | CORE | IMPL |
| incident_id | bigint | ✓ | — | promote | FK telemetry_incidents; cc.notifications | Identity | CORE | IMPL |
| notification_type | text | ✓ | — | promote (CHECK 7 типів) | cc.notifications | Classification | CORE | IMPL |
| provider | text | ✓ | 'Discord' | promote | cc.notifications | Config | CORE | IMPL |
| status | text | ✓ | 'Pending' | promote/Notifier (CHECK 5 станів) | cc.notifications; uniq_notification_dedup | Status | CORE | IMPL |
| payload | jsonb | ✗ | — | promote (10 keys) | Notifier (claim) | Diagnostics | OPTIONAL | IMPL |
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

**Trivia (VER):** `error_message` ≠ `last_error` — різна семантика (фінал черги vs остання спроба retry). НЕ MERGE (KB Rejected #68).

### 9.2. notification_attempts

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

---

## 10. `incident_*` tables

### 10.1. incident_policy (8 cols, 5 rows, FOREVER)

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

### 10.2. incident_status_log (7 cols, 1y) / incident_notes (5 cols, 1y)

**incident_status_log:**

| Колонка | Тип | NN | Default | Source | Reader | Category | Class |
|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | cc.incident_timeline | Identification | CORE |
| incident_id | bigint | ✓ | — | transition_incident/assign_owner | FK CASCADE; cc.incident_timeline | Identity | CORE |
| from_status | text | ✓ | — | transition_incident | cc.incident_timeline | Workflow | CORE |
| to_status | text | ✓ | — | transition_incident | cc.incident_timeline | Workflow | CORE |
| changed_by | text | ✓ | 'system' | transition_incident | cc.incident_timeline | Audit | CORE |
| changed_at | timestamptz | ✓ | now() | DB | cc.incident_timeline | Time | CORE |
| note | text | ✗ | — | transition_incident | cc.incident_timeline | Diagnostics | OPTIONAL |

**incident_notes:**

| Колонка | Тип | NN | Default | Source | Reader | Category | Class |
|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | cc.incident_notes_view | Identification | CORE |
| incident_id | bigint | ✓ | — | add_incident_note | FK CASCADE; cc.incident_notes_view | Identity | CORE |
| content | text | ✓ | — | add_incident_note | cc.incident_notes_view | Content | CORE |
| created_by | text | ✓ | 'admin' | add_incident_note | cc.incident_notes_view | Audit | CORE |
| created_at | timestamptz | ✓ | now() | DB | cc.incident_notes_view | Time | CORE |

---

## 11. `knowledge_*` tables (FOREVER)

### 11.1. knowledge_entries (19 cols)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class |
|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | cc.knowledge_*; FK | Identification | CORE |
| fingerprint_key/hash | text | ✓ | — | create_knowledge_from_incident | cc.knowledge_*; matching | Classification | CORE |
| component/operation/signal | text | ✓ | — | create_knowledge_from_incident | cc.knowledge_*; match_priority2 | Classification | CORE |
| title | text | ✓ | — | create/update_knowledge_entry | cc.knowledge_* | Content | CORE |
| symptoms | text | ✗ | — | update_knowledge_entry | cc.knowledge_* | Content | OPTIONAL |
| known_cause | text | ✓ | — | create/update_knowledge_entry | cc.knowledge_* | Content | CORE |
| workaround/permanent_fix | text | ✗ | — | update_knowledge_entry | cc.knowledge_* | Content | OPTIONAL |
| affected_versions | text[] | ✗ | — | update_knowledge_entry | cc.knowledge_list | Version | OPTIONAL |
| fixed_version | text | ✗ | — | update_knowledge_entry | cc.knowledge_*; verify_knowledge_auto | Version | OPTIONAL |
| confidence | text | ✓ | 'Low' | create/transition (CHECK Low/Medium/High/Verified) | cc.knowledge_*; verify_knowledge_auto | Status | CORE |
| status | text | ✓ | 'Draft' | create/transition (CHECK Draft/Reviewed/Verified/Deprecated/Archived) | cc.knowledge_*; matching | Status | CORE |
| created_by/updated_by | text | ✓ | — | create/update/transition | cc.knowledge_*; audit_trigger | Audit | CORE |
| created_at/updated_at | timestamptz | ✓ | now() | DB | cc.knowledge_* | Audit | CORE |

### 11.2. knowledge_references (5 cols)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class |
|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | FK | Identification | CORE |
| knowledge_id | bigint | ✓ | — | add_knowledge_reference | FK RESTRICT; cc.knowledge_entry_detail | Identity | CORE |
| reference_type | text | ✓ | — | add_knowledge_reference (CHECK 5 типів) | cc.knowledge_entry_detail | Classification | CORE |
| url | text | ✗ | — | add_knowledge_reference | cc.knowledge_entry_detail | Content | OPTIONAL |
| label | text | ✗ | — | add_knowledge_reference | cc.knowledge_entry_detail | Content | OPTIONAL |

### 11.3. knowledge_version_history (9 cols)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class |
|---|---|---|---|---|---|---|---|
| id | bigserial | ✓ | seq | DB | get_knowledge_version_detail | Identification | CORE |
| knowledge_id | bigint | ✓ | — | knowledge_audit_trigger | FK RESTRICT; get_knowledge_history | Identity | CORE |
| version | int | ✓ | — | knowledge_audit_trigger (MAX+1) | get_knowledge_history; optimistic concurrency | Audit | CORE |
| snapshot | jsonb | ✓ | — | knowledge_audit_trigger (to_jsonb NEW) | get_knowledge_version_detail | Audit | CORE |
| changed_by | text | ✗ | — | knowledge_audit_trigger | get_knowledge_history | Audit | OPTIONAL |
| db_user | text | ✓ | CURRENT_USER | DB | get_knowledge_history (dual-identity) | Audit | CORE |
| changed_at | timestamptz | ✓ | now() | DB | get_knowledge_history | Time | CORE |
| change_reason | text | ✗ | — | set_knowledge_change_context | get_knowledge_history | Audit | OPTIONAL |
| change_type | text | ✓ | 'Updated' | set_knowledge_change_context | get_knowledge_history | Audit | CORE |

---

## 12. RESERVED tables (0 rows, KEEP)

### 12.1. error_reports (13 cols, RESERVED)

> Зарезервована (міграція 00003). 0 продюсерів. KEEP через KB Rejected #39.

| Колонка | Тип | Категорія | Class |
|---|---|---|---|
| id, user_id, install_id, error_type, message, stack_trace, app_version, localization_version, game_folder_path, selected_environment, context, is_resolved, created_at | diverse | Identification/Content/Diagnostics/Status/Audit/Version/Config | FUTURE |

**Плановані source:** client Report bug (ApplicationException.GetType().Name, Exception.Message/StackTrace, IInstallationContextProvider fields).

### 12.2. admin_audit_log (7 cols, RESERVED)

> Зарезервована для майбутньої адмін-панелі (міграція 00004). 0 продюсерів. KEEP.

| Колонка | Тип | Class |
|---|---|---|
| id, admin_discord_id, action, target_user_id, target_install_id, details, created_at | diverse | FUTURE |

### 12.3. user_discord_guilds (5 cols, RESERVED)

> Код `DiscordGuildSyncService` мертвий (identify scope only — без guilds.members.read). KEEP до розширення OAuth scope.

| Колонка | Тип | Class |
|---|---|---|
| id, user_id, discord_guild_id, guild_name, synced_at | diverse | FUTURE |

---

## 13. `pipeline_health_meta` (2 cols, singleton, FOREVER)

| Колонка | Тип | NN | Default | Source | Reader | Category | Class |
|---|---|---|---|---|---|---|---|
| singleton | boolean | ✓ | true (CHECK singleton=true) | DB | cc.observability_health | Config | CORE |
| last_knowledge_refresh | timestamptz | ✗ | — | refresh_knowledge_coverage | cc.observability_health | Time | CORE |

---

## 14. `auth.users` (35 cols, Supabase-managed)

> **Системна таблиця Supabase GoTrue.** Не керується міграціями SCLOC-Verse. Структура 35 колонок ідентична в production та replica.

**Використовувані колонки:** `id`, `email`, `raw_user_meta_data` (provider_id, full_name, custom_claims.global_name, avatar_url), `created_at`, `last_sign_in_at`, `email_confirmed_at`, `is_anonymous`, `banned_until`, `deleted_at`.

**Dependency graph:** `auth.users` → `public.user_analytics` (VIEW) → `control_center.users` (VIEW). Жодної власної `public.users` таблиці немає.

---

## 15. Data Lifetime — зведена таблиця

| Таблиця | Lifetime | Reason | Tool |
|---|---|---|---|
| app_installations | FOREVER (поки user_id існує) | історія установок | — |
| telemetry_events | 90d | append-only, retention | service_role purge (Phase 5.1 ✅) |
| telemetry_incidents | 1y після closed_at | інциденти — коротка пам'ять | service_role archive (Phase 5.2+) |
| incident_policy | FOREVER | reference data | — |
| incident_status_log | 1y | audit journal | service_role archive (Phase 5.2+) |
| incident_notes | 1y | audit journal | service_role archive (Phase 5.2+) |
| notification_queue | 30d після delivered_at | транзиція | service_role purge (Phase 5.2+) |
| notification_attempts | 90d | audit | service_role purge (Phase 5.2+) |
| knowledge_entries | FOREVER | knowledge base | — |
| knowledge_references | FOREVER | audit | — |
| knowledge_version_history | FOREVER | audit | — |
| error_reports | RESERVED | не визначено (таблиця порожня) | — |
| admin_audit_log | RESERVED | не визначено | — |
| user_discord_guilds | RESERVED | не визначено (код мертвий) | — |
| pipeline_health_meta | FOREVER | singleton | — |
| auth.users | Supabase-managed | GoTrue lifecycle | auth.admin API |

> ✅ **Retention Phase 5.1 Implemented (2026-07-11)** — `telemetry_events` (90d) через `pg_cron` + `run_retention_pipeline()`. Інші таблиці — Phase 5.2+, закоментовано в dispatcher.

---

## 16. Phase 2 Database Cleanup Review (2026-07-07)

> Повний аналітичний аудит схеми без реалізації. Усі цифри — з живої БД через `pg_catalog`/`pg_stat_user_indexes`.

### 16.1. Нові знахідки (10 пунктів)

| # | Знахідка | Доказ |
|---|---|---|
| C-1 | Резервна схема `backup_pre_1_0_0_1` (12 таблиць-дублів) | `pg_class` по схемі; жодних продюсерів, 0 залежностей |
| C-2 | `telemetry_events.http_status` + `supabase_code` 100% NULL | `count(*) FILTER`; ErrorContextExtractor не заповнює |
| C-3 | `detail.signal_name` НЕ існує в живих даних (0 зустрічей) | `jsonb_object_keys` частотний аналіз |
| C-4 | VIEWs у KB занижено: 19 → 25 фактично (24 cc + 1 public) | `pg_class WHERE relkind IN ('v','m')` |
| C-5 | SECURITY DEFINER функцій: ~24 → 28 фактично; лише 2 з 28 мали `SET search_path` | `pg_proc WHERE prosecdef=true` + `proconfig` |
| C-6 | `pg_cron` встановлено (Phase 5.1, 2026-07-11) — `run_retention_pipeline()` daily 03:00 UTC | `cron.job` 1 row active |
| C-7 | Дубль індексу `app_installations.install_id`: NON-UNIQUE + UNIQUE | `pg_stat_user_indexes` — планувальник обходить UNIQUE |
| C-8 | `ecosystem_stats()` — мертва (`.Rpc(` в C# не знайдено; `pg_depend=[]`) | лише docs як RPC-контракт |
| C-9 | `promote_incident_candidates()` (batch) — мертва (`pg_depend=[]`) | тригер викликає лише `_for_event` |
| C-10 | 11 з 24 control_center views НЕ викликаються з C#/Blazor | grep `.razor`+`.cs` |

### 16.2. Database Cleanup Matrix

**🟢 KEEP (40):** `public`, `control_center`; 13 живих таблиць; усі 13 FK; 5 triggers; 13 активних views; 24 активні функції; усі 28 RLS; 29 структурних/використовуваних індексів.

**🟡 ACTIVATE (10):** 4 активації `app_installations.*` через `IInstallationContextProvider`; `git_commit` (MSBuild); `category` диференційована; Phase 0 (3 partial-індекси); Phase 3 (3 матв'юхи); generated column `incident_code` (замість формування в 4 views + Notifier).

**❌ MERGE (0)** — обидва кандидати зняті:
- `notification_queue.error_message` ↔ `last_error` — **різна семантика**. KB Rejected #68.
- `telemetry_events.detail.retry_count` — **заготовка під Retry Policy**, НЕ мертва. KB Rejected #69.

**🔴 REMOVE (3 індекси — доведено дубль/невикористання):**
- `idx_app_installations_install_id` — дублює UNIQUE `app_installations_install_id_key`.
- `idx_app_installations_machine_id` — `idx_scan=0`, machine_id не шукається.
- `idx_user_discord_guilds_user_id` — дублює провідний стовпець UNIQUE.

> Усі 3 — `DROP INDEX` (additive). **2/3 DONE Phase 3A production 2026-07-07** (install_id + user_discord_guilds); machine_id → DEFER (ризик regression).

**⏸ DEFER (28):** таблиці `error_reports`/`admin_audit_log`/`user_discord_guilds` (Rejected #39); 11 невикористовуваних views (additive view-контракт); функції `promote_incident_candidates`/`ecosystem_stats`/`get_knowledge_version_detail` (DROP заборонено API Freeze); `app_installations.os_build`/`install_source`; `telemetry_events.country` (Rejected #35).

### 16.3. SEC-11 — COMPLETED

✅ COMPLETED (2026-07-08 + 2026-07-14). Усі 30 public SECURITY DEFINER функцій тепер мають `SET search_path = public, pg_catalog`. Міграція: `supabase/migrations/20260708235000_security_definer_search_path.sql`.

### 16.4. SEC-12 — COMPLETED

✅ COMPLETED (2026-07-14). `REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA public FROM PUBLIC;` + `GRANT EXECUTE ... TO cc_readonly, cc_notifier;`. Будь-який `authenticated` більше не може викликати admin-функції через `POST /rest/v1/rpc/...`.

### 16.5. FK Indexes (7) — COMPLETED (2026-07-14)

CREATE INDEX на 7 неіндексованих FK колонок: `incident_notes(incident_id)`, `incident_status_log(incident_id)`, `knowledge-version_history(knowledge_id)`, `telemetry_events(install_id)`, `telemetry_events(user_id)`, `telemetry_incidents(root_event_id)`, `telemetry_incidents(last_event_id)`.

---

## 17. Посилання

- **KB §3 Database** — короткий огляд + покажчик на цей документ.
- **KB §4 app_installations** — короткий огляд.
- **KB §14.36** — Performance Advisor Fix + Backup Schema Cleanup (RLS init plan + FK indexes + DROP backup schemas).
- **KB §14.35** — SEC-12 Production Fix (REVOKE EXECUTE).
- **`docs/observability/FORENSIC-DATA-PIPELINE-RAW.md`** — повні CREATE/ALTER SQL.
- **`docs/observability/FORENSIC-DATA-PIPELINE-DETAIL.md`** — деталі pipeline.
- **`docs/observability/app-installations-forensic-2026-07-05.md`** — forensic app_installations колонок.
- **`docs/observability/app-installations-implementation-plan.md`** — план `IInstallationContextProvider`.
- **`supabase/migrations/`** — 26+ міграцій (SCLOC-Verse).
- **`docs/checklists/Database-Verification.md`** — RLS/таблиці чеклист (Стаття 17 Конституції).
