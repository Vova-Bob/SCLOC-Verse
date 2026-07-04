-- Міграція 00022: Knowledge Engine — Slice 3.5 refinements + Slice 4 Workflow foundation.
-- Заморожений дизайн v1.1.
--
-- Ця міграція:
-- 1. Додає change_type в knowledge_version_history.
-- 2. Розширює enum reference_type до GitCommit/GitHubIssue/Documentation/ReleaseNotes/External.
-- 3. Переробляє archive у transition_knowledge.
-- 4. Додає transition_knowledge(knowledge_id, target_status, reason, expected_version).
-- 5. Додає match_knowledge_priority2(p_incident_id) — component+signal серед Verified.
-- 6. Вбудовує can_edit_knowledge перевірку в update/add/remove функції.
-- 7. Оновлює тригер audit для запису change_type.

BEGIN;

-- 1. Додаємо change_type в історію версій
ALTER TABLE public.knowledge_version_history
    ADD COLUMN IF NOT EXISTS change_type text NOT NULL DEFAULT 'Updated';

-- 2. Оновлюємо enum reference_type
ALTER TABLE public.knowledge_references
    DROP CONSTRAINT IF EXISTS chk_knowledge_ref_type;

ALTER TABLE public.knowledge_references
    ADD CONSTRAINT chk_knowledge_ref_type CHECK (reference_type IN ('GitCommit','GitHubIssue','Documentation','ReleaseNotes','External'));

-- 3. Оновлений audit trigger — обчислює change_type на основі контексту
--    change_type заповнюється через SET LOCAL у функціях, які змінюють knowledge_entries.
CREATE OR REPLACE FUNCTION public.knowledge_audit_trigger()
RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    next_ver int;
    v_change_type text;
BEGIN
    SELECT COALESCE(MAX(version), 0) + 1 INTO next_ver
    FROM public.knowledge_version_history
    WHERE knowledge_id = NEW.id;

    -- Визначаємо change_type: якщо функція встановила через SET LOCAL, беремо його;
    -- інакше — визначаємо за зміною статусу.
    BEGIN
        v_change_type := current_setting('knowledge.change_type', true);
    EXCEPTION WHEN OTHERS THEN
        v_change_type := '';
    END;

    IF v_change_type IS NULL OR v_change_type = '' THEN
        IF TG_OP = 'INSERT' THEN
            v_change_type := 'Created';
        ELSIF NEW.status IS DISTINCT FROM OLD.status THEN
            IF NEW.status = 'Archived' THEN
                v_change_type := 'Archived';
            ELSE
                v_change_type := 'WorkflowTransition';
            END IF;
        ELSE
            v_change_type := 'Updated';
        END IF;
    END IF;

    -- change_reason: беремо з контексту, якщо встановлено; інакше auto-audit
    DECLARE
        v_change_reason text;
    BEGIN
        v_change_reason := current_setting('knowledge.change_reason', true);
        IF v_change_reason IS NULL OR v_change_reason = '' THEN
            v_change_reason := 'auto-audit';
        END IF;

        INSERT INTO public.knowledge_version_history
            (knowledge_id, version, snapshot, changed_by, change_reason, db_user, change_type)
        VALUES
            (NEW.id, next_ver, to_jsonb(NEW), COALESCE(NEW.updated_by, NEW.created_by),
             v_change_reason, current_user, v_change_type);
    END;

    RETURN NEW;
END;
$$;

-- 4. Допоміжна функція: встановити change_type для поточної транзакції
CREATE OR REPLACE FUNCTION public.set_knowledge_change_context(p_change_type text, p_change_reason text)
RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    PERFORM set_config('knowledge.change_type', p_change_type, true);
    PERFORM set_config('knowledge.change_reason', p_change_reason, true);
END;
$$;

-- 5. Допоміжна функція: перевірити, чи Knowledge Entry не Archived
CREATE OR REPLACE FUNCTION public.require_not_archived(p_knowledge_id bigint)
RETURNS void LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE v_status text;
BEGIN
    SELECT status INTO v_status FROM public.knowledge_entries WHERE id = p_knowledge_id;
    IF v_status IS NULL THEN
        RAISE EXCEPTION 'Knowledge entry % not found', p_knowledge_id;
    END IF;
    IF v_status = 'Archived' THEN
        RAISE EXCEPTION 'Knowledge entry % is archived and cannot be modified', p_knowledge_id;
    END IF;
END;
$$;

