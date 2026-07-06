# Форензик-звіт: data pipeline SCLOC-Verse

Дата збору: 2026-07-06.
Мета: задокументувати, які дані зберігаються/передаються/використовуються у проєкті SCLOC-Verse.
Формат: факти, шляхи, рядки, імена об'єктів, імена колонок, C# символи.

---

## 1. Supabase SQL міграції

Розташування: `F:\C#\SCLocalizationUA\supabase\migrations`.
Кількість файлів: 25 міграцій (20260630000001…20260705030000).

### 1.1. Перелік SQL-об'єктів за типами

| Тип | Ім'я об'єкта | Файл міграції | Примітка |
|-----|--------------|---------------|----------|
| TABLE | `public.app_installations` | 20260630000001_create_app_installations.sql | метадані інсталяції |
| TABLE | `public.error_reports` | 20260630000003_create_error_reports.sql | не використовується клієнтом |
| TABLE | `public.admin_audit_log` | 20260630000004_create_admin_audit_log.sql | аудит адмін-дій |
| TABLE | `public.user_discord_guilds` | 20260630000005_create_user_discord_guilds.sql | синхронізація гільдій (вимкнена) |
| VIEW | `public.user_analytics` | 20260630000006_user_analytics_view.sql | аналітичне VIEW |
| SCHEMA | `control_center` | 20260630000007_control_center_contract.sql | схема контракту |
| VIEW | `control_center.contract_info` | 20260630000007 | метадані контракту |
| VIEW | `control_center.users` | 20260630000007 | проєкція користувачів |
| VIEW | `control_center.installations` | 20260630000007 | проєкція інсталяцій |
| VIEW | `control_center.statistics` | 20260630000007 | статистика |
| VIEW | `control_center.errors` | 20260630000007 | проєкція error_reports |
| VIEW | `control_center.health` | 20260630000007 | active_installations_last_7d |
| ROLE | `cc_readonly` | 20260630000008_control_center_readonly_role.sql | read-only роль CC |
| TABLE | `public.telemetry_events` | 20260630000009_create_telemetry_events.sql | append-only події |
| VIEW | `control_center.telemetry_events` | 20260630000010_control_center_telemetry.sql | проєкція подій |
| VIEW | `control_center.traces` | 20260630000010 | trace-реконструкція |
| VIEW | `control_center.incident_candidates_live` | 20260630000011_incident_candidates_views.sql | кандидати 10 хв |
| VIEW | `control_center.incident_candidates_24h` | 20260630000011 | кандидати 24 год |
| TABLE | `public.telemetry_incidents` | 20260630000012_incident_tables.sql | інциденти |
| TABLE | `public.incident_policy` | 20260630000012_incident_tables.sql | політика порогів |
| FUNCTION | `public.promote_incident_candidates()` | 20260630000013_promotion_engine.sql | batch промоутер |
| VIEW | `control_center.incidents` | 20260630000013 | VIEW інцидентів |
| TABLE | `public.notification_queue` | 20260630000016_notification_engine.sql | черга сповіщень |
| VIEW | `control_center.notifications` | 20260630000016, 20260704000017 | VIEW черги |
| FUNCTION | `public.promote_incident_candidates()` (оновлена) | 20260630000016 | додає enqueue notification |
| TABLE | `public.notification_attempts` | 20260704000017_notification_audit_and_retry.sql | аудит спроб |
| ROLE | `cc_notifier` | 20260704000018_cc_notifier_role.sql | роль Worker |
| TABLE | `public.knowledge_entries` | 20260704000019_knowledge_engine_core.sql | база знань |
| TABLE | `public.knowledge_references` | 20260704000019 | посилання |
| TABLE | `public.knowledge_version_history` | 20260704000019 | історія версій |
| FUNCTION | `public.knowledge_audit_trigger()` | 20260704000019 | тригер аудиту |
| VIEW | `control_center.knowledge_entry_detail` | 20260704000019 | деталь knowledge |
| FUNCTION | `public.match_knowledge_for_incident(bigint)` | 20260704000019 | exact fingerprint match |
| FUNCTION | `public.create_knowledge_from_incident(...)` | 20260704000020_knowledge_authoring.sql | створення Draft |
| FUNCTION | `public.can_create_knowledge_for_incident(bigint)` | 20260704000020 | preflight |
| FUNCTION | `public.parse_version_list(text)` | 20260704000021_knowledge_lifecycle.sql | парсинг версій |
| FUNCTION | `public.update_knowledge_entry(...)` | 20260704000021, 20260704000022 | редагування |
| FUNCTION | `public.archive_knowledge_entry(...)` | 20260704000021 | застаріла, видалена у 00023 |
| FUNCTION | `public.add_knowledge_reference(...)` | 20260704000021, 00022 | додавання reference |
| FUNCTION | `public.remove_knowledge_reference(...)` | 20260704000021, 00022 | видалення reference |
| FUNCTION | `public.get_knowledge_history(bigint)` | 20260704000021, 00022 | історія |
| FUNCTION | `public.get_knowledge_version_detail(bigint)` | 20260704000021 | snapshot |
| FUNCTION | `public.get_knowledge_current_version(bigint)` | 20260704000021 | поточна версія |
| FUNCTION | `public.can_edit_knowledge(bigint)` | 20260704000021 | перевірка редагування |
| FUNCTION | `public.set_knowledge_change_context(...)` | 20260704000022_knowledge_workflow.sql | контекст зміни |
| FUNCTION | `public.require_not_archived(bigint)` | 20260704000022 | перевірка статусу |
| FUNCTION | `public.transition_knowledge(...)` | 20260704000022 | workflow transition |
| FUNCTION | `public.match_knowledge_priority2(bigint)` | 20260704000022 | component+signal match |
| FUNCTION | `public.verify_knowledge_auto()` | 20260704000023_knowledge_coverage.sql | авто-верифікація |
| FUNCTION | `public.search_knowledge(...)` | 20260704000023 | ручний пошук |
| MATERIALIZED VIEW | `control_center.knowledge_coverage` | 20260704000023 | покриття |
| VIEW | `control_center.knowledge_list` | 20260704000023 | список |
| VIEW | `control_center.top_missing_knowledge` | 20260704000023 | топ непокритих |
| TABLE | `control_center.pipeline_health_meta` | 20260705021100_observability_pipeline_automation.sql | singleton health |
| FUNCTION | `public.refresh_knowledge_coverage()` | 20260705021100 (оновлена) | REFRESH MV |
| FUNCTION | `public.promote_incident_candidates_for_event(uuid)` | 20260705021100 | per-event промоутер |
| FUNCTION | `public.tg_promote_after_failed()` | 20260705021100, 20260705030000 | trigger function |
| FUNCTION | `public.tg_incident_refresh_coverage()` | 20260705021100 | trigger function |
| TRIGGER | `trg_telemetry_failed_promote` | 20260705021100 | AFTER INSERT Failed |
| TRIGGER | `trg_incident_refresh_coverage` | 20260705021100 | AFTER INSERT/UPDATE incidents |
| FUNCTION | `public.auto_close_stale_incidents()` | 20260705030000_auto_close_on_event.sql | авто-close |
| VIEW | `control_center.observability_health` | 20260705021200_observability_health_view.sql | health singleton |
| VIEW | `control_center.release_health_detail` | 20260705021200 | детальна health версії |

