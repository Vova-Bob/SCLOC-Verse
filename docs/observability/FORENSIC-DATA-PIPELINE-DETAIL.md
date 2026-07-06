# Детальний форензик-звіт: data pipeline SCLOC-Verse

Дата збору: 2026-07-06.
Мета: точні джерела, CREATE-тексти, C# виклики `.Track()` з рядками, SQL репозиторіїв та Notifier.
Формат: факти, шляхи, рядки, імена об'єктів, імена колонок, CREATE-тексти.

---

## 1. Міграції Supabase: призначення та CREATE-тексти

Розташування: `F:\C#\SCLocalizationUA\supabase\migrations`.

### 1.1. 20260630000001_create_app_installations.sql

**Призначення:** створення таблиці `public.app_installations` — єдиного джерела метаданих інсталяції SCLOC-Verse. Зв'язок з `auth.users` через `user_id` (ON DELETE SET NULL). Унікальність за `install_id`.

**CREATE TABLE:**

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

**Обмеження / індекси / RLS (повний текст):**

```sql
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'app_installations_install_id_key'
          AND conrelid = 'public.app_installations'::regclass
    ) THEN
        ALTER TABLE public.app_installations
            ADD CONSTRAINT app_installations_install_id_key UNIQUE (install_id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_app_installations_user_id
    ON public.app_installations(user_id);
CREATE INDEX IF NOT EXISTS idx_app_installations_install_id
    ON public.app_installations(install_id);
CREATE INDEX IF NOT EXISTS idx_app_installations_machine_id
    ON public.app_installations(machine_id);
CREATE INDEX IF NOT EXISTS idx_app_installations_last_seen
    ON public.app_installations(last_seen);

ALTER TABLE public.app_installations ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "deny all anon on app_installations" ON public.app_installations;
CREATE POLICY "deny all anon on app_installations"
    ON public.app_installations AS RESTRICTIVE
    FOR ALL TO anon
    USING (false)
    WITH CHECK (false);

DROP POLICY IF EXISTS "Users can view own app_installations" ON public.app_installations;
DROP POLICY IF EXISTS "Users can insert own app_installations" ON public.app_installations;
DROP POLICY IF EXISTS "Users can update own app_installations" ON public.app_installations;
DROP POLICY IF EXISTS "Users can delete own app_installations" ON public.app_installations;

CREATE POLICY "Users can view own app_installations"
    ON public.app_installations
    FOR SELECT TO authenticated
    USING (user_id = auth.uid());

CREATE POLICY "Users can insert own app_installations"
    ON public.app_installations
    FOR INSERT TO authenticated
    WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users can update own app_installations"
    ON public.app_installations
    FOR UPDATE TO authenticated
    USING (user_id = auth.uid())
    WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users can delete own app_installations"
    ON public.app_installations
    FOR DELETE TO authenticated
    USING (user_id = auth.uid());

GRANT SELECT, INSERT, UPDATE, DELETE ON public.app_installations TO authenticated;
GRANT SELECT ON public.app_installations TO anon;
```

---

### 1.2. 20260630000009_create_telemetry_events.sql

**Призначення:** єдина append-only таблиця спостережуваності `public.telemetry_events`. Контракт: тільки INSERT, `received_at` DEFAULT now(), `client_event_id` UNIQUE для дедуплікації, CHECK outcome/severity/category, CHECK failed_has_signal. RLS owner-only.

**CREATE TABLE:**

```sql
CREATE TABLE IF NOT EXISTS public.telemetry_events (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    client_event_id   uuid NOT NULL,
    session_id        uuid NOT NULL,
    correlation_id    uuid NOT NULL,
    step              int  NOT NULL,
    install_id        text REFERENCES public.app_installations(install_id) ON DELETE SET NULL,
    user_id           uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    occurred_at       timestamptz NOT NULL,
    received_at       timestamptz NOT NULL DEFAULT now(),
    app_version       text NOT NULL,
    git_commit        text,
    channel           text NOT NULL DEFAULT 'stable',
    telemetry_version int  NOT NULL DEFAULT 1,
    os_version        text,
    country           text,
    component         text NOT NULL,
    operation         text NOT NULL,
    outcome           text NOT NULL,
    severity          text NOT NULL DEFAULT 'Info',
    category          text NOT NULL DEFAULT 'Operational',
    source            text,
    http_status       int,
    hresult           text,
    supabase_code     text,
    exception_type    text,
    error_message     text,
    duration_ms       int,
    detail            jsonb,
    CONSTRAINT chk_telemetry_outcome  CHECK (outcome  IN ('Started','Succeeded','Failed','Cancelled','Skipped')),
    CONSTRAINT chk_telemetry_severity CHECK (severity IN ('Info','Warning','Error','Critical','Crash')),
    CONSTRAINT chk_telemetry_category CHECK (category IN ('Critical','Operational','Diagnostic','Analytics')),
    CONSTRAINT chk_telemetry_failed_has_signal CHECK (
        outcome <> 'Failed'
        OR COALESCE(source, hresult, supabase_code, http_status::text, exception_type) IS NOT NULL
    )
);
```

**Індекси / RLS (повний текст):**

```sql
CREATE UNIQUE INDEX IF NOT EXISTS uniq_telemetry_client_event_id
    ON public.telemetry_events(client_event_id);
CREATE INDEX IF NOT EXISTS idx_telemetry_received
    ON public.telemetry_events(received_at DESC);
CREATE INDEX IF NOT EXISTS idx_telemetry_trace
    ON public.telemetry_events(correlation_id, step);
CREATE INDEX IF NOT EXISTS idx_telemetry_detect
    ON public.telemetry_events(component, operation, outcome, received_at DESC);

ALTER TABLE public.telemetry_events ENABLE ROW LEVEL SECURITY;

REVOKE ALL ON public.telemetry_events FROM anon;
REVOKE ALL ON public.telemetry_events FROM authenticated;
GRANT SELECT, INSERT ON public.telemetry_events TO authenticated;

CREATE POLICY "deny all anon on telemetry_events"
    ON public.telemetry_events AS RESTRICTIVE
    FOR ALL TO anon
    USING (false) WITH CHECK (false);

CREATE POLICY "auth select own telemetry_events"
    ON public.telemetry_events
    FOR SELECT TO authenticated
    USING (user_id = auth.uid());

CREATE POLICY "auth insert own telemetry_events"
    ON public.telemetry_events
    FOR INSERT TO authenticated
    WITH CHECK (user_id = auth.uid());
```

---

### 1.3. 20260630000012_incident_tables.sql

**Призначення:** Phase 4 — Incident Engine. Таблиці `public.telemetry_incidents` (життя інцидентів), `public.incident_policy` (per-component пороги), seed-записи політик.

**CREATE TABLE telemetry_incidents:**

```sql
CREATE TABLE IF NOT EXISTS public.telemetry_incidents (
    id                    bigserial PRIMARY KEY,
    fingerprint_key       text NOT NULL,
    fingerprint_hash      text NOT NULL,
    release               text NOT NULL,
    component             text NOT NULL,
    operation             text NOT NULL,
    signal                text NOT NULL,
    root_event_id         uuid REFERENCES public.telemetry_events(id) ON DELETE SET NULL,
    last_event_id         uuid REFERENCES public.telemetry_events(id) ON DELETE SET NULL,
    opened_at             timestamptz NOT NULL DEFAULT now(),
    last_event_at         timestamptz NOT NULL DEFAULT now(),
    closed_at             timestamptz,
    status                text NOT NULL DEFAULT 'Active',
    highest_severity      text NOT NULL DEFAULT 'Warning',
    peak_failure_pct      numeric(5,1),
    affected_users        int NOT NULL DEFAULT 0,
    affected_installs     int NOT NULL DEFAULT 0,
    event_count           bigint NOT NULL DEFAULT 0,
    CONSTRAINT chk_incident_status CHECK (status IN ('Active','Confirmed','Investigating','Resolved','Closed')),
    CONSTRAINT chk_incident_severity CHECK (highest_severity IN ('Critical','Warning'))
);

CREATE INDEX IF NOT EXISTS idx_incidents_fp_open
    ON public.telemetry_incidents(fingerprint_hash) WHERE status != 'Closed';
CREATE INDEX IF NOT EXISTS idx_incidents_opened
    ON public.telemetry_incidents(opened_at DESC);

ALTER TABLE public.telemetry_incidents ENABLE ROW LEVEL SECURITY;
CREATE POLICY "deny all on telemetry_incidents"
    ON public.telemetry_incidents AS RESTRICTIVE FOR ALL TO anon, authenticated
    USING (false) WITH CHECK (false);
```

**CREATE TABLE incident_policy:**

```sql
CREATE TABLE IF NOT EXISTS public.incident_policy (
    component                    text NOT NULL,
    operation                    text NOT NULL DEFAULT '_',
    signal                       text NOT NULL DEFAULT '_',
    min_sample                   int NOT NULL DEFAULT 5,
    failure_threshold_pct        numeric(5,1) NOT NULL DEFAULT 20.0,
    critical_affected_threshold  int NOT NULL DEFAULT 10,
    auto_close_after_minutes     int NOT NULL DEFAULT 60,
    enabled                      boolean NOT NULL DEFAULT true,
    PRIMARY KEY (component, operation, signal)
);

ALTER TABLE public.incident_policy ENABLE ROW LEVEL SECURITY;
CREATE POLICY "deny all on incident_policy"
    ON public.incident_policy AS RESTRICTIVE FOR ALL TO anon, authenticated
    USING (false) WITH CHECK (false);
```

**Seed (повний):**

```sql
INSERT INTO public.incident_policy (component) VALUES ('_default')
    ON CONFLICT DO NOTHING;
INSERT INTO public.incident_policy (component, failure_threshold_pct, critical_affected_threshold)
VALUES ('Installation', 20.0, 5) ON CONFLICT DO NOTHING;
INSERT INTO public.incident_policy (component, failure_threshold_pct, critical_affected_threshold)
VALUES ('Auth', 40.0, 10) ON CONFLICT DO NOTHING;
INSERT INTO public.incident_policy (component, failure_threshold_pct, min_sample, critical_affected_threshold)
VALUES ('Updater', 5.0, 3, 5) ON CONFLICT DO NOTHING;
INSERT INTO public.incident_policy (component, failure_threshold_pct, min_sample, critical_affected_threshold)
VALUES ('LIA', 5.0, 1, 2) ON CONFLICT DO NOTHING;
```

---

### 1.4. 20260630000016_notification_engine.sql

**Призначення:** Phase 5b — Notification Engine. Таблиця черги `public.notification_queue`, VIEW `control_center.notifications`, оновлення `public.promote_incident_candidates()` з авто-close та enqueue `IncidentCreated`.

**CREATE TABLE notification_queue:**

```sql
CREATE TABLE IF NOT EXISTS public.notification_queue (
    id                bigserial PRIMARY KEY,
    incident_id       bigint NOT NULL REFERENCES public.telemetry_incidents(id) ON DELETE CASCADE,
    notification_type text NOT NULL,
    provider          text NOT NULL DEFAULT 'Discord',
    status            text NOT NULL DEFAULT 'Pending',
    payload           jsonb,
    retry_count       int NOT NULL DEFAULT 0,
    last_attempt_at   timestamptz,
    delivered_at      timestamptz,
    error_message     text,
    created_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_notif_status CHECK (status IN ('Pending','Delivered','Failed')),
    CONSTRAINT chk_notif_type CHECK (notification_type IN ('IncidentCreated','IncidentEscalated','IncidentClosed'))
);

CREATE UNIQUE INDEX IF NOT EXISTS uniq_notification_dedup
    ON public.notification_queue(incident_id, notification_type)
    WHERE status != 'Failed';

CREATE INDEX IF NOT EXISTS idx_notif_pending
    ON public.notification_queue(created_at)
    WHERE status = 'Pending';

ALTER TABLE public.notification_queue ENABLE ROW LEVEL SECURITY;
CREATE POLICY "deny all on notification_queue"
    ON public.notification_queue AS RESTRICTIVE FOR ALL TO anon, authenticated, cc_readonly
    USING (false) WITH CHECK (false);
```

