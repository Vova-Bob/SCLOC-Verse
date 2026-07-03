-- Міграція: Discord-спільноти користувача
--
-- Призначення: заділ під Community Center (ролі за гільдіями Discord).
-- Продюсер (синхронізація гільдій) відключено у клієнті (identify scope) —
-- таблиця порожня, але схема + RLS готові до майбутнього використання.

CREATE TABLE IF NOT EXISTS public.user_discord_guilds (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    discord_guild_id text NOT NULL,
    guild_name text,
    synced_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (user_id, discord_guild_id)
);

CREATE INDEX IF NOT EXISTS idx_user_discord_guilds_user_id ON public.user_discord_guilds(user_id);
CREATE INDEX IF NOT EXISTS idx_user_discord_guilds_guild_id ON public.user_discord_guilds(discord_guild_id);

ALTER TABLE public.user_discord_guilds ENABLE ROW LEVEL SECURITY;

-- RLS: deny-all для anon.
DROP POLICY IF EXISTS "deny all anon on user_discord_guilds" ON public.user_discord_guilds;
CREATE POLICY "deny all anon on user_discord_guilds"
    ON public.user_discord_guilds AS RESTRICTIVE
    FOR ALL TO anon
    USING (false)
    WITH CHECK (false);

-- Owner-only для authenticated (user_id = auth.uid()).
DROP POLICY IF EXISTS "Users can view own user_discord_guilds" ON public.user_discord_guilds;
DROP POLICY IF EXISTS "Users can insert own user_discord_guilds" ON public.user_discord_guilds;
DROP POLICY IF EXISTS "Users can update own user_discord_guilds" ON public.user_discord_guilds;
DROP POLICY IF EXISTS "Users can delete own user_discord_guilds" ON public.user_discord_guilds;

CREATE POLICY "Users can view own user_discord_guilds"
    ON public.user_discord_guilds
    FOR SELECT TO authenticated
    USING (user_id = auth.uid());

CREATE POLICY "Users can insert own user_discord_guilds"
    ON public.user_discord_guilds
    FOR INSERT TO authenticated
    WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users can update own user_discord_guilds"
    ON public.user_discord_guilds
    FOR UPDATE TO authenticated
    USING (user_id = auth.uid())
    WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users can delete own user_discord_guilds"
    ON public.user_discord_guilds
    FOR DELETE TO authenticated
    USING (user_id = auth.uid());

GRANT SELECT, INSERT, UPDATE, DELETE ON public.user_discord_guilds TO authenticated;
