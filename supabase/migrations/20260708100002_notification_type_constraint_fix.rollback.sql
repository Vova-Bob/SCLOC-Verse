-- Rollback 20260708100002: повертає notification_type-constraint до стану міграції 00016
-- (єдиний chk_notif_type, 3 значення).
--
-- Аварійна процедура відкату. УВАГА: відновлює name-mismatch-bug + обмежує
-- дозволені типи 3 значеннями (escalation/resolution-сповіщення знову впадуть).
-- Застосовувати лише якщо міграція спричинила регресію, доведену тестом.

ALTER TABLE public.notification_queue DROP CONSTRAINT IF EXISTS chk_notif_type;
ALTER TABLE public.notification_queue DROP CONSTRAINT IF EXISTS chk_notification_type;
ALTER TABLE public.notification_queue
    ADD CONSTRAINT chk_notif_type CHECK (notification_type IN
        ('IncidentCreated','IncidentEscalated','IncidentClosed'));
