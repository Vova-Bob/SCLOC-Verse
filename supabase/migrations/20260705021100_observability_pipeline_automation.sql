-- Міграція 20260705021100: Observability Pipeline Automation (Коміт A)
--
-- Мета: доведення observability pipeline до завершеного стану.
-- Замість ручного виклику promote_incident_candidates() — автоматична
-- реакція на INSERT Failed telemetry_events.
--
-- Архітектурні рішення:
--   1. НЕ ЧІПАТИ існуючу promote_incident_candidates() — вона доведена
--      експериментально. Залишається як batch-варіант (для майбутнього cron).
--   2. ДОДАТИ per-event wrapper promote_incident_candidates_for_event(uuid)
--      — обчислює статистику лише для одного fingerprint замість ітерації
--      по всіх candidates. Миттєво.
--   3. Trigger AFTER INSERT WHEN outcome='Failed' викликає wrapper.
--      EXCEPTION wrapper у trigger function гарантує, що помилка промоутера
--      НЕ ламає INSERT клієнтської телеметрії.
--   4. Trigger AFTER INSERT OR UPDATE на telemetry_incidents автоматично
--      викликає refresh_knowledge_coverage() — MV singleton, REFRESH без
--      CONCURRENTLY (виконується за мілісекунди, прийнятне блокування).
--   5. pipeline_health_meta (singleton table) зберігає last_knowledge_refresh
--      для observability_health VIEW (Коміт B).
--   6. error_reports офіційно позначається "Reserved for future" — не мертва,
--      а зарезервована для майбутнього каналу client-side crash reports.

BEGIN;

-- ============================================================================
-- 1. Singleton-таблиця для health-метрик pipeline
-- ============================================================================
CREATE TABLE IF NOT EXISTS control_center.pipeline_health_meta (
    singleton      boolean PRIMARY KEY DEFAULT true,
    last_knowledge_refresh timestamptz,
    CONSTRAINT singleton_chk CHECK (singleton = true)
);

INSERT INTO control_center.pipeline_health_meta (singleton, last_knowledge_refresh)
VALUES (true, NULL)
ON CONFLICT (singleton) DO NOTHING;

GRANT SELECT ON control_center.pipeline_health_meta TO cc_readonly;

COMMENT ON TABLE control_center.pipeline_health_meta IS
    'Singleton (1 рядок). Зберігає timestamps останніх pipeline-операцій для observability_health VIEW. Не шлеться клієнтом — оновлюється SECURITY DEFINER функціями всередині БД.';

-- ============================================================================
-- 2. Оновити refresh_knowledge_coverage — фіксувати час у health_meta
-- ============================================================================
CREATE OR REPLACE FUNCTION public.refresh_knowledge_coverage()
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    REFRESH MATERIALIZED VIEW control_center.knowledge_coverage;
    UPDATE control_center.pipeline_health_meta
    SET    last_knowledge_refresh = NOW()
    WHERE  singleton = true;
END;
$$;

GRANT EXECUTE ON FUNCTION public.refresh_knowledge_coverage() TO cc_readonly;

COMMENT ON FUNCTION public.refresh_knowledge_coverage IS
    'Slice 5 + automation: REFRESH MV knowledge_coverage + оновити pipeline_health_meta.last_knowledge_refresh. SECURITY DEFINER, бо cc_readonly не має прав REFRESH.';

-- ============================================================================
-- 3. Per-event promote (lightweight wrapper)
-- ============================================================================
-- Логіка ідентична promote_incident_candidates(), але:
--   - приймає конкретний event uuid
--   - будує fingerprint напряму з event (без CTE по всіх Failed)
--   - рахує статистику лише для цього fingerprint за 10 хв вікном
--   - UPSERT в telemetry_incidents + INSERT в notification_queue
-- Повертає void. Невикидаюча (всі гілки RETURN, не EXCEPTION).
CREATE OR REPLACE FUNCTION public.promote_incident_candidates_for_event(p_event uuid)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_component       text;
    v_operation       text;
    v_app_version     text;
    v_hresult         text;
    v_supabase_code   text;
    v_http_status     integer;
    v_exception_type  text;
    v_signal          text;
    v_fingerprint_key text;
    v_fingerprint_hash text;
    v_window          interval := interval '10 minutes';
    v_pol             public.incident_policy%ROWTYPE;
    v_failed_now      bigint;
    v_total_now       bigint;
    v_affected_installs bigint;
    v_affected_users  bigint;
    v_failure_pct     numeric;
    v_existing_id     bigint;
    v_new_id          bigint;
    v_root_id         uuid;
    v_last_id         uuid;
    v_new_sev         text;
