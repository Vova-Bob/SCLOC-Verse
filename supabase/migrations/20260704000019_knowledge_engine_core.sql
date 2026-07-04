-- Міграція 00019: Knowledge Engine Slice 1 — core tables.
-- Заморожений дизайн v1.1.
-- Застосовано до робочої БД: 2026-07-04.

-- 1. Основна таблиця знань
CREATE TABLE IF NOT EXISTS public.knowledge_entries (
    id bigserial PRIMARY KEY,
    fingerprint_key text NOT NULL,
    fingerprint_hash text NOT NULL,
    component text NOT NULL,
    operation text NOT NULL,
    signal text NOT NULL,
    title text NOT NULL,
    symptoms text,
    known_cause text NOT NULL,
    workaround text,
    permanent_fix text,
    affected_versions text[],
    fixed_version text,
    confidence text NOT NULL DEFAULT 'Low',
    status text NOT NULL DEFAULT 'Draft',
    created_by text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_by text NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT chk_knowledge_confidence CHECK (confidence IN ('Low','Medium','High','Verified')),
    CONSTRAINT chk_knowledge_status CHECK (status IN ('Draft','Reviewed','Verified','Deprecated','Archived')),
    -- v1.1: інваріант Status/Confidence — забороняє неможливі комбінації на рівні схеми
    CONSTRAINT chk_knowledge_status_confidence CHECK (
        (status = 'Verified'   AND confidence IN ('High','Verified')) OR
        (status IN ('Draft','Reviewed') AND confidence IN ('Low','Medium','High')) OR
        (status IN ('Deprecated','Archived'))
    )
);

-- 2. Посилання на commit/issue/doc (1:N, RESTRICT на видалення)
CREATE TABLE IF NOT EXISTS public.knowledge_references (
    id bigserial PRIMARY KEY,
    knowledge_id bigint NOT NULL REFERENCES public.knowledge_entries(id) ON DELETE RESTRICT,
    reference_type text NOT NULL,
    url text,
    label text,
    CONSTRAINT chk_knowledge_ref_type CHECK (reference_type IN ('GitCommit','GitHubIssue','Documentation','External'))
);

-- 3. Append-only audit з dual-identity (v1.1: db_user = реальний БД-користувач)
CREATE TABLE IF NOT EXISTS public.knowledge_version_history (
    id bigserial PRIMARY KEY,
    knowledge_id bigint NOT NULL REFERENCES public.knowledge_entries(id) ON DELETE RESTRICT,
    version int NOT NULL,
    snapshot jsonb NOT NULL,
    changed_by text,
    db_user text NOT NULL DEFAULT current_user,
    changed_at timestamptz NOT NULL DEFAULT now(),
    change_reason text
);

-- 4. Індекси
CREATE INDEX IF NOT EXISTS idx_knowledge_fingerprint ON public.knowledge_entries(fingerprint_hash);
CREATE INDEX IF NOT EXISTS idx_knowledge_component_signal ON public.knowledge_entries(component, signal)
    WHERE status = 'Verified';
CREATE INDEX IF NOT EXISTS idx_knowledge_status_updated ON public.knowledge_entries(status, updated_at DESC, id DESC);
CREATE INDEX IF NOT EXISTS idx_knowledge_refs_kid ON public.knowledge_references(knowledge_id);

-- 5. Trigger audit: після INSERT/UPDATE пишемо snapshot у history
CREATE OR REPLACE FUNCTION public.knowledge_audit_trigger()
RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE next_ver int;
BEGIN
    SELECT COALESCE(MAX(version), 0) + 1 INTO next_ver
    FROM public.knowledge_version_history
    WHERE knowledge_id = NEW.id;

    INSERT INTO public.knowledge_version_history
        (knowledge_id, version, snapshot, changed_by, change_reason, db_user)
    VALUES
        (NEW.id, next_ver, to_jsonb(NEW), COALESCE(NEW.updated_by, NEW.created_by), 'auto-audit', current_user);

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS knowledge_audit ON public.knowledge_entries;
CREATE TRIGGER knowledge_audit
    AFTER INSERT OR UPDATE ON public.knowledge_entries
    FOR EACH ROW EXECUTE FUNCTION public.knowledge_audit_trigger();

-- 6. RLS deny-all
ALTER TABLE public.knowledge_entries ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.knowledge_references ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.knowledge_version_history ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "deny all knowledge_entries" ON public.knowledge_entries;
CREATE POLICY "deny all knowledge_entries" ON public.knowledge_entries
    AS RESTRICTIVE FOR ALL TO anon, authenticated, cc_readonly USING (false) WITH CHECK (false);

DROP POLICY IF EXISTS "deny all knowledge_references" ON public.knowledge_references;
CREATE POLICY "deny all knowledge_references" ON public.knowledge_references
    AS RESTRICTIVE FOR ALL TO anon, authenticated, cc_readonly USING (false) WITH CHECK (false);

DROP POLICY IF EXISTS "deny all knowledge_version_history" ON public.knowledge_version_history;
CREATE POLICY "deny all knowledge_version_history" ON public.knowledge_version_history
    AS RESTRICTIVE FOR ALL TO anon, authenticated, cc_readonly USING (false) WITH CHECK (false);

-- 7. VIEW для Control Center (cc_readonly)
DROP VIEW IF EXISTS control_center.knowledge_entry_detail;
CREATE VIEW control_center.knowledge_entry_detail AS
SELECT
    ke.id,
    ke.fingerprint_key,
    ke.fingerprint_hash,
    ke.component,
    ke.operation,
    ke.signal,
    ke.title,
    ke.symptoms,
    ke.known_cause,
    ke.workaround,
    ke.permanent_fix,
    ke.affected_versions,
    ke.fixed_version,
    ke.confidence,
    ke.status,
    ke.created_by,
    ke.created_at,
    ke.updated_by,
    ke.updated_at,
    COALESCE(jsonb_agg(
        jsonb_build_object('type', kr.reference_type, 'url', kr.url, 'label', kr.label)
        ORDER BY kr.id
    ) FILTER (WHERE kr.id IS NOT NULL), '[]'::jsonb) AS references
FROM public.knowledge_entries ke
LEFT JOIN public.knowledge_references kr ON kr.knowledge_id = ke.id
GROUP BY ke.id;

-- 8. SECURITY DEFINER: match by incident id (exact fingerprint only, Slice 1)
CREATE OR REPLACE FUNCTION public.match_knowledge_for_incident(p_incident_id bigint)
RETURNS TABLE (knowledge_id bigint, priority int) LANGUAGE plpgsql SECURITY DEFINER AS $$
BEGIN
    RETURN QUERY
    SELECT ke.id, 1
    FROM public.knowledge_entries ke
    JOIN public.telemetry_incidents i ON i.fingerprint_hash = ke.fingerprint_hash
    WHERE i.id = p_incident_id
      AND ke.status = 'Verified'
    ORDER BY ke.updated_at DESC, ke.id DESC
    LIMIT 1;
END;
$$;

-- 9. GRANT (cc_readonly EXECUTE + SELECT на VIEW — патерн Статті 23)
GRANT SELECT ON public.knowledge_entries, public.knowledge_references, public.knowledge_version_history TO cc_readonly;
GRANT SELECT ON control_center.knowledge_entry_detail TO cc_readonly;
GRANT EXECUTE ON FUNCTION public.match_knowledge_for_incident(bigint) TO cc_readonly;
