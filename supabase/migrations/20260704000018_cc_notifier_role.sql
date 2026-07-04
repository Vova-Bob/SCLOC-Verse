-- Міграція 00018: роль cc_notifier для Worker (Notification Dispatcher).
-- cc_notifier має права ТІЛЬКИ на notification_queue + notification_attempts + читання incident.
-- НЕ має доступу до telemetry_events чи control_center-схеми напряму через insert/update.
-- Застосовано до робочої БД: 2026-07-04.
--
-- Пароль задається окремо через Supabase Dashboard / secure channel
-- і ніколи не зберігається в SCLOC-Verse репозиторії (Стаття 29 — Secret Independence).

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cc_notifier') THEN
        CREATE ROLE cc_notifier WITH LOGIN NOCREATEDB NOCREATEROLE NOSUPERUSER;
    END IF;
END $$;

-- Базові права на схему та таблиці
GRANT USAGE ON SCHEMA public TO cc_notifier;
GRANT SELECT, INSERT, UPDATE ON public.notification_queue TO cc_notifier;
GRANT SELECT, INSERT, UPDATE ON public.notification_attempts TO cc_notifier;
GRANT SELECT ON public.telemetry_incidents TO cc_notifier;

-- Sequences (для INSERT ... DEFAULT nextval)
GRANT USAGE, SELECT ON SEQUENCE public.notification_attempts_id_seq TO cc_notifier;
GRANT USAGE, SELECT ON SEQUENCE public.notification_queue_id_seq TO cc_notifier;

-- RLS: cc_notifier має повний доступ до таблиць сповіщень
DROP POLICY IF EXISTS "allow cc_notifier notification_queue" ON public.notification_queue;
CREATE POLICY "allow cc_notifier notification_queue" ON public.notification_queue
    FOR ALL TO cc_notifier USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS "allow cc_notifier notification_attempts" ON public.notification_attempts;
CREATE POLICY "allow cc_notifier notification_attempts" ON public.notification_attempts
    FOR ALL TO cc_notifier USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS "allow cc_notifier read incidents" ON public.telemetry_incidents;
CREATE POLICY "allow cc_notifier read incidents" ON public.telemetry_incidents
    FOR SELECT TO cc_notifier USING (true);
