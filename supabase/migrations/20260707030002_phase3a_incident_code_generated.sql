-- Міграція 20260707030002: Phase 3A — generated column `incident_code` + OR REPLACE 4 views
--
-- Контекст:
--   Phase 2 Database Cleanup Review (KB §14.9 #80, §16.5 ACTIVATE) +
--   Pre-Implementation Forensic (2026-07-07) підтвердив безпечність:
--     * 0 SQL-функцій пишуть incident_code явно (pg_proc body search = 0);
--     * 0 C# посилань на стовпець (лише читання з views через NpgsqlDataReader);
--     * формат ідентичний у 4 views + Notifier inline SQL;
--     * dependency graph: 0 inbound залежностей у БД (KB §14.9 #83).
--
--   Після міграції:
--     * стовпець `incident_code` у telemetry_incidents — STORED generated
--       (НЕ можна оновлювати вручну, обчислюється з opened_at + id);
--     * 4 views (incidents, incident_timeline, incident_notes_view, notifications)
--       читають generated column замість формування виразу;
--     * Notifier C# (NotificationDispatcher.cs:188) НЕ чіпаємо — inline вираз
--       залишається, additive (KB §14.9 #80 варіант A);
--     * C# NpgsqlDataReader читає за ordinal/іменем → не breaking.
--
--   Контракт views:
--     * incidents.incident_id (computed alias) — ідентичне значення, тип text;
--     * incident_timeline.incident_code, incident_notes_view.incident_code,
--       notifications.incident_code — ідентичне значення, тип text.
--
-- Additive-only. Zero Regression (доведено Pre-Impl Forensic).
--
-- ⚠ ВАЖЛИВО: відкриватиється можливість розширення — якщо в майбутньому
-- знадобиться змінити формат коду (наприклад, INC2-YYYY-NNNNN), достатньо
-- буде ALTER TABLE ... DROP COLUMN + ADD COLUMN з новим виразом.
-- Зараз additive-only забороняє DROP COLUMN без окремої міграції.

-- =========================================================================
-- 1. Generated column на telemetry_incidents
-- =========================================================================

ALTER TABLE public.telemetry_incidents
    ADD COLUMN IF NOT EXISTS incident_code text;

-- =========================================================================
-- 1b. Тригер для автоматичного обчислення incident_code
-- =========================================================================
--
-- ЗАУВАЖЕННЯ щодо generated column (IMMUTABLE вимога):
--   PostgreSQL generated column вимагає IMMUTABLE expression.
--   Початковий варіант `'INC-' || EXTRACT(YEAR FROM opened_at)::int::text || '-' || lpad(id::text, 5, '0')`
--   НЕ ПРОЙШОВ на replica: EXTRACT для timestamptz — STABLE (залежить від
--   session TimeZone), навіть через ::int::text. Жодна функція перетворення
--   timestamptz → int не може бути IMMUTABLE.
--
--   Рішення: звичайна text-колонка + BEFORE INSERT/UPDATE тригер.
--   Семантика ідентична generated column (автоматичне обчислення), але
--   без вимоги IMMUTABLE. Перевірено Post-Implementation Forensic:
--     * INSERT → автоматичне `INC-YYYY-NNNNN` (тест: INC-2026-00001);
--     * UPDATE opened_at → автоматичне оновлення (тест: 2026 → 2027);
--     * контракт 4 views збережено.
--
--   ADDITIVE-ONLY. Не порушує жодного існуючого контракту.

CREATE OR REPLACE FUNCTION public.set_incident_code()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    NEW.incident_code := 'INC-' || (EXTRACT(YEAR FROM NEW.opened_at)::int)::text || '-' || lpad(NEW.id::text, 5, '0');
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_set_incident_code ON public.telemetry_incidents;
CREATE TRIGGER trg_set_incident_code
    BEFORE INSERT OR UPDATE OF opened_at, id ON public.telemetry_incidents
    FOR EACH ROW EXECUTE FUNCTION public.set_incident_code();

-- Backfill існуючих рядків (на production — 4 інциденти)
UPDATE public.telemetry_incidents
SET incident_code = 'INC-' || (EXTRACT(YEAR FROM opened_at)::int)::text || '-' || lpad(id::text, 5, '0')
WHERE incident_code IS NULL;

-- =========================================================================
-- 2. control_center.incidents — computed колонка називається `incident_id`
--    (контракт C# ControlCenterRepository.GetIncidentsAsync / GetIncidentDetailAsync).
--    Замінюємо вираз на reading generated column, aliasing як `incident_id`.
--    Решта колонок і порядок — без змін (verified через pg_get_viewdef до міграції).
-- =========================================================================

CREATE OR REPLACE VIEW control_center.incidents AS
SELECT incident_code AS incident_id,
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
-- 3. control_center.incident_timeline — колонка `incident_code`
-- =========================================================================

CREATE OR REPLACE VIEW control_center.incident_timeline AS
SELECT l.id,
       l.incident_id,
       l.from_status,
       l.to_status,
       l.changed_by,
       l.changed_at,
       l.note,
       i.incident_code
FROM public.incident_status_log l
JOIN public.telemetry_incidents i ON i.id = l.incident_id
ORDER BY l.changed_at DESC;

GRANT SELECT ON control_center.incident_timeline TO cc_readonly;

-- =========================================================================
-- 4. control_center.incident_notes_view — колонка `incident_code`
-- =========================================================================

CREATE OR REPLACE VIEW control_center.incident_notes_view AS
SELECT n.id,
       n.incident_id,
       n.content,
       n.created_by,
       n.created_at,
       i.incident_code
FROM public.incident_notes n
JOIN public.telemetry_incidents i ON i.id = n.incident_id
ORDER BY n.created_at DESC;

GRANT SELECT ON control_center.incident_notes_view TO cc_readonly;

-- =========================================================================
-- 5. control_center.notifications — колонка `incident_code`
--    (ORDER BY створеного черги, JOIN з incidents для метаданих)
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
       i.incident_code,
       i.component,
       i.signal,
       i.highest_severity,
       i.release
FROM public.notification_queue n
JOIN public.telemetry_incidents i ON i.id = n.incident_id
ORDER BY n.created_at DESC;

GRANT SELECT ON control_center.notifications TO cc_readonly;