BEGIN
    -- 3.1. Отримати поля події
    SELECT component, operation, app_version, hresult, supabase_code,
           http_status, exception_type
    INTO v_component, v_operation, v_app_version, v_hresult, v_supabase_code,
         v_http_status, v_exception_type
    FROM public.telemetry_events
    WHERE id = p_event;

    IF v_component IS NULL THEN RETURN; END IF;

    -- 3.2. Сигнал (ідентично VIEW incident_candidates_live)
    v_signal := COALESCE(v_hresult, v_supabase_code,
                         v_http_status::text, v_exception_type, '-');
    v_fingerprint_key := v_component || '|' || v_operation || '|' ||
                         v_signal || '|' || v_app_version;
    v_fingerprint_hash := md5(v_fingerprint_key);

    -- 3.3. Політика
    SELECT * INTO v_pol FROM public.incident_policy
    WHERE component = v_component AND enabled
    ORDER BY (signal = '_') DESC, (operation = '_') DESC LIMIT 1;
    IF NOT FOUND THEN
        SELECT * INTO v_pol FROM public.incident_policy
        WHERE component = '_default' AND enabled LIMIT 1;
    END IF;
    IF NOT FOUND THEN RETURN; END IF;

    -- 3.4. Статистика за вікном (10 хв) — точковий fingerprint
    SELECT count(*),
           count(DISTINCT install_id),
           count(DISTINCT user_id)
    INTO v_failed_now, v_affected_installs, v_affected_users
    FROM public.telemetry_events
    WHERE outcome = 'Failed'
      AND component = v_component
      AND operation = v_operation
      AND app_version = v_app_version
      AND COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') = v_signal
      AND received_at > NOW() - v_window;

    SELECT count(*) INTO v_total_now
    FROM public.telemetry_events
    WHERE outcome IN ('Succeeded', 'Failed')
      AND component = v_component
      AND operation = v_operation
      AND app_version = v_app_version
      AND received_at > NOW() - v_window;

    v_failure_pct := CASE WHEN v_total_now > 0
                          THEN round(100.0 * v_failed_now / v_total_now, 1)
                          ELSE 0 END;

    -- 3.5. Перевірка порогів
    IF v_failed_now < v_pol.min_sample THEN RETURN; END IF;
    IF v_failure_pct < v_pol.failure_threshold_pct THEN RETURN; END IF;

    -- 3.6. Severity
    v_new_sev := CASE WHEN v_affected_installs >= v_pol.critical_affected_threshold
                      THEN 'Critical' ELSE 'Warning' END;

    -- 3.7. Існуючий відкритий інцидент з тим самим fingerprint
    SELECT id INTO v_existing_id FROM public.telemetry_incidents
    WHERE fingerprint_key = v_fingerprint_key AND status != 'Closed'
    LIMIT 1;

    -- 3.8. last_event_id для цього fingerprint
    SELECT id INTO v_last_id FROM public.telemetry_events
    WHERE component = v_component AND operation = v_operation
      AND COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') = v_signal
      AND app_version = v_app_version AND outcome = 'Failed'
    ORDER BY received_at DESC LIMIT 1;

    -- 3.9. UPSERT
    IF v_existing_id IS NOT NULL THEN
        UPDATE public.telemetry_incidents
        SET last_event_at      = NOW(),
            last_event_id      = v_last_id,
            affected_installs  = GREATEST(affected_installs, v_affected_installs),
            affected_users     = GREATEST(affected_users, v_affected_users),
            peak_failure_pct   = GREATEST(peak_failure_pct, v_failure_pct),
            event_count        = event_count + 1,
            highest_severity   = CASE WHEN v_new_sev = 'Critical' OR highest_severity = 'Critical'
                                      THEN 'Critical' ELSE 'Warning' END
        WHERE id = v_existing_id;
    ELSE
        -- 3.10. root_event_id (перша Failed у цьому fingerprint)
        SELECT id INTO v_root_id FROM public.telemetry_events
        WHERE component = v_component AND operation = v_operation
          AND COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') = v_signal
          AND app_version = v_app_version AND outcome = 'Failed'
        ORDER BY received_at ASC LIMIT 1;

        INSERT INTO public.telemetry_incidents (
            fingerprint_key, fingerprint_hash, release, component, operation, signal,
            root_event_id, last_event_id, opened_at, last_event_at, status,
            highest_severity, peak_failure_pct, affected_users, affected_installs, event_count
        ) VALUES (
            v_fingerprint_key, v_fingerprint_hash, v_app_version,
            v_component, v_operation, v_signal,
            v_root_id, v_last_id, NOW(), NOW(), 'Active',
            v_new_sev, v_failure_pct, v_affected_users, v_affected_installs, v_failed_now
        )
        RETURNING id INTO v_new_id;

        -- 3.11. Notification (дедуплікація через ON CONFLICT)
        INSERT INTO public.notification_queue (incident_id, notification_type, provider, payload)
        VALUES (v_new_id, 'IncidentCreated', 'Discord',
            jsonb_build_object(
                'version', 1,
                'incident_id', v_new_id,
                'component', v_component,
                'operation', v_operation,
                'signal', v_signal,
                'severity', v_new_sev,
                'release', v_app_version,
                'affected_installs', v_affected_installs,
                'affected_users', v_affected_users,
                'failure_pct', v_failure_pct
            )
        )
        ON CONFLICT DO NOTHING;
    END IF;
