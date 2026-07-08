-- Rollback 20260708100001: повертає chk_incident_status до стану міграції 00012 (5 значень).
--
-- Аварійна процедура відкату. УВАГА: відновлює P0-bug F2 — статус Mitigated
-- знову стає недосяжним (transition_incident кине CheckViolationException).
-- Застосовувати лише якщо міграція спричинила регресію, доведену тестом.

ALTER TABLE public.telemetry_incidents DROP CONSTRAINT IF EXISTS chk_incident_status;
ALTER TABLE public.telemetry_incidents
    ADD CONSTRAINT chk_incident_status CHECK (status IN
        ('Active','Confirmed','Investigating','Resolved','Closed'));