**CREATE VIEW control_center.notifications:**

```sql
CREATE OR REPLACE VIEW control_center.notifications AS
SELECT n.id, n.incident_id, n.notification_type, n.provider, n.status,
       n.retry_count, n.last_attempt_at, n.delivered_at, n.error_message,
       n.created_at,
       'INC-' || to_char(i.opened_at,'YYYY') || '-' || lpad(i.id::text,5,'0') AS incident_code,
       i.component, i.signal, i.highest_severity
FROM public.notification_queue n
JOIN public.telemetry_incidents i ON i.id = n.incident_id
ORDER BY n.created_at DESC;
```

**CREATE OR REPLACE FUNCTION public.promote_incident_candidates():**

```sql
CREATE OR REPLACE FUNCTION public.promote_incident_candidates()
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    cand RECORD;
    existing_id bigint;
    root_id uuid;
    last_id uuid;
    pol RECORD;
    new_sev text;
    new_incident_id bigint;
BEGIN
    UPDATE public.telemetry_incidents i
    SET status = 'Closed', closed_at = now()
    WHERE i.status != 'Closed'
      AND i.last_event_at < now() - (
        COALESCE(
          (SELECT auto_close_after_minutes FROM public.incident_policy p
           WHERE p.component = i.component AND p.enabled
           ORDER BY (p.signal = '_') DESC, (p.operation = '_') DESC LIMIT 1),
          (SELECT auto_close_after_minutes FROM public.incident_policy WHERE component = '_default' LIMIT 1),
          60
        ) || ' minutes'
      )::interval;

    FOR cand IN SELECT * FROM control_center.incident_candidates_live LOOP
        SELECT * INTO pol FROM public.incident_policy
        WHERE component = cand.component AND enabled
        ORDER BY (signal = '_') DESC, (operation = '_') DESC LIMIT 1;
        IF NOT FOUND THEN
            SELECT * INTO pol FROM public.incident_policy WHERE component = '_default' AND enabled LIMIT 1;
        END IF;
        IF NOT FOUND THEN CONTINUE; END IF;
        IF cand.failed_now < pol.min_sample THEN CONTINUE; END IF;
        IF cand.failure_pct < pol.failure_threshold_pct THEN CONTINUE; END IF;

        new_sev := CASE WHEN cand.affected_installs >= pol.critical_affected_threshold THEN 'Critical' ELSE 'Warning' END;

        SELECT id INTO existing_id FROM public.telemetry_incidents
        WHERE fingerprint_key = cand.fingerprint_key AND status != 'Closed' LIMIT 1;

        IF existing_id IS NOT NULL THEN
            SELECT id INTO last_id FROM public.telemetry_events
            WHERE component = cand.component AND operation = cand.operation
              AND COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') = cand.signal
              AND app_version = cand.release AND outcome = 'Failed'
            ORDER BY received_at DESC LIMIT 1;
            UPDATE public.telemetry_incidents SET
                last_event_at = now(), last_event_id = last_id,
                affected_installs = GREATEST(affected_installs, cand.affected_installs),
                affected_users = GREATEST(affected_users, cand.affected_users),
                peak_failure_pct = GREATEST(peak_failure_pct, cand.failure_pct),
                event_count = event_count + cand.failed_now,
                highest_severity = CASE WHEN new_sev = 'Critical' OR highest_severity = 'Critical' THEN 'Critical' ELSE 'Warning' END
            WHERE id = existing_id;
        ELSE
            SELECT id INTO root_id FROM public.telemetry_events
            WHERE component = cand.component AND operation = cand.operation
              AND COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') = cand.signal
              AND app_version = cand.release AND outcome = 'Failed'
            ORDER BY received_at ASC LIMIT 1;
            SELECT id INTO last_id FROM public.telemetry_events
            WHERE component = cand.component AND operation = cand.operation
              AND COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') = cand.signal
              AND app_version = cand.release AND outcome = 'Failed'
            ORDER BY received_at DESC LIMIT 1;

            INSERT INTO public.telemetry_incidents
                (fingerprint_key, fingerprint_hash, release, component, operation, signal,
                 root_event_id, last_event_id, opened_at, last_event_at,
                 status, highest_severity, peak_failure_pct,
                 affected_users, affected_installs, event_count)
            VALUES
                (cand.fingerprint_key, cand.fingerprint_hash, cand.release,
                 cand.component, cand.operation, cand.signal,
                 root_id, last_id, now(), now(),
                 'Active', new_sev, cand.failure_pct,
                 cand.affected_users, cand.affected_installs, cand.failed_now)
            RETURNING id INTO new_incident_id;

            INSERT INTO public.notification_queue (incident_id, notification_type, provider, payload)
            VALUES (new_incident_id, 'IncidentCreated', 'Discord',
                    jsonb_build_object(
                        'incident_id', new_incident_id,
                        'component', cand.component,
                        'operation', cand.operation,
                        'signal', cand.signal,
                        'severity', new_sev,
                        'release', cand.release,
                        'affected_installs', cand.affected_installs,
                        'affected_users', cand.affected_users,
                        'failure_pct', cand.failure_pct
                    ))
            ON CONFLICT DO NOTHING;
        END IF;
    END LOOP;
END;
$$;
```

---

### 1.5. 20260704000017_notification_audit_and_retry.sql

**Призначення:** додавання audit-таблиці спроб доставки `public.notification_attempts`, розширення `notification_queue` колонками retry/zombie, оновлення CHECK-статусів та VIEW.

**ALTER TABLE notification_queue:**

```sql
ALTER TABLE public.notification_queue
    DROP CONSTRAINT chk_notif_status;
ALTER TABLE public.notification_queue
    ADD CONSTRAINT chk_notif_status CHECK (status IN
        ('Pending','Sending','Delivered','Failed','RetryScheduled'));

ALTER TABLE public.notification_queue
    ADD COLUMN IF NOT EXISTS next_attempt_at timestamptz,
    ADD COLUMN IF NOT EXISTS max_retries int NOT NULL DEFAULT 3,
    ADD COLUMN IF NOT EXISTS claimed_at timestamptz,
    ADD COLUMN IF NOT EXISTS claimed_by text,
    ADD COLUMN IF NOT EXISTS last_error text;
```

**CREATE TABLE notification_attempts:**

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

ALTER TABLE public.notification_attempts ENABLE ROW LEVEL SECURITY;
CREATE POLICY "deny all on notification_attempts" ON public.notification_attempts
    AS RESTRICTIVE FOR ALL TO anon, authenticated, cc_readonly USING (false) WITH CHECK (false);
```

**CREATE VIEW control_center.notifications (оновлений):**

```sql
DROP VIEW IF EXISTS control_center.notifications;
CREATE VIEW control_center.notifications AS
SELECT n.id, n.incident_id, n.notification_type, n.provider, n.status,
       n.retry_count, n.max_retries, n.last_attempt_at, n.next_attempt_at,
       n.claimed_at, n.claimed_by, n.last_error,
       n.delivered_at, n.error_message, n.created_at,
       'INC-' || to_char(i.opened_at,'YYYY') || '-' || lpad(i.id::text,5,'0') AS incident_code,
       i.component, i.signal, i.highest_severity, i.release
FROM public.notification_queue n
JOIN public.telemetry_incidents i ON i.id = n.incident_id
ORDER BY n.created_at DESC;
```

---

### 1.6. 20260704000018_cc_notifier_role.sql

**Призначення:** створення ролі `cc_notifier` для Notifier Worker з мінімальними правами на `notification_queue`, `notification_attempts`, `telemetry_incidents` та sequences.

**CREATE ROLE / GRANT / RLS:**

```sql
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cc_notifier') THEN
        CREATE ROLE cc_notifier WITH LOGIN NOCREATEDB NOCREATEROLE NOSUPERUSER;
    END IF;
END $$;

GRANT USAGE ON SCHEMA public TO cc_notifier;
GRANT SELECT, INSERT, UPDATE ON public.notification_queue TO cc_notifier;
GRANT SELECT, INSERT, UPDATE ON public.notification_attempts TO cc_notifier;
GRANT SELECT ON public.telemetry_incidents TO cc_notifier;

GRANT USAGE, SELECT ON SEQUENCE public.notification_attempts_id_seq TO cc_notifier;
GRANT USAGE, SELECT ON SEQUENCE public.notification_queue_id_seq TO cc_notifier;

DROP POLICY IF EXISTS "allow cc_notifier notification_queue" ON public.notification_queue;
CREATE POLICY "allow cc_notifier notification_queue" ON public.notification_queue
    FOR ALL TO cc_notifier USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS "allow cc_notifier notification_attempts" ON public.notification_attempts;
CREATE POLICY "allow cc_notifier notification_attempts" ON public.notification_attempts
    FOR ALL TO cc_notifier USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS "allow cc_notifier read incidents" ON public.telemetry_incidents;
CREATE POLICY "allow cc_notifier read incidents" ON public.telemetry_incidents
    FOR SELECT TO cc_notifier USING (true);
```

---

### 1.7. 20260704000019_knowledge_engine_core.sql

**Призначення:** Knowledge Engine Slice 1 — core tables (`knowledge_entries`, `knowledge_references`, `knowledge_version_history`), audit trigger, VIEW `control_center.knowledge_entry_detail`, функція `match_knowledge_for_incident(bigint)`.

**CREATE TABLE knowledge_entries:**

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
        (status = 'Verified'   AND confidence IN ('High','Verified')) OR
        (status IN ('Draft','Reviewed') AND confidence IN ('Low','Medium','High')) OR
        (status IN ('Deprecated','Archived'))
    )
);
```

**CREATE TABLE knowledge_references:**

```sql
CREATE TABLE IF NOT EXISTS public.knowledge_references (
    id bigserial PRIMARY KEY,
    knowledge_id bigint NOT NULL REFERENCES public.knowledge_entries(id) ON DELETE RESTRICT,
    reference_type text NOT NULL,
    url text,
    label text,
    CONSTRAINT chk_knowledge_ref_type CHECK (reference_type IN ('GitCommit','GitHubIssue','Documentation','External'))
);
```

**CREATE TABLE knowledge_version_history:**

```sql
CREATE TABLE IF NOT EXISTS public.knowledge_version_history (
    id bigserial PRIMARY KEY,
    knowledge_id bigint NOT NULL REFERENCES public.knowledge_entries(id) ON DELETE RESTRICT,
    version int NOT NULL,
    snapshot jsonb NOT NULL,
    changed_by text,
    db_user text NOT NULL DEFAULT current_user,
    changed_at timestamptz NOT NULL DEFAULT now(),
    change_reason text
);
```

**CREATE FUNCTION knowledge_audit_trigger():**

```sql
CREATE OR REPLACE FUNCTION public.knowledge_audit_trigger()
RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE next_ver int;
BEGIN
    SELECT COALESCE(MAX(version), 0) + 1 INTO next_ver
    FROM public.knowledge_version_history
    WHERE knowledge_id = NEW.id;

    INSERT INTO public.knowledge_version_history
        (knowledge_id, version, snapshot, changed_by, change_reason, db_user)
    VALUES
        (NEW.id, next_ver, to_jsonb(NEW), COALESCE(NEW.updated_by, NEW.created_by), 'auto-audit', current_user);

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS knowledge_audit ON public.knowledge_entries;
CREATE TRIGGER knowledge_audit
    AFTER INSERT OR UPDATE ON public.knowledge_entries
    FOR EACH ROW EXECUTE FUNCTION public.knowledge_audit_trigger();
```