END;
$$;

GRANT EXECUTE ON FUNCTION public.promote_incident_candidates_for_event(uuid) TO cc_readonly;

COMMENT ON FUNCTION public.promote_incident_candidates_for_event(uuid) IS
    'Per-event lightweight промоутер. Викликається тригером trg_telemetry_failed_promote після INSERT outcome=''Failed''. Логіка ідентична promote_incident_candidates(), але оптимізована під один fingerprint (без циклу по candidates). Невикидаюча.';

-- ============================================================================
-- 4. Trigger function: EXCEPTION-safe wrapper
-- ============================================================================
-- Гарантує: якщо промоутер впаде, клієнтський INSERT НЕ відкочується.
-- Структура: зовнішній BEGIN/EXCEPTION ловить усе, повертає NEW завжди.
CREATE OR REPLACE FUNCTION public.tg_promote_after_failed()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    BEGIN
        PERFORM public.promote_incident_candidates_for_event(NEW.id);
    EXCEPTION WHEN OTHERS THEN
        -- Ковтнути: телеметрія клієнта важливіша за інцидент.
        -- RAISE NOTICE → видно у Postgres логах Supabase Studio.
        RAISE NOTICE 'promote_incident_candidates_for_event failed for event %: % (%)',
            NEW.id, SQLERRM, SQLSTATE;
    END;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION public.tg_promote_after_failed() IS
    'AFTER INSERT FOR EACH ROW wrapper для promote_incident_candidates_for_event. EXCEPTION-safe: телеметрія клієнта гарантовано записується навіть якщо промоутер впав.';

-- ============================================================================
-- 5. Trigger на telemetry_events
-- ============================================================================
DROP TRIGGER IF EXISTS trg_telemetry_failed_promote ON public.telemetry_events;

CREATE TRIGGER trg_telemetry_failed_promote
    AFTER INSERT ON public.telemetry_events
    FOR EACH ROW
    WHEN (NEW.outcome = 'Failed')
    EXECUTE FUNCTION public.tg_promote_after_failed();

-- ============================================================================
-- 6. Trigger на telemetry_incidents → auto-refresh knowledge_coverage
-- ============================================================================
CREATE OR REPLACE FUNCTION public.tg_incident_refresh_coverage()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    BEGIN
        PERFORM public.refresh_knowledge_coverage();
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE 'refresh_knowledge_coverage failed: % (%)', SQLERRM, SQLSTATE;
    END;
    RETURN NULL;  -- AFTER STATEMENT → NULL коректно
