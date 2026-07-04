# Knowledge Engine Design — Architecture Review

> **Форензик-аудит** специфікації `Knowledge-Engine-Design.md` (v1.0)
> до початку будь-якої реалізації Slice 1.
>
> **Мета:** виявити архітектурні проблеми до написання коду/міграцій.
>
> **Метод:** перевірка 14 аспектів проти Конституції (28 статей), RC1-фундаменту,
> принципів KISS/DRY/Zero Regression/Append-Only/Additive.

---

## Зведена таблиця вердиктів

| # | Аспект | Вердикт |
|---|---|---|
| 1 | Відповідність 28 статтям Конституції | ⚠️ 1 конфлікт (Стаття 20 + модель ролей) |
| 2 | Ізоляція від RC1 | ✅ Повна |
| 3 | Модель БД (3 таблиці) | ✅ Достатня, без зайвого |
| 4 | Status vs Confidence ортогональність | ❌ Логічно нестабільна |
| 5 | Matching Policy | ⚠️ Priority 3 підозрілий |
| 6 | Reference vs Evidence | ✅ Reference достатньо |
| 7 | Циклічна залежність Knowledge↔Incident | ✅ Відсутня |
| 8 | Авто-Verification через release_health | ⚠️ Формула неконкретна |
| 9 | Масштабування 100/1K/10K/100K | ⚠️ Пошук без FTS — ризик на 10K+ |
| 10 | Free Tier обсяг/JOIN/вузькі місця | ⚠️ coverage VIEW — ризик |
| 11 | Безпека (RLS, DEFINER, ролі) | ❌ changed_by ненадійний |
| 12 | Принципи (KISS/DRY/ZR/AO/AS) | ⚠️ "Append-Only" двозначний |
| 13 | Конкретні рішення — нижче | — |
| 14 | **Фінальний висновок** | **Design Ready після 6 обов'язкових правок** |

---

## 1. Відповідність Конституції (28 статей)

### ✅ Узгоджені статті

| Стаття | Реалізація в дизайні |
|---|---|
| 1 (Isolation) | §2 принцип 1, §1 "що НЕ робить" |
| 4 (Zero PII) | §2 принцип 7 — санітизація при введенні |
| 6 (Idempotent Retries) | (не застосовно напряму; audit history забезпечує trace) |
| 9 (Trace Recovery) | через fingerprint → incident → root_event_id → trace |
| 12 (Single Ingestion) | Knowledge Engine не обходить телеметрію |
| 13 (Additive-Only) | §2 принцип — нові таблиці, не зміна існуючих |
| 17 (DB Verification) | чеклист треба адаптувати під cc_knowledge_editor |
| 18 (Production Pending) | auto-verify отримає цей статус |
| 21 (Incident Identity) | §2 принцип 4 — матчинг через fingerprint_key |
| 22 (Immutable History) | knowledge_version_history append-only |
| 28 (Knowledge Preservation) | реалізується повністю |

### ❌ Конфлікт: Стаття 20 + модель ролей

**Стаття 20:** «Control Center (Dashboard) только ЧИТАЄ готові VIEW… `cc_readonly` має лише `SELECT` на об'єктах схеми `control_center`. Немає `INSERT/UPDATE/DELETE` ні на що.»

**Стаття 23** зробила виняток для Workflow: `cc_readonly` отримав `EXECUTE` на три SECURITY DEFINER функції. Це встановило патерн: dashboard пише через функції, а не напряму в таблиці.

**У дизайні Knowledge Engine:**
- §9: «нова роль `cc_knowledge_editor` (SELECT/INSERT/UPDATE)»
- §10: «доступні `cc_knowledge_editor`»
- §8.4: Knowledge Management сторінка інтегрована в Control Center Blazor

**Проблема:** Control Center Blazor має ОДНЕ підключення `cc_readonly`. Як оператор пише Knowledge Entry з UI? Варіанти:
- **A.** Друге підключення `cc_knowledge_editor` у Blazor (два NpgsqlDataSource) → ускладнює DI.
- **B.** Розширити `cc_readonly` EXECUTE на Knowledge функції (як Стаття 23) → порушує простоту cc_readonly, але узгоджується з патерном.
- **C.** Knowledge Management як окремий застосунок з власним підключенням → порушує єдність Control Center.

**Рекомендація: B.** Патерн Статті 23 — правильний прецедент. Нова роль `cc_knowledge_editor` непотрібна; `cc_readonly` отримує EXECUTE на Knowledge-функції. Усі writes через SECURITY DEFINER (як workflow). Це узгоджується з Статтею 20 (cc_readonly не пише в таблиці напряму — лише через функції).

