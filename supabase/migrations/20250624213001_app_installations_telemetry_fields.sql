-- Міграція: додавання телеметричних полів до public.app_installations
--
-- Контекст:
--   Додаємо updated_at та last_session_at для відстеження активності інсталяції.
--   first_seen отримує DEFAULT now(), щоб бути immutable при upsert.

ALTER TABLE public.app_installations
    ADD COLUMN IF NOT EXISTS updated_at timestamptz,
    ADD COLUMN IF NOT EXISTS last_session_at timestamptz,
    ALTER COLUMN first_seen SET DEFAULT now();

-- Даємо права на нові стовпці через грант на таблицю (ідемпотентно)
GRANT SELECT, INSERT, UPDATE, DELETE ON public.app_installations TO authenticated;
GRANT SELECT ON public.app_installations TO anon;
