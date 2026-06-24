-- Міграція: відкрити authenticated-доступ (owner-only) до user_discord_guilds
-- для синхронізації Discord guilds клієнтом SCLOC-Verse.
--
-- Контекст:
--   Таблиця public.user_discord_guilds уже існує (створена поза трековими
--   міграціями) з UNIQUE(user_id, discord_guild_id), PK(id),
--   FK user_id -> auth.users(id) ON DELETE CASCADE.
--   Зараз дві RESTRICTIVE політики з qual=false блокують усе для anon і authenticated.
--   Анонім залишається забороненим; для authenticated додаємо owner-only політики
--   (як вже зроблено для app_installations).

-- 1. Прибрати рестриктивну заборону для authenticated (anon-deny залишаємо).
DROP POLICY IF EXISTS "deny all authenticated on user_discord_guilds" ON public.user_discord_guilds;

-- 2. Permissive політики: користувач керує лише власними рядками.
DROP POLICY IF EXISTS "Users can view own user_discord_guilds" ON public.user_discord_guilds;
DROP POLICY IF EXISTS "Users can insert own user_discord_guilds" ON public.user_discord_guilds;
DROP POLICY IF EXISTS "Users can update own user_discord_guilds" ON public.user_discord_guilds;
DROP POLICY IF EXISTS "Users can delete own user_discord_guilds" ON public.user_discord_guilds;

CREATE POLICY "Users can view own user_discord_guilds"
    ON public.user_discord_guilds
    FOR SELECT
    TO authenticated
    USING (user_id = auth.uid());

CREATE POLICY "Users can insert own user_discord_guilds"
    ON public.user_discord_guilds
    FOR INSERT
    TO authenticated
    WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users can update own user_discord_guilds"
    ON public.user_discord_guilds
    FOR UPDATE
    TO authenticated
    USING (user_id = auth.uid())
    WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users can delete own user_discord_guilds"
    ON public.user_discord_guilds
    FOR DELETE
    TO authenticated
    USING (user_id = auth.uid());

-- 3. Гранти для authenticated (письмо/читання власних рядків).
GRANT SELECT, INSERT, UPDATE, DELETE ON public.user_discord_guilds TO authenticated;