---

## 2. Ізоляція від RC1

✅ **Повна ізоляція.** Таблиця в дизайні §3 підтверджує: Knowledge Engine не модифікує жодної таблиці RC1. Усі нові таблиці в `public` з префіксом `knowledge_*`, нові VIEWs в `control_center`. Фундамент RC1 недоторканий.

---

## 3. Модель БД (3 таблиці)

### ✅ Достатньо

- `knowledge_entries` — основна
- `knowledge_references` — 1:N посилання
- `knowledge_version_history` — append-only audit

### Перевірка на зайве

Жодної зайвої таблиці. `knowledge_references` окрема від `knowledge_entries` — правильно (1:N, без jsonb-масиву в entries → можливість індексувати й фільтрувати посилання).

### Перевірка версіонування

`knowledge_version_history` через тригер AFTER INSERT/UPDATE — стандартний патерн. Snapshot у jsonb забезпечує повне відтворення стану на будь-який момент.

**⚠️ Прихована проблема:** якщо `knowledge_entries` DELETE дозволено (за дизайном Archive ≠ Delete), то при випадковому DELETE record з history залишиться без батька → треба FOREIGN KEY ... ON DELETE CASCADE або заборона DELETE через RLS.

**Рекомендація:** RLS забороняє DELETE для всіх ролей (Archive — це Status, не фізичне видалення). Тоді ON DELETE не потрібен. Зафіксувати в дизайні явно.

---

## 4. Status vs Confidence — ❌ Логічно нестабільно

### Опис проблеми

| Status | Означає |
|---|---|
| Draft / Reviewed / Verified / Deprecated / Archived | workflow публікації запису |

| Confidence | Означає |
|---|---|
| Low / Medium / High / Verified | довіра до змісту |

Дизайн §5 вимагає для переходу Reviewed → Verified: **«Confidence ≥ High»**.

Але після Verify оператор може вручну змінити Confidence з High на Medium (або навіть Low). Тоді виникає:

> **Status = Verified** + **Confidence = Medium**

Це логічно непослідовно: статус каже «перевірено», довіра каже «неповністю».

### Інша нестабільна комбінація

Reopen Verified → Reviewed (дизайн §5). Але Confidence при цьому не змінюється автоматично. Отримуємо: **Status = Reviewed + Confidence = Verified**. Теж дивно.

### Варіанти рішення

**Варіант 1: жорсткий інваріант.**
CHECK-обмеження на рівні БД:
- Status = 'Verified' → Confidence IN ('High','Verified')
- Status IN ('Draft','Reviewed') → Confidence IN ('Low','Medium','High')
- Status IN ('Deprecated','Archived') → будь-яка

**Варіант 2: об'єднати в одну шкалу.**
Відмовитись від розділення. Стани: Draft → Reviewed → High → Verified → Deprecated → Archived. Мінус: втрачається ортогональність (можливість мати Active Low-confidence — молода гіпотеза).

**Варіант 3: зробити Confidence похідним від Status.**
Verified Status автоматично дає Confidence=Verified. Дублювання прибирається.

**Рекомендація: Варіант 1** (жорсткий інваріант). Зберігає гнучкість (Draft з Low, Reviewed з Medium), але усуває неможливі комбінації. Реалізується CHECK-обмеженнями на БД — гарантується на рівні схеми (як Стаття 5/25).

---

## 5. Matching Policy — ⚠️ Priority 3 підозрілий

### Priority 1 (exact fingerprint) — ✅ коректний
Індекс `fingerprint_hash`, O(log n), гарантована відповідність.

### Priority 2 (component + signal) — ✅ коректний
Індекс `(component, signal) WHERE status='Verified'`. Розумне relaxation.

### Priority 3 (component + ILIKE KnownCause на exception_type) — ❌ НЕкоректний

**Проблема 1: джерело `exception_type`.**
В інцидентах `signal` = `COALESCE(hresult, supabase_code, http_status, exception_type)`. Тобто exception_type вже частина signal у Priority 1/2. Якщо signal=hresult (напр., `0x800B0109`), окремого `exception_type` в інцидентах немає. Шукати `exception_type` окремо — немає з чого.

**Проблема 2: ILIKE по повному тексту KnownCause.**
Наприклад, KnowledgeEntry.KnownCause містить «викликає InvalidOperationException при валідації» — спрацює на будь-який інцидент з exception_type=«InvalidOperationException». Без фільтру за component — високий ризик хибних спрацьовувань.