-- 6. Оновлення Knowledge Entry з оптимістичною конкуренцією + can_edit перевіркою
CREATE OR REPLACE FUNCTION public.update_knowledge_entry(
    p_knowledge_id bigint,
    p_expected_version int,
    p_title text,
    p_symptoms text,
    p_known_cause text,
    p_workaround text,
    p_permanent_fix text,
    p_affected_versions text,
    p_fixed_version text,
    p_changed_by text,
    p_change_reason text
)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_current_version int;
BEGIN
    SELECT COALESCE(MAX(version), 0) INTO v_current_version
    FROM public.knowledge_version_history
    WHERE knowledge_id = p_knowledge_id;

    IF v_current_version IS DISTINCT FROM p_expected_version THEN
        RAISE EXCEPTION 'Knowledge entry was modified concurrently (expected version %, current %)', p_expected_version, v_current_version;
    END IF;

    PERFORM public.require_not_archived(p_knowledge_id);
    PERFORM public.set_knowledge_change_context('Updated', p_change_reason);

    UPDATE public.knowledge_entries
    SET title = p_title,
        symptoms = p_symptoms,
        known_cause = p_known_cause,
        workaround = p_workaround,
        permanent_fix = p_permanent_fix,
        affected_versions = public.parse_version_list(p_affected_versions),
        fixed_version = NULLIF(trim(p_fixed_version), ''),
        updated_by = p_changed_by,
        updated_at = now()
    WHERE id = p_knowledge_id;
END;
$$;

GRANT EXECUTE ON FUNCTION public.update_knowledge_entry(bigint, int, text, text, text, text, text, text, text, text, text) TO cc_readonly;

-- 7. Єдина функція Workflow transition
CREATE OR REPLACE FUNCTION public.transition_knowledge(
    p_knowledge_id bigint,
    p_target_status text,
    p_reason text,
    p_expected_version int,
    p_changed_by text
)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_current text;
    v_current_version int;
BEGIN
    SELECT status INTO v_current FROM public.knowledge_entries WHERE id = p_knowledge_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Knowledge entry % not found', p_knowledge_id;
    END IF;

    SELECT COALESCE(MAX(version), 0) INTO v_current_version
    FROM public.knowledge_version_history
    WHERE knowledge_id = p_knowledge_id;

    IF v_current_version IS DISTINCT FROM p_expected_version THEN
        RAISE EXCEPTION 'Knowledge entry was modified concurrently (expected version %, current %)', p_expected_version, v_current_version;
    END IF;

    -- Дозволені переходи
    IF v_current = p_target_status THEN
        RAISE EXCEPTION 'Knowledge entry % is already %', p_knowledge_id, v_current;
    END IF;

    IF NOT (
        (v_current = 'Draft' AND p_target_status IN ('Reviewed','Archived')) OR
        (v_current = 'Reviewed' AND p_target_status IN ('Verified','Archived')) OR
        (v_current = 'Verified' AND p_target_status IN ('Deprecated','Archived')) OR
        (v_current = 'Deprecated' AND p_target_status IN ('Reviewed','Archived'))
    ) THEN
        RAISE EXCEPTION 'Invalid knowledge transition from % to %', v_current, p_target_status;
    END IF;

    -- Archive потребує reason
    IF p_target_status = 'Archived' AND (p_reason IS NULL OR trim(p_reason) = '') THEN
        RAISE EXCEPTION 'Archive reason is required';
    END IF;

    -- Archive записуємо як окремий change_type
    PERFORM public.set_knowledge_change_context(
        CASE WHEN p_target_status = 'Archived' THEN 'Archived' ELSE 'WorkflowTransition' END,
        p_reason
    );

    -- Verified вимагає High або Verified confidence (інваріант §4 також перевірить)
    IF p_target_status = 'Verified' THEN
        UPDATE public.knowledge_entries
        SET status = p_target_status,
            confidence = 'High',
            updated_by = p_changed_by,
            updated_at = now()
        WHERE id = p_knowledge_id;
    ELSE
        UPDATE public.knowledge_entries
        SET status = p_target_status,
            updated_by = p_changed_by,
            updated_at = now()
        WHERE id = p_knowledge_id;
    END IF;
END;
$$;

GRANT EXECUTE ON FUNCTION public.transition_knowledge(bigint, text, text, int, text) TO cc_readonly;

