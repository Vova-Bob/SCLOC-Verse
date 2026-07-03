-- Phase 3 — Incident Engine: candidate detection VIEWs
--
-- Два VIEW над public.telemetry_events для детекції інцидентів:
--   incident_candidates_live — вікно 10 хв (детекція real-time)
--   incident_candidates_24h  — вікно 24 год (аналітика/тренди)
--
-- Конституція: Стаття 20 (Dashboard Purity) — усі рішення обчислюються тут,
-- у SQL. Dashboard = SELECT *.
--
-- Fingerprint: component|operation|signal|release (людськочитний) + md5 (для Phase 4).
-- telemetry_version — окрема колонка (не в fingerprint поки tv=1).
-- severity — виводиться (Critical/Warning/NULL). Пороги хардкод (Phase 4 → incident_policy).
--
-- Конвенція (див. 20260630000007): ТІЛЬКИ schema + views.

-- ============================================================================
-- incident_candidates_live — детектор (10 хв вікно)
-- ============================================================================
CREATE OR REPLACE VIEW control_center.incident_candidates_live AS
WITH failed AS (
    SELECT app_version, telemetry_version, component, operation,
           COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') AS signal,
           install_id, user_id, received_at
    FROM public.telemetry_events
    WHERE outcome = 'Failed'
      AND received_at > now() - interval '10 minutes'
),
totals AS (
    SELECT app_version, component, operation, COUNT(*) AS total
    FROM public.telemetry_events
    WHERE outcome IN ('Succeeded', 'Failed')
      AND received_at > now() - interval '10 minutes'
    GROUP BY app_version, component, operation
),
grp AS (
    SELECT app_version, telemetry_version, component, operation, signal,
           COUNT(*) AS failed_now,
           COUNT(DISTINCT install_id) AS affected_installs,
           COUNT(DISTINCT user_id) AS affected_users,
           MIN(received_at) AS first_seen,
           MAX(received_at) AS last_seen
    FROM failed
    GROUP BY app_version, telemetry_version, component, operation, signal
)
SELECT
    g.component || '|' || g.operation || '|' || g.signal || '|' || g.app_version
        AS fingerprint_key,
    md5(g.component || '|' || g.operation || '|' || g.signal || '|' || g.app_version)
        AS fingerprint_hash,
    g.app_version     AS release,
    g.telemetry_version,
    g.component,
    g.operation,
    g.signal,
    g.failed_now,
    COALESCE(t.total, 0) AS total_now,
    ROUND(100.0 * g.failed_now / NULLIF(COALESCE(t.total, 0), 0), 1) AS failure_pct,
    g.affected_installs,
    g.affected_users,
    g.first_seen,
    g.last_seen,
    CASE
        WHEN g.affected_installs >= 10
          OR 100.0 * g.failed_now / NULLIF(COALESCE(t.total, 0), 0) > 50
            THEN 'Critical'
        WHEN g.failed_now >= 5
         AND 100.0 * g.failed_now / NULLIF(COALESCE(t.total, 0), 0) > 20
            THEN 'Warning'
        ELSE NULL
    END AS severity
FROM grp g
LEFT JOIN totals t
       ON t.app_version = g.app_version
      AND t.component = g.component
      AND t.operation = g.operation
WHERE g.failed_now >= 1;

-- ============================================================================
-- incident_candidates_24h — аналітика (24 год вікно)
-- ============================================================================
CREATE OR REPLACE VIEW control_center.incident_candidates_24h AS
WITH failed AS (
    SELECT app_version, telemetry_version, component, operation,
           COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') AS signal,
           install_id, user_id, received_at
    FROM public.telemetry_events
    WHERE outcome = 'Failed'
      AND received_at > now() - interval '24 hours'
),
totals AS (
    SELECT app_version, component, operation, COUNT(*) AS total
    FROM public.telemetry_events
    WHERE outcome IN ('Succeeded', 'Failed')
      AND received_at > now() - interval '24 hours'
    GROUP BY app_version, component, operation
),
grp AS (
    SELECT app_version, telemetry_version, component, operation, signal,
           COUNT(*) AS failed_24h,
           COUNT(DISTINCT install_id) AS affected_installs_24h,
           COUNT(DISTINCT user_id) AS affected_users_24h,
           MIN(received_at) AS first_seen,
           MAX(received_at) AS last_seen
    FROM failed
    GROUP BY app_version, telemetry_version, component, operation, signal
)
SELECT
    g.component || '|' || g.operation || '|' || g.signal || '|' || g.app_version
        AS fingerprint_key,
    md5(g.component || '|' || g.operation || '|' || g.signal || '|' || g.app_version)
        AS fingerprint_hash,
    g.app_version     AS release,
    g.telemetry_version,
    g.component,
    g.operation,
    g.signal,
    g.failed_24h,
    COALESCE(t.total, 0) AS total_24h,
    ROUND(100.0 * g.failed_24h / NULLIF(COALESCE(t.total, 0), 0), 1) AS failure_pct_24h,
    g.affected_installs_24h,
    g.affected_users_24h,
    g.first_seen,
    g.last_seen
FROM grp g
LEFT JOIN totals t
       ON t.app_version = g.app_version
      AND t.component = g.component
      AND t.operation = g.operation
WHERE g.failed_24h >= 1;
