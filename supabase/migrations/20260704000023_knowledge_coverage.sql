-- Міграція 00023: Knowledge Engine Slice 5 — Release Integration (auto-verify) + Coverage + Search.
-- Заморожений дизайн v1.1.
--
-- Ця міграція:
-- 1. verify_knowledge_auto() — детерміноване авто-підвищення Confidence до 'Verified' (5 умов §6.1).
-- 2. search_knowledge() — ручний пошук з пагінацією.
-- 3. Materialized view control_center.knowledge_coverage — метрика покриття.
-- 4. VIEW control_center.knowledge_list — список для сторінки Knowledge.
-- 5. VIEW control_center.top_missing_knowledge — топ непокритих component+signal.
-- 6. DROP FUNCTION archive_knowledge_entry (deprecated Slice 3.5, замінена на transition_knowledge).
-- 7. CHECK-розширення notification_queue.notification_type значенням 'KnowledgeVerified'.

BEGIN;

-- 1. Автоматична верифікація знань (5 умов §6.1)
CREATE OR REPLACE FUNCTION public.verify_knowledge_auto()
RETURNS TABLE (knowledge_id bigint, title text, reason text)
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_count int := 0;
    v_now timestamptz := now();
BEGIN
    RETURN QUERY
    SELECT ke.id, ke.title,
        'Auto-verified: fixed_version=' || ke.fixed_version ||
        ', no recurrence 14d, confidence High→Verified' AS reason
    FROM public.knowledge_entries ke
    WHERE ke.confidence = 'High'
      AND ke.status IN ('Reviewed','Verified')
      AND ke.fixed_version IS NOT NULL
      -- Умова 1: Fixed Version має installs ≥ 50
      AND EXISTS (
          SELECT 1 FROM control_center.release_health_detail rh
          WHERE rh.app_version = ke.fixed_version
            AND rh.active_installs >= 50
      )
      -- Умова 2: success_rate на Fixed Version ≥ 95%
      AND EXISTS (
          SELECT 1 FROM control_center.release_health_detail rh
          WHERE rh.app_version = ke.fixed_version
            AND rh.success_rate >= 95
      )
      -- Умова 3: 14 днів без рецидиву fingerprint (немає інцидентів за останні 14 діб)
      AND NOT EXISTS (
          SELECT 1 FROM public.telemetry_incidents i
          WHERE i.fingerprint_hash = ke.fingerprint_hash
            AND i.last_event_at >= v_now - interval '14 days'
      );

    -- Виконуємо підвищення
    v_count := 0;
    FOR v_count IN
        SELECT ke.id
        FROM public.knowledge_entries ke
        WHERE ke.confidence = 'High'
          AND ke.status IN ('Reviewed','Verified')
          AND ke.fixed_version IS NOT NULL
          AND EXISTS (
              SELECT 1 FROM control_center.release_health_detail rh
              WHERE rh.app_version = ke.fixed_version
                AND rh.active_installs >= 50
          )
          AND EXISTS (
              SELECT 1 FROM control_center.release_health_detail rh
              WHERE rh.app_version = ke.fixed_version
                AND rh.success_rate >= 95
          )
          AND NOT EXISTS (
              SELECT 1 FROM public.telemetry_incidents i
              WHERE i.fingerprint_hash = ke.fingerprint_hash
                AND i.last_event_at >= v_now - interval '14 days'
          )
    LOOP
        PERFORM public.set_knowledge_change_context(
            'WorkflowTransition',
            'Auto-verified by verify_knowledge_auto() (§6.1)'
        );
        UPDATE public.knowledge_entries
        SET confidence = 'Verified',
            status = CASE WHEN status = 'Reviewed' THEN 'Verified' ELSE status END,
            updated_by = 'system:auto-verify',
            updated_at = v_now
        WHERE id = v_count AND confidence = 'High';
    END LOOP;
END;
$$;

GRANT EXECUTE ON FUNCTION public.verify_knowledge_auto() TO cc_readonly;

-- 2. Ручний пошук Knowledge з пагінацією
CREATE OR REPLACE FUNCTION public.search_knowledge(
    p_query text,
    p_component text DEFAULT NULL,
    p_status text DEFAULT NULL,
    p_confidence text DEFAULT NULL,
    p_limit int DEFAULT 20,
    p_offset int DEFAULT 0
)
RETURNS TABLE (
    knowledge_id bigint,
    title text,
    component text,
    signal text,
    status text,
    confidence text,
    fixed_version text,
    updated_at timestamptz,
    total_count bigint
)
LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
    v_pattern text;
    v_total bigint;
BEGIN
    v_pattern := '%' || COALESCE(trim(p_query), '') || '%';

    SELECT count(*) INTO v_total
    FROM public.knowledge_entries ke
    WHERE (v_pattern = '%%' OR ke.title ILIKE v_pattern OR ke.fingerprint_key ILIKE v_pattern
           OR ke.component ILIKE v_pattern OR ke.signal ILIKE v_pattern)
      AND (p_component IS NULL OR ke.component = p_component)
      AND (p_status IS NULL OR ke.status = p_status)
      AND (p_confidence IS NULL OR ke.confidence = p_confidence);

    RETURN QUERY
    SELECT ke.id, ke.title, ke.component, ke.signal, ke.status, ke.confidence,
           ke.fixed_version, ke.updated_at, v_total
    FROM public.knowledge_entries ke
    WHERE (v_pattern = '%%' OR ke.title ILIKE v_pattern OR ke.fingerprint_key ILIKE v_pattern
           OR ke.component ILIKE v_pattern OR ke.signal ILIKE v_pattern)
      AND (p_component IS NULL OR ke.component = p_component)
      AND (p_status IS NULL OR ke.status = p_status)
      AND (p_confidence IS NULL OR ke.confidence = p_confidence)
    ORDER BY ke.updated_at DESC, ke.id DESC
    LIMIT GREATEST(1, LEAST(p_limit, 100))
    OFFSET GREATEST(0, p_offset);