**Рекомендація:**
- **Прибрати Priority 3 у v1.** Залишити 3 пріоритети: exact / component+signal / manual.
- Або **замінити на KnowledgeEntry.ExceptionKeywords text[]** — оператор явно вказує ключові слова, за якими матчиться. Тоді match = `incident.exception_type = ANY(knowledge.exception_keywords)`. Детерміновано.
- Версія 2 може додати embeddings для семантичного матчингу.

### Деградація при конфлікті (§7)

«Якщо декілька Priority 1 — обрати з AffectedVersions що містить release, інакше найновіший» — коректно. Але якщо декілька з однаковою UpdatedAt? Додати tiebreaker по KnowledgeId (визначено).

---

## 6. Reference vs Evidence — ✅ Reference достатньо

**Reference** — оператор вказує на джерело (commit, issue, doc).
**Evidence** — об'єктивні дані інциденту (trace, stack).

`telemetry_incidents.evidence` (Стаття 9) вже містить evidence інциденту. Knowledge Entry — це АНАЛІТИЧНИЙ результат дослідження evidence, а не сам evidence. Окремого поля Evidence не потрібно. Якщо оператор хоче послатись на конкретний trace — додає Reference з URL на Trace Explorer.

✅ Дизайн коректний.

---

## 7. Циклічна залежність Knowledge ↔ Incident — ✅ Відсутня

Зв'язок через спільне поле `fingerprint_key`, не через FK. KnowledgeEntry не має `incident_id`, інцидент не має `knowledge_id`. Матчинг обчислюється на льоту через VIEW. Це правильно.

**Перевірка:** Create Knowledge з інциденту → prefetch `fingerprint_key` у форму → запис створюється з цим fingerprint. Немає FK інцидент→знання, але є логічний зв'язок через fingerprint. Циклу нема.

---

## 8. Авто-Verification через release_health — ⚠️ Формула неконкретна

### Дизайн §6:
> `Verified` автоматично при: `FixedVersion ≤ current_release` AND `release_health` для цього fingerprint без рецидивів ≥ 14 днів.

### Проблеми

**1. «current_release» не визначено.**
- max(app_version) у telemetry_events? Будь-яка одинична подія зі старою версією зможе опустити current.
- Останній реліз у release_health? Як release_health визначає «реліз»? Потрібно ≥ N install events за T днів.

**2. «без рецидивів ≥ 14 днів» неоднозначно.**
- Немає нових інцидентів з цим fingerprint? Або взагалі немає Failed подій з цим signal?
- Якщо sample size впав (мало юзерів на FixedVersion) — відсутність інцидентів не означає фіксу.
- Потрібно: `success_rate(component, operation) у FixedVersion ≥ поріг (95%?)` AND `min_sample ≥ N`.

**3. Хибне auto-verify.**
Якщо оператор помилився у FixedVersion (вказав 1.0.2, а реально фікс у 1.0.3) — система підтвердить неправду. З точки зору надійності — операторська помилка, не системна. Але дизайн має це визнати: Verification ≠ 100% істина.

### Рекомендація: конкретизувати формулу

`auto-verify(knowledge_id)` спрацьовує, якщо ВСЕ перелічене:

1. `knowledge_entries.FixedVersion IS NOT NULL`
2. Існує реліз R у `control_center.release_health` з `release = FixedVersion` і `install_count ≥ 50` за останні 14 днів (значуща вибірка).
3. `success_rate(component, operation, FixedVersion) ≥ 0.95` (з release_health_detail).
4. Жодного інциденту з `fingerprint_key = knowledge.fingerprint_key` з `opened_at >= release_date(FixedVersion)`.
5. `Confidence = High` (Verification вимагає високої довіри).

Тільки всі 5 умов → auto-verify. Інакше — залишається High.

Також: **нотифікація оператору** при auto-verify через `notification_queue` з типом `KnowledgeVerified` (Стаття 24 — той самий механізм).

---

## 9. Масштабування 100 / 1K / 10K / 100K

| Обсяг | Оцінка |
|---|---|
| **100** | ✅ Будь-який запит мілісекунди |
| **1K** | ✅ Без проблем |
| **10K** | ⚠️ `search_knowledge` без FTS — повне сканування. Match (Priority 1/2) — все ще швидкий через індекси |
| **100K** | ❌ `search_knowledge` + `knowledge_coverage` aggregation стануть вузьким місцем |

### Рекомендації

**Для v1 (Free Tier):** 100–1000 знань — реалістичний обсяг на 1–2 роки. Дизайн працює.

