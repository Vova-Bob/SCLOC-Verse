-- Міграція 20260707030001: Phase 3A — Phase 0 partial індекси на telemetry_events
--
-- Контекст:
--   Затверджено в KB #70 (Phase 0 Optimization-Plan) + KB §16.5 ACTIVATE.
--   Pre-Implementation Forensic (2026-07-07) підтвердив, що нові індекси
--   не дублюють існуючі (idx_telemetry_received на received_at,
--   idx_telemetry_detect на (component, operation, outcome, received_at)).
--
--   Статистика до міграції (pg_stat_user_indexes):
--     idx_telemetry_received  799 scans / 11822 tup read
--     idx_telemetry_detect    206 scans / 554 tup read
--     idx_telemetry_trace      27 scans / 3361 tup read
--   → отримаємо 3 нові комбінації, не дублюючі існуючі.
--
--   1) idx_telemetry_failed (component, operation, received_at DESC) WHERE outcome='Failed'
--      → прискорить trigger trg_telemetry_failed_promote та функцію
--        promote_incident_candidates_for_event (фільтр по Failed у 10-хв вікні).
--        Partial зменшить розмір (наразі ~5 з 705 подій = Failed).
--
--   2) idx_telemetry_version_window (app_version, received_at DESC) WHERE app_version IS NOT NULL
--      → прискорить views release_health, release_health_detail (фільтр по
--        app_version за 7 днів).
--
--   3) idx_telemetry_occurred (occurred_at DESC)
--      → нова комбінація (зараз є лише idx_telemetry_received на received_at,
--        але occurred_at — клієнтський час, отримує запити від CC Traces page).
--
-- Additive-only. Усі IF NOT EXISTS (ідемпотентні).

CREATE INDEX IF NOT EXISTS idx_telemetry_failed
    ON public.telemetry_events(component, operation, received_at DESC)
    WHERE outcome = 'Failed';

CREATE INDEX IF NOT EXISTS idx_telemetry_version_window
    ON public.telemetry_events(app_version, received_at DESC)
    WHERE app_version IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_telemetry_occurred
    ON public.telemetry_events(occurred_at DESC);
