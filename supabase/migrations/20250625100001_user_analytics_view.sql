-- Міграція: адміністративне аналітичне VIEW public.user_analytics
--
-- Призначення: лише аналітичне представлення даних. Не таблиця, не зберігає власних
-- даних, не дублює інформацію. Єдине джерело географічних даних — public.app_installations.
--
-- Дизайн (Engineering Decision):
--   * 1 рядок = 1 інсталяція (плоский JOIN auth.users ↔ app_installations).
--   * Window functions дають агрегати кількості по користувачу без втрати деталізації.
--   * user_countries (унікальні країни користувача) обчислюється через CTE,
--     бо PostgreSQL не підтримує DISTINCT у window functions.
--   * public.profiles не приєднується — порожня, не несе даних (заділ на майбутнє).
--
-- Безпека:
--   * security_invoker = false (default) → VIEW виконується з правами власника (postgres),
--     що дає читати auth.users.
--   * REVOKE від anon/authenticated → доступ лише через service_role (адмін через Dashboard).
--   * PII (email, discord_id) не доступний клієнтським ролям.
--
-- Нуль регресій: VIEW не модифікує існуючі таблиці, тригери, клієнтський код.

create or replace view public.user_analytics as
with user_country_agg as (
  -- Унікальні країни користувача (усіх його інсталяцій).
  select
    ai2.user_id,
    string_agg(distinct ai2.country, ', ') filter (where ai2.country is not null) as user_countries
  from public.app_installations ai2
  group by ai2.user_id
)
select
  -- Ідентифікація користувача
  u.id                                             as user_id,
  u.email,
  u.raw_user_meta_data ->> 'provider_id'           as discord_id,
  u.raw_user_meta_data ->> 'full_name'             as username,
  coalesce(
    u.raw_user_meta_data #>> '{custom_claims,global_name}',
    u.raw_user_meta_data ->> 'full_name',
    u.raw_user_meta_data ->> 'name'
  )                                                as display_name,
  u.raw_user_meta_data ->> 'avatar_url'            as avatar_url,

  -- Профіль користувача (із auth.users)
  u.created_at                                     as user_created_at,
  u.last_sign_in_at                                as user_last_sign_in_at,
  u.email_confirmed_at                             as user_email_confirmed_at,
  u.is_anonymous                                   as user_is_anonymous,
  (u.banned_until is not null and u.banned_until > now()) as user_is_banned,

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
  ai.is_active                                     as is_install_active,
  ai.first_seen                                    as install_first_seen,
  ai.last_seen                                     as install_last_seen,
  ai.last_session_at                               as install_last_session_at,
  ai.created_at                                    as install_created_at,

  -- Агрегати по користувачу (window functions — кількість інсталяцій)
  count(*) over (partition by u.id)                as install_count,
  count(*) filter (where ai.is_active) over (partition by u.id) as active_install_count,

  -- Унікальні країни користувача (з CTE, бо DISTINCT у window не підтримується)
  uca.user_countries
from auth.users u
left join public.app_installations ai on ai.user_id = u.id
left join user_country_agg uca on uca.user_id = u.id
where u.deleted_at is null;

-- Доступ: лише адміністратор (service_role / postgres через Dashboard).
-- Клієнтські ролі (anon, authenticated) не мають доступу до аналітики з PII.
revoke all on public.user_analytics from anon, authenticated;
