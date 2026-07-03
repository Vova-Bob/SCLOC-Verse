-- Phase 4 — Incident Engine: таблиці життя інцидентів + політика
--
-- Стаття 21 (Incident Identity): інцидент = incident_id, не fingerprint.
-- Рецидив після Closed = новий інцидент.
-- Стаття 19 (Promotion Immutability): promotion тільки читає telemetry_events.
-- Ніякого jsonb. Плоскі таблиці.

-- 1. telemetry_incidents — життя інцидентів
CREATE TABLE IF NOT EXISTS public.telemetry_incidents (
    id                    bigserial PRIMARY KEY,
    fingerprint_key       text NOT NULL,         -- Installation|Sync|42501|1.2.0
    fingerprint_hash      text NOT NULL,         -- md5(fingerprint_key)
    release               text NOT NULL,
    component             text NOT NULL,
    operation             text NOT NULL,
    signal                text NOT NULL,
    root_event_id         uuid REFERENCES public.telemetry_events(id) ON DELETE SET NULL,
    last_event_id         uuid REFERENCES public.telemetry_events(id) ON DELETE SET NULL,
    opened_at             timestamptz NOT NULL DEFAULT now(),
    last_event_at         timestamptz NOT NULL DEFAULT now(),
    closed_at             timestamptz,
    status                text NOT NULL DEFAULT 'Active',
    highest_severity      text NOT NULL DEFAULT 'Warning',
    peak_failure_pct      numeric(5,1),
    affected_users        int NOT NULL DEFAULT 0,
    affected_installs     int NOT NULL DEFAULT 0,
    event_count           bigint NOT NULL DEFAULT 0,
    CONSTRAINT chk_incident_status CHECK (status IN ('Active','Confirmed','Investigating','Resolved','Closed')),
    CONSTRAINT chk_incident_severity CHECK (highest_severity IN ('Critical','Warning'))
);

CREATE INDEX IF NOT EXISTS idx_incidents_fp_open
    ON public.telemetry_incidents(fingerprint_hash) WHERE status != 'Closed';
CREATE INDEX IF NOT EXISTS idx_incidents_opened
    ON public.telemetry_incidents(opened_at DESC);

-- RLS: deny-all для клієнтів. Promotion-функція (SECURITY DEFINER) обходить RLS.
-- cc_readonly читає через control_center.incidents VIEW.
ALTER TABLE public.telemetry_incidents ENABLE ROW LEVEL SECURITY;
CREATE POLICY "deny all on telemetry_incidents"
    ON public.telemetry_incidents AS RESTRICTIVE FOR ALL TO anon, authenticated
    USING (false) WITH CHECK (false);

-- 2. incident_policy — per-component пороги (НЕ singleton)
CREATE TABLE IF NOT EXISTS public.incident_policy (
    component                    text NOT NULL,           -- '_default' або конкретний
    operation                    text NOT NULL DEFAULT '_',  -- '_' = будь-яка операція
    signal                       text NOT NULL DEFAULT '_',  -- '_' = будь-який signal
    min_sample                   int NOT NULL DEFAULT 5,
    failure_threshold_pct        numeric(5,1) NOT NULL DEFAULT 20.0,
    critical_affected_threshold  int NOT NULL DEFAULT 10,
    auto_close_after_minutes     int NOT NULL DEFAULT 60,
    enabled                      boolean NOT NULL DEFAULT true,
    PRIMARY KEY (component, operation, signal)
);

ALTER TABLE public.incident_policy ENABLE ROW LEVEL SECURITY;
CREATE POLICY "deny all on incident_policy"
    ON public.incident_policy AS RESTRICTIVE FOR ALL TO anon, authenticated
    USING (false) WITH CHECK (false);

-- Seed: default + per-component (різні пороги для різних сервісів)
INSERT INTO public.incident_policy (component) VALUES ('_default')
    ON CONFLICT DO NOTHING;
INSERT INTO public.incident_policy (component, failure_threshold_pct, critical_affected_threshold)
VALUES ('Installation', 20.0, 5) ON CONFLICT DO NOTHING;
INSERT INTO public.incident_policy (component, failure_threshold_pct, critical_affected_threshold)
VALUES ('Auth', 40.0, 10) ON CONFLICT DO NOTHING;
INSERT INTO public.incident_policy (component, failure_threshold_pct, min_sample, critical_affected_threshold)
VALUES ('Updater', 5.0, 3, 5) ON CONFLICT DO NOTHING;
INSERT INTO public.incident_policy (component, failure_threshold_pct, min_sample, critical_affected_threshold)
VALUES ('LIA', 5.0, 1, 2) ON CONFLICT DO NOTHING;
