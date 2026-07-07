-- Rollback для міграції 20260707030000_phase3a_drop_dup_indexes.sql
--
-- Аварійний відкат: повертає 2 індекси-дублікати, що були DROP у міграції.
--
-- Колонки таблиць не зачіпаються. UNIQUE constraints (app_installations_install_id_key,
-- user_discord_guilds_user_id_discord_guild_id_key) не зачіпаються міграцією і не
-- зачіпаються rollback. ADDITIVE (повертає попередній стан схеми).
--
-- Застосовується лише у разі виявлення критичної проблеми після міграції.

-- Повернення дубль-індексу на app_installations.install_id
CREATE INDEX IF NOT EXISTS idx_app_installations_install_id
    ON public.app_installations(install_id);

-- Повернення дубль-індексу на user_discord_guilds.user_id
CREATE INDEX IF NOT EXISTS idx_user_discord_guilds_user_id
    ON public.user_discord_guilds(user_id);
