-- Міграція: виділена read-only роль для Control Center
--
-- Створює виділену роль cc_readonly, яка використовується Control Center
-- для читання офіційного контракту control_center.
--
-- Ця роль не має жодних прав поза схемою control_center.
-- Пароль задається окремо через Supabase Dashboard / secure channel
-- і ніколи не зберігається в SCLOC-Verse репозиторії.

-- 1. Створення ролі (idempotent)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cc_readonly') THEN
        CREATE ROLE cc_readonly WITH LOGIN NOCREATEDB NOCREATEROLE NOSUPERUSER;
    END IF;
END $$;

-- 2. Мінімальні права на схему та об'єкти control_center
GRANT USAGE ON SCHEMA control_center TO cc_readonly;
GRANT SELECT ON ALL TABLES IN SCHEMA control_center TO cc_readonly;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA control_center TO cc_readonly;

-- 3. Права за замовчуванням для майбутніх об'єктів схеми
ALTER DEFAULT PRIVILEGES IN SCHEMA control_center
    GRANT SELECT ON TABLES TO cc_readonly;
ALTER DEFAULT PRIVILEGES IN SCHEMA control_center
    GRANT SELECT ON SEQUENCES TO cc_readonly;

-- 4. Заборонено будь-які права поза control_center (за замовчуванням їх немає,
--    але ця міграція явно не надає їх).
--    Control Center отримує доступ лише до офіційного read-контракту.
