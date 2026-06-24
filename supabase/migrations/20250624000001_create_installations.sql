-- Міграція: створення таблиці installations для зв'язку User → Installations

CREATE TABLE IF NOT EXISTS public.installations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    install_id text NOT NULL,
    device_name text,
    last_seen_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(user_id, install_id)
);

-- Увімкнення RLS
ALTER TABLE public.installations ENABLE ROW LEVEL SECURITY;

-- Політики: користувач бачить та керує лише власними інсталяціями
CREATE POLICY "Users can view own installations"
    ON public.installations
    FOR SELECT
    TO authenticated
    USING (user_id = auth.uid());

CREATE POLICY "Users can insert own installations"
    ON public.installations
    FOR INSERT
    TO authenticated
    WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users can update own installations"
    ON public.installations
    FOR UPDATE
    TO authenticated
    USING (user_id = auth.uid())
    WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users can delete own installations"
    ON public.installations
    FOR DELETE
    TO authenticated
    USING (user_id = auth.uid());

-- Права доступу
GRANT SELECT, INSERT, UPDATE, DELETE ON public.installations TO authenticated;
GRANT SELECT ON public.installations TO anon;