**Закласти в дизайн:**
- `search_knowledge` обов'язково з `LIMIT/OFFSET` (або keyset).
- У §9 (Database) додати примітку: «при досягненні 5000+ записів — мігрувати пошук на tsvector (Postgres вбудований FTS), schema additive: додати column `search_vector tsvector` + GIN index».
- `knowledge_coverage` — **materialized view** з REFRESH раз на добу через pg_cron (не LIVE).

---

## 10. Free Tier — обсяг / JOIN / вузькі місця

### Обсяг БД

| Таблиця | Розмір/запис | 1000 знань | 5 років експлуатації |
|---|---|---|---|
| `knowledge_entries` | ~1.5 КБ | 1.5 МБ | 1.5 МБ (стабільно) |
| `knowledge_references` | ~200 байт × 3 = 600 байт | 0.6 МБ | 0.6 МБ |
| `knowledge_version_history` | ~1.5 КБ × 10 змін | 15 МБ | 15 МБ |
| Разом | — | ~17 МБ | ~17 МБ |

✅ 17 МБ із 500 МБ Free Tier — 3.4%. Запас величезний.

### JOIN навантаження

| Запит | Оцінка |
|---|---|
| `match_knowledge_for_incident` Priority 1 | 1 indexed lookup — <1 мс |
| `match_knowledge_for_incident` Priority 2 | index range scan — <5 мс |
| `control_center.knowledge_entry_detail` | 1 JOIN з references — <10 мс |
| `control_center.knowledge_coverage` | aggregate over incidents+knowledge per release — ⚠️ 100+ мс при 10 релізах × 1000 знань |
| `search_knowledge` (10K+) | повне сканування — >1 с |

### Вузькі місця

1. **`knowledge_coverage` LIVE VIEW** — проблематично. Агрегує incidents × knowledge × release. Краще materialized view.
2. **`search_knowledge` без FTS** — на 10K+ нестерпно.

### Рекомендація

У Slice 5 (Release Integration) `knowledge_coverage` реалізувати як materialized view з REFRESH через pg_cron (раз на добу або на подію нового інциденту). LIVE aggregation — неприйнятно для Free Tier.

---

## 11. Безпека — ❌ changed_by ненадійний

### Поточний дизайн

§10: усі функції приймають параметр `changed_by text`. Control Center передає Discord username / GitHub login як цей параметр.

### Проблема

`cc_readonly` (якщо обрати рішення B з §1 цього аудиту) — єдина роль у Control Center. Будь-хто з операторів, що має доступ до пароля cc_readonly, може передати **будь-яке** значення `changed_by`. Audit-журнал `knowledge_version_history` фіксуватиме те, що клієнт стверджує, а не реального користувача.

Якщо завтра з'явиться конфлікт («хто змінив цей запис?») — неможливо довести, хто це зробив насправді. Довіра до audit = довіра до оператора.

### Рекомендація: два шари audit

1. `changed_by text` (з клієнта) — що оператор стверджує (Discord username).
2. `session_user text` (з тригера) — автоматично через `current_user` PostgreSQL.

У `knowledge_version_history` додати колонку `session_user text NOT NULL DEFAULT current_user`. Тригер заповнює автоматично. Це дасть два шари: заявлений + реальний БД-користувач.

**Для повноцінної автентифікації користувачів** (хто саме з операторів) потрібна інтеграція з Supabase Auth → майбутнє (post-Free Tier). Поки що session_user достатньо для невідмовності audit.

### Інші аспекти безпеки

| Аспект | Оцінка |
|---|---|
| RLS deny-all за замовчуванням | ✅ Класичний патерн |
| SECURITY DEFINER функції | ✅ Як Workflow (Стаття 23) |
| `cc_notifier` не торкається знань | ✅ Ізоляція Worker |
| Ескалація через Update без пермішенів | ✅ Заборонено RLS |
| SQL injection у search_knowledge | ⚠️ Параметризований query — OK, але ILIKE з user input вимагає екранування `%` `_` |

---

## 12. Принципи (KISS / DRY / Zero Regression / Append-Only / Additive)

| Принцип | Оцінка | Деталі |
|---|---|---|
| **KISS** | ✅ | 3 таблиці, 11 функцій — помірно для задачі. Кожен елемент обґрунтований |
| **DRY** | ⚠️ | Status і Confidence обидва мають «Verified». Джерело плутанини (див. §4) |
| **Zero Regression** | ✅ | Адитививний дизайн, не чіпає RC1 |
| **Append-Only** | ❌ двозначно | §2 принцип 2 пише «Append-Only» — але `knowledge_entries` UPDATE дозволено. Треба уточнити: **append-only history, але не сам запис** |
| **Additive Schema** | ✅ | Усі нові таблиці/VIEWs/функції additive |

