-- Міграція: REQ-6 — Control Center Contract Schema
--
-- Контракт живе в Supabase. SCLOC-Verse як власник міграцій створює та
-- еволюціонує цей контракт. Control Center читає лише об'єкти схеми
-- control_center і ніколи не залежить від внутрішньої структури public.*.
--
-- Ця міграція містить ТІЛЬКИ контракт (schema + views). Безпека (роль cc_readonly)
-- винесена в окрему міграцію 20260630000008.

-- 1. Схема контракту
CREATE SCHEMA IF NOT EXISTS control_center;

-- 2. Метадані контракту (View, не таблиця)
CREATE OR REPLACE VIEW control_center.contract_info AS
SELECT
    'SCLOC-Verse' AS product,
    '1.8.0'       AS product_version,
    '1.0.0'       AS contract_version,
    '1.0.0'       AS api_version,
    now()         AS generated_at;

-- 3. Публічні проєкції для Control Center.
--    Це не копії внутрішніх об'єктів, а стабільні read-моделі.
--    Внутрішня реалізація SCLOC-Verse може змінюватись без впливу на контракт.

CREATE OR REPLACE VIEW control_center.users AS
SELECT
    ua.user_id,
    ua.email,
    ua.discord_id,
    ua.username,
    ua.display_name,
    ua.avatar_url,
    ua.user_created_at,
    ua.user_last_sign_in_at,
    ua.user_email_confirmed_at,
    ua.user_is_anonymous,
    ua.user_is_banned,
    ua.install_id,
    ua.country,
    ua.app_version,
    ua.platform,
    ua.machine_id,
    ua.os_version,
    ua.update_channel,
    ua.install_source,
    ua.selected_environment,
    ua.is_install_active,
    ua.install_first_seen,
    ua.install_last_seen,
    ua.install_created_at,
    ua.install_count,
    ua.active_install_count,
    ua.user_countries
FROM public.user_analytics ua;

CREATE OR REPLACE VIEW control_center.installations AS
SELECT
    i.id,
    i.created_at,
    i.install_id,
    i.app_version,
    i.localization_version,
    i.country,
    i.platform,
    i.first_seen,
    i.last_seen,
    i.user_id,
    i.machine_id,
    i.os_version,
    i.os_build,
    i.update_channel,
    i.install_source,
    i.game_folder_path,
    i.selected_environment,
    i.is_active,
    i.updated_at
FROM public.app_installations i;

CREATE OR REPLACE VIEW control_center.statistics AS
SELECT
    (SELECT COUNT(*) FROM public.app_installations) AS total_installations,
    (SELECT COUNT(*) FROM auth.users)               AS total_users;

CREATE OR REPLACE VIEW control_center.errors AS
SELECT
    er.id,
    er.user_id,
    er.install_id,
    er.error_type,
    er.message,
    er.stack_trace,
    er.app_version,
    er.localization_version,
    er.game_folder_path,
    er.selected_environment,
    er.context,
    er.is_resolved,
    er.created_at
FROM public.error_reports er;

CREATE OR REPLACE VIEW control_center.health AS
SELECT
    (SELECT COUNT(*) FROM public.app_installations WHERE last_seen > now() - interval '7 days') AS active_installations_last_7d;

-- 4. Заборонено додавати сюди ролі, grants, функції, тригери або таблиці.
--    Тільки schema + views.