**CREATE VIEW control_center.knowledge_entry_detail:**

```sql
DROP VIEW IF EXISTS control_center.knowledge_entry_detail;
CREATE VIEW control_center.knowledge_entry_detail AS
SELECT
    ke.id,
    ke.fingerprint_key,
    ke.fingerprint_hash,
    ke.component,
    ke.operation,
    ke.signal,
    ke.title,
    ke.symptoms,
    ke.known_cause,
    ke.workaround,
    ke.permanent_fix,
    ke.affected_versions,
    ke.fixed_version,
    ke.confidence,
    ke.status,
    ke.created_by,
    ke.created_at,
    ke.updated_by,
    ke.updated_at,
    COALESCE(jsonb_agg(
        jsonb_build_object('type', kr.reference_type, 'url', kr.url, 'label', kr.label)
        ORDER BY kr.id
    ) FILTER (WHERE kr.id IS NOT NULL), '[]'::jsonb) AS references
FROM public.knowledge_entries ke
LEFT JOIN public.knowledge_references kr ON kr.knowledge_id = ke.id
GROUP BY ke.id;
```

**CREATE FUNCTION match_knowledge_for_incident(bigint):**

```sql
CREATE OR REPLACE FUNCTION public.match_knowledge_for_incident(p_incident_id bigint)
RETURNS TABLE (knowledge_id bigint, priority int) LANGUAGE plpgsql SECURITY DEFINER AS $$
BEGIN
    RETURN QUERY
    SELECT ke.id, 1
    FROM public.knowledge_entries ke
    JOIN public.telemetry_incidents i ON i.fingerprint_hash = ke.fingerprint_hash
    WHERE i.id = p_incident_id
      AND ke.status = 'Verified'
    ORDER BY ke.updated_at DESC, ke.id DESC
    LIMIT 1;
END;
$$;
```

---

### 1.8. 20260704000022_knowledge_workflow.sql

**Призначення:** Knowledge Engine Slice 3.5/4 — workflow, change_type, розширення reference_type, оновлені функції `update_knowledge_entry`, `transition_knowledge`, `add_knowledge_reference`, `remove_knowledge_reference`, `get_knowledge_history`, `match_knowledge_priority2`, допоміжні `set_knowledge_change_context`, `require_not_archived`.

**ALTER TABLE knowledge_version_history:**

```sql
ALTER TABLE public.knowledge_version_history
    ADD COLUMN IF NOT EXISTS change_type text NOT NULL DEFAULT 'Updated';
```

**ALTER TABLE knowledge_references CHECK:**

```sql
ALTER TABLE public.knowledge_references
    DROP CONSTRAINT IF EXISTS chk_knowledge_ref_type;

ALTER TABLE public.knowledge_references
    ADD CONSTRAINT chk_knowledge_ref_type CHECK (reference_type IN ('GitCommit','GitHubIssue','Documentation','ReleaseNotes','External'));
```

**CREATE OR REPLACE FUNCTION knowledge_audit_trigger():**

```sql
CREATE OR REPLACE FUNCTION public.knowledge_audit_trigger()
RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    next_ver int;
    v_change_type text;
BEGIN
    SELECT COALESCE(MAX(version), 0) + 1 INTO next_ver
    FROM public.knowledge_version_history
    WHERE knowledge_id = NEW.id;

    BEGIN
        v_change_type := current_setting('knowledge.change_type', true);
    EXCEPTION WHEN OTHERS THEN
        v_change_type := '';
    END;

    IF v_change_type IS NULL OR v_change_type = '' THEN
        IF TG_OP = 'INSERT' THEN
            v_change_type := 'Created';
        ELSIF NEW.status IS DISTINCT FROM OLD.status THEN
            IF NEW.status = 'Archived' THEN
                v_change_type := 'Archived';
            ELSE
                v_change_type := 'WorkflowTransition';
            END IF;
        ELSE
            v_change_type := 'Updated';
        END IF;
    END IF;

    DECLARE
        v_change_reason text;
    BEGIN
        v_change_reason := current_setting('knowledge.change_reason', true);
        IF v_change_reason IS NULL OR v_change_reason = '' THEN
            v_change_reason := 'auto-audit';
        END IF;

        INSERT INTO public.knowledge_version_history
            (knowledge_id, version, snapshot, changed_by, change_reason, db_user, change_type)
        VALUES
            (NEW.id, next_ver, to_jsonb(NEW), COALESCE(NEW.updated_by, NEW.created_by),
             v_change_reason, current_user, v_change_type);
    END;

    RETURN NEW;
END;
$$;
```

**CREATE FUNCTION set_knowledge_change_context:**

```sql
CREATE OR REPLACE FUNCTION public.set_knowledge_change_context(p_change_type text, p_change_reason text)
RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    PERFORM set_config('knowledge.change_type', p_change_type, true);
    PERFORM set_config('knowledge.change_reason', p_change_reason, true);
END;
$$;
```

**CREATE FUNCTION require_not_archived:**

```sql
CREATE OR REPLACE FUNCTION public.require_not_archived(p_knowledge_id bigint)
RETURNS void LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE v_status text;
BEGIN
    SELECT status INTO v_status FROM public.knowledge_entries WHERE id = p_knowledge_id;
    IF v_status IS NULL THEN
        RAISE EXCEPTION 'Knowledge entry % not found', p_knowledge_id;
    END IF;
    IF v_status = 'Archived' THEN
        RAISE EXCEPTION 'Knowledge entry % is archived and cannot be modified', p_knowledge_id;
    END IF;
END;
$$;
```

**CREATE OR REPLACE FUNCTION update_knowledge_entry:**

```sql
CREATE OR REPLACE FUNCTION public.update_knowledge_entry(
    p_knowledge_id bigint,
    p_expected_version int,
    p_title text,
    p_symptoms text,
    p_known_cause text,
    p_workaround text,
    p_permanent_fix text,
    p_affected_versions text,
    p_fixed_version text,
    p_changed_by text,
    p_change_reason text
)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_current_version int;
BEGIN
    SELECT COALESCE(MAX(version), 0) INTO v_current_version
    FROM public.knowledge_version_history
    WHERE knowledge_id = p_knowledge_id;

    IF v_current_version IS DISTINCT FROM p_expected_version THEN
        RAISE EXCEPTION 'Knowledge entry was modified concurrently (expected version %, current %)', p_expected_version, v_current_version;
    END IF;

    PERFORM public.require_not_archived(p_knowledge_id);
    PERFORM public.set_knowledge_change_context('Updated', p_change_reason);

    UPDATE public.knowledge_entries
    SET title = p_title,
        symptoms = p_symptoms,
        known_cause = p_known_cause,
        workaround = p_workaround,
        permanent_fix = p_permanent_fix,
        affected_versions = public.parse_version_list(p_affected_versions),
        fixed_version = NULLIF(trim(p_fixed_version), ''),
        updated_by = p_changed_by,
        updated_at = now()
    WHERE id = p_knowledge_id;
END;
$$;

GRANT EXECUTE ON FUNCTION public.update_knowledge_entry(bigint, int, text, text, text, text, text, text, text, text, text) TO cc_readonly;
```

**CREATE OR REPLACE FUNCTION transition_knowledge:**

```sql
CREATE OR REPLACE FUNCTION public.transition_knowledge(
    p_knowledge_id bigint,
    p_target_status text,
    p_reason text,
    p_expected_version int,
    p_changed_by text
)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_current text;
    v_current_version int;
BEGIN
    SELECT status INTO v_current FROM public.knowledge_entries WHERE id = p_knowledge_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Knowledge entry % not found', p_knowledge_id;
    END IF;

    SELECT COALESCE(MAX(version), 0) INTO v_current_version
    FROM public.knowledge_version_history
    WHERE knowledge_id = p_knowledge_id;

    IF v_current_version IS DISTINCT FROM p_expected_version THEN
        RAISE EXCEPTION 'Knowledge entry was modified concurrently (expected version %, current %)', p_expected_version, v_current_version;
    END IF;

    IF v_current = p_target_status THEN
        RAISE EXCEPTION 'Knowledge entry % is already %', p_knowledge_id, v_current;
    END IF;

    IF NOT (
        (v_current = 'Draft' AND p_target_status IN ('Reviewed','Archived')) OR
        (v_current = 'Reviewed' AND p_target_status IN ('Verified','Archived')) OR
        (v_current = 'Verified' AND p_target_status IN ('Deprecated','Archived')) OR
        (v_current = 'Deprecated' AND p_target_status IN ('Reviewed','Archived'))
    ) THEN
        RAISE EXCEPTION 'Invalid knowledge transition from % to %', v_current, p_target_status;
    END IF;

    IF p_target_status = 'Archived' AND (p_reason IS NULL OR trim(p_reason) = '') THEN
        RAISE EXCEPTION 'Archive reason is required';
    END IF;

    PERFORM public.set_knowledge_change_context(
        CASE WHEN p_target_status = 'Archived' THEN 'Archived' ELSE 'WorkflowTransition' END,
        p_reason
    );

    IF p_target_status = 'Verified' THEN
        UPDATE public.knowledge_entries
        SET status = p_target_status,
            confidence = 'High',
            updated_by = p_changed_by,
            updated_at = now()
        WHERE id = p_knowledge_id;
    ELSE
        UPDATE public.knowledge_entries
        SET status = p_target_status,
            updated_by = p_changed_by,
            updated_at = now()
        WHERE id = p_knowledge_id;
    END IF;
END;
$$;

GRANT EXECUTE ON FUNCTION public.transition_knowledge(bigint, text, text, int, text) TO cc_readonly;
```

**CREATE OR REPLACE FUNCTION add_knowledge_reference:**

```sql
CREATE OR REPLACE FUNCTION public.add_knowledge_reference(
    p_knowledge_id bigint,
    p_reference_type text,
    p_url text,
    p_label text,
    p_added_by text
)
RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_ref_id bigint;
BEGIN
    PERFORM public.require_not_archived(p_knowledge_id);

    INSERT INTO public.knowledge_references (knowledge_id, reference_type, url, label)
    VALUES (p_knowledge_id, p_reference_type, p_url, p_label)
    RETURNING id INTO v_ref_id;

    PERFORM public.set_knowledge_change_context('ReferenceAdded', 'Added reference: ' || p_reference_type);

    UPDATE public.knowledge_entries
    SET updated_by = p_added_by,
        updated_at = now()
    WHERE id = p_knowledge_id;

    RETURN v_ref_id;
END;
$$;

GRANT EXECUTE ON FUNCTION public.add_knowledge_reference(bigint, text, text, text, text) TO cc_readonly;
```

**CREATE OR REPLACE FUNCTION remove_knowledge_reference:**

```sql
CREATE OR REPLACE FUNCTION public.remove_knowledge_reference(
    p_reference_id bigint,
    p_removed_by text
)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_knowledge_id bigint;
BEGIN
    SELECT knowledge_id INTO v_knowledge_id
    FROM public.knowledge_references
    WHERE id = p_reference_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Reference % not found', p_reference_id;
    END IF;

    PERFORM public.require_not_archived(v_knowledge_id);

    DELETE FROM public.knowledge_references WHERE id = p_reference_id;

    PERFORM public.set_knowledge_change_context('ReferenceRemoved', 'Removed reference');

    UPDATE public.knowledge_entries
    SET updated_by = p_removed_by,
        updated_at = now()
    WHERE id = v_knowledge_id;
END;
$$;

GRANT EXECUTE ON FUNCTION public.remove_knowledge_reference(bigint, text) TO cc_readonly;
```

**CREATE OR REPLACE FUNCTION get_knowledge_history:**

