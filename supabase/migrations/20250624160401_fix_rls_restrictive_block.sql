-- Міграція: видалення restrictive політики, яка блокує authenticated користувачів
--
-- Контекст:
--   Попередня міграція додала permissive RLS політики для authenticated.
--   Однак існуюча restrictive політика "deny all authenticated on app_installations"
--   з qual = false перекриває permissive політики та забороняє всі операції.
--
--   Ця міграція видаляє restrictive політику для authenticated, залишаючи
--   restrictive політику для anon (яка правильно забороняє все anon).

DROP POLICY IF EXISTS "deny all authenticated on app_installations" ON public.app_installations;
