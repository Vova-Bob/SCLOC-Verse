-- Міграція: адміністративне аналітичне VIEW public.user_analytics
--
-- Призначення: лише аналітичне представлення даних. Не таблиця, не зберігає
-- власних даних, не дублює інформацію. Єдине джерело географічних даних —
-- public.app_installations.
--
-- Дизайн:
--   * 1 рядок = 1 інсталяція (плоский JOIN auth.users ↔ app_installations).
--   * Window functions дають агрегати кількості по користувачу без втрати деталізації.
--   * user_countries (унікальні країни користувача) обчислюється через CTE,
--     бо PostgreSQL не підтримує DISTINCT у window functions.
--
-- Безпека:
--   * security_invoker = false (default) → VIEW виконується з правами власника (postgres),
--     що дає читати auth.users.
--   * REVOKE від anon/authenticated → доступ лише через service_role (адмін через Dashboard).

CREATE OR REPLACE VIEW public.user_analytics AS
WITH user_country_agg AS (
    -- Унікальні країни користувача (усіх його інсталяцій).
    SELECT
        ai2.user_id,
        string_agg(DISTINCT ai2.country, ', ') FILTER (WHERE ai2.country IS NOT NULL) AS user_countries
    FROM public.app_installations ai2
    GROUP BY ai2.user_id
)
SELECT
    -- Ідентифікація користувача
    u.id                                             AS user_id,
    u.email,
    u.raw_user_meta_data ->> 'provider_id'           AS discord_id,
    u.raw_user_meta_data ->> 'full_name'             AS username,
    COALESCE(
        u.raw_user_meta_data #>> '{custom_claims,global_name}',
        u.raw_user_meta_data ->> 'full_name',
        u.raw_user_meta_data ->> 'name'
    )                                                AS display_name,
    u.raw_user_meta_data ->> 'avatar_url'            AS avatar_url,

    -- Профіль користувача (із auth.users)
    u.created_at                                     AS user_created_at,
    u.last_sign_in_at                                AS user_last_sign_in_at,
    u.email_confirmed_at                             AS user_email_confirmed_at,
    u.is_anonymous                                   AS user_is_anonymous,
    (u.banned_until IS NOT NULL AND u.banned_until > now()) AS user_is_banned,

    -- Дані інсталяції (із public.app_installations — єдине джерело гео)
    ai.install_id,
    ai.country,
    ai.app_version,
    ai.platform,
    ai.machine_id,
    ai.os_version,
    ai.update_channel,
    ai.install_source,
    ai.selected_environment,
    ai.is_active                                     AS is_install_active,
    ai.first_seen                                    AS install_first_seen,
    ai.last_seen                                     AS install_last_seen,
    ai.created_at                                    AS install_created_at,

    -- Агрегати по користувачу (window functions — кількість інсталяцій)
    COUNT(*) OVER (PARTITION BY u.id)                AS install_count,
    COUNT(*) FILTER (WHERE ai.is_active) OVER (PARTITION BY u.id) AS active_install_count,

    -- Унікальні країни користувача (з CTE, бо DISTINCT у window не підтримується)
    uca.user_countries
FROM auth.users u
LEFT JOIN public.app_installations ai ON ai.user_id = u.id
LEFT JOIN user_country_agg uca ON uca.user_id = u.id
WHERE u.deleted_at IS NULL;

-- Доступ: лише адміністратор (service_role / postgres через Dashboard).
-- Клієнтські ролі (anon, authenticated) не мають доступу до аналітики з PII.
REVOKE ALL ON public.user_analytics FROM anon, authenticated;