```sql
DROP FUNCTION IF EXISTS public.get_knowledge_history(bigint);

CREATE OR REPLACE FUNCTION public.get_knowledge_history(p_knowledge_id bigint)
RETURNS TABLE (
    version int,
    changed_by text,
    db_user text,
    changed_at timestamptz,
    change_reason text,
    change_type text
)
LANGUAGE plpgsql SECURITY DEFINER AS $$
BEGIN
    RETURN QUERY
    SELECT h.version, h.changed_by, h.db_user, h.changed_at, h.change_reason, h.change_type
    FROM public.knowledge_version_history h
    WHERE h.knowledge_id = p_knowledge_id
    ORDER BY h.version DESC;
END;
$$;

GRANT EXECUTE ON FUNCTION public.get_knowledge_history(bigint) TO cc_readonly;
```

**CREATE OR REPLACE FUNCTION match_knowledge_priority2:**

```sql
CREATE OR REPLACE FUNCTION public.match_knowledge_priority2(p_incident_id bigint)
RETURNS TABLE (knowledge_id bigint, priority int)
LANGUAGE plpgsql SECURITY DEFINER AS $$
BEGIN
    RETURN QUERY
    SELECT ke.id, 2
    FROM public.knowledge_entries ke
    JOIN public.telemetry_incidents i
      ON i.component = ke.component AND i.signal = ke.signal
    WHERE i.id = p_incident_id
      AND ke.status = 'Verified'
      AND NOT EXISTS (
          SELECT 1 FROM public.knowledge_entries ke2
          WHERE ke2.fingerprint_hash = i.fingerprint_hash AND ke2.status = 'Verified'
      )
    ORDER BY ke.confidence DESC, ke.updated_at DESC, ke.id DESC
    LIMIT 1;
END;
$$;

GRANT EXECUTE ON FUNCTION public.match_knowledge_priority2(bigint) TO cc_readonly;
```

---

### 1.9. 20260704000023_knowledge_coverage.sql

**Призначення:** Knowledge Engine Slice 5 — auto-verify (`verify_knowledge_auto`), manual search (`search_knowledge`), materialized view `control_center.knowledge_coverage`, views `control_center.knowledge_list`, `control_center.top_missing_knowledge`, drop deprecated `archive_knowledge_entry`, розширення CHECK notification_type значенням 'KnowledgeVerified'.

**CREATE OR REPLACE FUNCTION verify_knowledge_auto:**

```sql
CREATE OR REPLACE FUNCTION public.verify_knowledge_auto()
RETURNS TABLE (knowledge_id bigint, title text, reason text)
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_count int := 0;
    v_now timestamptz := now();
BEGIN
    RETURN QUERY
    SELECT ke.id, ke.title,
        'Auto-verified: fixed_version=' || ke.fixed_version ||
        ', no recurrence 14d, confidence High→Verified' AS reason
    FROM public.knowledge_entries ke
    WHERE ke.confidence = 'High'
      AND ke.status IN ('Reviewed','Verified')
      AND ke.fixed_version IS NOT NULL
      AND EXISTS (
          SELECT 1 FROM control_center.release_health_detail rh
          WHERE rh.app_version = ke.fixed_version
            AND rh.active_installs >= 50
      )
      AND EXISTS (
          SELECT 1 FROM control_center.release_health_detail rh
          WHERE rh.app_version = ke.fixed_version
            AND rh.success_rate >= 95
      )
      AND NOT EXISTS (
          SELECT 1 FROM public.telemetry_incidents i
          WHERE i.fingerprint_hash = ke.fingerprint_hash
            AND i.last_event_at >= v_now - interval '14 days'
      );

    v_count := 0;
    FOR v_count IN
        SELECT ke.id
        FROM public.knowledge_entries ke
        WHERE ke.confidence = 'High'
          AND ke.status IN ('Reviewed','Verified')
          AND ke.fixed_version IS NOT NULL
          AND EXISTS (
              SELECT 1 FROM control_center.release_health_detail rh
              WHERE rh.app_version = ke.fixed_version
                AND rh.active_installs >= 50
          )
          AND EXISTS (
              SELECT 1 FROM control_center.release_health_detail rh
              WHERE rh.app_version = ke.fixed_version
                AND rh.success_rate >= 95
          )
          AND NOT EXISTS (
              SELECT 1 FROM public.telemetry_incidents i
              WHERE i.fingerprint_hash = ke.fingerprint_hash
                AND i.last_event_at >= v_now - interval '14 days'
          )
    LOOP
        PERFORM public.set_knowledge_change_context(
            'WorkflowTransition',
            'Auto-verified by verify_knowledge_auto() (§6.1)'
        );
        UPDATE public.knowledge_entries
        SET confidence = 'Verified',
            status = CASE WHEN status = 'Reviewed' THEN 'Verified' ELSE status END,
            updated_by = 'system:auto-verify',
            updated_at = v_now
        WHERE id = v_count AND confidence = 'High';
    END LOOP;
END;
$$;

GRANT EXECUTE ON FUNCTION public.verify_knowledge_auto() TO cc_readonly;
```

**CREATE OR REPLACE FUNCTION search_knowledge:**

```sql
CREATE OR REPLACE FUNCTION public.search_knowledge(
    p_query text,
    p_component text DEFAULT NULL,
    p_status text DEFAULT NULL,
    p_confidence text DEFAULT NULL,
    p_limit int DEFAULT 20,
    p_offset int DEFAULT 0
)
RETURNS TABLE (
    knowledge_id bigint,
    title text,
    component text,
    signal text,
    status text,
    confidence text,
    fixed_version text,
    updated_at timestamptz,
    total_count bigint
)
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_pattern text;
    v_total bigint;
BEGIN
    v_pattern := '%' || COALESCE(trim(p_query), '') || '%';

    SELECT count(*) INTO v_total
    FROM public.knowledge_entries ke
    WHERE (v_pattern = '%%' OR ke.title ILIKE v_pattern OR ke.fingerprint_key ILIKE v_pattern
           OR ke.component ILIKE v_pattern OR ke.signal ILIKE v_pattern)
      AND (p_component IS NULL OR ke.component = p_component)
      AND (p_status IS NULL OR ke.status = p_status)
      AND (p_confidence IS NULL OR ke.confidence = p_confidence);

    RETURN QUERY
    SELECT ke.id, ke.title, ke.component, ke.signal, ke.status, ke.confidence,
           ke.fixed_version, ke.updated_at, v_total
    FROM public.knowledge_entries ke
    WHERE (v_pattern = '%%' OR ke.title ILIKE v_pattern OR ke.fingerprint_key ILIKE v_pattern
           OR ke.component ILIKE v_pattern OR ke.signal ILIKE v_pattern)
      AND (p_component IS NULL OR ke.component = p_component)
      AND (p_status IS NULL OR ke.status = p_status)
      AND (p_confidence IS NULL OR ke.confidence = p_confidence)
    ORDER BY ke.updated_at DESC, ke.id DESC
    LIMIT GREATEST(1, LEAST(p_limit, 100))
    OFFSET GREATEST(0, p_offset);
END;
$$;

GRANT EXECUTE ON FUNCTION public.search_knowledge(text, text, text, text, int, int) TO cc_readonly;
```

**CREATE MATERIALIZED VIEW control_center.knowledge_coverage:**

```sql
DROP MATERIALIZED VIEW IF EXISTS control_center.knowledge_coverage;
CREATE MATERIALIZED VIEW control_center.knowledge_coverage AS
WITH fingerprints AS (
    SELECT DISTINCT fingerprint_hash
    FROM public.telemetry_incidents
),
verified AS (
    SELECT DISTINCT fingerprint_hash
    FROM public.knowledge_entries
    WHERE status = 'Verified'
)
SELECT
    (SELECT count(*) FROM fingerprints) AS total_fingerprints,
    (SELECT count(*) FROM fingerprints f JOIN verified v ON f.fingerprint_hash = v.fingerprint_hash) AS covered_fingerprints,
    (SELECT count(*) FROM fingerprints f LEFT JOIN verified v ON f.fingerprint_hash = v.fingerprint_hash WHERE v.fingerprint_hash IS NULL) AS uncovered_fingerprints,
    CASE
        WHEN (SELECT count(*) FROM fingerprints) = 0 THEN 0
        ELSE ROUND(
            100.0 * (SELECT count(*) FROM fingerprints f JOIN verified v ON f.fingerprint_hash = v.fingerprint_hash)
            / (SELECT count(*) FROM fingerprints),
            1
        )
    END AS coverage_pct;

GRANT SELECT ON control_center.knowledge_coverage TO cc_readonly;
CREATE UNIQUE INDEX IF NOT EXISTS idx_knowledge_coverage_singleton ON control_center.knowledge_coverage (total_fingerprints);
```

**CREATE OR REPLACE FUNCTION refresh_knowledge_coverage:**

```sql
CREATE OR REPLACE FUNCTION public.refresh_knowledge_coverage()
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER AS $$
BEGIN
    REFRESH MATERIALIZED VIEW control_center.knowledge_coverage;
END;
$$;

GRANT EXECUTE ON FUNCTION public.refresh_knowledge_coverage() TO cc_readonly;
```

**CREATE VIEW control_center.knowledge_list:**

```sql
DROP VIEW IF EXISTS control_center.knowledge_list;
CREATE VIEW control_center.knowledge_list AS
SELECT
    ke.id,
    ke.fingerprint_key,
    ke.component,
    ke.operation,
    ke.signal,
    ke.title,
    ke.confidence,
    ke.status,
    ke.fixed_version,
    COALESCE(array_to_string(ke.affected_versions, ','), '') AS affected_versions,
    ke.created_at,
    ke.updated_at,
    ke.updated_by
FROM public.knowledge_entries ke
ORDER BY ke.updated_at DESC;

GRANT SELECT ON control_center.knowledge_list TO cc_readonly;
```

**CREATE VIEW control_center.top_missing_knowledge:**

```sql
DROP VIEW IF EXISTS control_center.top_missing_knowledge;
CREATE VIEW control_center.top_missing_knowledge AS
SELECT
    i.component,
    i.signal,
    count(DISTINCT i.fingerprint_hash) AS fingerprint_count,
    sum(i.event_count) AS total_events,
    max(i.last_event_at) AS last_seen,
    max(i.highest_severity) AS highest_severity
FROM public.telemetry_incidents i
WHERE NOT EXISTS (
    SELECT 1 FROM public.knowledge_entries ke
    WHERE ke.fingerprint_hash = i.fingerprint_hash AND ke.status = 'Verified'
)
GROUP BY i.component, i.signal
ORDER BY total_events DESC
LIMIT 10;

GRANT SELECT ON control_center.top_missing_knowledge TO cc_readonly;
```

**DROP deprecated / CHECK-розширення notification_type:**

```sql
DROP FUNCTION IF EXISTS public.archive_knowledge_entry(bigint, text, text);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'notification_queue'
          AND column_name = 'notification_type'
    ) THEN
        BEGIN
            ALTER TABLE public.notification_queue
                DROP CONSTRAINT IF EXISTS chk_notification_type;
            ALTER TABLE public.notification_queue
                ADD CONSTRAINT chk_notification_type CHECK (
                    notification_type IN (
                        'IncidentCreated','IncidentEscalated','IncidentMitigated',
                        'IncidentResolved','WeeklyDigest','TestAlert','KnowledgeVerified'
                    )
                );
        EXCEPTION WHEN OTHERS THEN
            RAISE NOTICE 'Could not update notification_type constraint: %', SQLERRM;
        END;
    END IF;
END $$;
```

---

### 1.10. 20260705021100_observability_pipeline_automation.sql

**Призначення:** автоматизація observability pipeline. Singleton `control_center.pipeline_health_meta`, оновлення `refresh_knowledge_coverage()` з фіксацією часу, per-event `promote_incident_candidates_for_event(uuid)`, EXCEPTION-safe тригер `tg_promote_after_failed()`, тригер `trg_telemetry_failed_promote`, тригер `trg_incident_refresh_coverage` для авто-REFRESH `knowledge_coverage`.

