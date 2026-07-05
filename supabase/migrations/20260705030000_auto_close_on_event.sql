-- Міграція 20260705030000: Повернення авто-close у per-event pipeline (F-2)
--
-- Контекст:
--   У міграції 00016 автор заложив авто-close старих інцидентів у тілі
--   batch-функції promote_incident_candidates(). Після переходу на per-event
--   промоутер (Коміт A, міграція 20260705021100) авто-close загубився —
--   інциденти статусом 'Active' залишались відкритими вічно.
--
--   Ця міграція повертає авторський задум: будь-який 'Active' інцидент,
--   що не отримав нової Failed за policy.auto_close_after_minutes,
--   автоматично закривається при наступній Failed-події.
--
-- Архітектурні рішення (узгоджені):
--
--   1. Окрема функція auto_close_stale_incidents() — єдина відповідальність
--      «закривати застарілі Active інциденти». Невикидаюча, SECURITY DEFINER.
--
--   2. Лише status = 'Active' (НЕ '!= Closed'). Безпечно для майбутніх
--      статусів Investigating/Acknowledged/Resolved, які автор додавав
--      у Phase 5 (transition_incident) — авто-close їх не зачепить.
--
--   3. Один wrapper — tg_promote_after_failed() розширено:
--      ПЕРЕД promote_incident_candidates_for_event() викликається
--      auto_close_stale_incidents(). Один wrapper — одне місце відмови,
--      простіше дебажити. Логіка дублює авторський порядок з міграції 00016
--      (авто-close на початку batch-функції, перед циклом candidates).
--
--   4. EXCEPTION-safe — кожен PERFORM у власному BEGIN/EXCEPTION.
--      Помилка авто-close НЕ ламає ні промоутер, ні INSERT клієнтської
--      телеметрії (RAISE NOTICE у логах Supabase Studio).

BEGIN;

-- ============================================================================
-- 1. auto_close_stale_incidents()
-- ============================================================================
-- Логіка — точна копія авто-close блоку з міграції 00016 (рядки 60-72),
-- єдина зміна: WHERE i.status = 'Active' замість WHERE i.status != 'Closed'.
CREATE OR REPLACE FUNCTION public.auto_close_stale_incidents()
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    UPDATE public.telemetry_incidents i
    SET status = 'Closed', closed_at = now()
    WHERE i.status = 'Active'
      AND i.last_event_at < now() - (
        COALESCE(
          (SELECT auto_close_after_minutes FROM public.incident_policy p
           WHERE p.component = i.component AND p.enabled
           ORDER BY (p.signal = '_') DESC, (p.operation = '_') DESC LIMIT 1),
          (SELECT auto_close_after_minutes FROM public.incident_policy WHERE component = '_default' LIMIT 1),
          60
        ) || ' minutes'
      )::interval;
END;
$$;

GRANT EXECUTE ON FUNCTION public.auto_close_stale_incidents() TO cc_readonly;

COMMENT ON FUNCTION public.auto_close_stale_incidents() IS
    'Авто-close інцидентів статусом Active, що не отримали нових Failed за policy.auto_close_after_minutes (з fallback на _default=60 хв). Викликається з tg_promote_after_failed() перед per-event промоутером. Лише Active — Investigating/Acknowledged/Resolved не зачеплює. Авторський задум відновлено з міграції 00016.';

