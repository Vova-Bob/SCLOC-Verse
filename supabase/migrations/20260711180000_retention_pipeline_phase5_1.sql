-- Phase 5.1: Retention Pipeline — telemetry_events (90d)
-- Additive-only. SECURITY DEFINER (owner postgres). pg_cron daily schedule.
-- FK ON DELETE SET NULL — безпечно для telemetry_incidents.
-- Dispatcher pattern: run_retention_pipeline() — єдина точка входу.

-- 1. pg_cron extension
CREATE EXTENSION IF NOT EXISTS pg_cron WITH SCHEMA extensions;

-- 2. Purge function — telemetry_events
CREATE OR REPLACE FUNCTION public.purge_old_telemetry_events(retention_days int DEFAULT 90)
RETURNS bigint
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_catalog
AS $$
DECLARE
    purged_count bigint;
BEGIN
    IF retention_days <= 0 THEN
        RAISE EXCEPTION 'retention_days must be positive, got %', retention_days;
    END IF;

    RAISE LOG '[Retention] purge_old_telemetry_events: start (retention_days=%)', retention_days;

    DELETE FROM public.telemetry_events
    WHERE received_at < now() - (retention_days * interval '1 day');

    GET DIAGNOSTICS purged_count = ROW_COUNT;

    RAISE LOG '[Retention] purge_old_telemetry_events: purged % rows', purged_count;

    RETURN purged_count;
END;
$$;

ALTER FUNCTION public.purge_old_telemetry_events(int) OWNER TO postgres;

-- 3. Dispatcher — єдина точка входу для всіх retention-задач
CREATE OR REPLACE FUNCTION public.run_retention_pipeline()
RETURNS TABLE(target_table text, rows_purged bigint)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_catalog
AS $$
BEGIN
    RAISE LOG '[Retention] run_retention_pipeline: start';

    -- Phase 5.1: telemetry_events (90d)
    RETURN QUERY
        SELECT 'telemetry_events'::text,
               public.purge_old_telemetry_events(90);

    -- Phase 5.2 (future): notification_queue (30d)
    -- RETURN QUERY SELECT 'notification_queue'::text, public.purge_old_notification_queue(30);

    -- Phase 5.3 (future): notification_attempts (90d)
    -- RETURN QUERY SELECT 'notification_attempts'::text, public.purge_old_notification_attempts(90);

    -- Phase 5.4 (future): telemetry_incidents (1y after closed_at)
    -- RETURN QUERY SELECT 'telemetry_incidents'::text, public.purge_old_incidents(365);

    RAISE LOG '[Retention] run_retention_pipeline: complete';
END;
$$;

ALTER FUNCTION public.run_retention_pipeline() OWNER TO postgres;

-- 4. Schedule daily 03:00 UTC
DO $$
BEGIN
    PERFORM cron.unschedule('retention-pipeline-daily');
EXCEPTION WHEN OTHERS THEN
    -- Job не існує — нормально при першому застосуванні.
END;
$$;

SELECT cron.schedule(
    'retention-pipeline-daily',
    '0 3 * * *',
    $$SELECT * FROM public.run_retention_pipeline()$$
);