**CREATE TABLE control_center.pipeline_health_meta:**

```sql
CREATE TABLE IF NOT EXISTS control_center.pipeline_health_meta (
    singleton      boolean PRIMARY KEY DEFAULT true,
    last_knowledge_refresh timestamptz,
    CONSTRAINT singleton_chk CHECK (singleton = true)
);

INSERT INTO control_center.pipeline_health_meta (singleton, last_knowledge_refresh)
VALUES (true, NULL)
ON CONFLICT (singleton) DO NOTHING;

GRANT SELECT ON control_center.pipeline_health_meta TO cc_readonly;
```

**CREATE OR REPLACE FUNCTION refresh_knowledge_coverage:**

```sql
CREATE OR REPLACE FUNCTION public.refresh_knowledge_coverage()
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    REFRESH MATERIALIZED VIEW control_center.knowledge_coverage;
    UPDATE control_center.pipeline_health_meta
    SET    last_knowledge_refresh = NOW()
    WHERE  singleton = true;
END;
$$;

GRANT EXECUTE ON FUNCTION public.refresh_knowledge_coverage() TO cc_readonly;
```

**CREATE OR REPLACE FUNCTION promote_incident_candidates_for_event(uuid):**

```sql
CREATE OR REPLACE FUNCTION public.promote_incident_candidates_for_event(p_event uuid)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_component       text;
    v_operation       text;
    v_app_version     text;
    v_hresult         text;
    v_supabase_code   text;
    v_http_status     integer;
    v_exception_type  text;
    v_signal          text;
    v_fingerprint_key text;
    v_fingerprint_hash text;
    v_window          interval := interval '10 minutes';
    v_pol             public.incident_policy%ROWTYPE;
    v_failed_now      bigint;
    v_total_now       bigint;
    v_affected_installs bigint;
    v_affected_users  bigint;
    v_failure_pct     numeric;
    v_existing_id     bigint;
    v_new_id          bigint;
    v_root_id         uuid;
    v_last_id         uuid;
    v_new_sev         text;
BEGIN
    SELECT component, operation, app_version, hresult, supabase_code,
           http_status, exception_type
    INTO v_component, v_operation, v_app_version, v_hresult, v_supabase_code,
         v_http_status, v_exception_type
    FROM public.telemetry_events
    WHERE id = p_event;

    IF v_component IS NULL THEN RETURN; END IF;

    v_signal := COALESCE(v_hresult, v_supabase_code,
                         v_http_status::text, v_exception_type, '-');
    v_fingerprint_key := v_component || '|' || v_operation || '|' ||
                         v_signal || '|' || v_app_version;
    v_fingerprint_hash := md5(v_fingerprint_key);

    SELECT * INTO v_pol FROM public.incident_policy
    WHERE component = v_component AND enabled
    ORDER BY (signal = '_') DESC, (operation = '_') DESC LIMIT 1;
    IF NOT FOUND THEN
        SELECT * INTO v_pol FROM public.incident_policy
        WHERE component = '_default' AND enabled LIMIT 1;
    END IF;
    IF NOT FOUND THEN RETURN; END IF;

    SELECT count(*),
           count(DISTINCT install_id),
           count(DISTINCT user_id)
    INTO v_failed_now, v_affected_installs, v_affected_users
    FROM public.telemetry_events
    WHERE outcome = 'Failed'
      AND component = v_component
      AND operation = v_operation
      AND app_version = v_app_version
      AND COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') = v_signal
      AND received_at > NOW() - v_window;

    SELECT count(*) INTO v_total_now
    FROM public.telemetry_events
    WHERE outcome IN ('Succeeded', 'Failed')
      AND component = v_component
      AND operation = v_operation
      AND app_version = v_app_version
      AND received_at > NOW() - v_window;

    v_failure_pct := CASE WHEN v_total_now > 0
                          THEN round(100.0 * v_failed_now / v_total_now, 1)
                          ELSE 0 END;

    IF v_failed_now < v_pol.min_sample THEN RETURN; END IF;
    IF v_failure_pct < v_pol.failure_threshold_pct THEN RETURN; END IF;

    v_new_sev := CASE WHEN v_affected_installs >= v_pol.critical_affected_threshold
                      THEN 'Critical' ELSE 'Warning' END;

    SELECT id INTO v_existing_id FROM public.telemetry_incidents
    WHERE fingerprint_key = v_fingerprint_key AND status != 'Closed'
    LIMIT 1;

    SELECT id INTO v_last_id FROM public.telemetry_events
    WHERE component = v_component AND operation = v_operation
      AND COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') = v_signal
      AND app_version = v_app_version AND outcome = 'Failed'
    ORDER BY received_at DESC LIMIT 1;

    IF v_existing_id IS NOT NULL THEN
        UPDATE public.telemetry_incidents
        SET last_event_at      = NOW(),
            last_event_id      = v_last_id,
            affected_installs  = GREATEST(affected_installs, v_affected_installs),
            affected_users     = GREATEST(affected_users, v_affected_users),
            peak_failure_pct   = GREATEST(peak_failure_pct, v_failure_pct),
            event_count        = event_count + 1,
            highest_severity   = CASE WHEN v_new_sev = 'Critical' OR highest_severity = 'Critical'
                                      THEN 'Critical' ELSE 'Warning' END
        WHERE id = v_existing_id;
    ELSE
        SELECT id INTO v_root_id FROM public.telemetry_events
        WHERE component = v_component AND operation = v_operation
          AND COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') = v_signal
          AND app_version = v_app_version AND outcome = 'Failed'
        ORDER BY received_at ASC LIMIT 1;

        INSERT INTO public.telemetry_incidents (
            fingerprint_key, fingerprint_hash, release, component, operation, signal,
            root_event_id, last_event_id, opened_at, last_event_at, status,
            highest_severity, peak_failure_pct, affected_users, affected_installs, event_count
        ) VALUES (
            v_fingerprint_key, v_fingerprint_hash, v_app_version,
            v_component, v_operation, v_signal,
            v_root_id, v_last_id, NOW(), NOW(), 'Active',
            v_new_sev, v_failure_pct, v_affected_users, v_affected_installs, v_failed_now
        )
        RETURNING id INTO v_new_id;

        INSERT INTO public.notification_queue (incident_id, notification_type, provider, payload)
        VALUES (v_new_id, 'IncidentCreated', 'Discord',
            jsonb_build_object(
                'version', 1,
                'incident_id', v_new_id,
                'component', v_component,
                'operation', v_operation,
                'signal', v_signal,
                'severity', v_new_sev,
                'release', v_app_version,
                'affected_installs', v_affected_installs,
                'affected_users', v_affected_users,
                'failure_pct', v_failure_pct
            )
        )
        ON CONFLICT DO NOTHING;
    END IF;
END;
$$;

GRANT EXECUTE ON FUNCTION public.promote_incident_candidates_for_event(uuid) TO cc_readonly;
```

**CREATE OR REPLACE FUNCTION tg_promote_after_failed:**

```sql
CREATE OR REPLACE FUNCTION public.tg_promote_after_failed()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    BEGIN
        PERFORM public.promote_incident_candidates_for_event(NEW.id);
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE 'promote_incident_candidates_for_event failed for event %: % (%)',
            NEW.id, SQLERRM, SQLSTATE;
    END;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_telemetry_failed_promote ON public.telemetry_events;

CREATE TRIGGER trg_telemetry_failed_promote
    AFTER INSERT ON public.telemetry_events
    FOR EACH ROW
    WHEN (NEW.outcome = 'Failed')
    EXECUTE FUNCTION public.tg_promote_after_failed();
```

**CREATE OR REPLACE FUNCTION tg_incident_refresh_coverage:**

```sql
CREATE OR REPLACE FUNCTION public.tg_incident_refresh_coverage()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    BEGIN
        PERFORM public.refresh_knowledge_coverage();
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE 'refresh_knowledge_coverage failed: % (%)', SQLERRM, SQLSTATE;
    END;
    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS trg_incident_refresh_coverage ON public.telemetry_incidents;

CREATE TRIGGER trg_incident_refresh_coverage
    AFTER INSERT OR UPDATE ON public.telemetry_incidents
    FOR EACH STATEMENT
    EXECUTE FUNCTION public.tg_incident_refresh_coverage();
```

---

### 1.11. 20260705021200_observability_health_view.sql

**Призначення:** health VIEW `control_center.observability_health` — singleton з last_* timestamps та `pipeline_healthy`. Smoke test у транзакції.

**CREATE OR REPLACE VIEW control_center.observability_health:**

```sql
CREATE OR REPLACE VIEW control_center.observability_health AS
WITH last_failed AS (
    SELECT MAX(received_at) AS at
    FROM public.telemetry_events
    WHERE outcome = 'Failed'
),
last_incident AS (
    SELECT MAX(opened_at) AS at
    FROM public.telemetry_incidents
),
last_notification AS (
    SELECT MAX(created_at) AS at
    FROM public.notification_queue
),
candidates_orphan AS (
    SELECT count(*) AS cnt
    FROM control_center.incident_candidates_24h c
    WHERE NOT EXISTS (
        SELECT 1
        FROM public.telemetry_incidents i
        WHERE i.fingerprint_key = c.fingerprint_key
          AND i.status != 'Closed'
    )
),
failed_age AS (
    SELECT EXTRACT(EPOCH FROM (NOW() - (SELECT at FROM last_failed)))::bigint AS seconds
    WHERE (SELECT at FROM last_failed) IS NOT NULL
)
SELECT
    (SELECT at FROM last_failed)        AS last_failed_event_at,
    (SELECT at FROM last_incident)      AS last_incident_opened_at,
    (SELECT at FROM last_notification)  AS last_notification_at,
    (SELECT last_knowledge_refresh
       FROM control_center.pipeline_health_meta
       WHERE singleton = true)          AS last_knowledge_refresh_at,
    (SELECT cnt FROM candidates_orphan) AS candidates_without_open_incident,
    CASE
        WHEN (SELECT at FROM last_failed) IS NULL THEN true
        WHEN (SELECT cnt FROM candidates_orphan) = 0 THEN true
        WHEN COALESCE((SELECT seconds FROM failed_age), 0) < 120 THEN true
        ELSE false
    END AS pipeline_healthy;

GRANT SELECT ON control_center.observability_health TO cc_readonly;
```

---

### 1.12. 20260705030000_auto_close_on_event.sql

**Призначення:** повернення авто-close старих Active інцидентів у per-event pipeline. Окрема функція `auto_close_stale_incidents()`, розширення `tg_promote_after_failed()` — спочатку auto_close, потім promote.

**CREATE OR REPLACE FUNCTION auto_close_stale_incidents:**

```sql
CREATE OR REPLACE FUNCTION public.auto_close_stale_incidents()
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    UPDATE public.telemetry_incidents i
    SET status = 'Closed', closed_at = now()
    WHERE i.status = 'Active'
      AND i.last_event_at < now() - (
        COALESCE(
          (SELECT auto_close_after_minutes FROM public.incident_policy p
           WHERE p.component = i.component AND p.enabled
           ORDER BY (p.signal = '_') DESC, (p.operation = '_') DESC LIMIT 1),
          (SELECT auto_close_after_minutes FROM public.incident_policy WHERE component = '_default' LIMIT 1),
          60
        ) || ' minutes'
      )::interval;
END;
$$;

GRANT EXECUTE ON FUNCTION public.auto_close_stale_incidents() TO cc_readonly;
```

**CREATE OR REPLACE FUNCTION tg_promote_after_failed (оновлений):**

