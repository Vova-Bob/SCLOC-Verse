-- Міграція: створення таблиці public.sessions для мінімальної телеметрії
--
-- Контекст:
--   1 запуск додатка = 1 сесія.
--   Дозволяє рахувати DAU, MAU, кількість запусків, середню тривалість.

CREATE TABLE IF NOT EXISTS public.sessions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid REFERENCES auth.users(id) ON DELETE CASCADE,
    install_id text NOT NULL REFERENCES public.app_installations(install_id) ON DELETE CASCADE,
    started_at timestamptz NOT NULL DEFAULT now(),
    ended_at timestamptz,
    app_version text,
    os_version text
);

-- RLS: користувач бачить лише власні сесії
ALTER TABLE public.sessions ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Users own sessions" ON public.sessions;
CREATE POLICY "Users own sessions"
    ON public.sessions
    FOR ALL
    TO authenticated
    USING (user_id = auth.uid())
    WITH CHECK (user_id = auth.uid());

-- Індекси для типових запитів телеметрії
CREATE INDEX IF NOT EXISTS idx_sessions_user_id_started_at
    ON public.sessions(user_id, started_at DESC);

CREATE INDEX IF NOT EXISTS idx_sessions_install_id_started_at
    ON public.sessions(install_id, started_at DESC);

-- Права доступу
GRANT SELECT, INSERT, UPDATE ON public.sessions TO authenticated;
GRANT SELECT ON public.sessions TO anon;