END;
$$;

GRANT EXECUTE ON FUNCTION public.search_knowledge(text, text, text, text, int, int) TO cc_readonly;

-- 3. Materialized view: knowledge_coverage
DROP MATERIALIZED VIEW IF EXISTS control_center.knowledge_coverage;
CREATE MATERIALIZED VIEW control_center.knowledge_coverage AS
WITH fingerprints AS (
    SELECT DISTINCT fingerprint_hash
    FROM public.telemetry_incidents
),
verified AS (
    SELECT DISTINCT fingerprint_hash
    FROM public.knowledge_entries
    WHERE status = 'Verified'
)
SELECT
    (SELECT count(*) FROM fingerprints) AS total_fingerprints,
    (SELECT count(*) FROM fingerprints f JOIN verified v ON f.fingerprint_hash = v.fingerprint_hash) AS covered_fingerprints,
    (SELECT count(*) FROM fingerprints f LEFT JOIN verified v ON f.fingerprint_hash = v.fingerprint_hash WHERE v.fingerprint_hash IS NULL) AS uncovered_fingerprints,
    CASE
        WHEN (SELECT count(*) FROM fingerprints) = 0 THEN 0
        ELSE ROUND(
            100.0 * (SELECT count(*) FROM fingerprints f JOIN verified v ON f.fingerprint_hash = v.fingerprint_hash)
            / (SELECT count(*) FROM fingerprints),
            1
        )
    END AS coverage_pct;

GRANT SELECT ON control_center.knowledge_coverage TO cc_readonly;
CREATE UNIQUE INDEX IF NOT EXISTS idx_knowledge_coverage_singleton ON control_center.knowledge_coverage (total_fingerprints);

-- 3a. SECURITY DEFINER функція для REFRESH (cc_readonly не має прав власника на MV)
CREATE OR REPLACE FUNCTION public.refresh_knowledge_coverage()
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER AS $$
BEGIN
    REFRESH MATERIALIZED VIEW control_center.knowledge_coverage;
END;
$$;

GRANT EXECUTE ON FUNCTION public.refresh_knowledge_coverage() TO cc_readonly;

-- 4. VIEW: knowledge_list (для сторінки Knowledge)
DROP VIEW IF EXISTS control_center.knowledge_list;
CREATE VIEW control_center.knowledge_list AS
SELECT
    ke.id,
    ke.fingerprint_key,
    ke.component,
    ke.operation,
    ke.signal,
    ke.title,
    ke.confidence,
    ke.status,
    ke.fixed_version,
    COALESCE(array_to_string(ke.affected_versions, ','), '') AS affected_versions,
    ke.created_at,
    ke.updated_at,
    ke.updated_by
FROM public.knowledge_entries ke
ORDER BY ke.updated_at DESC;

GRANT SELECT ON control_center.knowledge_list TO cc_readonly;

-- 5. VIEW: top_missing_knowledge (топ непокритих component+signal)
DROP VIEW IF EXISTS control_center.top_missing_knowledge;
CREATE VIEW control_center.top_missing_knowledge AS
SELECT
    i.component,
    i.signal,
    count(DISTINCT i.fingerprint_hash) AS fingerprint_count,
    sum(i.event_count) AS total_events,
    max(i.last_event_at) AS last_seen,
    max(i.highest_severity) AS highest_severity
FROM public.telemetry_incidents i
WHERE NOT EXISTS (
    SELECT 1 FROM public.knowledge_entries ke
    WHERE ke.fingerprint_hash = i.fingerprint_hash AND ke.status = 'Verified'
)
GROUP BY i.component, i.signal
ORDER BY total_events DESC
LIMIT 10;

GRANT SELECT ON control_center.top_missing_knowledge TO cc_readonly;

-- 6. Cleanup: DROP deprecated archive_knowledge_entry (замінена на transition_knowledge у Slice 4)
DROP FUNCTION IF EXISTS public.archive_knowledge_entry(bigint, text, text);

-- 7. CHECK-розширення notification_queue.notification_type (additive)
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'notification_queue'
          AND column_name = 'notification_type'
    ) THEN
        BEGIN
            ALTER TABLE public.notification_queue
                DROP CONSTRAINT IF EXISTS chk_notification_type;
            ALTER TABLE public.notification_queue
                ADD CONSTRAINT chk_notification_type CHECK (
                    notification_type IN (
                        'IncidentCreated','IncidentEscalated','IncidentMitigated',
                        'IncidentResolved','WeeklyDigest','TestAlert','KnowledgeVerified'
                    )
                );
        EXCEPTION WHEN OTHERS THEN
            RAISE NOTICE 'Could not update notification_type constraint: %', SQLERRM;
        END;
    END IF;
END $$;

-- 8. Коментарі
COMMENT ON FUNCTION public.verify_knowledge_auto IS
    'Slice 5: auto-verify Knowledge (§6.1). Conditions: confidence=High, status Reviewed/Verified, fixed_version set, ≥50 installs, ≥95% success_rate, no recurrence 14d. Returns candidates + performs promotion.';
COMMENT ON FUNCTION public.search_knowledge IS
    'Slice 5: manual search Knowledge by title/fingerprint/component/signal with pagination.';
COMMENT ON MATERIALIZED VIEW control_center.knowledge_coverage IS
    'Slice 5: knowledge coverage metric. Refresh via REFRESH MATERIALIZED VIEW control_center.knowledge_coverage;';

COMMIT;
