-- Спеціалізовані VIEWs для Dashboard (Стаття 20 — Dashboard Purity)
-- Жодна сторінка UI не агрегує з telemetry_events напряму — лише через ці VIEWs.

-- 1. control_center.release_health — per-version success rates (7 днів)
CREATE OR REPLACE VIEW control_center.release_health AS
SELECT app_version,
       COUNT(*) FILTER (WHERE outcome = 'Succeeded') AS succeeded,
       COUNT(*) FILTER (WHERE outcome = 'Failed') AS failed,
       COUNT(DISTINCT install_id) AS active_installs
FROM public.telemetry_events
WHERE received_at > now() - interval '7 days'
  AND app_version IS NOT NULL
GROUP BY app_version
ORDER BY app_version DESC;

-- 2. control_center.platform_stats — KPI агрегати
CREATE OR REPLACE VIEW control_center.platform_stats AS
SELECT
  COUNT(*) FILTER (WHERE received_at > now() - interval '24 hours') AS events_24h,
  COUNT(DISTINCT user_id) FILTER (WHERE received_at > now() - interval '24 hours') AS active_users_24h,
  COUNT(DISTINCT install_id) FILTER (WHERE received_at > now() - interval '7 days') AS active_installations,
  (SELECT COUNT(*) FROM public.telemetry_incidents WHERE status != 'Closed') AS open_incidents
FROM public.telemetry_events;

-- 3. control_center.component_health — GREEN/YELLOW/RED per component (efficient CTE)
CREATE OR REPLACE VIEW control_center.component_health AS
WITH comps AS (
    SELECT * FROM (VALUES ('Application'), ('Auth'), ('Installation'), ('Updater'), ('LIA')) AS c(component)
),
cand AS (
    SELECT component, MAX(severity) AS sev
    FROM control_center.incident_candidates_live
    GROUP BY component
),
incs AS (
    SELECT component, COUNT(*) AS cnt
    FROM public.telemetry_incidents
    WHERE status = 'Active'
    GROUP BY component
),
lasts AS (
    SELECT component, MAX(received_at) AS last_event
    FROM public.telemetry_events
    WHERE received_at > now() - interval '24 hours'
    GROUP BY component
)
SELECT
    c.component,
    CASE
        WHEN cand.sev = 'Critical' THEN 'RED'
        WHEN cand.sev = 'Warning' THEN 'YELLOW'
        ELSE 'GREEN'
    END AS health,
    COALESCE(incs.cnt, 0) AS active_incidents,
    lasts.last_event AS last_event_at
FROM comps c
LEFT JOIN cand ON cand.component = c.component
LEFT JOIN incs ON incs.component = c.component
LEFT JOIN lasts ON lasts.component = c.component;
