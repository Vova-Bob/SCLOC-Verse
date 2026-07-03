-- Phase 5 — Incident Management Engine
-- Стаття 22: Incident History Is Immutable (append-only journal).
-- Стаття 23: Workflow через SECURITY DEFINER функції (контрольований write-path).

-- 1. incident_status_log — append-only журнал переходів
CREATE TABLE IF NOT EXISTS public.incident_status_log (
    id          bigserial PRIMARY KEY,
    incident_id bigint NOT NULL REFERENCES public.telemetry_incidents(id) ON DELETE CASCADE,
    from_status text NOT NULL,
    to_status   text NOT NULL,
    changed_by  text NOT NULL DEFAULT 'system',
    changed_at  timestamptz NOT NULL DEFAULT now(),
    note        text
);

-- 2. incident_notes — append-only нотатки
CREATE TABLE IF NOT EXISTS public.incident_notes (
    id          bigserial PRIMARY KEY,
    incident_id bigint NOT NULL REFERENCES public.telemetry_incidents(id) ON DELETE CASCADE,
    content     text NOT NULL,
    created_by  text NOT NULL DEFAULT 'admin',
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- 3. owner column
ALTER TABLE public.telemetry_incidents ADD COLUMN IF NOT EXISTS owner text;

-- RLS: deny direct access (writes only via SECURITY DEFINER functions)
ALTER TABLE public.incident_status_log ENABLE ROW LEVEL SECURITY;
CREATE POLICY "deny all on incident_status_log" ON public.incident_status_log
    AS RESTRICTIVE FOR ALL TO anon, authenticated, cc_readonly
    USING (false) WITH CHECK (false);

ALTER TABLE public.incident_notes ENABLE ROW LEVEL SECURITY;
CREATE POLICY "deny all on incident_notes" ON public.incident_notes
    AS RESTRICTIVE FOR ALL TO anon, authenticated, cc_readonly
    USING (false) WITH CHECK (false);

-- 4. transition_incident() — єдиний спосіб змінити статус (Стаття 22/23)
CREATE OR REPLACE FUNCTION public.transition_incident(
    p_incident_id bigint,
    p_to_status text,
    p_changed_by text DEFAULT 'admin',
    p_note text DEFAULT NULL
) RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_current text;
    v_order int;
    v_to_order int;
BEGIN
    SELECT status INTO v_current FROM public.telemetry_incidents WHERE id = p_incident_id;
    IF NOT FOUND THEN RAISE EXCEPTION 'Incident % not found', p_incident_id; END IF;

    v_order := CASE v_current
        WHEN 'Active' THEN 1 WHEN 'Confirmed' THEN 2 WHEN 'Investigating' THEN 3
        WHEN 'Mitigated' THEN 4 WHEN 'Resolved' THEN 5 WHEN 'Closed' THEN 6
        ELSE 0 END;
    v_to_order := CASE p_to_status
        WHEN 'Active' THEN 1 WHEN 'Confirmed' THEN 2 WHEN 'Investigating' THEN 3
        WHEN 'Mitigated' THEN 4 WHEN 'Resolved' THEN 5 WHEN 'Closed' THEN 6
        ELSE 0 END;

    IF v_to_order <= v_order THEN
        RAISE EXCEPTION 'Invalid transition: % -> % (forward only)', v_current, p_to_status;
    END IF;

    UPDATE public.telemetry_incidents
    SET status = p_to_status,
        closed_at = CASE WHEN p_to_status = 'Closed' THEN now() ELSE closed_at END
    WHERE id = p_incident_id;

    INSERT INTO public.incident_status_log (incident_id, from_status, to_status, changed_by, note)
    VALUES (p_incident_id, v_current, p_to_status, p_changed_by, p_note);
END;
$$;

-- 5. add_incident_note()
CREATE OR REPLACE FUNCTION public.add_incident_note(
    p_incident_id bigint, p_content text, p_created_by text DEFAULT 'admin'
) RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    INSERT INTO public.incident_notes (incident_id, content, created_by)
    VALUES (p_incident_id, p_content, p_created_by);
END;
$$;

-- 6. assign_incident_owner()
CREATE OR REPLACE FUNCTION public.assign_incident_owner(
    p_incident_id bigint, p_owner text
) RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    UPDATE public.telemetry_incidents SET owner = p_owner WHERE id = p_incident_id;
    INSERT INTO public.incident_status_log (incident_id, from_status, to_status, changed_by, note)
    SELECT p_incident_id, status, status, 'system', 'Owner: ' || p_owner
    FROM public.telemetry_incidents WHERE id = p_incident_id;
END;
$$;

-- 7. VIEWs for Dashboard (READ ONLY)
CREATE OR REPLACE VIEW control_center.incident_timeline AS
SELECT l.id, l.incident_id, l.from_status, l.to_status, l.changed_by, l.changed_at, l.note,
       'INC-' || to_char(i.opened_at,'YYYY') || '-' || lpad(i.id::text,5,'0') AS incident_code
FROM public.incident_status_log l
JOIN public.telemetry_incidents i ON i.id = l.incident_id
ORDER BY l.changed_at DESC;

CREATE OR REPLACE VIEW control_center.incident_notes_view AS
SELECT n.id, n.incident_id, n.content, n.created_by, n.created_at,
       'INC-' || to_char(i.opened_at,'YYYY') || '-' || lpad(i.id::text,5,'0') AS incident_code
FROM public.incident_notes n
JOIN public.telemetry_incidents i ON i.id = n.incident_id
ORDER BY n.created_at DESC;

-- 8. Grant EXECUTE to cc_readonly (Стаття 23 — контрольований write-path)
GRANT EXECUTE ON FUNCTION public.transition_incident(bigint, text, text, text) TO cc_readonly;
GRANT EXECUTE ON FUNCTION public.add_incident_note(bigint, text, text) TO cc_readonly;
GRANT EXECUTE ON FUNCTION public.assign_incident_owner(bigint, text) TO cc_readonly;