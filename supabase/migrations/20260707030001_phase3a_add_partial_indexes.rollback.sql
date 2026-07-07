-- Rollback для міграції 20260707030001_phase3a_add_partial_indexes.sql
--
-- Аварійний відкат: DROP 3 partial індексів, доданих у міграції Phase 0.
--
-- Індекси ADDITIVE, не несуть даних (індекси завжди похідні від таблиці).
-- DROP INDEX безпечний, не порушує схематичний контракт. UNIQUE/PK/constraints
-- (uniq_telemetry_client_event_id, telemetry_events_pkey) не зачіпаються.
--
-- Застосовується лише у разі виявлення критичної проблеми після міграції.

DROP INDEX IF EXISTS public.idx_telemetry_failed;
DROP INDEX IF EXISTS public.idx_telemetry_version_window;
DROP INDEX IF EXISTS public.idx_telemetry_occurred;