### 1.2. Детальні визначення: `public.users` (auth.users)

Усі CC VIEW працюють з `auth.users` через `public.user_analytics`.
Використані колонки:
- `id` (uuid) — PK.
- `email` (text).
- `raw_user_meta_data ->> 'provider_id'` → discord_id.
- `raw_user_meta_data ->> 'full_name'` → username.
- `raw_user_meta_data #>> '{custom_claims,global_name}'` → display_name.
- `raw_user_meta_data ->> 'avatar_url'` → avatar_url.
- `created_at` → user_created_at.
- `last_sign_in_at` → user_last_sign_in_at.
- `email_confirmed_at` → user_email_confirmed_at.
- `is_anonymous` → user_is_anonymous.
- `banned_until` → user_is_banned.
- `deleted_at` — фільтр `WHERE u.deleted_at IS NULL`.

### 1.3. Детальні визначення: `public.app_installations`

```sql
CREATE TABLE IF NOT EXISTS public.app_installations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at timestamptz NOT NULL DEFAULT now(),
    install_id text NOT NULL,
    app_version text,
    localization_version text,
    country text,
    platform text,
    first_seen timestamptz DEFAULT now(),
    last_seen timestamptz,
    user_id uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    machine_id text,
    os_version text,
    os_build text,
    update_channel text DEFAULT 'stable',
    install_source text DEFAULT 'unknown',
    game_folder_path text,
    selected_environment text,
    is_active boolean DEFAULT true,
    updated_at timestamptz
);
```

Обмеження / індекси / RLS:
- UNIQUE `app_installations_install_id_key` на `install_id`.
- Індекси: `idx_app_installations_user_id`, `idx_app_installations_install_id`, `idx_app_installations_machine_id`, `idx_app_installations_last_seen`.
- RLS enabled. `deny all anon`, owner-only CRUD для `authenticated` (`user_id = auth.uid()`).
- Тригер: `trg_app_installations_set_country` (BEFORE INSERT OR UPDATE) викликає `public.set_country_from_cf()` — заповнює `country` з `cf-ipcountry` заголовка.

Клієнт C# використовує модель `AppInstallation` (`SCLOCVerse\Models\Auth\AppInstallation.cs`):
- мапить: `id`, `user_id`, `install_id`, `app_version`, `platform`, `machine_id`, `os_version`, `last_seen`, `first_seen`, `created_at`, `is_active`, `updated_at`.
- НЕ мапить: `localization_version`, `country`, `os_build`, `update_channel`, `install_source`, `game_folder_path`, `selected_environment`.

### 1.4. Детальні визначення: `public.telemetry_events`

```sql
CREATE TABLE IF NOT EXISTS public.telemetry_events (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    client_event_id uuid NOT NULL,
    session_id uuid NOT NULL,
    correlation_id uuid NOT NULL,
    step int NOT NULL,
    install_id text REFERENCES public.app_installations(install_id) ON DELETE SET NULL,
    user_id uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    occurred_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    app_version text NOT NULL,
    git_commit text,
    channel text NOT NULL DEFAULT 'stable',
    telemetry_version int NOT NULL DEFAULT 1,
    os_version text,
    country text,
    component text NOT NULL,
    operation text NOT NULL,
    outcome text NOT NULL,
    severity text NOT NULL DEFAULT 'Info',
    category text NOT NULL DEFAULT 'Operational',
    source text,
    http_status int,
    hresult text,
    supabase_code text,
    exception_type text,
    error_message text,
    duration_ms int,
    detail jsonb,
    CONSTRAINT chk_telemetry_outcome  CHECK (outcome IN ('Started','Succeeded','Failed','Cancelled','Skipped')),
    CONSTRAINT chk_telemetry_severity CHECK (severity IN ('Info','Warning','Error','Critical','Crash')),
    CONSTRAINT chk_telemetry_category CHECK (category IN ('Critical','Operational','Diagnostic','Analytics')),
    CONSTRAINT chk_telemetry_failed_has_signal CHECK (
        outcome <> 'Failed'
        OR COALESCE(source, hresult, supabase_code, http_status::text, exception_type) IS NOT NULL
    )
);
```

Індекси: `uniq_telemetry_client_event_id` (UNIQUE), `idx_telemetry_received`, `idx_telemetry_trace`, `idx_telemetry_detect`.
RLS: deny-all anon; `authenticated` — owner-only SELECT+INSERT (`user_id = auth.uid()`).
Тригер: `trg_telemetry_failed_promote` AFTER INSERT WHEN `NEW.outcome = 'Failed'` → `public.tg_promote_after_failed()`.

C# модель `TelemetryEvent` (`SCLOCVerse\Models\Observability\TelemetryEvent.cs`) мапить усі колонки окрім `received_at` (DEFAULT now(), NOT NULL).

### 1.5. Детальні визначення: `public.telemetry_incidents`

```sql
CREATE TABLE IF NOT EXISTS public.telemetry_incidents (
    id bigserial PRIMARY KEY,
    fingerprint_key text NOT NULL,
    fingerprint_hash text NOT NULL,
    release text NOT NULL,
    component text NOT NULL,
    operation text NOT NULL,
    signal text NOT NULL,
    root_event_id uuid REFERENCES public.telemetry_events(id) ON DELETE SET NULL,
    last_event_id uuid REFERENCES public.telemetry_events(id) ON DELETE SET NULL,
    opened_at timestamptz NOT NULL DEFAULT now(),
    last_event_at timestamptz NOT NULL DEFAULT now(),
    closed_at timestamptz,
    status text NOT NULL DEFAULT 'Active',
    highest_severity text NOT NULL DEFAULT 'Warning',
    peak_failure_pct numeric(5,1),
    affected_users int NOT NULL DEFAULT 0,
    affected_installs int NOT NULL DEFAULT 0,
    event_count bigint NOT NULL DEFAULT 0,
    owner text,                              -- додано у 20260630000015
    CONSTRAINT chk_incident_status CHECK (status IN ('Active','Confirmed','Investigating','Resolved','Closed')),
    CONSTRAINT chk_incident_severity CHECK (highest_severity IN ('Critical','Warning'))
);
```

Індекси: `idx_incidents_fp_open` (status != 'Closed'), `idx_incidents_opened`.
RLS: deny all для anon/authenticated.
CC VIEW: `control_center.incidents` додає computed `incident_id` (`INC-YYYY-NNNNN`), включає всі колонки + `owner`.

### 1.6. Детальні визначення: `public.notification_queue`

```sql
CREATE TABLE IF NOT EXISTS public.notification_queue (
    id bigserial PRIMARY KEY,
    incident_id bigint NOT NULL REFERENCES public.telemetry_incidents(id) ON DELETE CASCADE,
    notification_type text NOT NULL,
    provider text NOT NULL DEFAULT 'Discord',
    status text NOT NULL DEFAULT 'Pending',
    payload jsonb,
    retry_count int NOT NULL DEFAULT 0,
    last_attempt_at timestamptz,
    delivered_at timestamptz,
    error_message text,
    created_at timestamptz NOT NULL DEFAULT now(),
    next_attempt_at timestamptz,             -- додано 00017
    max_retries int NOT NULL DEFAULT 3,      -- додано 00017
    claimed_at timestamptz,                  -- додано 00017
    claimed_by text,                         -- додано 00017
    last_error text,                         -- додано 00017
    CONSTRAINT chk_notif_status CHECK (status IN ('Pending','Sending','Delivered','Failed','RetryScheduled')),
    CONSTRAINT chk_notification_type CHECK (
        notification_type IN (
            'IncidentCreated','IncidentEscalated','IncidentMitigated','IncidentResolved','WeeklyDigest','TestAlert','KnowledgeVerified'
        )
    )
);
```

