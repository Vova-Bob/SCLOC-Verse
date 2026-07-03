# Database Verification Checklist

> **Обов'язковий** для будь-якої нової таблиці або RLS-політики.
> Норматив: [`Observability-Constitution.md`, Стаття 17 — Production Database Verification](../observability/Observability-Constitution.md).

**Суть.** Компіляція + SQL-наявність ≠ Done. Інцидент `42501` траплявся двічі (`app_installations`, `telemetry_events`) і обидва рази ховався в GRANT/RLS/RETURNING, а не в коді. Тому верифікація під реальною роллю `authenticated` — окрема обов'язкова стадія. Без закритого чеклиста задача **не вважається завершеною**.

---

## Чеклист (для кожної нової таблиці)

```
□ Migration applied
□ RLS enabled
□ authenticated INSERT
□ authenticated SELECT
□ authenticated UPDATE (якщо потрібно)
□ authenticated DELETE (якщо потрібно)
□ anon denied (INSERT/SELECT/UPDATE/DELETE)
□ чужий SELECT denied (невидимі — count = 0)
□ чужий INSERT denied (RLS WITH CHECK)
□ чужий UPDATE denied
□ RETURNING працює (SDK Prefer: return=representation)
□ Control Center View працює
□ cc_readonly бачить дані
□ Runtime Verified (живий запуск)
```

---

## Як тестувати (доведений патерн)

Тестуємо під роллю `authenticated` з реальним `sub` (із `auth.users`), уводячи винятки через `DO`-блок, щоб кожен BLOCKED-випадок фіксувався без переривання батчу.

```sql
-- 0. Знайти реальний user_id для тесту:
-- select (id)::text from auth.users order by created_at limit 1;

reset role; reset request.jwt.claims;
set role authenticated;
set request.jwt.claims to '{"role":"authenticated","sub":"<USER_UUID>"}';

create temp table rls_results(test text, outcome text);
do $$
declare c int;
begin
  -- власний INSERT (expect OK)
  begin
    insert into public.<table>(...) values (... user_id='<USER_UUID>' ...);
    insert into rls_results values('1_own_insert','OK');
  exception when others then
    insert into rls_results values('1_own_insert','BLOCKED '||sqlstate);
  end;
  -- власний SELECT (expect OK)
  begin
    select count(*) into c from public.<table> where user_id='<USER_UUID>'::uuid;
    insert into rls_results values('2_own_select','OK count='||c);
  exception when others then
    insert into rls_results values('2_own_select','BLOCKED '||sqlstate);
  end;
  -- чужий INSERT (expect BLOCKED 42501)
  begin
    insert into public.<table>(...) values (... user_id='<OTHER_UUID>' ...);
    insert into rls_results values('3_insert_other','LEAK');
  exception when others then
    insert into rls_results values('3_insert_other','BLOCKED '||sqlstate);
  end;
  -- чужі рядки (expect count=0)
  begin
    select count(*) into c from public.<table> where user_id <> '<USER_UUID>'::uuid;
    insert into rls_results values('4_other_rows','count='||c);
  exception when others then
    insert into rls_results values('4_other_rows','BLOCKED '||sqlstate);
  end;
  -- UPDATE (expect BLOCKED, якщо append-only)
  begin
    update public.<table> set ... where user_id='<USER_UUID>'::uuid;
    insert into rls_results values('5_update_own','LEAK');
  exception when others then
    insert into rls_results values('5_update_own','BLOCKED '||sqlstate);
  end;
  -- DELETE (expect BLOCKED, якщо append-only)
  begin
    delete from public.<table> where user_id='<USER_UUID>'::uuid;
    insert into rls_results values('6_delete_own','LEAK');
  exception when others then
    insert into rls_results values('6_delete_own','BLOCKED '||sqlstate);
  end;
end $$;
reset role; reset request.jwt.claims;
select test, outcome from rls_results order by test;

-- anon (expect повний deny):
reset role; set role anon;
-- perform count(*) / insert ... → обидва BLOCKED 42501
reset role;
```

> ⚠️ `RETURNING` вимагає SELECT-привілей. Якщо SDK використовує `Prefer: return=representation`
> (а Supabase C# SDK так робить) — без `GRANT SELECT` інсерт упаде в `42501`.
> Тому для owner-only таблиць грантують `SELECT, INSERT` + owner-scoped RLS
> (`USING/WITH CHECK user_id = auth.uid()`), а UPDATE/DELETE лишають без гранту (append-only).

---

## Референс: `telemetry_events` (Slice 1, доведено 2026-07-03)

Результати live-тесту під `authenticated`:

| Тест | Очікувано | Факт |
|---|---|---|
| own INSERT | OK | ✅ OK |
| own SELECT | OK | ✅ count=2 |
| чужий INSERT | BLOCKED | ✅ BLOCKED 42501 |
| чужі рядки | count=0 | ✅ count=0 |
| UPDATE own | BLOCKED | ✅ BLOCKED 42501 (append-only) |
| DELETE own | BLOCKED | ✅ BLOCKED 42501 (append-only) |
| anon INSERT/SELECT | BLOCKED | ✅ BLOCKED 42501 |
| RETURNING | OK | ✅ (після додавання SELECT) |
| `cc_readonly` → views | OK | ✅ `has_table_privilege = true` |

**Виявлено й виправлено в процесі:** відсутній `GRANT SELECT` → 42501 на `RETURNING`.
Фікс: `GRANT SELECT, INSERT ... TO authenticated` + `auth select own` policy.

---

## Коли чеклист НЕ потрібен

Слайси, що лише **додають події** в уже верифіковану `telemetry_events` (OAuth, Installation, Updater, L.I.A), **не створюють нових таблиць** — для них DB-верифікація N/A. Достатньо: Build + Runtime + Control Center Verification.
