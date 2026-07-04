-- Міграція 00021: Knowledge Engine Slice 3 — Knowledge Lifecycle (Versioning).
-- Заморожений дизайн v1.1.
--
-- Правила цього слайсу (узгоджені):
-- 1. Редагування Knowledge через SECURITY DEFINER функцію update_knowledge_entry.
-- 2. Оптимістична конкуренція: expected_version порівнюється з поточною версією запису.
-- 3. AffectedVersions передається як текст '1.0.0,1.0.1' і парситься в text[] на БД.
-- 4. Archive потребує обов'язкової archive_reason.
-- 5. References (add/remove) створюють нову версію в knowledge_version_history.
-- 6. Всі writes через SECURITY DEFINER; cc_readonly має тільки SELECT + EXECUTE.

-- 1. Допоміжна функція: парсинг рядка версій у text[]
CREATE OR REPLACE FUNCTION public.parse_version_list(p_versions text)
RETURNS text[]
LANGUAGE plpgsql IMMUTABLE SECURITY DEFINER AS $$
BEGIN
    IF p_versions IS NULL OR trim(p_versions) = '' THEN
        RETURN NULL;
    END IF;
    RETURN array_agg(trim(v)) FROM unnest(string_to_array(p_versions, ',')) v
    WHERE trim(v) <> '';
END;
$$;

-- 2. Оновлення Knowledge Entry з оптимістичною конкуренцією
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

-- 3. Архівування Knowledge Entry з обов'язковою причиною
CREATE OR REPLACE FUNCTION public.archive_knowledge_entry(
    p_knowledge_id bigint,
    p_archive_reason text,
    p_changed_by text
)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_current text;
BEGIN
    SELECT status INTO v_current FROM public.knowledge_entries WHERE id = p_knowledge_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Knowledge entry % not found', p_knowledge_id;
    END IF;
    IF v_current = 'Archived' THEN
        RAISE EXCEPTION 'Knowledge entry % is already archived', p_knowledge_id;
    END IF;
    IF p_archive_reason IS NULL OR trim(p_archive_reason) = '' THEN
        RAISE EXCEPTION 'Archive reason is required';
    END IF;

    UPDATE public.knowledge_entries
    SET status = 'Archived',
        updated_by = p_changed_by,
        updated_at = now()
    WHERE id = p_knowledge_id;
END;
$$;

GRANT EXECUTE ON FUNCTION public.archive_knowledge_entry(bigint, text, text) TO cc_readonly;

-- 4. Додавання reference: створює audit-версію через тригер
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
    INSERT INTO public.knowledge_references (knowledge_id, reference_type, url, label)
    VALUES (p_knowledge_id, p_reference_type, p_url, p_label)
    RETURNING id INTO v_ref_id;

    -- Примусово оновити knowledge_entries, щоб спрацював тригер audit
    UPDATE public.knowledge_entries
    SET updated_by = p_added_by,
        updated_at = now()
    WHERE id = p_knowledge_id;

    RETURN v_ref_id;
END;
$$;

GRANT EXECUTE ON FUNCTION public.add_knowledge_reference(bigint, text, text, text, text) TO cc_readonly;

-- 5. Видалення reference: створює audit-версію через тригер
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

    DELETE FROM public.knowledge_references WHERE id = p_reference_id;

    UPDATE public.knowledge_entries
    SET updated_by = p_removed_by,
        updated_at = now()
    WHERE id = v_knowledge_id;
END;
$$;

GRANT EXECUTE ON FUNCTION public.remove_knowledge_reference(bigint, text) TO cc_readonly;

-- 6. Історія версій: список для Knowledge
CREATE OR REPLACE FUNCTION public.get_knowledge_history(p_knowledge_id bigint)
RETURNS TABLE (
    version int,
    changed_by text,
    db_user text,
    changed_at timestamptz,
    change_reason text
)
LANGUAGE plpgsql SECURITY DEFINER AS $$
BEGIN
    RETURN QUERY
    SELECT h.version, h.changed_by, h.db_user, h.changed_at, h.change_reason
    FROM public.knowledge_version_history h
    WHERE h.knowledge_id = p_knowledge_id
    ORDER BY h.version DESC;
END;
$$;

GRANT EXECUTE ON FUNCTION public.get_knowledge_history(bigint) TO cc_readonly;

-- 7. Детальна версія (snapshot)
CREATE OR REPLACE FUNCTION public.get_knowledge_version_detail(p_version_id bigint)
RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_snapshot jsonb;
BEGIN
    SELECT h.snapshot INTO v_snapshot
    FROM public.knowledge_version_history h
    WHERE h.id = p_version_id;
    RETURN v_snapshot;
END;
$$;

GRANT EXECUTE ON FUNCTION public.get_knowledge_version_detail(bigint) TO cc_readonly;

-- 8. Поточна версія Knowledge для оптимістичної конкуренції
CREATE OR REPLACE FUNCTION public.get_knowledge_current_version(p_knowledge_id bigint)
RETURNS int
LANGUAGE plpgsql SECURITY DEFINER AS $$
BEGIN
    RETURN COALESCE(
        (SELECT MAX(version) FROM public.knowledge_version_history WHERE knowledge_id = p_knowledge_id),
        0
    );
END;
$$;

GRANT EXECUTE ON FUNCTION public.get_knowledge_current_version(bigint) TO cc_readonly;

-- 9. Допоміжна функція: перевірити, чи Knowledge Entry редагована (не Archived)
CREATE OR REPLACE FUNCTION public.can_edit_knowledge(p_knowledge_id bigint)
RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_status text;
BEGIN
    SELECT status INTO v_status FROM public.knowledge_entries WHERE id = p_knowledge_id;
    RETURN v_status IS NOT NULL AND v_status != 'Archived';
END;
$$;

GRANT EXECUTE ON FUNCTION public.can_edit_knowledge(bigint) TO cc_readonly;

-- 10. Коментарі
COMMENT ON FUNCTION public.update_knowledge_entry IS
    'Slice 3: edit Knowledge Entry with optimistic concurrency (expected_version). Parses affected_versions comma string into text[].';
COMMENT ON FUNCTION public.archive_knowledge_entry IS
    'Slice 3: archive Knowledge Entry. Requires archive_reason. Final state.';
COMMENT ON FUNCTION public.add_knowledge_reference IS
    'Slice 3: add reference to Knowledge Entry. Triggers audit version.';
COMMENT ON FUNCTION public.remove_knowledge_reference IS
    'Slice 3: remove reference from Knowledge Entry. Triggers audit version.';
COMMENT ON FUNCTION public.get_knowledge_history IS
    'Slice 3: return version history list for Knowledge Entry.';
COMMENT ON FUNCTION public.get_knowledge_version_detail IS
    'Slice 3: return full snapshot of a specific version.';
COMMENT ON FUNCTION public.get_knowledge_current_version IS
    'Slice 3: current version number for optimistic concurrency.';
COMMENT ON FUNCTION public.can_edit_knowledge IS
    'Slice 3: check if Knowledge Entry can be edited (not Archived).';