Індекси: `uniq_notification_dedup` UNIQUE ON `(incident_id, notification_type) WHERE status != 'Failed'`; `idx_notif_pending`.
RLS: deny all для anon/authenticated/cc_readonly.
Роль `cc_notifier` має ALL policy через RLS (`allow cc_notifier notification_queue`).

### 1.7. Детальні визначення: `public.notification_attempts`

```sql
CREATE TABLE IF NOT EXISTS public.notification_attempts (
    id bigserial PRIMARY KEY,
    queue_id bigint NOT NULL REFERENCES public.notification_queue(id) ON DELETE CASCADE,
    attempt_no int NOT NULL,
    provider text NOT NULL,
    status text NOT NULL,
    http_status int,
    provider_message_id text,
    error_message text,
    started_at timestamptz NOT NULL DEFAULT now(),
    finished_at timestamptz,
    CONSTRAINT chk_attempt_status CHECK (status IN ('Sending','Delivered','Failed'))
);
CREATE INDEX IF NOT EXISTS idx_attempts_queue ON public.notification_attempts(queue_id);
CREATE INDEX IF NOT EXISTS idx_attempts_started ON public.notification_attempts(started_at);
```

RLS: deny all; `cc_notifier` ALL policy.

### 1.8. Детальні визначення: Knowledge (`public.knowledge_entries`, `knowledge_references`, `knowledge_version_history`)

#### `public.knowledge_entries`

```sql
CREATE TABLE IF NOT EXISTS public.knowledge_entries (
    id bigserial PRIMARY KEY,
    fingerprint_key text NOT NULL,
    fingerprint_hash text NOT NULL,
    component text NOT NULL,
    operation text NOT NULL,
    signal text NOT NULL,
    title text NOT NULL,
    symptoms text,
    known_cause text NOT NULL,
    workaround text,
    permanent_fix text,
    affected_versions text[],
    fixed_version text,
    confidence text NOT NULL DEFAULT 'Low',
    status text NOT NULL DEFAULT 'Draft',
    created_by text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_by text NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_knowledge_confidence CHECK (confidence IN ('Low','Medium','High','Verified')),
    CONSTRAINT chk_knowledge_status CHECK (status IN ('Draft','Reviewed','Verified','Deprecated','Archived')),
    CONSTRAINT chk_knowledge_status_confidence CHECK (
        (status = 'Verified' AND confidence IN ('High','Verified')) OR
        (status IN ('Draft','Reviewed') AND confidence IN ('Low','Medium','High')) OR
        (status IN ('Deprecated','Archived'))
    )
);
```

Індекси: `idx_knowledge_fingerprint`, `idx_knowledge_component_signal` (Verified), `idx_knowledge_status_updated`.
Тригер: `knowledge_audit` AFTER INSERT/UPDATE → `public.knowledge_audit_trigger()` записує snapshot у `knowledge_version_history`.

#### `public.knowledge_references`

```sql
CREATE TABLE IF NOT EXISTS public.knowledge_references (
    id bigserial PRIMARY KEY,
    knowledge_id bigint NOT NULL REFERENCES public.knowledge_entries(id) ON DELETE RESTRICT,
    reference_type text NOT NULL,
    url text,
    label text,
    CONSTRAINT chk_knowledge_ref_type CHECK (reference_type IN ('GitCommit','GitHubIssue','Documentation','ReleaseNotes','External'))
);
```

#### `public.knowledge_version_history`

```sql
CREATE TABLE IF NOT EXISTS public.knowledge_version_history (
    id bigserial PRIMARY KEY,
    knowledge_id bigint NOT NULL REFERENCES public.knowledge_entries(id) ON DELETE RESTRICT,
    version int NOT NULL,
    snapshot jsonb NOT NULL,
    changed_by text,
    db_user text NOT NULL DEFAULT current_user,
    changed_at timestamptz NOT NULL DEFAULT now(),
    change_reason text,
    change_type text NOT NULL DEFAULT 'Updated'  -- додано 00022
);
```

### 1.9. Детальні визначення: Control Center views

#### `control_center.users`

SELECT-колонки: `user_id`, `email`, `discord_id`, `username`, `display_name`, `avatar_url`, `user_created_at`, `user_last_sign_in_at`, `user_email_confirmed_at`, `user_is_anonymous`, `user_is_banned`, `install_id`, `country`, `app_version`, `platform`, `machine_id`, `os_version`, `update_channel`, `install_source`, `selected_environment`, `is_install_active`, `install_first_seen`, `install_last_seen`, `install_created_at`, `install_count`, `active_install_count`, `user_countries`.

#### `control_center.installations`

SELECT-колонки: `id`, `created_at`, `install_id`, `app_version`, `localization_version`, `country`, `platform`, `first_seen`, `last_seen`, `user_id`, `machine_id`, `os_version`, `os_build`, `update_channel`, `install_source`, `game_folder_path`, `selected_environment`, `is_active`, `updated_at`.

#### `control_center.telemetry_events`

SELECT-колонки: `id`, `client_event_id`, `session_id`, `correlation_id`, `step`, `install_id`, `user_id`, `occurred_at`, `received_at`, `app_version`, `git_commit`, `channel`, `telemetry_version`, `os_version`, `country`, `component`, `operation`, `outcome`, `severity`, `category`, `source`, `http_status`, `hresult`, `supabase_code`, `exception_type`, `error_message`, `duration_ms`, `detail`.

#### `control_center.traces`

SELECT-колонки: `correlation_id`, `session_id`, `install_id`, `app_version`, `step`, `occurred_at`, `component`, `operation`, `outcome`, `category`, `signal` (COALESCE), `error_message`, `duration_ms`.

#### `control_center.incidents`

SELECT-колонки: computed `incident_id`, `id`, `fingerprint_key`, `fingerprint_hash`, `release`, `component`, `operation`, `signal`, `root_event_id`, `last_event_id`, `opened_at`, `last_event_at`, `closed_at`, `status`, `highest_severity`, `peak_failure_pct`, `affected_users`, `affected_installs`, `event_count`, `owner`.

#### `control_center.incident_candidates_live`

SELECT-колонки: `fingerprint_key`, `fingerprint_hash`, `release`, `telemetry_version`, `component`, `operation`, `signal`, `failed_now`, `total_now`, `failure_pct`, `affected_installs`, `affected_users`, `first_seen`, `last_seen`, `severity`.

#### `control_center.component_health`

SELECT-колонки: `component`, `health`, `active_incidents`, `last_event_at`.

#### `control_center.release_health`

SELECT-колонки: `app_version`, `succeeded`, `failed`, `active_installs`.

#### `control_center.platform_stats`

SELECT-колонки: `events_24h`, `active_users_24h`, `active_installations`, `open_incidents`.

#### `control_center.release_health_detail`