-- 8. Додавання reference: перевірка can_edit + встановлення change_type
CREATE OR REPLACE FUNCTION public.add_knowledge_reference(
    p_knowledge_id bigint,
    p_reference_type text,
    p_url text,
    p_label text,
    p_added_by text
)
RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_ref_id bigint;
BEGIN
    PERFORM public.require_not_archived(p_knowledge_id);

    INSERT INTO public.knowledge_references (knowledge_id, reference_type, url, label)
    VALUES (p_knowledge_id, p_reference_type, p_url, p_label)
    RETURNING id INTO v_ref_id;

    PERFORM public.set_knowledge_change_context('ReferenceAdded', 'Added reference: ' || p_reference_type);

    UPDATE public.knowledge_entries
    SET updated_by = p_added_by,
        updated_at = now()
    WHERE id = p_knowledge_id;

    RETURN v_ref_id;
END;
$$;

GRANT EXECUTE ON FUNCTION public.add_knowledge_reference(bigint, text, text, text, text) TO cc_readonly;

-- 9. Видалення reference: перевірка can_edit + встановлення change_type
CREATE OR REPLACE FUNCTION public.remove_knowledge_reference(
    p_reference_id bigint,
    p_removed_by text
)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_knowledge_id bigint;
BEGIN
    SELECT knowledge_id INTO v_knowledge_id
    FROM public.knowledge_references
    WHERE id = p_reference_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Reference % not found', p_reference_id;
    END IF;

    PERFORM public.require_not_archived(v_knowledge_id);

    DELETE FROM public.knowledge_references WHERE id = p_reference_id;

    PERFORM public.set_knowledge_change_context('ReferenceRemoved', 'Removed reference');

    UPDATE public.knowledge_entries
    SET updated_by = p_removed_by,
        updated_at = now()
    WHERE id = v_knowledge_id;
END;
$$;

GRANT EXECUTE ON FUNCTION public.remove_knowledge_reference(bigint, text) TO cc_readonly;

-- 10. Оновлена get_knowledge_history з change_type (DROP + CREATE через зміну сигнатури)
DROP FUNCTION IF EXISTS public.get_knowledge_history(bigint);

CREATE OR REPLACE FUNCTION public.get_knowledge_history(p_knowledge_id bigint)
RETURNS TABLE (
    version int,
    changed_by text,
    db_user text,
    changed_at timestamptz,
    change_reason text,
    change_type text
)
LANGUAGE plpgsql SECURITY DEFINER AS $$
BEGIN
    RETURN QUERY
    SELECT h.version, h.changed_by, h.db_user, h.changed_at, h.change_reason, h.change_type
    FROM public.knowledge_version_history h
    WHERE h.knowledge_id = p_knowledge_id
    ORDER BY h.version DESC;
END;
$$;

GRANT EXECUTE ON FUNCTION public.get_knowledge_history(bigint) TO cc_readonly;

-- 11. Priority 2 match: component + signal, тільки Verified
CREATE OR REPLACE FUNCTION public.match_knowledge_priority2(p_incident_id bigint)
RETURNS TABLE (knowledge_id bigint, priority int)
LANGUAGE plpgsql SECURITY DEFINER AS $$
BEGIN
    RETURN QUERY
    SELECT ke.id, 2
    FROM public.knowledge_entries ke
    JOIN public.telemetry_incidents i
      ON i.component = ke.component AND i.signal = ke.signal
    WHERE i.id = p_incident_id
      AND ke.status = 'Verified'
      AND NOT EXISTS (
          SELECT 1 FROM public.knowledge_entries ke2
          WHERE ke2.fingerprint_hash = i.fingerprint_hash AND ke2.status = 'Verified'
      )
    ORDER BY ke.confidence DESC, ke.updated_at DESC, ke.id DESC
    LIMIT 1;
END;
$$;

GRANT EXECUTE ON FUNCTION public.match_knowledge_priority2(bigint) TO cc_readonly;

-- 12. Коментарі
COMMENT ON FUNCTION public.match_knowledge_for_incident IS
    'Slice 1: exact fingerprint match (Priority 1) among Verified knowledge entries.';
COMMENT ON FUNCTION public.match_knowledge_priority2 IS
    'Slice 4: component + signal match (Priority 2) among Verified knowledge entries. Excludes incidents with exact match.';
COMMENT ON FUNCTION public.transition_knowledge IS
    'Slice 4: unified workflow transition for Knowledge Entry. Allowed transitions: Draft→Reviewed/Archived, Reviewed→Verified/Archived, Verified→Deprecated/Archived, Deprecated→Reviewed/Archived.';

COMMIT;
