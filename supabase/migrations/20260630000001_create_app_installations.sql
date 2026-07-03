-- Міграція: створення таблиці public.app_installations
--
-- Призначення: єдине джерело метаданих інсталяції SCLOC-Verse.
-- Зв'язок: user_id → auth.users (ON DELETE SET NULL — зберігаємо історію
-- інсталяцій після видалення акаунту, відв'язуючи user_id).
--
-- Стабільні ідентифікатори:
--   install_id — генерується клієнтом, стабільний для фізичної машини
--                (файл + реєстр). UNIQUE глобально.
--   first_seen / created_at — immutable (DEFAULT now()).

CREATE TABLE IF NOT EXISTS public.app_installations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at timestamptz NOT NULL DEFAULT now(),
    install_id text NOT NULL,
    app_version text,
    localization_version text,
    country text,
    platform text,
    first_seen timestamptz DEFAULT now(),
    last_seen timestamptz,
    user_id uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    machine_id text,
    os_version text,
    os_build text,
    update_channel text DEFAULT 'stable',
    install_source text DEFAULT 'unknown',
    game_folder_path text,
    selected_environment text,
    is_active boolean DEFAULT true,
    updated_at timestamptz
);

-- UNIQUE на install_id (стабільний ідентифікатор машини).
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'app_installations_install_id_key'
          AND conrelid = 'public.app_installations'::regclass
    ) THEN
        ALTER TABLE public.app_installations
            ADD CONSTRAINT app_installations_install_id_key UNIQUE (install_id);
    END IF;
END $$;

-- Індекси для типових запитів аналітики.
CREATE INDEX IF NOT EXISTS idx_app_installations_user_id
    ON public.app_installations(user_id);
CREATE INDEX IF NOT EXISTS idx_app_installations_install_id
    ON public.app_installations(install_id);
CREATE INDEX IF NOT EXISTS idx_app_installations_machine_id
    ON public.app_installations(machine_id);
CREATE INDEX IF NOT EXISTS idx_app_installations_last_seen
    ON public.app_installations(last_seen);

-- RLS: deny-all для anon (повне блокування).
ALTER TABLE public.app_installations ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "deny all anon on app_installations" ON public.app_installations;
CREATE POLICY "deny all anon on app_installations"
    ON public.app_installations AS RESTRICTIVE
    FOR ALL TO anon
    USING (false)
    WITH CHECK (false);

-- Owner-only для authenticated (user_id = auth.uid()).
DROP POLICY IF EXISTS "Users can view own app_installations" ON public.app_installations;
DROP POLICY IF EXISTS "Users can insert own app_installations" ON public.app_installations;
DROP POLICY IF EXISTS "Users can update own app_installations" ON public.app_installations;
DROP POLICY IF EXISTS "Users can delete own app_installations" ON public.app_installations;

CREATE POLICY "Users can view own app_installations"
    ON public.app_installations
    FOR SELECT TO authenticated
    USING (user_id = auth.uid());

CREATE POLICY "Users can insert own app_installations"
    ON public.app_installations
    FOR INSERT TO authenticated
    WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users can update own app_installations"
    ON public.app_installations
    FOR UPDATE TO authenticated
    USING (user_id = auth.uid())
    WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users can delete own app_installations"
    ON public.app_installations
    FOR DELETE TO authenticated
    USING (user_id = auth.uid());

-- Гранти.
GRANT SELECT, INSERT, UPDATE, DELETE ON public.app_installations TO authenticated;
GRANT SELECT ON public.app_installations TO anon;
