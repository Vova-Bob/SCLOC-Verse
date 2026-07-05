-- Міграція 20260705021200: Observability Health VIEW (Коміт B)
--
-- Мета: єдина health-метрика платформи, доступна через SELECT без ручного
-- форензик-аналізу. Відображає стан pipeline end-to-end і дозволяє
-- Control Center показувати green/yellow/red індикатор.
--
-- Складається з:
--   1. VIEW control_center.observability_health — singleton з last_* timestamps.
--   2. Smoke test у вигляді DO-блоку з ASSERT для доведення працездатності
--      автоматичного pipeline (ідентичний до ручного smoke-тесту з коміту A).
--
-- Поля VIEW:
--   last_failed_event_at        — час останньої Failed телеметрії
--   last_incident_opened_at     — час останнього створеного інциденту
--   last_notification_at        — час останнього сповіщення в черзі
--   last_knowledge_refresh_at   — час останнього REFRESH knowledge_coverage
--   candidates_without_open_incident — candidates з incident_candidates_24h
--                                       без відповідного відкритого інциденту
--   pipeline_healthy            — boolean: true якщо автоматичний pipeline
--                                 працює (нема Failed старших за 2 хв без інциденту)
--
-- Логіка pipeline_healthy:
--   - true  якщо нема жодної Failed події (платформа idle)
--   - true  якщо для всіх candidates (24 год) є відкритий incident
--   - true  якщо остання Failed < 2 хв тому (trigger міг не встигнути)
--   - false якщо є Failed старша за 2 хв без відповідного incident

BEGIN;

-- ============================================================================
-- 1. observability_health VIEW
-- ============================================================================
CREATE OR REPLACE VIEW control_center.observability_health AS
WITH last_failed AS (
    SELECT MAX(received_at) AS at
    FROM public.telemetry_events
    WHERE outcome = 'Failed'
),
last_incident AS (
    SELECT MAX(opened_at) AS at
    FROM public.telemetry_incidents
),
last_notification AS (
    SELECT MAX(created_at) AS at
    FROM public.notification_queue
),
candidates_orphan AS (
    -- Candidates з incident_candidates_24h, для яких нема відкритого інциденту
    SELECT count(*) AS cnt
    FROM control_center.incident_candidates_24h c
    WHERE NOT EXISTS (
        SELECT 1
        FROM public.telemetry_incidents i
        WHERE i.fingerprint_key = c.fingerprint_key
          AND i.status != 'Closed'
    )
),
failed_age AS (
    SELECT EXTRACT(EPOCH FROM (NOW() - (SELECT at FROM last_failed)))::bigint AS seconds
    WHERE (SELECT at FROM last_failed) IS NOT NULL
)
SELECT
    (SELECT at FROM last_failed)        AS last_failed_event_at,
    (SELECT at FROM last_incident)      AS last_incident_opened_at,
    (SELECT at FROM last_notification)  AS last_notification_at,
    (SELECT last_knowledge_refresh
       FROM control_center.pipeline_health_meta
       WHERE singleton = true)          AS last_knowledge_refresh_at,
    (SELECT cnt FROM candidates_orphan) AS candidates_without_open_incident,
    CASE
        -- Нема Failed = нема проблем
        WHEN (SELECT at FROM last_failed) IS NULL THEN true
        -- Усі candidates промотовані
        WHEN (SELECT cnt FROM candidates_orphan) = 0 THEN true
        -- Остання Failed свіжа (< 120с) — trigger міг не встигнути
        WHEN COALESCE((SELECT seconds FROM failed_age), 0) < 120 THEN true
        -- Є старі Failed без інциденту — pipeline зламаний
        ELSE false
    END AS pipeline_healthy;

GRANT SELECT ON control_center.observability_health TO cc_readonly;

COMMENT ON VIEW control_center.observability_health IS
    'Health-метрика Observability Platform (singleton). Відображає last_* timestamps кожного етапу pipeline + pipeline_healthy boolean. pipeline_healthy=false якщо є Failed старша за 2 хв без відповідного відкритого інциденту. Контрактна умова Статті про Pipeline Automation.';

-- ============================================================================
-- 2. Smoke test (доведення end-to-end через реальний тригер)
-- ============================================================================
-- Виконується в межах транзакції міграції. ASSERT гарантує:
--   - INSERT Failed створив incident автоматично
--   - INSERT Failed створив notification автоматично
--   - тригер НЕ ламає INSERT клієнтської телеметрії
-- Cleanup в кінці повертає БД до чистого стану.
DO $$
DECLARE
    v_event_id     uuid;
    v_inc_before   bigint;
    v_inc_after    bigint;
    v_notif_before bigint;
    v_notif_after  bigint;
    v_fingerprint  text := 'LIA|SmokeB|0xSMOKEB|1.0.0.1';
BEGIN
    SELECT count(*), (SELECT count(*) FROM public.notification_queue)
    INTO v_inc_before, v_notif_before
    FROM public.telemetry_incidents
    WHERE fingerprint_key = v_fingerprint;

    -- INSERT Failed — тригер спрацює автоматично
    INSERT INTO public.telemetry_events (
        client_event_id, session_id, correlation_id, step,
        install_id, occurred_at, app_version,
        component, operation, outcome, severity, category, source,
        hresult, error_message, detail
    ) VALUES (
        'cafe2222-0000-0000-0000-000000000001'::uuid,
        'cafe2222-0000-0000-0000-000000000002'::uuid,
        'cafe2222-0000-0000-0000-000000000002'::uuid,
        1,
        '46c630be2be24e4f8d09a68b59787ff5',
        NOW(),
        '1.0.0.1',
        'LIA',
        'SmokeB',
        'Failed',
        'Error',
        'Operational',
        'SmokeB',
        '0xSMOKEB',
        'Pipeline smoke test (Commits A+B)',
        '{"smoke_test":true,"migration":"20260705021200"}'::jsonb
    )
    RETURNING id INTO v_event_id;

    SELECT count(*) INTO v_inc_after
    FROM public.telemetry_incidents
    WHERE fingerprint_key = v_fingerprint;

    SELECT count(*) INTO v_notif_after FROM public.notification_queue;

    -- Інваріанти
    ASSERT v_inc_after = v_inc_before + 1,
        'Smoke B FAILED: expected +1 incident, got before=% after=%',
        v_inc_before, v_inc_after;
    ASSERT v_notif_after = v_notif_before + 1,
        'Smoke B FAILED: expected +1 notification, got before=% after=%',
        v_notif_before, v_notif_after;

    RAISE NOTICE '✓ Smoke B PASSED: event % → incident + notification автоматично', v_event_id;

    -- Cleanup
    DELETE FROM public.notification_queue
    WHERE incident_id IN (
        SELECT id FROM public.telemetry_incidents WHERE fingerprint_key = v_fingerprint
    );
    DELETE FROM public.telemetry_incidents WHERE fingerprint_key = v_fingerprint;
    DELETE FROM public.telemetry_events WHERE id = v_event_id;

    RAISE NOTICE '✓ Smoke B cleanup done';
END;
$$;

COMMIT;