```sql
CREATE OR REPLACE FUNCTION public.tg_promote_after_failed()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    BEGIN
        PERFORM public.auto_close_stale_incidents();
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE 'auto_close_stale_incidents failed: % (%)', SQLERRM, SQLSTATE;
    END;

    BEGIN
        PERFORM public.promote_incident_candidates_for_event(NEW.id);
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE 'promote_incident_candidates_for_event failed for event %: % (%)',
            NEW.id, SQLERRM, SQLSTATE;
    END;

    RETURN NEW;
END;
$$;
```

---

## 2. C# моделі

### 2.1. SCLOCVerse\Models\Auth\AppInstallation.cs

Повний файл:

```csharp
using Supabase.Postgrest.Models;
using System;

namespace SCLOCVerse.Models.Auth
{
    /// <summary>
    /// Модель таблиці public.app_installations для зв'язку User → Installations.
    /// </summary>
    [Supabase.Postgrest.Attributes.Table("app_installations")]
    public class AppInstallation : BaseModel
    {
        [Supabase.Postgrest.Attributes.PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Supabase.Postgrest.Attributes.Column("user_id")]
        public Guid? UserId { get; set; }

        [Supabase.Postgrest.Attributes.Column("install_id")]
        public string InstallId { get; set; } = string.Empty;

        [Supabase.Postgrest.Attributes.Column("app_version")]
        public string? AppVersion { get; set; }

        [Supabase.Postgrest.Attributes.Column("platform")]
        public string? Platform { get; set; }

        [Supabase.Postgrest.Attributes.Column("machine_id")]
        public string? MachineId { get; set; }

        [Supabase.Postgrest.Attributes.Column("os_version")]
        public string? OsVersion { get; set; }

        [Supabase.Postgrest.Attributes.Column("last_seen")]
        public DateTimeOffset? LastSeen { get; set; }

        [Supabase.Postgrest.Attributes.Column("first_seen")]
        public DateTimeOffset? FirstSeen { get; set; }

        [Supabase.Postgrest.Attributes.Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [Supabase.Postgrest.Attributes.Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Supabase.Postgrest.Attributes.Column("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
```

### 2.2. SCLOCVerse\Models\Observability\TelemetryEvent.cs

Повний файл:

```csharp
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System;

namespace SCLOCVerse.Models.Observability
{
    /// <summary>
    /// Модель таблиці public.telemetry_events — єдиного append-only журналу
    /// SCLOC Observability Platform (Конституція, Стаття 5).
    /// </summary>
    [Table("telemetry_events")]
    public class TelemetryEvent : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("client_event_id")]
        public Guid ClientEventId { get; set; }

        [Column("session_id")]
        public Guid SessionId { get; set; }

        [Column("correlation_id")]
        public Guid CorrelationId { get; set; }

        [Column("step")]
        public int Step { get; set; }

        [Column("install_id")]
        public string InstallId { get; set; } = string.Empty;

        [Column("user_id")]
        public Guid? UserId { get; set; }

        [Column("occurred_at")]
        public DateTimeOffset OccurredAt { get; set; }

        [Column("app_version")]
        public string AppVersion { get; set; } = string.Empty;

        [Column("git_commit")]
        public string? GitCommit { get; set; }

        [Column("channel")]
        public string Channel { get; set; } = "stable";

        [Column("telemetry_version")]
        public int TelemetryVersion { get; set; } = 1;

        [Column("os_version")]
        public string? OsVersion { get; set; }

        [Column("country")]
        public string? Country { get; set; }

        [Column("component")]
        public string Component { get; set; } = string.Empty;

        [Column("operation")]
        public string Operation { get; set; } = string.Empty;

        [Column("outcome")]
        public string Outcome { get; set; } = string.Empty;

        [Column("severity")]
        public string Severity { get; set; } = "Info";

        [Column("category")]
        public string Category { get; set; } = "Operational";

        [Column("source")]
        public string? Source { get; set; }

        [Column("http_status")]
        public int? HttpStatus { get; set; }

        [Column("hresult")]
        public string? Hresult { get; set; }

        [Column("supabase_code")]
        public string? SupabaseCode { get; set; }

        [Column("exception_type")]
        public string? ExceptionType { get; set; }

        [Column("error_message")]
        public string? ErrorMessage { get; set; }

        [Column("duration_ms")]
        public int? DurationMs { get; set; }

        [Column("detail")]
        public System.Collections.Generic.Dictionary<string, object?>? Detail { get; set; }
    }
}
```

### 2.3. SCLOCVerse\Models\Observability\TelemetryContext.cs

Повний файл:

```csharp
using System.Collections.Generic;

namespace SCLOCVerse.Models.Observability
{
    /// <summary>
    /// Додатковий контекст події (опціональний). Для щасливих подій (Start/Success)
    /// передається null; для невдач — несе diagnostic-сигнали (Конституція, Стаття 13:
    /// Failed обов'язково має signal).
    /// </summary>
    public sealed class TelemetryContext
    {
        /// <summary>Перевизначення severity (інакше виводиться з outcome).</summary>
        public string? Severity { get; set; }

        /// <summary>Перевизначення категорії (Critical/Operational/Diagnostic/Analytics).</summary>
        public string? Category { get; set; }

        /// <summary>Джерело помилки: Supabase/GitHub/PowerShell/HttpClient/CLR.</summary>
        public string? Source { get; set; }

        public int? HttpStatus { get; set; }

        /// <summary>Шістнадцяткове представлення, напр. "0x800B0109".</summary>
        public string? Hresult { get; set; }

        /// <summary>Postgrest/Postgres код, напр. "42501".</summary>
        public string? SupabaseCode { get; set; }

        public string? ExceptionType { get; set; }

        /// <summary>Коротке, санітизоване повідомлення (НЕ повний stack).</summary>
        public string? ErrorMessage { get; set; }

        public int? DurationMs { get; set; }

        /// <summary>Структуровані деталі (напр. sanitized stack-trace для Crash).</summary>
        public Dictionary<string, object?>? Detail { get; set; }
    }
}
```

---

## 3. C# сервіси

### 3.1. SCLOCVerse\Services\Auth\InstallationService.cs

Повний файл (фрагменти з SQL-взаємодіями та TrackSync):

SELECT за `install_id`:

```csharp
// рядки 58-62
var response = await _supabase
    .From<AppInstallation>()
    .Filter("install_id", Supabase.Postgrest.Constants.Operator.Equals, _installId)
    .Get(cancellationToken: cancellationToken)
    .ConfigureAwait(false);
```

INSERT нової інсталяції:

```csharp
// рядки 86-89
await _supabase
    .From<AppInstallation>()
    .Insert(installation, cancellationToken: cancellationToken)
    .ConfigureAwait(false);
```

UPDATE існуючої інсталяції:

```csharp
// рядки 97-109
await _supabase
    .From<AppInstallation>()
    .Filter("install_id", Supabase.Postgrest.Constants.Operator.Equals, _installId)
    .Set(i => i.UserId, userId)
    .Set(i => i.AppVersion, appVersion)
    .Set(i => i.Platform, platform)
    .Set(i => i.MachineId, machineId)
    .Set(i => i.OsVersion, osVersion)
    .Set(i => i.LastSeen, now)
    .Set(i => i.UpdatedAt, now)
    .Set(i => i.IsActive, true)
    .Update(cancellationToken: cancellationToken)
    .ConfigureAwait(false);
```

TrackSync (виклики `.Track()`):

- `Services\Auth\InstallationService.cs:46` — `TrackSync("Started", phase);`
- `Services\Auth\InstallationService.cs:114` — `TrackSync("Succeeded", phase, sw.ElapsedMilliseconds);`
- `Services\Auth\InstallationService.cs:118` — `TrackSync("Failed", phase, sw.ElapsedMilliseconds, ex);`

---

### 3.2. SCLOCVerse\Services\Auth\DiscordGuildSyncService.cs

DELETE за `user_id` (рядки 77-81):

```csharp
await _supabase
    .From<UserDiscordGuild>()
    .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
    .Delete(cancellationToken: cancellationToken)
    .ConfigureAwait(false);
```

INSERT списку гільдій (рядки 85-88):

```csharp
await _supabase
    .From<UserDiscordGuild>()
    .Insert(rows, null, cancellationToken)
    .ConfigureAwait(false);
```

---

### 3.3. SCLOCVerse\Services\Observability\TelemetryUploader.cs

Повний файл. INSERT події (рядки 69-72):

```csharp
await client
    .From<TelemetryEvent>()
    .Insert(evt)
    .ConfigureAwait(false);
```

Встановлення `UserId` перед INSERT (рядок 65):

```csharp
evt.UserId = userId; // обовʼязково для RLS.
```

---

### 3.4. SCLOCVerse\Services\Observability\ErrorContextExtractor.cs

Повний файл. Заповнює `TelemetryContext`: `ExceptionType`, `ErrorMessage`, `HttpStatus`, `SupabaseCode`, `Hresult`, `Source`. Для `LiaInstallException` додає в `Detail`: `phase`, `retry_count`, `powershell_exit_code`, `installer_type`, `certificate_present`, `certificate_subject`, `certificate_thumbprint`, `activity_id`, `appx_log`, `signal_name`.

---

### 3.5. SCLOCVerse\Services\Observability\TelemetryClient.cs

Повний файл. `BuildEvent` мапить `TelemetryContext` на властивості `TelemetryEvent`, викликає `PrivacySanitizer.Sanitize` для `ErrorMessage`.

---

### 3.6. SCLOCVerse\Services\Observability\PrivacySanitizer.cs

Повний файл. Регулярні вирази:

- Windows user path: `([A-Za-z]:\\Users\\)[^\\]+` → `$1*`
- UNC user path: `(\\\\[^\\]+\\Users\\)[^\\]+` → `$1*`
- Bearer token: `(?i)bearer\s+[A-Za-z0-9\-._~+/=]+` → `Bearer ***`
- JWT: `eyJ[A-Za-z0-9_\-]+\.eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+` → `***JWT***`

---

## 4. C# виклики `.Track()` з точними рядками

### 4.1. ApplicationUpdate (UpdateDownloader.cs, UpdateInstaller.cs, UpdateVerifier.cs)

**UpdateDownloader.cs:**

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateDownloader.cs:44`
  ```csharp
  var sw = Stopwatch.StartNew();
  UpdateEvents.Track(_telemetry, "Download", "Started");
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateDownloader.cs:54`
  ```csharp
  await response.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

  UpdateEvents.Track(_telemetry, "Download", "Succeeded", sw.ElapsedMilliseconds);
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateDownloader.cs:59`
  ```csharp
  catch (Exception ex)
  {
      UpdateEvents.Track(_telemetry, "Download", "Failed", sw.ElapsedMilliseconds, ex);
      throw; // Zero Regression.
  }
  ```

**UpdateInstaller.cs:**

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateInstaller.cs:40`
  ```csharp
  var sw = Stopwatch.StartNew();
  UpdateEvents.Track(_telemetry, "Install", "Started");
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateInstaller.cs:46`
  ```csharp
  if (!File.Exists(installerPath))
  {
      UpdateEvents.Track(_telemetry, "Install", "Failed", sw.ElapsedMilliseconds, phase: "InstallerNotFound");
      return false;
  }
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateInstaller.cs:76`
  ```csharp
  UpdateEvents.Track(_telemetry, "Install", launched ? "Succeeded" : "Failed", sw.ElapsedMilliseconds,
      phase: launched ? "LauncherStarted" : "LaunchFailed");
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateInstaller.cs:82`
  ```csharp
  catch (Exception ex)
  {
      UpdateEvents.Track(_telemetry, "Install", "Failed", sw.ElapsedMilliseconds, ex);
      throw; // Zero Regression.
  }
  ```

