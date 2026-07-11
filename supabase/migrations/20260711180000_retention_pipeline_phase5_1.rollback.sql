-- Rollback Phase 5.1: Retention Pipeline

-- 1. Unschedule cron job
DO $$
BEGIN
    PERFORM cron.unschedule('retention-pipeline-daily');
EXCEPTION WHEN OTHERS THEN
    NULL;
END;
$$;

-- 2. Drop dispatcher
DROP FUNCTION IF EXISTS public.run_retention_pipeline();

-- 3. Drop purge function
DROP FUNCTION IF EXISTS public.purge_old_telemetry_events(int);

-- 4. pg_cron extension залишаємо (може використовуватись іншими job-ами майбутніх фаз).
-- Для повного видалення: DROP EXTENSION IF EXISTS pg_cron;