END;
$$;

DROP TRIGGER IF EXISTS trg_incident_refresh_coverage ON public.telemetry_incidents;

CREATE TRIGGER trg_incident_refresh_coverage
    AFTER INSERT OR UPDATE ON public.telemetry_incidents
    FOR EACH STATEMENT
    EXECUTE FUNCTION public.tg_incident_refresh_coverage();

COMMENT ON FUNCTION public.tg_incident_refresh_coverage() IS
    'AFTER INSERT OR UPDATE FOR EACH STATEMENT — автоматичний REFRESH knowledge_coverage при зміні incidents. EXCEPTION-safe.';

-- ============================================================================
-- 7. error_reports — офіційний статус Reserved
-- ============================================================================
COMMENT ON TABLE public.error_reports IS
    'Reserved for future: client-side crash reports (unhandled exceptions, stack traces, screenshots). Наразі невикористовується — клієнтський код не шле сюди (спеціалізована телеметрія йде в telemetry_events). Не частина автоматичного observability pipeline. RLS deny all для anon/authenticated. Збережена для майбутнього каналу ручних краш-репортів поза типізованою телеметрією.';

-- ============================================================================
-- 8. Smoke test (доведення працездатності в межах міграції)
-- ============================================================================
-- Інваріант: Failed → ≤ 1 секунда → incident + notification.
-- Виконується в тій самій транзакції з міграцією. Якщо впаде — міграція rollback.
DO $$
DECLARE
    v_event_id     uuid;
    v_inc_before   bigint;
    v_inc_after    bigint;
    v_notif_before bigint;
    v_notif_after  bigint;
    v_fingerprint  text := 'LIA|SmokeTest|0xSMOKE|1.0.0.1';
BEGIN
    SELECT count(*), (SELECT count(*) FROM public.notification_queue)
    INTO v_inc_before, v_notif_before
    FROM public.telemetry_incidents
    WHERE fingerprint_key = v_fingerprint;

    INSERT INTO public.telemetry_events (
        client_event_id, session_id, correlation_id, step,
        install_id, occurred_at, app_version,
        component, operation, outcome, severity, category, source,
        hresult, error_message, detail
    ) VALUES (
        'cafe0000-0000-0000-0000-000000000001'::uuid,
        'cafe0000-0000-0000-0000-000000000002'::uuid,
        'cafe0000-0000-0000-0000-000000000002'::uuid,
        1,
        '46c630be2be24e4f8d09a68b59787ff5',
        NOW(),
        '1.0.0.1',
        'LIA',
        'SmokeTest',
        'Failed',
        'Error',
        'Operational',
        'SmokeTest',
        '0xSMOKE',
        'Pipeline smoke test (auto-cleanup)',
        '{"smoke_test":true}'::jsonb
    )
    RETURNING id INTO v_event_id;

    -- Тригер спрацював синхронно (AFTER INSERT FOR EACH ROW у тій самій транзакції)
    SELECT count(*) INTO v_inc_after
    FROM public.telemetry_incidents
    WHERE fingerprint_key = v_fingerprint;

    SELECT count(*) INTO v_notif_after FROM public.notification_queue;

    -- Інваріант: ≤ 1 секунда (насправді синхронно в межах транзакції)
    ASSERT v_inc_after = v_inc_before + 1,
        'Smoke test FAILED: очікувалось +1 incident, got before=% after=%', v_inc_before, v_inc_after;
    ASSERT v_notif_after = v_notif_before + 1,
        'Smoke test FAILED: очікувалось +1 notification, got before=% after=%', v_notif_before, v_notif_after;

    RAISE NOTICE '✓ Smoke test PASSED: event % → incident created → notification enqueued', v_event_id;

    -- Cleanup: повернення БД до чистого стану
    DELETE FROM public.notification_queue
    WHERE incident_id IN (
        SELECT id FROM public.telemetry_incidents WHERE fingerprint_key = v_fingerprint
    );
    DELETE FROM public.telemetry_incidents WHERE fingerprint_key = v_fingerprint;
    DELETE FROM public.telemetry_events WHERE id = v_event_id;

    RAISE NOTICE '✓ Smoke test cleanup done';
END;
$$;

COMMIT;
