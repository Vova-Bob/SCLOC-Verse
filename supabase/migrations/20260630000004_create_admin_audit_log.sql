-- Міграція: аудит адміністративних дій
--
-- Призначення: незмінний журнал адмін-операцій (майбутня адмін-панель).
-- admin_discord_id — ідентифікатор адміна (не user_id), щоб зберегти запис
-- навіть після видалення акаунту адміна.
-- target_user_id / target_install_id — об'єкт дії (ON DELETE SET NULL — зберігаємо
-- аудит навіть після видалення цілі).
--
-- RLS: deny-all (доступ лише service_role через адмін-панель).

CREATE TABLE IF NOT EXISTS public.admin_audit_log (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    admin_discord_id text NOT NULL,
    action text NOT NULL,
    target_user_id uuid REFERENCES auth.users(id) ON DELETE SET NULL,
    target_install_id text REFERENCES public.app_installations(install_id) ON DELETE SET NULL,
    details jsonb DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_admin_audit_admin_discord_id ON public.admin_audit_log(admin_discord_id);
CREATE INDEX IF NOT EXISTS idx_admin_audit_target_user_id ON public.admin_audit_log(target_user_id);
CREATE INDEX IF NOT EXISTS idx_admin_audit_target_install_id ON public.admin_audit_log(target_install_id);
CREATE INDEX IF NOT EXISTS idx_admin_audit_created_at ON public.admin_audit_log(created_at);

ALTER TABLE public.admin_audit_log ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "deny all anon on admin_audit_log" ON public.admin_audit_log;
DROP POLICY IF EXISTS "deny all authenticated on admin_audit_log" ON public.admin_audit_log;

CREATE POLICY "deny all anon on admin_audit_log"
    ON public.admin_audit_log AS RESTRICTIVE
    FOR ALL TO anon
    USING (false)
    WITH CHECK (false);

CREATE POLICY "deny all authenticated on admin_audit_log"
    ON public.admin_audit_log AS RESTRICTIVE
    FOR ALL TO authenticated
    USING (false)
    WITH CHECK (false);