### Рекомендація

У §2 принцип 2 переформулювати:
> «Append-Only History. Будь-яка зміна `knowledge_entries` залишає незмінний запис у `knowledge_version_history`. Сам запис редагується (Workflow-операції), але кожна версія незнищенна. Видалення (`DELETE`) заборонено на рівні RLS для всіх ролей.»

---

## 13. Конкретні архітектурні рішення (підсумок)

Шість обов'язкових правок дизайну перед Slice 1.

### CRITICAL (блокують старт)

**Зміна #1: модель ролей (§1 аудиту).**
Прибрати `cc_knowledge_editor`. Розширити `cc_readonly` EXECUTE на Knowledge-функції (патерн Статті 23). Усі writes через SECURITY DEFINER. Це узгоджується з Статтею 20.

**Зміна #2: ортогональність Status/Confidence (§4 аудиту).**
Додати CHECK-обмеження на `knowledge_entries`:
```
CHECK (
  (status='Verified'  AND confidence IN ('High','Verified')) OR
  (status IN ('Draft','Reviewed') AND confidence IN ('Low','Medium','High')) OR
  (status IN ('Deprecated','Archived'))
)
```
Усуне неможливі комбінації на рівні схеми.

**Зміна #3: ненадійний changed_by (§11 аудиту).**
У `knowledge_version_history` додати `session_user text NOT NULL DEFAULT current_user`. Тригер автоматично. Два шари audit.

### HIGH (важливі)

**Зміна #4: прибрати Priority 3 match (§5 аудиту).**
У §7 залишити 3 пріоритети: exact fingerprint / component+signal / manual. Або замінити на `KnowledgeEntry.ExceptionKeywords text[]` з матчем `incident.signal = ANY(exception_keywords)`.

**Зміна #5: конкретизувати auto-verify (§8 аудиту).**
У §6 замінити «без рецидивів ≥ 14 днів» на 5 умов з цього аудиту (FixedVersion + реліз з ≥50 installs + success_rate ≥95% + 0 нових інцидентів з fingerprint + Confidence=High).

**Зміна #6: knowledge_coverage — materialized view (§10 аудиту).**
У §9 позначити `control_center.knowledge_coverage` як materialized view з REFRESH через pg_cron. LIVE aggregation заборонена для Free Tier.

### MEDIUM (бажано)

**Зміна #7 (опц.): закладка FTS.**
У §9 додати примітку про tsvector при 5000+ записів.

**Зміна #8 (опц.): принцип Append-Only уточнити (§12).**

**Зміна #9 (опц.): Стаття 17 чеклист адаптувати під cc_readonly (через SECURITY DEFINER, не прямі writes).**

---

## 14. Фінальний висновок

### 🟡 Design Ready після 6 обов'язкових правок

Дизайн архітектурно здоровий: правильна ізоляція від RC1, адекватна модель БД, принцип Human-Verified (Стаття 28) витриманий. Але 3 критичних і 3 важливих проблеми вимагають правок перед стартом реалізації:

| # | Проблема | Критичність |
|---|---|---|
| 1 | cc_knowledge_editor vs cc_readonly — конфлікт зі Статтею 20 | CRITICAL |
| 2 | Status/Confidence — можливі неможливі комбінації | CRITICAL |
| 3 | changed_by ненадійний (session_user відсутній) | CRITICAL |
| 4 | Priority 3 match підозрілий | HIGH |
| 5 | Auto-verify формула неконкретна | HIGH |
| 6 | knowledge_coverage LIVE — ризик Free Tier | HIGH |

**Не рекомендується:** перепроєктування. Усі 6 проблем мають конкретні рішення в межах поточного дизайну.

**Не вимагають змін:** Reference vs Evidence (#6 аудиту), циклічна залежність (#7), Scaling до 1K (#9), обсяг БД (#10).

**Наступний крок:** застосувати 6 правок до `Knowledge-Engine-Design.md` → bump версії до v1.1 → повторно погодити → старт Slice 1.

---

## Статус документа

- **Версія:** 1.0 (Review)
- **Дата:** 2026-07-04
- **Базис:** Knowledge-Engine-Design.md v1.0, Constitution v28, RC1 (tag v0.9.0-observability-rc1)
- **Вердикт:** Design Ready після 6 обов'язкових правок
