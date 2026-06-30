-- Міграція: GeoIP country через Cloudflare cf-ipcountry + PostgREST GUC
--
-- Повністю серверне визначення країни користувача.
-- PostgREST передає заголовки запиту через GUC request.headers.
-- Cloudflare додає cf-ipcountry до кожного запиту (allowed header Supabase).
-- Клієнт C# НЕ змінюється. Сторонні GeoIP-сервіси НЕ використовуються.
--
-- Прив'язка: країна встановлюється автоматично при кожному INSERT/UPDATE
-- app_installations через клієнтський PostgREST-запит.

CREATE OR REPLACE FUNCTION public.set_country_from_cf()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    headers_json json;
    cf_country text;
BEGIN
    -- GUC request.headers може бути порожнім рядком після завершення транзакції,
    -- тому обертаємо в nullif (див. документацію PostgREST).
    headers_json := nullif(current_setting('request.headers', true), '')::json;

    IF headers_json IS NULL THEN
        -- Запит не через PostgREST (наприклад, прямий SQL з Dashboard) — не чіпаємо country.
        RETURN NEW;
    END IF;

    cf_country := headers_json->>'cf-ipcountry';

    -- Заповнюємо country лише за наявності валідного коду від Cloudflare.
    -- Нормалізуємо до UPPER (UA), бо Cloudflare інколи повертає в нижньому регістрі.
    IF cf_country IS NOT NULL AND char_length(cf_country) = 2 THEN
        NEW.country := upper(cf_country);
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_app_installations_set_country ON public.app_installations;

CREATE TRIGGER trg_app_installations_set_country
    BEFORE INSERT OR UPDATE ON public.app_installations
    FOR EACH ROW
    EXECUTE FUNCTION public.set_country_from_cf();

-- Функція не призначена для прямого виклику через RPC — прибираємо публічний доступ.
REVOKE EXECUTE ON FUNCTION public.set_country_from_cf() FROM anon, authenticated;