SELECT-колонки: `app_version`, `succeeded`, `failed`, `success_rate`, `active_installs`, `users_24h`, `new_incidents`, `critical_incidents`, `first_seen`, `last_seen`, `top_fingerprint`, `top_component`, `top_signal`, `top_event_count`, `top_severity`.

#### `control_center.knowledge_entry_detail`

SELECT-колонки: `id`, `fingerprint_key`, `fingerprint_hash`, `component`, `operation`, `signal`, `title`, `symptoms`, `known_cause`, `workaround`, `permanent_fix`, `affected_versions`, `fixed_version`, `confidence`, `status`, `created_by`, `created_at`, `updated_by`, `updated_at`, `references` (jsonb_agg).

#### `control_center.knowledge_coverage`

SELECT-колонки: `total_fingerprints`, `covered_fingerprints`, `uncovered_fingerprints`, `coverage_pct`.

#### `control_center.knowledge_list`

SELECT-колонки: `id`, `fingerprint_key`, `component`, `operation`, `signal`, `title`, `confidence`, `status`, `fixed_version`, `affected_versions`, `created_at`, `updated_at`, `updated_by`.

#### `control_center.top_missing_knowledge`

SELECT-колонки: `component`, `signal`, `fingerprint_count`, `total_events`, `last_seen`, `highest_severity`.

#### `control_center.observability_health`

SELECT-колонки: `last_failed_event_at`, `last_incident_opened_at`, `last_notification_at`, `last_knowledge_refresh_at`, `candidates_without_open_incident`, `pipeline_healthy`.

---

## 2. Edge Functions

Розташування: `F:\C#\SCLocalizationUA\supabase\functions`.
Результат: директорія відсутня або порожня — Edge Functions не знайдено.
Пошук в C# за шаблонами `supabase.functions`, `.Rpc(`, `functions.`:
- у `SCLOCVerse` — не знайдено;
- у `SCLOCVerse.ControlCenter` — не знайдено;
- у `SCLOCVerse.Notifier` — не знайдено.

Висновок: проєкт не використовує Supabase Edge Functions для збору/обробки даних.

---

## 3. C# telemetry producers

### 3.1. Інтерфейс / черга / клієнт

| Файл | Символ | Призначення |
|------|--------|-------------|
| `SCLOCVerse\Interfaces\ITelemetryService.cs:33` | `ITelemetryService.Track(...)` | контракт |
| `SCLOCVerse\Services\Observability\TelemetryClient.cs:21` | `TelemetryClient` | єдина реалізація |
| `SCLOCVerse\Services\Observability\TelemetryEventQueue.cs:15` | `TelemetryEventQueue` | in-memory черга |
| `SCLOCVerse\Services\Observability\TelemetryUploader.cs:19` | `TelemetryUploader` | відправка в Supabase |
| `SCLOCVerse\Models\Observability\TelemetryEvent.cs:11` | `TelemetryEvent` | модель таблиці |
| `SCLOCVerse\Models\Observability\TelemetryContext.cs:10` | `TelemetryContext` | optional details |

### 3.2. Мапінг `TelemetryEvent` → `public.telemetry_events`

| C# властивість | SQL колонка | Примітка |
|----------------|-------------|----------|
| `Id` | `id` | сервер генерує, але C# шле Guid |
| `ClientEventId` | `client_event_id` | UNIQUE дедуплікація |
| `SessionId` | `session_id` | один запуск |
| `CorrelationId` | `correlation_id` | trace |
| `Step` | `step` | порядок |
| `InstallId` | `install_id` | FK app_installations |
| `UserId` | `user_id` | встановлює Uploader перед INSERT |
| `OccurredAt` | `occurred_at` | UTC клієнта |
| `AppVersion` | `app_version` | |
| `GitCommit` | `git_commit` | |
| `Channel` | `channel` | |
| `TelemetryVersion` | `telemetry_version` | |
| `OsVersion` | `os_version` | |
| `Country` | `country` | |
| `Component` | `component` | |
| `Operation` | `operation` | |
| `Outcome` | `outcome` | CHECK |
| `Severity` | `severity` | |
| `Category` | `category` | |
| `Source` | `source` | |
| `HttpStatus` | `http_status` | |
| `Hresult` | `hresult` | |
| `SupabaseCode` | `supabase_code` | |
| `ExceptionType` | `exception_type` | |
| `ErrorMessage` | `error_message` | санітайзується PrivacySanitizer |
| `DurationMs` | `duration_ms` | |
| `Detail` | `detail` | jsonb Dictionary |

`received_at` — не мапиться, сервер ставить `now()`.

### 3.3. Всі виклики `.Track()` у SCLOCVerse

