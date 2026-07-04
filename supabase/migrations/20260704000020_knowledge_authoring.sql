-- Міграція 00020: Knowledge Engine Slice 2 — Authoring.
-- Заморожений дизайн v1.1.
--
-- Правила цього слайсу (узгоджені):
-- 1. Knowledge створюється ЛИШЕ з інциденту (не з нуля).
-- 2. Дозволені статуси інциденту: Mitigated, Resolved, Closed.
-- 3. Інцидент МАЄ мати Owner.
-- 4. Для одного fingerprint_hash може існувати лише один не-Archived запис.
-- 5. References, versioning, workflow — у наступних слайсах.

-- 1. SECURITY DEFINER: створення Draft Knowledge Entry з інциденту
CREATE OR REPLACE FUNCTION public.create_knowledge_from_incident(
    p_incident_id bigint,
    p_title text,
    p_known_cause text,
    p_workaround text,
    p_created_by text
)
RETURNS TABLE (knowledge_id bigint, created_new boolean)
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_incident public.telemetry_incidents%ROWTYPE;
    v_existing_id bigint;
    v_new_id bigint;
BEGIN
    -- 1.1 Знайти інцидент
    SELECT * INTO v_incident
    FROM public.telemetry_incidents
    WHERE id = p_incident_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Incident % not found', p_incident_id;
    END IF;

    -- 1.2 Статус інциденту має бути Mitigated, Resolved або Closed
    IF v_incident.status NOT IN ('Mitigated','Resolved','Closed') THEN
        RAISE EXCEPTION 'Knowledge can only be created for incidents in status Mitigated, Resolved or Closed (current: %)',
            v_incident.status;
    END IF;

    -- 1.3 Інцидент має мати Owner
    IF v_incident.owner IS NULL OR trim(v_incident.owner) = '' THEN
        RAISE EXCEPTION 'Knowledge can only be created for incidents with an assigned owner';
    END IF;

    -- 1.4 Перевірити, чи існує не-Archived запис для цього fingerprint
    SELECT ke.id INTO v_existing_id
    FROM public.knowledge_entries ke
    WHERE ke.fingerprint_hash = v_incident.fingerprint_hash
      AND ke.status != 'Archived'
    LIMIT 1;

    IF FOUND THEN
        RETURN QUERY SELECT v_existing_id, false;
        RETURN;
    END IF;

    -- 1.5 Створити Draft Knowledge Entry
    INSERT INTO public.knowledge_entries (
        fingerprint_key,
        fingerprint_hash,
        component,
        operation,
        signal,
        title,
        symptoms,
        known_cause,
        workaround,
        permanent_fix,
        affected_versions,
        fixed_version,
        confidence,
        status,
        created_by,
        updated_by
    )
    VALUES (
        v_incident.fingerprint_key,
        v_incident.fingerprint_hash,
        v_incident.component,
        v_incident.operation,
        COALESCE(v_incident.signal, '-'),
        p_title,
        NULL,
        p_known_cause,
        p_workaround,
        NULL,
        ARRAY[v_incident.release]::text[],
        NULL,
        'Low',
        'Draft',
        p_created_by,
        p_created_by
    )
    RETURNING id INTO v_new_id;

    RETURN QUERY SELECT v_new_id, true;
END;
$$;

-- 2. GRANT (cc_readonly EXECUTE — патерн Статті 23)
GRANT EXECUTE ON FUNCTION public.create_knowledge_from_incident(bigint, text, text, text, text) TO cc_readonly;

-- 3. Допоміжна функція: перевірити, чи інцидент можна зберегти як Knowledge
--    Використовується UI для визначення видимості/доступності кнопки.
CREATE OR REPLACE FUNCTION public.can_create_knowledge_for_incident(p_incident_id bigint)
RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_incident public.telemetry_incidents%ROWTYPE;
    v_exists boolean;
BEGIN
    SELECT * INTO v_incident
    FROM public.telemetry_incidents
    WHERE id = p_incident_id;

    IF NOT FOUND THEN
        RETURN false;
    END IF;

    IF v_incident.status NOT IN ('Mitigated','Resolved','Closed') THEN
        RETURN false;
    END IF;

    IF v_incident.owner IS NULL OR trim(v_incident.owner) = '' THEN
        RETURN false;
    END IF;

    SELECT EXISTS (
        SELECT 1 FROM public.knowledge_entries ke
        WHERE ke.fingerprint_hash = v_incident.fingerprint_hash
          AND ke.status != 'Archived'
    ) INTO v_exists;

    RETURN NOT v_exists;
END;
$$;

GRANT EXECUTE ON FUNCTION public.can_create_knowledge_for_incident(bigint) TO cc_readonly;

-- 4. Перевірка: функція create_knowledge_from_incident має працювати через cc_readonly
COMMENT ON FUNCTION public.create_knowledge_from_incident IS
    'Slice 2: create Draft Knowledge Entry from a resolved incident. Dual-identity audit via knowledge_audit trigger. Enforces one active knowledge per fingerprint.';
COMMENT ON FUNCTION public.can_create_knowledge_for_incident IS
    'Slice 2: preflight check for Save as Knowledge button visibility.';
