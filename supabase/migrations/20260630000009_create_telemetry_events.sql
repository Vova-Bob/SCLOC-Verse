-- Міграція: SCLOC Observability Platform — таблиця подій
--
-- Єдина append-only таблиця спостережуваності (Event Sourcing Light).
-- Конституція: Стаття 5 (Append-Only) — лише INSERT; UPDATE заборонено;
-- DELETE лише для retention-purge (service_role).
--
-- Схема повна (контракт) з першого слайсу — клієнт Slice 1 заповнює підмножину
-- колонок (Application.Start), решта лишається NULL до відповідних слайсів
-- (additive-only, Стаття 13). Деталі: docs/observability/Observability-Architecture.md

CREATE TABLE IF NOT EXISTS public.telemetry_events (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    client_event_id   uuid NOT NULL,                      -- дедуп (Стаття 6)
    session_id        uuid NOT NULL,                      -- запуск (Start → Exit)
    correlation_id    uuid NOT NULL,                      -- trace (Стаття 11)
    step              int  NOT NULL,                      -- порядок у trace
    install_id        text REFERENCES public.app_installations(install_id) ON DELETE SET NULL,
    user_id           uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    occurred_at       timestamptz NOT NULL,               -- клієнтський UTC (форензика)
    received_at       timestamptz NOT NULL DEFAULT now(), -- сервер (вікна детекції)
    app_version       text NOT NULL,
    git_commit        text,
    channel           text NOT NULL DEFAULT 'stable',
    telemetry_version int  NOT NULL DEFAULT 1,
    os_version        text,
    country           text,
    component         text NOT NULL,
    operation         text NOT NULL,
    outcome           text NOT NULL,
    severity          text NOT NULL DEFAULT 'Info',
    category          text NOT NULL DEFAULT 'Operational',
    source            text,
    http_status       int,
    hresult           text,
    supabase_code     text,
    exception_type    text,
    error_message     text,
    duration_ms       int,
    detail            jsonb,
    -- Інваріанти (Стаття 13 — additive CHECK):
    CONSTRAINT chk_telemetry_outcome  CHECK (outcome  IN ('Started','Succeeded','Failed','Cancelled','Skipped')),
    CONSTRAINT chk_telemetry_severity CHECK (severity IN ('Info','Warning','Error','Critical','Crash')),
    CONSTRAINT chk_telemetry_category CHECK (category IN ('Critical','Operational','Diagnostic','Analytics')),
    -- Кожна невдача має нести diagnostic-сигнал (придатність до групування).
    CONSTRAINT chk_telemetry_failed_has_signal CHECK (
        outcome <> 'Failed'
        OR COALESCE(source, hresult, supabase_code, http_status::text, exception_type) IS NOT NULL
    )
);

-- Дедуп + аналітика. Індекси lean (Стаття: Free-Tier-Driven).
CREATE UNIQUE INDEX IF NOT EXISTS uniq_telemetry_client_event_id
    ON public.telemetry_events(client_event_id);
CREATE INDEX IF NOT EXISTS idx_telemetry_received
    ON public.telemetry_events(received_at DESC);
CREATE INDEX IF NOT EXISTS idx_telemetry_trace
    ON public.telemetry_events(correlation_id, step);
CREATE INDEX IF NOT EXISTS idx_telemetry_detect
    ON public.telemetry_events(component, operation, outcome, received_at DESC);

-- Безпека (RLS). Патерн нових таблиць проєкту: anon deny-all; authenticated —
-- лише owner-only INSERT через JWT (Стаття 5). Жодного GRANT anon (foot-proof).
ALTER TABLE public.telemetry_events ENABLE ROW LEVEL SECURITY;

CREATE POLICY "deny all anon on telemetry_events"
    ON public.telemetry_events AS RESTRICTIVE
    FOR ALL TO anon
    USING (false) WITH CHECK (false);

CREATE POLICY "auth insert own telemetry_events"
    ON public.telemetry_events
    FOR INSERT TO authenticated
    WITH CHECK (user_id = auth.uid());

GRANT INSERT ON public.telemetry_events TO authenticated;