| Файл:рядок | Компонент | Операція | Outcome | Severity default / detail |
|------------|-----------|----------|---------|---------------------------|
| `App.xaml.cs:69` | `Application` | `Start` | `Started` | Info |
| `Services\Auth\AuthService.cs:66` | `Auth` | `SignIn` | `Started` | Info |
| `Services\Auth\AuthService.cs:83` | `Auth` | `SignIn` | `Failed` | Error (ErrorContextExtractor) |
| `Services\Auth\AuthService.cs:95` | `Auth` | `SignIn` | `Cancelled` | Info |
| `Services\Auth\AuthService.cs:107` | `Auth` | `SignIn` | `Cancelled` | Info |
| `Services\Auth\AuthService.cs:113` | `Auth` | `SignIn` | `Failed` | Error |
| `Services\Auth\AuthService.cs:119` | `Auth` | `SignIn` | `Failed` | Error |
| `Services\Auth\AuthService.cs:128` | `Auth` | `SignIn` | `Failed` | Error |
| `Services\Auth\AuthService.cs:140` | `Auth` | `SignIn` | `Succeeded` | Info, durationMs |
| `Services\Auth\AuthService.cs:145` | `Auth` | `SignIn` | `Cancelled` | Info |
| `Services\Auth\AuthService.cs:151` | `Auth` | `SignIn` | `Failed` | Error, durationMs |
| `Services\Auth\AuthService.cs:182` | `Auth` | `RestoreSession` | `Started` | Info |
| `Services\Auth\AuthService.cs:216` | `Auth` | `RestoreSession` | `Succeeded` | Info, durationMs |
| `Services\Auth\AuthService.cs:229` | `Auth` | `RestoreSession` | `Failed` | Error, durationMs |
| `Services\Auth\InstallationService.cs:46` | `Installation` | `Sync` | `Started` | Info, detail `{phase, retry_count}` |
| `Services\Auth\InstallationService.cs:114` | `Installation` | `Sync` | `Succeeded` | Info, durationMs, detail `{phase, retry_count}` |
| `Services\Auth\InstallationService.cs:118` | `Installation` | `Sync` | `Failed` | Error, durationMs, detail `{phase, retry_count}` |
| `Services\ApplicationUpdate\UpdateDownloader.cs:44` | `Updater` | `Download` | `Started` | Info |
| `Services\ApplicationUpdate\UpdateDownloader.cs:54` | `Updater` | `Download` | `Succeeded` | Info, durationMs |
| `Services\ApplicationUpdate\UpdateDownloader.cs:59` | `Updater` | `Download` | `Failed` | Error, durationMs, detail `{phase, retry_count}` |
| `Services\ApplicationUpdate\UpdateInstaller.cs:40` | `Updater` | `Install` | `Started` | Info |
| `Services\ApplicationUpdate\UpdateInstaller.cs:46` | `Updater` | `Install` | `Failed` | Info, detail `{phase, retry_count}` |
| `Services\ApplicationUpdate\UpdateInstaller.cs:76` | `Updater` | `Install` | Succeeded/Failed | durationMs, detail `{phase, retry_count}` |
| `Services\ApplicationUpdate\UpdateInstaller.cs:82` | `Updater` | `Install` | `Failed` | Error, durationMs, detail `{phase, retry_count}` |
| `Services\ApplicationUpdate\UpdateVerifier.cs:34` | `Updater` | `Verify` | `Started` | Info |
| `Services\ApplicationUpdate\UpdateVerifier.cs:40` | `Updater` | `Verify` | `Skipped` | Info, durationMs, detail `{phase: "NoChecksum"}` |
| `Services\ApplicationUpdate\UpdateVerifier.cs:46` | `Updater` | `Verify` | `Failed` | Info, durationMs, detail `{phase: "FileNotFound"}` |
| `Services\ApplicationUpdate\UpdateVerifier.cs:59` | `Updater` | `Verify` | Succeeded/Failed | durationMs, detail `{phase, retry_count}` |
| `Services\ApplicationUpdate\UpdateVerifier.cs:65` | `Updater` | `Verify` | `Failed` | Error, durationMs, detail `{phase, retry_count}` |
| `Services\LiaServices\Updater.cs:79` | `LIA` | `Install` | `Started` | Info, detail `{phase: "Download"}` |
| `Services\LiaServices\Updater.cs:95` | `LIA` | `Download` | `Started` | Info, detail `{phase: "InstallerAsset"}` |
| `Services\LiaServices\Updater.cs:104` | `LIA` | `Download` | `Failed` | Error, durationMs, detail з ErrorContextExtractor / LiaInstallException |
| `Services\LiaServices\Updater.cs:109` | `LIA` | `Download` | `Succeeded` | durationMs, detail `{phase: "InstallerAsset"}` |
| `Services\LiaServices\Updater.cs:116` | `LIA` | `Download` | `Started` | detail `{phase: "CertificateAsset"}` |
| `Services\LiaServices\Updater.cs:124` | `LIA` | `Download` | `Failed` | Error, durationMs, detail |
| `Services\LiaServices\Updater.cs:129` | `LIA` | `Download` | `Succeeded` | durationMs, detail `{phase: "CertificateAsset"}` |
| `Services\LiaServices\Updater.cs:137` | `LIA` | `Install` | `Started` | detail `{phase: "RunInstallerScript", ...}` |
| `Services\LiaServices\Updater.cs:143` | `LIA` | `Install` | `Succeeded` | durationMs, detail `{phase: "Complete"}` |
| `Services\LiaServices\Updater.cs:151` | `LIA` | `Install` | `Failed` | Error, durationMs, detail |
| `Services\LiaServices\Updater.cs:274` | `LIA` | `RunInstallerScript` | `Started` | detail |
| `Services\LiaServices\Updater.cs:293` | `LIA` | `RunInstallerScript` | `Failed` | Error, durationMs, detail |
| `Services\LiaServices\Updater.cs:308` | `LIA` | `RunInstallerScript` | `Failed` | Error, durationMs, detail |
| `Services\LiaServices\Updater.cs:316` | `LIA` | `RunInstallerScript` | `Failed` | Error, durationMs, detail |
| `Services\LiaServices\Updater.cs:323` | `LIA` | `RunInstallerScript` | `Succeeded` | durationMs, detail |

Додатково: `UpdateEvents.Track` та `LiaEvents.Track` — допоміжні обгортки над `_telemetry.Track`.
`ErrorContextExtractor.Extract(Exception)` заповнює: `ExceptionType`, `ErrorMessage`, `HttpStatus`, `SupabaseCode`, `Hresult`, `Source`, а для `LiaInstallException` також `Detail` з `phase`, `powershell_exit_code`, `installer_type`, `certificate_present`, `certificate_subject`, `certificate_thumbprint`, `activity_id`, `appx_log`, `signal_name`.

---

## 4. C# SQL/Supabase table consumers

### 4.1. `SCLOCVerse` (WPF клієнт)

| C# модель | Supabase таблиця / VIEW | Файл використання | Операції |
|-----------|-------------------------|-------------------|----------|
| `AppInstallation` (`Models\Auth\AppInstallation.cs`) | `public.app_installations` | `Services\Auth\InstallationService.cs:58` | SELECT (Filter install_id), INSERT, UPDATE |
| `UserDiscordGuild` (`Models\Auth\UserDiscordGuild.cs`) | `public.user_discord_guilds` | `Services\Auth\DiscordGuildSyncService.cs:78` | DELETE (Filter user_id), INSERT |
| `TelemetryEvent` (`Models\Observability\TelemetryEvent.cs`) | `public.telemetry_events` | `Services\Observability\TelemetryUploader.cs:70` | INSERT (події-за-подією) |

Інші SQL-взаємодії: `AuthService` використовує `Supabase.Client` для Gotrue OAuth (`SignIn`, `ExchangeCodeForSession`, `SetSession`, `GetUser`) — дані профілю беруться з `session.User` / `auth.users`, але напряму таблицю `auth.users` не читає/не пише.

### 4.2. `SCLOCVerse.ControlCenter` (Blazor Server)

Використовує Npgsql + схему `control_center`. Прямих викликів `.From<>()` / PostgREST немає.

| C# репозиторій | SQL об'єкти / функції | Файл |
|----------------|----------------------|------|
| `ControlCenterRepository` | `control_center.incidents`, `control_center.component_health`, `control_center.release_health`, `control_center.platform_stats`, `control_center.telemetry_events`, `control_center.traces`, `control_center.incident_timeline`, `control_center.incident_notes_view`, `control_center.knowledge_entry_detail`, `control_center.knowledge_coverage`, `control_center.top_missing_knowledge` | `Data\ControlCenterRepository.cs` |
| `ControlCenterRepository` | функції: `public.create_knowledge_from_incident`, `public.can_create_knowledge_for_incident`, `public.update_knowledge_entry`, `public.transition_knowledge`, `public.add_knowledge_reference`, `public.remove_knowledge_reference`, `public.get_knowledge_history`, `public.get_knowledge_current_version`, `public.can_edit_knowledge`, `public.match_knowledge_priority2`, `public.match_knowledge_for_incident`, `public.verify_knowledge_auto`, `public.search_knowledge`, `public.refresh_knowledge_coverage`, `public.transition_incident`, `public.add_incident_note`, `public.assign_incident_owner` | `Data\ControlCenterRepository.cs` |
| `TraceRepository` | `control_center.telemetry_events` | `Data\TraceRepository.cs` |

### 4.3. `SCLOCVerse.Notifier` (Worker)

Використовує Npgsql + таблиці `public.notification_queue`, `public.notification_attempts`, `public.telemetry_incidents`.

