-- Налаштування Supabase для SCLOC-Verse Discord OAuth

-- 1. Увімкнути Discord provider у Supabase Dashboard:
--    Auth → Providers → Discord
--    Вказати Client ID та Client Secret з Discord Application.

-- 2. URL Configuration:
--    Site URL: https://sclocverse.app
--    Additional Redirect URLs: http://localhost:*/auth/callback

-- 3. JWT настройки залишити за замовчуванням (1 година).

-- 4. Застосувати міграцію:
--    supabase migrations up
--    або виконати SQL з файлу migrations/20250624000001_create_installations.sql
