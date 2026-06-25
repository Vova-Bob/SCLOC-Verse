-- Міграція: GeoIP country через Cloudflare cf-ipcountry + PostgREST GUC
--
-- Варіант G (Engineering Decision Review):
--   Повністю серверне визначення країни користувача.
--   PostgREST передає заголовки запиту через GUC request.headers (офіційно documents
--   PostgREST + практично підтверджено в Supabase managed).
--   Cloudflare додає cf-ipcountry до кожного запиту (офіційно documents allowed header Supabase).
--
--   Клієнт C# НЕ змінюється. Сторонні GeoIP-сервіси НЕ використовуються.
--   Edge Functions НЕ використовуються. Logs API НЕ читається.
--
--   Прив'язка: країна встановлюється автоматично при кожному INSERT/UPDATE app_installations
--   через клієнтський PostgREST-запит (який несе cf-ipcountry від Cloudflare).

-- Функція, що витягує код країни з Cloudflare заголовка у PostgREST GUC.
-- SECURITY DEFINER + фіксований search_path — стабільність виконання.
create or replace function public.set_country_from_cf()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  headers_json json;
  cf_country   text;
begin
  -- GUC request.headers може бути порожнім рядком після завершення транзакції,
  -- тому обертаємо в nullif (див. документацію PostgREST).
  headers_json := nullif(current_setting('request.headers', true), '')::json;

  if headers_json is null then
    -- Запит не через PostgREST (наприклад, прямий SQL з Dashboard) — не чіпаємо country.
    return new;
  end if;

  cf_country := headers_json->>'cf-ipcountry';

  -- Заповнюємо country лише за наявності валідного коду від Cloudflare.
  -- Нормалізуємо до UPPER (UA), бо Cloudflare інколи повертає в нижньому регістрі.
  if cf_country is not null and char_length(cf_country) between 2 and 2 then
    new.country := upper(cf_country);
  end if;

  return new;
end;
$$;

-- Тригер BEFORE INSERT OR UPDATE на app_installations.
-- Спрацьовує автоматично при кожному клієнтському PostgREST-запиті.
drop trigger if exists trg_app_installations_set_country on public.app_installations;

create trigger trg_app_installations_set_country
  before insert or update on public.app_installations
  for each row
  execute function public.set_country_from_cf();

-- Функція не призначена для прямого виклику через RPC — прибираємо публічний доступ.
revoke execute on function public.set_country_from_cf() from anon, authenticated;