-- ============================================================================
-- 2. Розширення tg_promote_after_failed() — авто-close + per-event промоутер
-- ============================================================================
-- Інваріант: одна Failed-подія → одне місце обробки → два кроки послідовно:
--   1) auto_close_stale_incidents() — закрити застарілі (авторський порядок)
--   2) promote_incident_candidates_for_event(NEW.id) — промоутити цей event
-- Кожен крок у власному EXCEPTION-блоці — збій одного не блокує іншого
-- і не ламає INSERT клієнтської телеметрії.
CREATE OR REPLACE FUNCTION public.tg_promote_after_failed()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    -- Крок 1: авто-close старих Active інцидентів (авторський задум з міграції 00016).
    BEGIN
        PERFORM public.auto_close_stale_incidents();
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE 'auto_close_stale_incidents failed: % (%)', SQLERRM, SQLSTATE;
    END;

    -- Крок 2: промоутер конкретного event (Коміт A).
    BEGIN
        PERFORM public.promote_incident_candidates_for_event(NEW.id);
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE 'promote_incident_candidates_for_event failed for event %: % (%)',
            NEW.id, SQLERRM, SQLSTATE;
    END;

    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION public.tg_promote_after_failed() IS
    'AFTER INSERT FOR EACH ROW wrapper для обробки Failed-подій. Два кроки послідовно у власних EXCEPTION-блоках: (1) auto_close_stale_incidents — закрити застарілі Active інциденти; (2) promote_incident_candidates_for_event — промоутити цей event. Телеметрія клієнта гарантовано записується навіть при збоях.';

-- ============================================================================
-- 3. Smoke test — доведення повного інваріанту end-to-end
-- ============================================================================
-- Інваріант 1: INSERT Failed → event → candidates → incident → component_health → release_health
-- Інваріант 2: старий Active інцидент (75 хв) → закривається при наступній Failed
DO $$
DECLARE
    v_event_id        uuid;
    v_old_incident_id bigint;
    v_new_incident_id bigint;
    v_cnt             integer;
    v_health          text;
    v_failed_count    bigint;
