-- Міграція: звіти про помилки SCLOC-Verse
--
-- Призначення: збір помилок/stack-trace з клієнтів для діагностики.
-- user_id nullable — на випадок помилки до авторизації.
-- install_id nullable — на випадок, коли install ще не зареєстрований.
-- game_folder_path та selected_environment зберігаються для діагностики.
--
-- RLS: deny-all для anon та authenticated (доступ лише service_role).
-- Продюсер (клієнтський "Report bug" або Edge Function) відсутній до появи потреби.
-- Коли з'явиться клієнтський direct-INSERT — окрема міграція додасть
-- owner-only INSERT policy: WITH CHECK (user_id = auth.uid()).

CREATE TABLE IF NOT EXISTS public.error_reports (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    install_id text REFERENCES public.app_installations(install_id) ON DELETE SET NULL,
    error_type text NOT NULL,
    message text,
    stack_trace text,
    app_version text,
    localization_version text,
    game_folder_path text,
    selected_environment text,
    context jsonb,
    is_resolved boolean DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_error_user_id ON public.error_reports(user_id);
CREATE INDEX IF NOT EXISTS idx_error_install_id ON public.error_reports(install_id);
CREATE INDEX IF NOT EXISTS idx_error_type ON public.error_reports(error_type);
CREATE INDEX IF NOT EXISTS idx_error_created_at ON public.error_reports(created_at);
CREATE INDEX IF NOT EXISTS idx_error_resolved ON public.error_reports(is_resolved);

ALTER TABLE public.error_reports ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "deny all anon on error_reports" ON public.error_reports;
DROP POLICY IF EXISTS "deny all authenticated on error_reports" ON public.error_reports;

CREATE POLICY "deny all anon on error_reports"
    ON public.error_reports AS RESTRICTIVE
    FOR ALL TO anon
    USING (false)
    WITH CHECK (false);

CREATE POLICY "deny all authenticated on error_reports"
    ON public.error_reports AS RESTRICTIVE
    FOR ALL TO authenticated
    USING (false)
    WITH CHECK (false);