| Файл | SQL об'єкти | Операції |
|------|-------------|----------|
| `Dispatcher\NotificationDispatcher.cs:119` | `public.notification_queue` | UPDATE zombie recovery |
| `Dispatcher\NotificationDispatcher.cs:144` | `public.notification_queue` | CTE claim UPDATE … RETURNING |
| `Dispatcher\NotificationDispatcher.cs:188` | `public.telemetry_incidents` | SELECT incident data |
| `Dispatcher\NotificationDispatcher.cs:321` | `public.notification_attempts` | INSERT 'Sending' |
| `Dispatcher\NotificationDispatcher.cs:335` | `public.notification_attempts` | UPDATE статус |
| `Dispatcher\NotificationDispatcher.cs:365` | `public.notification_queue` | UPDATE final status |

---

## 5. Control Center (Blazor Server)

### 5.1. Структура

| Файл | Тип | Призначення |
|------|-----|-------------|
| `Program.cs` | entry point | DI, NpgsqlDataSource |
| `Components\App.razor` | root component | |
| `Components\Routes.razor` | routing | |
| `Components\_Imports.razor` | imports | |
| `Components\Layout\MainLayout.razor` | layout | |
| `Components\Layout\NavMenu.razor` | nav | 6 пунктів |
| `Components\Pages\Home.razor` | page | Overview |
| `Components\Pages\Incidents.razor` | page | Incidents + detail + workflow + knowledge |
| `Components\Pages\Traces.razor` | page | Trace Explorer |
| `Components\Pages\Releases.razor` | page | Release Health |
| `Components\Pages\Knowledge.razor` | page | Knowledge Base |
| `Components\Pages\Settings.razor` | page | placeholder |
| `Data\IControlCenterRepository.cs` | interface | 34 методи |
| `Data\ControlCenterRepository.cs` | repository | SQL через Npgsql |
| `Data\ITraceRepository.cs` | interface | 3 методи |
| `Data\TraceRepository.cs` | repository | SQL через Npgsql |
| `Services\PiiSanitizer.cs` | service | санітайзер |

### 5.2. Використані моделі (C#)

- `OverviewModels.cs`: `ActiveIncident`, `ComponentHealth`, `ReleaseSummary`, `PlatformStats`, `OverviewData`.
- `IncidentModels.cs`: `IncidentDetail`, `KnownSolution`, `KnownSolutionReference`, `KnowledgeDraftInput`, `KnowledgeCreateResult`, `KnowledgeEditInput`, `KnowledgeReferenceInput`, `KnowledgeHistoryEntry`, `KnowledgeCoverage`, `KnowledgeSearchResult`, `MissingKnowledgeEntry`, `KnowledgeAutoVerifyResult`, `TelemetryEventSummary`, `TraceStep`.
- `WorkflowModels.cs`: `IncidentTimelineEntry`, `IncidentNote`.
- `ReleaseModels.cs`: `ReleaseHealth`, `TopFingerprint`.
- `TraceModels.cs`: `TraceEvent`, `TraceSummary`, `TraceSearchFilter`.

### 5.3. Які колонки БД використовує кожна сторінка

#### Home.razor (`/`) — `IControlCenterRepository`

Виклики:
- `GetOverviewDataAsync()` — `OverviewData`.
- `RefreshKnowledgeCoverageAsync()` → `public.refresh_knowledge_coverage()`.
- `GetKnowledgeCoverageAsync()` → `control_center.knowledge_coverage`.
- `GetTopMissingKnowledgeAsync()` → `control_center.top_missing_knowledge`.

Відображаються поля:
- Active Incidents: `IncidentId`, `HighestSeverity`, `Component`, `Signal`, `EventCount`, `AffectedInstalls`, `OpenedAt`, `LastEventAt`, `Status`.
- System Health: `Component`, `Health`, `ActiveIncidents`, `LastEventAt`.
- Latest Releases: `AppVersion`, `SuccessRate`, `Failed`, `ActiveInstalls`.
- Platform Statistics: `Events24h`, `ActiveUsers24h`, `ActiveInstallations`, `OpenIncidents`.
- Knowledge Coverage: `CoveragePct`, `CoveredFingerprints`, `TotalFingerprints`, `UncoveredFingerprints`; missing: `Component`, `Signal`, `TotalEvents`.

#### Incidents.razor (`/incidents`) — `IControlCenterRepository`

Виклики:
- `GetIncidentsAsync(statusFilter)` — `control_center.incidents`.
- `GetIncidentDetailAsync(incidentId)` — `control_center.incidents` + related.
- `CanCreateKnowledgeForIncidentAsync(id)` — `public.can_create_knowledge_for_incident`.
- `TransitionIncidentAsync(...)` — `public.transition_incident`.
- `AssignOwnerAsync(...)` — `public.assign_incident_owner`.
- `AddIncidentNoteAsync(...)` — `public.add_incident_note`.
- `CreateKnowledgeFromIncidentAsync(...)` — `public.create_knowledge_from_incident`.
- `UpdateKnowledgeAsync(...)` — `public.update_knowledge_entry`.
- `TransitionKnowledgeAsync(...)` — `public.transition_knowledge`.
- `AddKnowledgeReferenceAsync(...)` — `public.add_knowledge_reference`.
- `RemoveKnowledgeReferenceAsync(...)` — `public.remove_knowledge_reference`.
- `GetKnowledgeHistoryAsync(...)` — `public.get_knowledge_history`.
- `GetKnowledgeCurrentVersionAsync(...)` — `public.get_knowledge_current_version`.

Відображаються поля інциденту (`IncidentDetail`):
`IncidentId`, `HighestSeverity`, `Status`, `FingerprintKey`, `Release`, `Component`, `Operation`, `Signal`, `EventCount`, `AffectedUsers`, `AffectedInstalls`, `PeakFailurePct`, `OpenedAt`, `LastEventAt`, `ClosedAt`, `Owner`.

RootEvent (`TelemetryEventSummary`): `Component`, `Operation`, `Outcome`, `Signal`, `ErrorMessage`, `Source`, `HttpStatus`, `Hresult`, `SupabaseCode`, `DurationMs`, `OccurredAt`.
Trace (`TraceStep`): `Step`, `Component`, `Operation`, `Outcome`, `Signal`, `OccurredAt`, `ErrorMessage`, `DurationMs`.
RelatedEvents: `OccurredAt`, `Component`, `Signal`, `ErrorMessage`, `DurationMs`.
Timeline: `ChangedAt`, `FromStatus`, `ToStatus`, `ChangedBy`, `Note`.
Notes: `CreatedBy`, `CreatedAt`, `Content`.

KnownSolution: `Priority`, `Title`, `KnownCause`, `Symptoms`, `Workaround`, `PermanentFix`, `FixedVersion`, `Confidence`, `Status`, `AffectedVersions`, `UpdatedAt`, `References` (`Type`, `Url`, `Label`, `ReferenceId`).

#### Traces.razor (`/traces`) — `ITraceRepository`

Виклики:
- `SearchTracesAsync(filter)` → `control_center.telemetry_events`.
- `GetTraceAsync(correlationId)` → `control_center.telemetry_events`.

Відображаються поля пошуку (TraceSummary): `CorrelationId`, `EventCount`, `AppVersion`, `FirstEvent`, `LastEvent`.
Відображаються поля події (TraceEvent): `Id`, `SessionId`, `CorrelationId`, `Step`, `InstallId`, `UserId`, `OccurredAt`, `ReceivedAt`, `AppVersion`, `TelemetryVersion`, `Component`, `Operation`, `Outcome`, `Severity`, `Category`, `Source`, `HttpStatus`, `Hresult`, `SupabaseCode`, `ExceptionType`, `ErrorMessage`, `DurationMs`, `DetailJson`.
Фільтри: `CorrelationId`, `InstallId`, `AppVersion`, `DateFrom`, `DateTo`.

