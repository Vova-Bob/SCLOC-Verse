-- Міграція 00017: Delivery Audit (Стаття 26) + retry + zombie recovery fields
-- Additive only. Не чіпає існуючі дані.
-- Застосовано до робочої БД: 2026-07-04.

-- 1. Розширити CHECK статусів черги
ALTER TABLE public.notification_queue
    DROP CONSTRAINT chk_notif_status;
ALTER TABLE public.notification_queue
    ADD CONSTRAINT chk_notif_status CHECK (status IN
        ('Pending','Sending','Delivered','Failed','RetryScheduled'));

-- 2. Колонки retry/zombie (additive)
ALTER TABLE public.notification_queue
    ADD COLUMN IF NOT EXISTS next_attempt_at timestamptz,
    ADD COLUMN IF NOT EXISTS max_retries int NOT NULL DEFAULT 3,
    ADD COLUMN IF NOT EXISTS claimed_at timestamptz,
    ADD COLUMN IF NOT EXISTS claimed_by text,
    ADD COLUMN IF NOT EXISTS last_error text;

-- 3. Append-only audit (Стаття 26 + Стаття 6)
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

-- 4. DROP + CREATE VIEW (OR REPLACE заборонено при зміні порядку колонок)
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
