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
--   - auth.users — РЕАЛЬНІ користувачі (збережено). Видаляються лише
--     тестові акаунти (test@*, *@example.com, UUID-патерни).
--   - app_installations — реальні UUID (business history). Видаляються
--     лише не-UUID тестові install_id.
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

-- ── 5. App Installations (Business State — НЕ повне очищення) ──────────────
--    УВАГА: app_installations — це HYBRID таблиця:
--      • Operational State: last_seen, country, app_version, is_active
--        (відновлюються з клієнта + Cloudflare-тригер)
--      • Business History:   first_seen, created_at
--        (НЕ відновлюються — це adoption-метрика)
--    Тому НЕ очищаємо повністю. Видаляємо лише гарантовано тестові
--    install_id (не-UUID патерн: 'install-1', 'sim-install-*' тощо).
--    Реальні UUID (32 hex) залишаються як business history.
DELETE FROM public.app_installations
WHERE install_id !~ '^[0-9a-f]{32}$';

-- ── 6. Тестові користувачі auth.users ───────────────────────────────────────
--    Видаляємо лише гарантовано тестові акаунти:
--      • email test@* / *@example.com
--      • UUID з патерном усіх однакових hex-цифр (33333..., 00000..., fffff...)
--    Реальні користувачі (38) ЗАЛИШАЮТЬСЯ.
--    FK: app_installations/error_reports/admin_audit_log/telemetry_events
--    мають ON DELETE NO ACTION — тому перед DELETE user треба переконатись,
--    що немає записів з цим user_id. Для тестових акаунтів це так (вони
--    створювались без installation/session).
DELETE FROM auth.users
WHERE email LIKE 'test@%'
   OR email LIKE '%@example.com'
   OR id::text ~ '^(00000000|11111111|22222222|33333333|44444444|55555555|66666666|77777777|88888888|99999999|aaaaaaaa|bbbbbbbb|cccccccc|dddddddd|eeeeeeee|ffffffff)-';

-- ── 7. REFRESH Materialized Views ───────────────────────────────────────────
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
       (SELECT count(*) FROM public.app_installations) AS installations_kept,
       (SELECT count(*) FROM auth.users) AS auth_users_kept;