#### Releases.razor (`/releases`) — `IControlCenterRepository`

Виклики:
- `GetReleaseHealthAsync()` → `control_center.release_health_detail`.
- `RefreshKnowledgeCoverageAsync()` → `public.refresh_knowledge_coverage`.
- `GetKnowledgeCoverageAsync()` → `control_center.knowledge_coverage`.

Відображаються поля ReleaseHealth: `AppVersion`, `LastSeen`, `Recommendation`, `RecommendationIcon`, `SuccessRate`, `Succeeded`, `Failed`, `ActiveInstalls`, `ActiveUsers24h`, `NewIncidents`, `CriticalIncidents`, `TopFingerprints` (`Fingerprint`, `Component`, `Severity`, `EventCount`).
Coverage: `CoveragePct`, `CoveredFingerprints`, `TotalFingerprints`, `UncoveredFingerprints`.

#### Knowledge.razor (`/knowledge`) — `IControlCenterRepository`

Виклики:
- `RefreshKnowledgeCoverageAsync()`.
- `GetKnowledgeCoverageAsync()`.
- `GetTopMissingKnowledgeAsync()`.
- `SearchKnowledgeAsync(...)`.
- `RunKnowledgeAutoVerifyAsync()` → `public.verify_knowledge_auto`.

Відображаються поля KnowledgeCoverage: `CoveragePct`, `CoveredFingerprints`, `UncoveredFingerprints`, `TotalFingerprints`.
MissingKnowledgeEntry: `Component`, `Signal`, `FingerprintCount`, `TotalEvents`, `HighestSeverity`, `LastSeen`.
KnowledgeSearchResult: `KnowledgeId`, `Title`, `Component`, `Signal`, `Status`, `Confidence`, `FixedVersion`, `UpdatedAt`, `TotalCount`.
KnowledgeAutoVerifyResult: `KnowledgeId`, `Title`, `Reason`.

#### Settings.razor (`/settings`)

Placeholder — не виконує жодних запитів до БД.

---

## 6. Notifier worker

Розташування: `F:\C#\SCLocalizationUA\SCLOCVerse.Notifier`.

### 6.1. Структура

| Файл | Символ | Призначення |
|------|--------|-------------|
| `Program.cs` | entry point | DI, NpgsqlDataSource, Discord provider |
| `Dispatcher\NotificationDispatcher.cs:47` | `NotificationDispatcher` | BackgroundService |
| `Providers\DiscordNotificationProvider.cs:12` | `DiscordNotificationProvider` | INotificationProvider |

### 6.2. Використані таблиці та колонки

#### `public.notification_queue` — читання + запис

Читає (claim + zombie recovery):
- `id`, `incident_id`, `notification_type`, `provider`, `payload`, `retry_count`, `max_retries`, `status`, `next_attempt_at`, `claimed_at`, `claimed_by`, `created_at`.

Пише:
- `status` (Pending/RetryScheduled → Sending → Delivered/Failed/RetryScheduled),
- `claimed_at`, `claimed_by`,
- `last_attempt_at`,
- `retry_count` (+1),
- `next_attempt_at` (експоненційний backoff),
- `delivered_at`,
- `last_error`,
- `error_message` (через update).

#### `public.telemetry_incidents` — тільки читання

Використані колонки:
- `id`,
- computed `incident_code` (`INC-YYYY-NNNNN`),
- `component`,
- `operation`,
- `signal`,
- `highest_severity`,
- `release`.

#### `public.notification_attempts` — тільки запис

Використані колонки:
- `queue_id`,
- `attempt_no`,
- `provider`,
- `status` (Sending / Delivered / Failed),
- `http_status`,
- `provider_message_id`,
- `error_message`,
- `started_at`,
- `finished_at`.

### 6.3. Payload fields (з `notification_queue.payload` jsonb)

Читає та використовує: `version`, `affected_installs`, `affected_users`, `failure_pct`, `severity`.
Доповнює з `telemetry_incidents`: `IncidentId`, `IncidentCode`, `Component`, `Operation`, `Signal`, `Severity`, `Release`, `NotificationType`.

### 6.4. Discord embed fields

Відправляє: `Component`, `Operation`, `Signal`, `Severity`, `Release`, `FailurePct`, `AffectedInstalls`, `AffectedUsers`, `IncidentCode`, `NotificationType`, `Version`.

---

## 7. Settings / файли налаштувань

### 7.1. `SCLOCVerse\Properties\Settings.settings`

Розташування: `F:\C#\SCLocalizationUA\SCLOCVerse\Settings.settings`.

| Setting | Type | Scope | Default |
|---------|------|-------|---------|
| `LastAppVersion` | `System.String` | User | (порожній) |
| `GameFolder` | `System.String` | User | (порожній) |
| `UpdateChannel` | `System.String` | User | `Stable` |
| `UpgradeRequired` | `System.Boolean` | User | `False` |
| `HangarOverlayX` | `System.Double` | User | `20` |
| `HangarOverlayY` | `System.Double` | User | `20` |
| `HangarOverlayScale` | `System.Double` | User | `0.6` |
| `HangarOverlayOpacity` | `System.Double` | User | `0.92` |
| `HangarCycleStartOverride` | `System.Int64` | User | `0` |
| `InputSystemBackend` | `System.String` | User | `RawInput` |
| `InputSystemDiagnostics` | `System.Boolean` | User | `False` |
| `RunAtStartup` | `System.Boolean` | User | `False` |
| `MinimizeToTray` | `System.Boolean` | User | `True` |
| `AutoUpdateLocalization` | `System.Boolean` | User | `False` |
| `LastLocalizationToast` | `System.String` | User | (порожній) |
| `LastLiaToast` | `System.String` | User | (порожній) |
| `LastAppToast` | `System.String` | User | (порожній) |
| `LastToastTimestampUtc` | `System.String` | User | (порожній) |

### 7.2. `SCLOCVerse\Settings.Designer.cs`

Розташування: `F:\C#\SCLocalizationUA\SCLOCVerse\Settings.Designer.cs`.
Всі 18 налаштувань згенеровані як властивості класу `SCLOCVerse.Settings` (User-scoped, ApplicationSettingsBase).
Примітка у файлі (рядки 11–16): додаткові поля (`RunAtStartup`, `MinimizeToTray`, `AutoUpdateLocalization`, toast-поля) додані вручну через CLI MSBuild.

### 7.3. Environment variables

Використовуються у C#:
- `SCLOCVERSE_SUPABASE_URL` → `AppCompositionRoot.cs:240`.
- `SCLOCVERSE_SUPABASE_ANON_KEY` → `AppCompositionRoot.cs:253`.
- `SCLOCVERSE_TELEMETRY_DISABLED` → `AppCompositionRoot.cs:268` (kill-switch).
- `SCLOC_CC_READONLY_DB` → `SCLOCVerse.ControlCenter\Program.cs:16`.
- `SCLOC_NOTIFIER_DB` → `SCLOCVerse.Notifier\Program.cs:16`.
- `SCLOC_DISCORD_WEBHOOK` → `SCLOCVerse.Notifier\Program.cs:27`.