**UpdateVerifier.cs:**

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateVerifier.cs:34`
  ```csharp
  var sw = Stopwatch.StartNew();
  UpdateEvents.Track(_telemetry, "Verify", "Started");
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateVerifier.cs:40`
  ```csharp
  if (string.IsNullOrWhiteSpace(expectedChecksum))
  {
      UpdateEvents.Track(_telemetry, "Verify", "Skipped", sw.ElapsedMilliseconds, phase: "NoChecksum");
      return false;
  }
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateVerifier.cs:46`
  ```csharp
  if (!File.Exists(filePath))
  {
      UpdateEvents.Track(_telemetry, "Verify", "Failed", sw.ElapsedMilliseconds, phase: "FileNotFound");
      return false;
  }
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateVerifier.cs:59`
  ```csharp
  var ok = string.Equals(actualChecksum, trimmedChecksum, StringComparison.OrdinalIgnoreCase);
  UpdateEvents.Track(_telemetry, "Verify", ok ? "Succeeded" : "Failed", sw.ElapsedMilliseconds,
      phase: ok ? null : "ChecksumMismatch");
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateVerifier.cs:65`
  ```csharp
  catch (Exception ex)
  {
      UpdateEvents.Track(_telemetry, "Verify", "Failed", sw.ElapsedMilliseconds, ex);
      throw; // Zero Regression.
  }
  ```

### 4.2. LIA (SCLOCVerse\Services\LiaServices\Updater.cs)

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:79`
  ```csharp
  var sw = System.Diagnostics.Stopwatch.StartNew();
  LiaEvents.Track(_telemetry, "Install", "Started", orchestrationPhase: "Download");
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:95`
  ```csharp
  // --- Download ---
  LiaEvents.Track(_telemetry, "Download", "Started", orchestrationPhase: "InstallerAsset");
  var downloadSw = System.Diagnostics.Stopwatch.StartNew();
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:104`
  ```csharp
  catch (Exception dlEx)
  {
      LiaEvents.Track(_telemetry, "Download", "Failed", downloadSw.ElapsedMilliseconds, dlEx, orchestrationPhase: "InstallerAsset");
      if (_telemetry is not null)
          await _telemetry.FlushAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
      throw;
  }
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:109`
  ```csharp
  LiaEvents.Track(_telemetry, "Download", "Succeeded", downloadSw.ElapsedMilliseconds, orchestrationPhase: "InstallerAsset");
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:116`
  ```csharp
  if (certificateAsset != null)
  {
      onProgress?.Invoke("Завантаження сертифіката...");
      LiaEvents.Track(_telemetry, "Download", "Started", orchestrationPhase: "CertificateAsset");
      var certSw = System.Diagnostics.Stopwatch.StartNew();
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:124`
  ```csharp
  catch (Exception certEx)
  {
      LiaEvents.Track(_telemetry, "Download", "Failed", certSw.ElapsedMilliseconds, certEx, orchestrationPhase: "CertificateAsset");
      if (_telemetry is not null)
          await _telemetry.FlushAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
      throw;
  }
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:129`
  ```csharp
  LiaEvents.Track(_telemetry, "Download", "Succeeded", certSw.ElapsedMilliseconds, orchestrationPhase: "CertificateAsset");
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:137`
  ```csharp
  onProgress?.Invoke("Запуск інсталяції Л.І.А...");
  LiaEvents.Track(_telemetry, "Install", "Started", orchestrationPhase: "RunInstallerScript",
      installerType: GetInstallerType(installerPath), certificatePresent: certificatePath != null,
      packageVersion: release.TagName);
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:143`
  ```csharp
  var installSw = System.Diagnostics.Stopwatch.StartNew();
  await RunInstallerScriptAsync(installerPath, certificatePath, cancellationToken).ConfigureAwait(false);
  LiaEvents.Track(_telemetry, "Install", "Succeeded", installSw.ElapsedMilliseconds, orchestrationPhase: "Complete");
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:151`
  ```csharp
  catch (Exception ex)
  {
      LiaEvents.Track(_telemetry, "Install", "Failed", sw.ElapsedMilliseconds, ex);

      if (_telemetry is not null)
          await _telemetry.FlushAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

      throw; // Zero Regression.
  }
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:274`
  ```csharp
  var scriptSw = System.Diagnostics.Stopwatch.StartNew();
  LiaEvents.Track(_telemetry, "RunInstallerScript", "Started");
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:293`
  ```csharp
  catch (Exception ex)
  {
      LiaEvents.Track(_telemetry, "RunInstallerScript", "Failed", scriptSw.ElapsedMilliseconds, ex,
          orchestrationPhase: "ProcessExecution");
      if (_telemetry is not null)
          await _telemetry.FlushAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
      throw;
  }
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:308`
  ```csharp
  if (forensic != null)
  {
      forensic.InstallerType ??= GetInstallerType(installerPath);
      var liaEx = new LiaInstallException(forensic, result.ExitCode, result.Error);
      LiaEvents.Track(_telemetry, "RunInstallerScript", "Failed", scriptSw.ElapsedMilliseconds, liaEx);
      if (_telemetry is not null)
          await _telemetry.FlushAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
      throw liaEx;
  }
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:316`
  ```csharp
  var fallbackEx = new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim());
  LiaEvents.Track(_telemetry, "RunInstallerScript", "Failed", scriptSw.ElapsedMilliseconds, fallbackEx,
      orchestrationPhase: "UnknownExitCode");
  if (_telemetry is not null)
      await _telemetry.FlushAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
  throw fallbackEx;
  ```

- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs:323`
  ```csharp
  LiaEvents.Track(_telemetry, "RunInstallerScript", "Succeeded", scriptSw.ElapsedMilliseconds);
  ```

---

## 5. SCLOCVerse\Composition\AppCompositionRoot.cs

Повний файл. Побудова `TelemetryClient` (рядки 73-76):

```csharp
var telemetryChannel = string.IsNullOrWhiteSpace(SCLOCVerse.Settings.Default.UpdateChannel)
    ? "stable"
    : SCLOCVerse.Settings.Default.UpdateChannel;