BEGIN
    -- =====================================================================
    -- Інваріант 1: повний pipeline від Client до release_health
    -- =====================================================================

    -- Підготовка: очистити smoke-дані, якщо раптом вони залишились від попередніх запусків
    DELETE FROM public.telemetry_incidents WHERE fingerprint_key = 'LIA|SmokeInv|0xSMOKEINV|1.0.0.1';
    DELETE FROM public.telemetry_events   WHERE client_event_id = 'cafe3000-0000-0000-0000-000000000001'::uuid;

    -- Симуляція Failed-події від клієнта
    INSERT INTO public.telemetry_events (
        client_event_id, session_id, correlation_id, step,
        install_id, occurred_at, app_version,
        component, operation, outcome, severity, category, source,
        hresult, error_message, detail
    ) VALUES (
        'cafe3000-0000-0000-0000-000000000001'::uuid,
        'cafe3000-0000-0000-0000-000000000002'::uuid,
        'cafe3000-0000-0000-0000-000000000002'::uuid,
        1,
        '46c630be2be24e4f8d09a68b59787ff5',
        NOW(),
        '1.0.0.1',
        'LIA',
        'SmokeInv',
        'Failed',
        'Error',
        'Operational',
        'SmokeInv',
        '0xSMOKEINV',
        'Invariant smoke test (auto-cleanup)',
        '{"smoke_test":true}'::jsonb
    )
    RETURNING id INTO v_event_id;

    -- Перевірка 1: candidates_live побачив Failed (10 хв вікно)
    SELECT count(*) INTO v_cnt
    FROM control_center.incident_candidates_live
    WHERE fingerprint_key = 'LIA|SmokeInv|0xSMOKEINV|1.0.0.1';
    ASSERT v_cnt >= 1,
        'Інваріант 1 FAILED: candidates_live не побачив event %', v_event_id;

    -- Перевірка 2: інцидент створено (через тригер промоутера)
    -- Примітка: policy для LIA має min_sample=1, failure_threshold_pct=5% — одна Failed достатня.
    SELECT id INTO v_new_incident_id
    FROM public.telemetry_incidents
    WHERE fingerprint_key = 'LIA|SmokeInv|0xSMOKEINV|1.0.0.1';
    ASSERT v_new_incident_id IS NOT NULL,
        'Інваріант 1 FAILED: інцидент не створено для event %', v_event_id;

    -- Перевірка 3: component_health показує LIA не GREEN (є active candidate)
    SELECT count(*) INTO v_cnt
    FROM control_center.component_health
    WHERE component = 'LIA' AND active_incidents >= 1;
    ASSERT v_cnt = 1,
        'Інваріант 1 FAILED: component_health.LIA не показує active_incidents';

    -- Перевірка 4: release_health показує failed >= 1 для 1.0.0.1
    SELECT count(*) INTO v_cnt
    FROM control_center.release_health
    WHERE app_version = '1.0.0.1' AND failed >= 1;
    ASSERT v_cnt = 1,
        'Інваріант 1 FAILED: release_health не показує failed для 1.0.0.1';

    RAISE NOTICE '✓ Інваріант 1 PASSED: Failed → candidates → incident → component_health → release_health';

    -- =====================================================================
    -- Інваріант 2: авто-close старого Active інциденту (75 хв тому)
    -- =====================================================================

    -- Створити історичний інцидент (75 хв тому — перевищує LIA policy 60 хв)
    INSERT INTO public.telemetry_incidents (
        fingerprint_key, fingerprint_hash, release, component, operation, signal,
        root_event_id, last_event_id, opened_at, last_event_at, status,
        highest_severity, peak_failure_pct, affected_users, affected_installs, event_count
    ) VALUES (
        'LIA|SmokeOld|0xSMOKEOLD|1.0.0.1', md5('LIA|SmokeOld|0xSMOKEOLD|1.0.0.1'),
        '1.0.0.1', 'LIA', 'SmokeOld', '0xSMOKEOLD',
        NULL, NULL,
        NOW() - interval '75 minutes',  -- opened 75 хв тому
        NOW() - interval '75 minutes',  -- last_event 75 хв тому (старіший за policy 60 хв)
        'Active', 'Warning', 20.0, 1, 1, 1
    )
    RETURNING id INTO v_old_incident_id;

    -- Підготувати ще одну Failed-подію, яка має запустити тригер
    INSERT INTO public.telemetry_events (
        client_event_id, session_id, correlation_id, step,
        install_id, occurred_at, app_version,
        component, operation, outcome, severity, category, source,
        hresult, error_message, detail
    ) VALUES (
        'cafe3000-0000-0000-0000-000000000003'::uuid,
        'cafe3000-0000-0000-0000-000000000004'::uuid,
        'cafe3000-0000-0000-0000-000000000004'::uuid,
        1,
        '46c630be2be24e4f8d09a68b59787ff5',
        NOW(),
        '1.0.0.1',
        'LIA',
        'SmokeInv2',
        'Failed',
        'Error',
        'Operational',
        'SmokeInv2',
        '0xSMOKEINV2',
        'Invariant smoke test 2 (auto-cleanup)',
        '{"smoke_test":true}'::jsonb
    );
    -- Тригер спрацював: auto_close_stale_incidents() закрив старий інцидент SmokeOld.

    -- Перевірка 5: старий інцидент закрився
    SELECT count(*) INTO v_cnt
    FROM public.telemetry_incidents
    WHERE id = v_old_incident_id AND status = 'Closed' AND closed_at IS NOT NULL;
    ASSERT v_cnt = 1,
        'Інваріант 2 FAILED: старий інцидент % не закрився авто-close', v_old_incident_id;

    RAISE NOTICE '✓ Інваріант 2 PASSED: 75-хв Active інцидент → Closed при наступній Failed';

    -- =====================================================================
    -- Cleanup — повернення БД до чистого стану
    -- =====================================================================
    DELETE FROM public.telemetry_incidents
    WHERE fingerprint_key IN ('LIA|SmokeInv|0xSMOKEINV|1.0.0.1',
                              'LIA|SmokeInv2|0xSMOKEINV2|1.0.0.1',
                              'LIA|SmokeOld|0xSMOKEOLD|1.0.0.1');
    DELETE FROM public.telemetry_events
    WHERE client_event_id IN ('cafe3000-0000-0000-0000-000000000001'::uuid,
                              'cafe3000-0000-0000-0000-000000000003'::uuid);

    RAISE NOTICE '✓ Cleanup done — БД повернута до чистого стану';
END;
$$;

COMMIT;
