-- Rollback для міграції 20260707030002_phase3a_incident_code_generated.sql
--
-- Аварійний відкат: повертає формування `incident_code`/`incident_id` через вираз
-- у 4 views, потім DROP generated column.
--
-- Порядок критичний:
--   1) спершу CREATE OR REPLACE 4 views з виразом (вони перестають залежати від
--      generated column);
--   2) лише потім ALTER TABLE ... DROP COLUMN — інакше PostgreSQL заблокує DROP
--      через залежність views.
--
-- Після rollback стан схеми ідентичний production до міграції (to_char вираз).

-- =========================================================================
-- 1. Повернути control_center.incidents з виразом (computed alias incident_id)
-- =========================================================================
CREATE OR REPLACE VIEW control_center.incidents AS
SELECT (('INC-' || to_char(opened_at, 'YYYY')) || '-') || lpad(id::text, 5, '0') AS incident_id,
       id,
       fingerprint_key,
       fingerprint_hash,
       release,
       component,
       operation,
       signal,
       root_event_id,
       last_event_id,
       opened_at,
       last_event_at,
       closed_at,
       status,
       highest_severity,
       peak_failure_pct,
       affected_users,
       affected_installs,
       event_count,
       owner
FROM public.telemetry_incidents
ORDER BY opened_at DESC;

GRANT SELECT ON control_center.incidents TO cc_readonly;

-- =========================================================================
-- 2. Повернути control_center.incident_timeline з виразом incident_code
-- =========================================================================
CREATE OR REPLACE VIEW control_center.incident_timeline AS
SELECT l.id,
       l.incident_id,
       l.from_status,
       l.to_status,
       l.changed_by,
       l.changed_at,
       l.note,
       (('INC-' || to_char(i.opened_at, 'YYYY')) || '-') || lpad(i.id::text, 5, '0') AS incident_code
FROM public.incident_status_log l
JOIN public.telemetry_incidents i ON i.id = l.incident_id
ORDER BY l.changed_at DESC;

GRANT SELECT ON control_center.incident_timeline TO cc_readonly;

-- =========================================================================
-- 3. Повернути control_center.incident_notes_view з виразом incident_code
-- =========================================================================
CREATE OR REPLACE VIEW control_center.incident_notes_view AS
SELECT n.id,
       n.incident_id,
       n.content,
       n.created_by,
       n.created_at,
       (('INC-' || to_char(i.opened_at, 'YYYY')) || '-') || lpad(i.id::text, 5, '0') AS incident_code
FROM public.incident_notes n
JOIN public.telemetry_incidents i ON i.id = n.incident_id
ORDER BY n.created_at DESC;

GRANT SELECT ON control_center.incident_notes_view TO cc_readonly;

-- =========================================================================
-- 4. Повернути control_center.notifications з виразом incident_code
-- =========================================================================
CREATE OR REPLACE VIEW control_center.notifications AS
SELECT n.id,
       n.incident_id,
       n.notification_type,
       n.provider,
       n.status,
       n.retry_count,
       n.max_retries,
       n.last_attempt_at,
       n.next_attempt_at,
       n.claimed_at,
       n.claimed_by,
       n.last_error,
       n.delivered_at,
       n.error_message,
       n.created_at,
       (('INC-' || to_char(i.opened_at, 'YYYY')) || '-') || lpad(i.id::text, 5, '0') AS incident_code,
       i.component,
       i.signal,
       i.highest_severity,
       i.release
FROM public.notification_queue n
JOIN public.telemetry_incidents i ON i.id = n.incident_id
ORDER BY n.created_at DESC;

GRANT SELECT ON control_center.notifications TO cc_readonly;

-- =========================================================================
-- 5. DROP generated column (тепер безпечно — views більше не посилаються)
-- =========================================================================
ALTER TABLE public.telemetry_incidents DROP COLUMN IF EXISTS incident_code;
