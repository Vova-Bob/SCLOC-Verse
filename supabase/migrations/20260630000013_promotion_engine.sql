-- Phase 4 — Incident Engine: Promotion Engine + Dashboard VIEW
--
-- Стаття 19: promotion тільки ЧИТАЄ telemetry_events. Пише лише в telemetry_incidents.
-- Стаття 20: Dashboard лише SELECT VIEW. Ніякого EXECUTE на функціях.
-- Стаття 21: рецидив = новий incident_id. Explicit logic (NO ON CONFLICT).

-- 1. promote_incident_candidates() — єдина функція, що створює/оновлює/закриває інциденти.
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
BEGIN
    -- 1. Auto-close: інциденти тихі > auto_close_after_minutes (per-component policy)
    UPDATE public.telemetry_incidents i
    SET status = 'Closed', closed_at = now()
    WHERE i.status != 'Closed'
      AND i.last_event_at < now() - (
        COALESCE(
          (SELECT auto_close_after_minutes FROM public.incident_policy p
           WHERE p.component = i.component AND p.enabled
           ORDER BY p.signal IS NULL DESC, p.operation IS NULL DESC LIMIT 1),
          (SELECT auto_close_after_minutes FROM public.incident_policy
           WHERE component = '_default' LIMIT 1),
          60
        ) || ' minutes'
      )::interval;

    -- 2. Обробити candidates (читає incident_candidates_live VIEW → ЧИТАЄ telemetry_events)
    FOR cand IN
        SELECT * FROM control_center.incident_candidates_live
    LOOP
        -- Знайти політику (найбільш специфічну → component → _default)
        SELECT * INTO pol FROM public.incident_policy
        WHERE component = cand.component AND enabled
        ORDER BY signal IS NULL DESC, operation IS NULL DESC LIMIT 1;

        IF NOT FOUND THEN
            SELECT * INTO pol FROM public.incident_policy
            WHERE component = '_default' AND enabled LIMIT 1;
        END IF;
        IF NOT FOUND THEN
            CONTINUE;
        END IF;

        -- Перевірити пороги
        IF cand.failed_now < pol.min_sample THEN CONTINUE; END IF;
        IF cand.failure_pct < pol.failure_threshold_pct THEN CONTINUE; END IF;

        new_sev := CASE WHEN cand.affected_installs >= pol.critical_affected_threshold
                        THEN 'Critical' ELSE 'Warning' END;

        -- Є відкритий інцидент з цим fingerprint?
        SELECT id INTO existing_id FROM public.telemetry_incidents
        WHERE fingerprint_key = cand.fingerprint_key AND status != 'Closed' LIMIT 1;

        IF existing_id IS NOT NULL THEN
            -- UPDATE існуючого
            SELECT id INTO last_id FROM public.telemetry_events
            WHERE component = cand.component AND operation = cand.operation
              AND COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') = cand.signal
              AND app_version = cand.release AND outcome = 'Failed'
            ORDER BY received_at DESC LIMIT 1;

            UPDATE public.telemetry_incidents SET
                last_event_at = now(),
                last_event_id = last_id,
                affected_installs = GREATEST(affected_installs, cand.affected_installs),
                affected_users = GREATEST(affected_users, cand.affected_users),
                peak_failure_pct = GREATEST(peak_failure_pct, cand.failure_pct),
                event_count = event_count + cand.failed_now,
                highest_severity = CASE
                    WHEN new_sev = 'Critical' OR highest_severity = 'Critical' THEN 'Critical'
                    ELSE 'Warning'
                END
            WHERE id = existing_id;

        ELSE
            -- INSERT (новий інцидент — Стаття 21: навіть якщо закритий з тим fingerprint існує)
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
                 cand.affected_users, cand.affected_installs, cand.failed_now);
        END IF;
    END LOOP;
END;
$$;

-- 2. control_center.incidents — READ ONLY VIEW для Dashboard (Стаття 20)
CREATE OR REPLACE VIEW control_center.incidents AS
SELECT
    'INC-' || to_char(opened_at, 'YYYY') || '-' || lpad(id::text, 5, '0') AS incident_id,
    id, fingerprint_key, fingerprint_hash, release, component, operation, signal,
    root_event_id, last_event_id, opened_at, last_event_at, closed_at,
    status, highest_severity, peak_failure_pct, affected_users, affected_installs, event_count
FROM public.telemetry_incidents
ORDER BY opened_at DESC;