### 7.4. Supabase config

`SCLOCVerse\Properties\SupabaseConfig.cs` — fallback для URL/anon key, але вміст цього файлу не прочитано в рамках звіту.

---

## 8. Knowledge engine — мапінг колонок

### 8.1. SQL → C# моделі Control Center

| SQL колонка (public.knowledge_entries) | C# властивість (`KnownSolution` / `KnowledgeSearchResult`) | Примітка |
|----------------------------------------|-------------------------------------------------------------|----------|
| `id` | `KnowledgeId` | long |
| `fingerprint_key` | не відображається напряму (для пошуку) | |
| `component` | `Component` | |
| `operation` | — | не в C# моделі KnownSolution |
| `signal` | `Signal` | |
| `title` | `Title` | |
| `symptoms` | `Symptoms` | |
| `known_cause` | `KnownCause` | |
| `workaround` | `Workaround` | |
| `permanent_fix` | `PermanentFix` | |
| `affected_versions` | `AffectedVersions` | text (array_to_string) |
| `fixed_version` | `FixedVersion` | |
| `confidence` | `Confidence` | |
| `status` | `Status` | |
| `created_by` | не відображається | |
| `created_at` | не відображається | |
| `updated_by` | `KnowledgeSearchResult.UpdatedBy` | |
| `updated_at` | `UpdatedAt` | |

### 8.2. SQL → C# моделі Knowledge reference

| SQL колонка | C# властивість (`KnownSolutionReference`) |
|-------------|-------------------------------------------|
| `id` | `ReferenceId` |
| `reference_type` | `Type` |
| `url` | `Url` |
| `label` | `Label` |

### 8.3. SQL → C# моделі Knowledge history

| SQL колонка | C# властивість (`KnowledgeHistoryEntry`) |
|-------------|------------------------------------------|
| `version` | `Version` |
| `changed_by` | `ChangedBy` |
| `db_user` | `DbUser` |
| `changed_at` | `ChangedAt` |
| `change_reason` | `ChangeReason` |
| `change_type` | `ChangeType` |

### 8.4. Knowledge coverage

| SQL (control_center.knowledge_coverage) | C# властивість (`KnowledgeCoverage`) |
|-----------------------------------------|--------------------------------------|
| `total_fingerprints` | `TotalFingerprints` |
| `covered_fingerprints` | `CoveredFingerprints` |
| `uncovered_fingerprints` | `UncoveredFingerprints` |
| `coverage_pct` | `CoveragePct` |

### 8.5. Top missing knowledge

| SQL (control_center.top_missing_knowledge) | C# властивість (`MissingKnowledgeEntry`) |
|----------------------------------------------|------------------------------------------|
| `component` | `Component` |
| `signal` | `Signal` |
| `fingerprint_count` | `FingerprintCount` |
| `total_events` | `TotalEvents` |
| `last_seen` | `LastSeen` |
| `highest_severity` | `HighestSeverity` |

---

## 9. Dead / непрочитані колонки

| Таблиця | Колонка | Чи пишеться C# | Чи читається C# / VIEW | Примітка |
|---------|---------|----------------|------------------------|----------|
| `app_installations` | `localization_version` | ні | ні | dead у клієнті |
| `app_installations` | `country` | ні (тригер) | через `control_center.users/installations` | dead для C# writer |
| `app_installations` | `os_build` | ні | ні | dead у клієнті |
| `app_installations` | `update_channel` | ні | через VIEW | dead у C# writer |
| `app_installations` | `install_source` | ні | через VIEW | dead у C# writer |
| `app_installations` | `game_folder_path` | ні | через VIEW | dead у C# writer |
| `app_installations` | `selected_environment` | ні | через VIEW | dead у C# writer |
| `telemetry_events` | `country` | ні (C# модель має, але BuildEvent не встановлює) | так, VIEW | мертва у продуценті |
| `telemetry_events` | `git_commit` | так (null) | так | завжди null |
| `telemetry_events` | `category` | так (`Operational`) | так | завжди Operational |
| `error_reports` | всі | ні | через `control_center.errors` | "Reserved for future" |
| `user_discord_guilds` | всі | синхронізація вимкнена (identify scope) | ні | таблиця порожня |
| `notification_queue` | `provider` | Default 'Discord' | так | інші канали не реалізовано |
| `notification_queue` | `notification_type` | 'IncidentCreated' | так | інші типи не створюються |

---

## 10. Ролі PostgreSQL / Supabase

| Роль | Призначення | Гранти |
|------|-------------|--------|
| `authenticated` | кінцевий користувач WPF | SELECT+INSERT `app_installations`, `user_discord_guilds`; SELECT+INSERT `telemetry_events` |
| `anon` | неавторизований | deny-all на всіх таблицях |
| `cc_readonly` | Control Center Blazor | USAGE+SELECT на `control_center`; EXECUTE на багатьох функцій; SELECT на `knowledge_*` |
| `cc_notifier` | Notifier Worker | SELECT/INSERT/UPDATE `notification_queue`, `notification_attempts`; SELECT `telemetry_incidents`; USAGE sequences |

---

## 11. Тригери

| Тригер | Таблиця | Подія | Функція |
|--------|---------|-------|---------|
| `trg_app_installations_set_country` | `public.app_installations` | BEFORE INSERT OR UPDATE | `public.set_country_from_cf()` |
| `knowledge_audit` | `public.knowledge_entries` | AFTER INSERT OR UPDATE | `public.knowledge_audit_trigger()` |
| `trg_telemetry_failed_promote` | `public.telemetry_events` | AFTER INSERT WHEN outcome='Failed' | `public.tg_promote_after_failed()` |
| `trg_incident_refresh_coverage` | `public.telemetry_incidents` | AFTER INSERT OR UPDATE (STATEMENT) | `public.tg_incident_refresh_coverage()` |

---

## 12. Підсумкова матриця: хто що пише / читає

| Об'єкт | Пише | Читає | Автоматизація |
|--------|------|-------|--------------|
| `public.app_installations` | WPF `InstallationService` | `public.user_analytics` → CC VIEW | тригер country |
| `public.telemetry_events` | WPF `TelemetryUploader` | CC VIEW, candidates, traces | тригер promote_failed |
| `public.telemetry_incidents` | SQL `promote_incident_candidates_for_event()` | CC VIEW | авто-close, knowledge refresh |
| `public.notification_queue` | SQL promote function | Notifier Worker | zombie recovery, retry |
| `public.notification_attempts` | Notifier Worker | not found | — |
| `public.knowledge_entries` | SQL authoring functions | CC VIEW | audit trigger |
| `public.knowledge_references` | SQL reference functions | CC VIEW (jsonb_agg) | audit trigger |
| `public.knowledge_version_history` | SQL audit trigger | CC function `get_knowledge_history` | — |
| `control_center.knowledge_coverage` | SQL `refresh_knowledge_coverage()` | CC pages | тригер incidents |
| `public.incident_policy` | not found | SQL promote function | — |
| `public.error_reports` | not found | CC `control_center.errors` | "Reserved for future" |
| `public.admin_audit_log` | not found | not found | — |

---

Кінець форензик-звіту.
