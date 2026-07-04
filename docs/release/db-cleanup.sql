-- ============================================================================
-- SCLOC-Verse 1.0.0.1 — Production DB Cleanup Script
-- ============================================================================
-- Призначення:  Підготовка абсолютно чистої Production БД перед релізом.
-- Проєкт:       nrytczdbhehiotflaagl
-- Дата:         2026-07-04
-- Режим:        Ідемпотентний. Виконується в одній транзакції.
--
-- ЩО ВИДАЛЯЄ:
--   - Усі observability дані (telemetry, incidents, knowledge, notifications).
--   - Усі alpha/beta installations (версія 1.0.0.0).
--   - Усі тестові записи (install-1, INC-2026-00008, abc123).
--
-- ЩО НЕ ТОРКАЄТЬСЯ:
--   - incident_policy (production пороги компонентів).
--   - auth.* (користувачі Supabase Auth, сесії).
--   - supabase_migrations.schema_migrations (історія міграцій).
--   - realtime.*, storage.* (системні схеми Supabase).
--   - RLS, функції, VIEW, схеми, розширення.
--
-- ПОРЯДОК:
--   1. Knowledge (version_history → references → entries) — через RESTRICT FK.
--   2. Notifications (attempts → queue).
--   3. Incidents (notes → status_log → incidents).
--   4. Telemetry events.
--   5. App installations (тільки observability — НЕ auth).
--   6. REFRESH materialized view knowledge_coverage.
--
-- ВИКОНАННЯ:
--   psql:    psql "$DATABASE_URL" -f db-cleanup.sql
--   Supabase: виконати через SQL Editor (dashboard).
-- ============================================================================

BEGIN;

-- ── 1. Knowledge Engine (RESTRICT FK — порядок обов'язковий) ────────────────
DELETE FROM public.knowledge_version_history;
DELETE FROM public.knowledge_references;
DELETE FROM public.knowledge_entries;

-- ── 2. Notification Engine ──────────────────────────────────────────────────
DELETE FROM public.notification_attempts;
DELETE FROM public.notification_queue;

-- ── 3. Incident Engine (CASCADE від telemetry_incidents, але явно для безпеки)
DELETE FROM public.incident_notes;
DELETE FROM public.incident_status_log;
DELETE FROM public.telemetry_incidents;

-- ── 4. Telemetry Events ─────────────────────────────────────────────────────
DELETE FROM public.telemetry_events;

-- ── 5. App Installations (observability) ────────────────────────────────────
--    УВАГА: app_installations має FK до auth.users через user_id, але user_id
--    nullable. Видаляємо лише observability installations.
--    Auth користувачі (auth.users) НЕ зачіпаються.
DELETE FROM public.app_installations;

-- ── 6. REFRESH Materialized Views ───────────────────────────────────────────
REFRESH MATERIALIZED VIEW control_center.knowledge_coverage;

COMMIT;

-- ============================================================================
-- ПІДТВЕРДЖЕННЯ (не частина транзакції — виконати окремо після COMMIT)
-- ============================================================================
SELECT 'CLEANUP COMPLETE' AS status,
       (SELECT count(*) FROM public.telemetry_events) AS events_left,
       (SELECT count(*) FROM public.telemetry_incidents) AS incidents_left,
       (SELECT count(*) FROM public.knowledge_entries) AS knowledge_left,
       (SELECT count(*) FROM public.notification_queue) AS notifications_left,
       (SELECT count(*) FROM public.app_installations) AS installations_left;
