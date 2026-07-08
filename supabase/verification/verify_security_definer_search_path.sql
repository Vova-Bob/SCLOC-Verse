-- Verification: Phase 2.2 C2 — SECURITY DEFINER search_path hardening
-- Перевіряє, що всі public SECURITY DEFINER функції мають search_path=public, pg_catalog.
-- Запускати після міграції 20260708235000_security_definer_search_path.sql.

-- 1. Список усіх public SECURITY DEFINER функцій з їхнім search_path.
-- Очікуваний результат: всі рядки мають proconfig = ['search_path=public, pg_catalog'].
SELECT
    proname AS function_name,
    pg_get_function_identity_arguments(p.oid) AS signature,
    prosecdef AS is_security_definer,
    proconfig
FROM pg_proc p
JOIN pg_namespace n ON p.pronamespace = n.oid
WHERE n.nspname = 'public'
  AND prosecdef = true
ORDER BY proname;

-- 2. Перевірка на відсутність незахищених функцій.
-- Очікуваний результат: 0 рядків.
SELECT
    proname AS function_name,
    pg_get_function_identity_arguments(p.oid) AS signature
FROM pg_proc p
JOIN pg_namespace n ON p.pronamespace = n.oid
WHERE n.nspname = 'public'
  AND prosecdef = true
  AND (proconfig IS NULL OR NOT (proconfig && ARRAY['search_path=public, pg_catalog']::text[]));
