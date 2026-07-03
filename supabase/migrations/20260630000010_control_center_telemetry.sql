-- Міграція: Control Center — контракт спостережуваності (Slice 1)
--
-- Додає VIEW поверх public.telemetry_events у схему control_center.
-- Control Center (Blazor) читає лише цю схему через роль cc_readonly.
--
-- Конвенція (див. 20260630000007_control_center_contract.sql):
-- ТІЛЬКИ schema + views. Без roles/grants/функцій/тригерів/таблиць.
-- Роль cc_readonly має ALTER DEFAULT PRIVILEGES для майбутніх об'єктів схеми —
-- цей VIEW автоматично отримає SELECT для cc_readonly.

CREATE OR REPLACE VIEW control_center.telemetry_events AS
SELECT
    e.id,
    e.client_event_id,
    e.session_id,
    e.correlation_id,
    e.step,
    e.install_id,
    e.user_id,
    e.occurred_at,
    e.received_at,
    e.app_version,
    e.git_commit,
    e.channel,
    e.telemetry_version,
    e.os_version,
    e.country,
    e.component,
    e.operation,
    e.outcome,
    e.severity,
    e.category,
    e.source,
    e.http_status,
    e.hresult,
    e.supabase_code,
    e.exception_type,
    e.error_message,
    e.duration_ms,
    e.detail
FROM public.telemetry_events e
ORDER BY e.received_at DESC;

-- Trace-реконструкція: послідовність кроків запуску (Стаття 11).
CREATE OR REPLACE VIEW control_center.traces AS
SELECT
    e.correlation_id,
    e.session_id,
    e.install_id,
    e.app_version,
    e.step,
    e.occurred_at,
    e.component,
    e.operation,
    e.outcome,
    e.category,
    COALESCE(e.hresult, e.supabase_code, e.http_status::text, e.exception_type, '-') AS signal,
    e.error_message,
    e.duration_ms
FROM public.telemetry_events e
ORDER BY e.correlation_id, e.step;