_telemetryClient = new TelemetryClient(BuildInfo.Create(telemetryChannel), enabled: !IsTelemetryDisabled());
```

Kill-switch телеметрії (рядки 266-271):

```csharp
private static bool IsTelemetryDisabled()
{
    var value = System.Environment.GetEnvironmentVariable("SCLOCVERSE_TELEMETRY_DISABLED");
    return string.Equals(value, "1", StringComparison.Ordinal)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
```

Supabase URL/anon key (рядки 238-262):

```csharp
private static string GetSupabaseUrl()
{
    var value = System.Environment.GetEnvironmentVariable("SCLOCVERSE_SUPABASE_URL");
    if (!string.IsNullOrWhiteSpace(value))
        return value;

    value = SCLOCVerse.Properties.SupabaseConfig.DefaultUrl;
    if (!string.IsNullOrWhiteSpace(value))
        return value;

    return "https://placeholder.supabase.co";
}

private static string GetSupabaseAnonKey()
{
    var value = System.Environment.GetEnvironmentVariable("SCLOCVERSE_SUPABASE_ANON_KEY");
    if (!string.IsNullOrWhiteSpace(value))
        return value;

    value = SCLOCVerse.Properties.SupabaseConfig.DefaultAnonKey;
    if (!string.IsNullOrWhiteSpace(value))
        return value;

    return "placeholder-anon-key";
}
```

---

## 6. SCLOCVerse\Properties\Settings

### 6.1. Settings.settings

Фактичний шлях: `F:\C#\SCLocalizationUA\SCLOCVerse\Settings.settings`.

Повний файл:

```xml
<?xml version="1.0" encoding="utf-8"?>
<SettingsFile xmlns="http://schemas.microsoft.com/VisualStudio/2004/01/settings" CurrentProfile="(Default)" GeneratedClassNamespace="SCLOCVerse" GeneratedClassName="Settings">
  <Profiles />
  <Settings>
    <Setting Name="LastAppVersion" Type="System.String" Scope="User">
      <Value Profile="(Default)" />
    </Setting>
    <Setting Name="GameFolder" Type="System.String" Scope="User">
      <Value Profile="(Default)" />
    </Setting>
    <Setting Name="UpdateChannel" Type="System.String" Scope="User">
      <Value Profile="(Default)">Stable</Value>
    </Setting>
    <Setting Name="UpgradeRequired" Type="System.Boolean" Scope="User">
      <Value Profile="(Default)">False</Value>
    </Setting>
    <Setting Name="HangarOverlayX" Type="System.Double" Scope="User">
      <Value Profile="(Default)">20</Value>
    </Setting>
    <Setting Name="HangarOverlayY" Type="System.Double" Scope="User">
      <Value Profile="(Default)">20</Value>
    </Setting>
    <Setting Name="HangarOverlayScale" Type="System.Double" Scope="User">
      <Value Profile="(Default)">0.6</Value>
    </Setting>
    <Setting Name="HangarOverlayOpacity" Type="System.Double" Scope="User">
      <Value Profile="(Default)">0.92</Value>
    </Setting>
    <Setting Name="HangarCycleStartOverride" Type="System.Int64" Scope="User">
      <Value Profile="(Default)">0</Value>
    </Setting>
    <Setting Name="InputSystemBackend" Type="System.String" Scope="User">
      <Value Profile="(Default)">RawInput</Value>
    </Setting>
    <Setting Name="InputSystemDiagnostics" Type="System.Boolean" Scope="User">
      <Value Profile="(Default)">False</Value>
    </Setting>
    <Setting Name="RunAtStartup" Type="System.Boolean" Scope="User">
      <Value Profile="(Default)">False</Value>
    </Setting>
    <Setting Name="MinimizeToTray" Type="System.Boolean" Scope="User">
      <Value Profile="(Default)">True</Value>
    </Setting>
    <Setting Name="AutoUpdateLocalization" Type="System.Boolean" Scope="User">
      <Value Profile="(Default)">False</Value>
    </Setting>
    <Setting Name="LastLocalizationToast" Type="System.String" Scope="User">
      <Value Profile="(Default)" />
    </Setting>
    <Setting Name="LastLiaToast" Type="System.String" Scope="User">
      <Value Profile="(Default)" />
    </Setting>
    <Setting Name="LastAppToast" Type="System.String" Scope="User">
      <Value Profile="(Default)" />
    </Setting>
    <Setting Name="LastToastTimestampUtc" Type="System.String" Scope="User">
      <Value Profile="(Default)" />
    </Setting>
  </Settings>
</SettingsFile>
```

### 6.2. Settings.Designer.cs

Фактичний шлях: `F:\C#\SCLocalizationUA\SCLOCVerse\Settings.Designer.cs`.

Клас `SCLOCVerse.Settings`, internal sealed partial, успадковує `ApplicationSettingsBase`. Містить 18 UserScopedSetting властивостей з відповідними `DefaultSettingValueAttribute`. Примітка рядків 11-16: додаткові поля додані вручно через CLI MSBuild.

---

## 7. SCLOCVerse.ControlCenter\Data\ControlCenterRepository.cs

Повний файл. Нижче — точний SQL текст кожного публічного методу та приватних query helpers.

### 7.1. GetOverviewDataAsync

Виконує 4 запити через одне підключення:

1. `SELECT incident_id, component, operation, signal, highest_severity, status, event_count, affected_installs, affected_users, opened_at, last_event_at FROM control_center.incidents WHERE status != 'Closed' ORDER BY opened_at DESC`
2. `SELECT component, health, active_incidents, last_event_at FROM control_center.component_health`
3. `SELECT app_version, succeeded, failed, active_installs FROM control_center.release_health LIMIT 5`
4. `SELECT events_24h, active_users_24h, active_installations, open_incidents FROM control_center.platform_stats`

### 7.2. GetIncidentsAsync

```sql
SELECT incident_id, component, operation, signal, highest_severity, status, event_count, affected_installs, affected_users, opened_at, last_event_at FROM control_center.incidents {where} ORDER BY opened_at DESC
```

`where` = `WHERE status = $1` (з параметром `statusFilter`) або `WHERE status != 'Closed'`.

### 7.3. GetIncidentDetailAsync

Комбінує кілька запитів:

1. Інцидент:
```sql
SELECT incident_id, id, fingerprint_key, release, component, operation, signal,
       root_event_id, last_event_id, opened_at, last_event_at, closed_at,
       status, highest_severity, peak_failure_pct, affected_users, affected_installs, event_count, owner
FROM control_center.incidents WHERE incident_id = $1
```

2. Root event summary:
```sql
SELECT id, correlation_id, step, component, operation, outcome,
       COALESCE(hresult, supabase_code, http_status::text, exception_type, '-'),
       occurred_at, error_message, duration_ms, source, http_status, hresult, supabase_code
FROM control_center.telemetry_events WHERE id = $1
```

3. Trace:
```sql
SELECT step, component, operation, outcome,
       COALESCE(hresult, supabase_code, http_status::text, exception_type, '-'),
       occurred_at, error_message, duration_ms
FROM control_center.telemetry_events WHERE correlation_id = $1 ORDER BY step
```

4. Related events:
```sql
SELECT id, correlation_id, step, component, operation, outcome,
       COALESCE(hresult, supabase_code, http_status::text, exception_type, '-'),
       occurred_at, error_message, duration_ms, source, http_status, hresult, supabase_code
FROM control_center.telemetry_events
WHERE component = $1 AND operation = $2 AND app_version = $3
  AND COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') = $4
  AND outcome = 'Failed'
ORDER BY occurred_at DESC LIMIT 20
```

5. Timeline:
```sql
SELECT id, incident_id, from_status, to_status, changed_by, changed_at, note FROM control_center.incident_timeline WHERE incident_id = $1 ORDER BY changed_at ASC
```

6. Notes:
```sql
SELECT id, incident_id, content, created_by, created_at FROM control_center.incident_notes_view WHERE incident_id = $1 ORDER BY created_at ASC
```

7. KnownSolution (exact match):
```sql
SELECT d.id, d.title, d.symptoms, d.known_cause, d.workaround, d.permanent_fix,
       d.fixed_version, d.confidence, d.status,
       COALESCE(array_to_string(d.affected_versions, ','), '') AS affected_versions,
       d.updated_at, d.references
FROM public.match_knowledge_for_incident($1) m
JOIN control_center.knowledge_entry_detail d ON d.id = m.knowledge_id
```

8. SimilarSolution (priority 2 match):
```sql
SELECT d.id, d.title, d.symptoms, d.known_cause, d.workaround, d.permanent_fix,
       d.fixed_version, d.confidence, d.status,
       COALESCE(array_to_string(d.affected_versions, ','), '') AS affected_versions,
       d.updated_at, d.references
FROM public.match_knowledge_priority2($1) m
JOIN control_center.knowledge_entry_detail d ON d.id = m.knowledge_id
```

9. Draft knowledge:
```sql
SELECT id, status FROM control_center.knowledge_entry_detail
WHERE fingerprint_key = $1 AND status NOT IN ('Verified','Archived')
ORDER BY updated_at DESC, id DESC LIMIT 1
```

### 7.4. CreateKnowledgeFromIncidentAsync

```sql
SELECT knowledge_id, created_new FROM public.create_knowledge_from_incident($1,$2,$3,$4,$5)
```

### 7.5. CanCreateKnowledgeForIncidentAsync

```sql
SELECT public.can_create_knowledge_for_incident($1)
```

### 7.6. UpdateKnowledgeAsync

```sql
SELECT public.update_knowledge_entry($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
```

### 7.7. TransitionKnowledgeAsync

```sql
SELECT public.transition_knowledge($1,$2,$3,$4,$5)
```

### 7.8. AddKnowledgeReferenceAsync

```sql
SELECT public.add_knowledge_reference($1,$2,$3,$4,$5)
```

### 7.9. RemoveKnowledgeReferenceAsync

```sql
SELECT public.remove_knowledge_reference($1,$2)
```

### 7.10. GetKnowledgeHistoryAsync

```sql
SELECT version, changed_by, db_user, changed_at, change_reason, change_type FROM public.get_knowledge_history($1)
```

### 7.11. GetKnowledgeCurrentVersionAsync

```sql
SELECT public.get_knowledge_current_version($1)
```

### 7.12. CanEditKnowledgeAsync

```sql
SELECT public.can_edit_knowledge($1)
```

### 7.13. GetKnowledgeCoverageAsync

```sql
SELECT total_fingerprints, covered_fingerprints, uncovered_fingerprints, coverage_pct FROM control_center.knowledge_coverage
```

### 7.14. GetTopMissingKnowledgeAsync

```sql
SELECT component, signal, fingerprint_count, total_events, last_seen, highest_severity FROM control_center.top_missing_knowledge
```

### 7.15. SearchKnowledgeAsync

```sql
SELECT * FROM public.search_knowledge($1,$2,$3,$4,$5,$6)
```

### 7.16. RunKnowledgeAutoVerifyAsync

```sql
SELECT knowledge_id, title, reason FROM public.verify_knowledge_auto()
```

### 7.17. RefreshKnowledgeCoverageAsync

```sql
SELECT public.refresh_knowledge_coverage()
```

### 7.18. GetReleaseHealthAsync

```sql
SELECT app_version, succeeded, failed, success_rate, active_installs, users_24h,
       new_incidents, critical_incidents, first_seen, last_seen,
       top_fingerprint, top_component, top_signal, top_event_count, top_severity
FROM control_center.release_health_detail
```

### 7.19. TransitionIncidentAsync

```sql
SELECT transition_incident($1,$2,$3,$4)
```

### 7.20. AddIncidentNoteAsync

```sql
SELECT add_incident_note($1,$2,$3)
```

### 7.21. AssignOwnerAsync

```sql
SELECT assign_incident_owner($1,$2)
```

### 7.22. Private helpers

- `QueryIncidentsAsync`: див. 7.2.
- `QueryComponentHealthAsync`: див. 7.1.
- `QueryReleaseHealthAsync`: див. 7.1.
- `QueryPlatformStatsAsync`: див. 7.1.
- `QueryIncidentByIdAsync`: див. 7.3.
- `QueryEventSummaryAsync`: див. 7.3.
- `QueryTraceAsync`: див. 7.3.
- `QueryRelatedEventsAsync`: див. 7.3.
- `ReadTimelineAsync`: див. 7.3.
- `ReadNotesAsync`: див. 7.3.
- `QueryDraftKnowledgeAsync`: див. 7.3.
- `MatchKnowledgePriority2Async`: див. 7.3 (priority 2).
- `GetKnownSolutionAsync`: див. 7.3 (priority 1).

---

## 8. SCLOCVerse.Notifier\Dispatcher\NotificationDispatcher.cs

Повний файл. Нижче — точний SQL текст claim, update, insert.

### 8.1. Zombie Recovery (рядки 119-127)

```sql
UPDATE public.notification_queue
SET status = 'RetryScheduled',
    claimed_at = NULL,
    claimed_by = NULL,
    last_error = COALESCE(NULLIF(last_error, '') || ' | ', '') || 'zombie recovery after ' || @timeout
WHERE status = 'Sending'
  AND claimed_at < now() - (@timeout || ' minutes')::interval
```

### 8.2. Claim batch (рядки 144-163)

```sql
WITH picked AS (
    SELECT id FROM public.notification_queue
    WHERE status IN ('Pending','RetryScheduled')
      AND (next_attempt_at IS NULL OR next_attempt_at <= now())
      AND retry_count < max_retries
    ORDER BY created_at
    LIMIT @batch
    FOR UPDATE SKIP LOCKED
)
UPDATE public.notification_queue q
SET status = 'Sending',
    claimed_at = now(),
    claimed_by = @instance,
    last_attempt_at = now(),
    retry_count = retry_count + 1
FROM picked
WHERE q.id = picked.id
RETURNING q.id, q.incident_id, q.notification_type, q.provider, q.payload, q.retry_count, q.max_retries
```

### 8.3. SELECT incident data (рядки 188-194)

```sql
SELECT id,
       'INC-' || to_char(opened_at,'YYYY') || '-' || lpad(id::text,5,'0'),
       component, operation, signal, COALESCE(highest_severity,'Warning'), release
FROM public.telemetry_incidents
WHERE id = ANY(@ids)
```

### 8.4. INSERT attempt 'Sending' (рядки 321-325)

```sql
INSERT INTO public.notification_attempts (queue_id, attempt_no, provider, status, started_at)
VALUES (@qid, @attempt, @provider, 'Sending', now())
RETURNING id
```

### 8.5. UPDATE attempt final status (рядки 334-342)

```sql
UPDATE public.notification_attempts
SET status = @status,
    http_status = @http,
    provider_message_id = @msg,
    error_message = @err,
    finished_at = now()
WHERE id = @id
```

### 8.6. UPDATE queue final status (рядки 364-373)

```sql
UPDATE public.notification_queue
SET status = @status,
    delivered_at = CASE WHEN @success THEN now() ELSE delivered_at END,
    claimed_at = NULL,
    claimed_by = NULL,
    next_attempt_at = {nextAttempt},
    last_error = @err
WHERE id = @id
```

`{nextAttempt}` — динамічно: `NULL` при success/permanent, інакше `now() + (power(2, {item.AttemptNo}) || ' seconds')::interval`.

---

## 9. Джерела

- `F:\C#\SCLocalizationUA\docs\observability\FORENSIC-DATA-PIPELINE-RAW.md`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Models\Auth\AppInstallation.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\Auth\InstallationService.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\Auth\DiscordGuildSyncService.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Models\Observability\TelemetryEvent.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Models\Observability\TelemetryContext.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\Observability\TelemetryUploader.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\Observability\ErrorContextExtractor.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\Observability\TelemetryClient.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\Observability\PrivacySanitizer.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateDownloader.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateInstaller.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\ApplicationUpdate\UpdateVerifier.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Services\LiaServices\Updater.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Settings.settings`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Settings.Designer.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse\Composition\AppCompositionRoot.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse.ControlCenter\Data\ControlCenterRepository.cs`
- `F:\C#\SCLocalizationUA\SCLOCVerse.Notifier\Dispatcher\NotificationDispatcher.cs`
- `F:\C#\SCLocalizationUA\supabase\migrations\20260630000001_create_app_installations.sql`
- `F:\C#\SCLocalizationUA\supabase\migrations\20260630000009_create_telemetry_events.sql`
- `F:\C#\SCLocalizationUA\supabase\migrations\20260630000012_incident_tables.sql`
- `F:\C#\SCLocalizationUA\supabase\migrations\20260630000016_notification_engine.sql`
- `F:\C#\SCLocalizationUA\supabase\migrations\20260704000017_notification_audit_and_retry.sql`
- `F:\C#\SCLocalizationUA\supabase\migrations\20260704000018_cc_notifier_role.sql`
- `F:\C#\SCLocalizationUA\supabase\migrations\20260704000019_knowledge_engine_core.sql`
- `F:\C#\SCLocalizationUA\supabase\migrations\20260704000022_knowledge_workflow.sql`
- `F:\C#\SCLocalizationUA\supabase\migrations\20260704000023_knowledge_coverage.sql`
- `F:\C#\SCLocalizationUA\supabase\migrations\20260705021100_observability_pipeline_automation.sql`
- `F:\C#\SCLocalizationUA\supabase\migrations\20260705021200_observability_health_view.sql`
- `F:\C#\SCLocalizationUA\supabase\migrations\20260705030000_auto_close_on_event.sql`
