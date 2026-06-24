-- Міграція: видалення зайвого unique constraint з machine_id та додавання upsert-захисту
--
-- Контекст:
--   Попередня схема мала UNIQUE constraint на app_installations.machine_id,
--   що забороняло одній машині мати кілька інсталяцій або перевстановлювати
--   додаток. Єдиним бізнес-ключем має бути (user_id, install_id).

-- Видаляємо зайвий unique constraint, якщо він існує.
ALTER TABLE public.app_installations
    DROP CONSTRAINT IF EXISTS app_installations_machine_id_key;

-- Гарантуємо, що правильний unique constraint на (user_id, install_id) існує.
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
