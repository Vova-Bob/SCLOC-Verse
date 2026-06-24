-- Міграція: виправлення RLS та unique constraint для public.app_installations
--
-- Контекст:
--   Таблиця public.app_installations вже існує та виконує роль Installation Entity
--   у моделі User → Installations для SCLOC-Verse Discord OAuth.
--
--   Поточні політики забороняють всі операції для authenticated. Ця міграція
--   додає permissive політики, які дозволяють користувачеві бачити та керувати
--   лише власними інсталяціями.

-- Unique constraint для прив'язки одного користувача до багатьох інсталяцій
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'app_installations_user_install_unique'
          AND conrelid = 'public.app_installations'::regclass
    ) THEN
        ALTER TABLE public.app_installations
            ADD CONSTRAINT app_installations_user_install_unique
            UNIQUE (user_id, install_id);
    END IF;
END $$;

-- Видалення дублюючої міграції, якщо вона була створена раніше
DROP TABLE IF EXISTS public.installations;

-- Видаляємо старі permissive політики, якщо вже існують, щоб уникнути конфліктів
DROP POLICY IF EXISTS "Users can view own app_installations" ON public.app_installations;
DROP POLICY IF EXISTS "Users can insert own app_installations" ON public.app_installations;
DROP POLICY IF EXISTS "Users can update own app_installations" ON public.app_installations;
DROP POLICY IF EXISTS "Users can delete own app_installations" ON public.app_installations;

-- Permissive RLS політики для authenticated користувачів
CREATE POLICY "Users can view own app_installations"
    ON public.app_installations
    FOR SELECT
    TO authenticated
    USING (user_id = auth.uid());

CREATE POLICY "Users can insert own app_installations"
    ON public.app_installations
    FOR INSERT
    TO authenticated
    WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users can update own app_installations"
    ON public.app_installations
    FOR UPDATE
    TO authenticated
    USING (user_id = auth.uid())
    WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users can delete own app_installations"
    ON public.app_installations
    FOR DELETE
    TO authenticated
    USING (user_id = auth.uid());

-- Права доступу
GRANT SELECT, INSERT, UPDATE, DELETE ON public.app_installations TO authenticated;
GRANT SELECT ON public.app_installations TO anon;
