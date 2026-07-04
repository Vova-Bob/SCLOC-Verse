-- Phase 5b — Notification Engine: queue table + promotion hook
-- Стаття 24: Notification Independence (Incident Engine не відправляє, лише queue).
-- Стаття 25: Notification Idempotency (UNIQUE incident_id + notification_type).

CREATE TABLE IF NOT EXISTS public.notification_queue (
    id                bigserial PRIMARY KEY,
    incident_id       bigint NOT NULL REFERENCES public.telemetry_incidents(id) ON DELETE CASCADE,
    notification_type text NOT NULL,           -- IncidentCreated, IncidentEscalated, IncidentClosed
    provider          text NOT NULL DEFAULT 'Discord',  -- Discord, Email, Telegram, Teams
    status            text NOT NULL DEFAULT 'Pending',  -- Pending, Delivered, Failed
    payload           jsonb,                   -- {incident_id, component, signal, severity, affected}
    retry_count       int NOT NULL DEFAULT 0,
    last_attempt_at   timestamptz,
    delivered_at      timestamptz,
    error_message     text,
    created_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_notif_status CHECK (status IN ('Pending','Delivered','Failed')),
    CONSTRAINT chk_notif_type CHECK (notification_type IN ('IncidentCreated','IncidentEscalated','IncidentClosed'))
);

-- Idempotency: один notification_type на incident (поки не Failed — Failed дозволяє retry)
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

-- VIEW для Dashboard (READ ONLY)
CREATE OR REPLACE VIEW control_center.notifications AS
SELECT n.id, n.incident_id, n.notification_type, n.provider, n.status,
       n.retry_count, n.last_attempt_at, n.delivered_at, n.error_message,
       n.created_at,
       'INC-' || to_char(i.opened_at,'YYYY') || '-' || lpad(i.id::text,5,'0') AS incident_code,
       i.component, i.signal, i.highest_severity
FROM public.notification_queue n
JOIN public.telemetry_incidents i ON i.id = n.incident_id
ORDER BY n.created_at DESC;

-- Оновити promote_incident_candidates: при створенні інциденту → enqueue notification
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
    -- Auto-close
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

            -- Стаття 24: enqueue notification (не відправляємо! лише queue)
            -- Стаття 25: UNIQUE(incident_id, notification_type) гарантує idempotency
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