# Knowledge Engine — Design Specification

> **Специфікація Phase 6 SCLOC Observability Platform.**
>
> Цей документ — контракт для майбутньої реалізації. Реєстрація слайсів
> (Phase 6 Slices 1–5) виконується за цією специфікацією без повторного
> перепроєктування (див. розділ **Design Freeze v1.1** наприкінці).
>
> Спирається на:
> - [`Observability-Constitution.md`](./Observability-Constitution.md) (Стаття 28 насамперед)
> - [`Observability-RC1-Release.md`](./Observability-RC1-Release.md) (фундамент RC1)
> - [`Knowledge-Engine-Design-Review.md`](./Knowledge-Engine-Design-Review.md) (аудит v1.0 → 6 правок у v1.1)
>
> **Статус:** Design v1.1 (Frozen). После погодження — розбивка на Slices у Roadmap.

---

## Історія версій

| Версія | Дата | Зміни |
|---|---|---|
| 1.0 | 2026-07-04 | Початковий дизайн |
| **1.1** | **2026-07-04** | **6 правок за Architecture Review: модель ролей, CHECK-інваріант Status/Confidence, session_user audit, прибрано Priority 3 match, конкретизовано auto-verify, knowledge_coverage → materialized view. Додано Design Freeze.** |

---

## 1. Мета

**Knowledge Engine** — підсистема, що перетворює підтверджений людський досвід
дослідження інцидентів у повторно використовувані знання, і автоматично пропонує
їх при рецидивах (Стаття 21) того самого `fingerprint_key`.

### Проблема, яку вирішує

Без Knowledge Engine кожен рецидив (`INC-2026-00042` з тим самим fingerprint, що й
закритий `INC-2026-00007`) досліджується з нуля. Розробник витрачає години на ту
саму forensic-роботу, яка вже була виконана колегою тиждень тому. Знання,
здобуті під час розслідування, випаровуються з закриттям інциденту.

### Що Knowledge Engine принципово НЕ робить

- ❌ **Не створює записи автоматично.** Жодної автогенерації з телеметрії чи LLM.
  Кожен запис — результат людського рішення (Стаття 28).
- ❌ **Не є джерелом правди.** Правдою залишається телеметрія (`telemetry_events`).
  Knowledge Entry лише супроводжує підказкою.
- ❌ **Не замінює Workflow інциденту.** Workflow (Стаття 22/23) продовжує бути
  основним життєвим циклом інциденту.
- ❌ **Не зберігає PII.** Санітизація на етапі введення (Стаття 4).
- ❌ **Не використовує семантичний/LLM matching у v1.** Матчинг повністю детермінований (§7).

### Критерії успіху Phase 6

1. При відкритті нового інциденту з відомим fingerprint — на сторінці деталей
   з'являється блок **"Known Solution"** з причиною та виправленням за ≤ 1 с.
2. Розробник може створити Knowledge Entry з деталей дослідженого інциденту
   через явну дію **"Create Knowledge"** у Workflow.
3. Жоден запис не з'являється автоматично; жоден запис не зникає без аудиту.

---

## 2. Принципи

| # | Принцип | Похідна стаття |
|---|---|---|
| 1 | **Human Verified.** Кожен запис створено людиною через явну дію. | Стаття 28 |
| 2 | **Append-Only History.** Будь-яка зміна `knowledge_entries` залишає незмінний запис у `knowledge_version_history`. Сам запис редагується (Workflow-операції), але кожна версія незнищенна. Видалення (`DELETE`) заборонено на рівні RLS для всіх ролей. | Стаття 6 (адаптовано) |
| 3 | **Version Aware.** Записи враховують версію релізу (`AffectedVersions`, `FixedVersion`) — знання може бути істинним для 1.0.0 і застарілим для 1.1.0. | Стаття 9 |
| 4 | **Fingerprint Driven.** Матчинг іде через `fingerprint_key` інциденту (Стаття 21), а не через INC-ID. Рецидив → одразу підказка. | Стаття 21 |
| 5 | **Zero Regression.** Knowledge Engine не модифікує існуючі підсистеми (Telemetry/Trace/Incident Engine/Workflow/Notification Engine). Тільки доповнення. | Стаття 12 |
| 6 | **Deterministic Matching.** Ніяких AI-евристик, embeddings чи fuzzy matching у v1 — лише детерміновані правила пріоритету. Семантичний пошук виноситься у Future Extensions. | Design v1.1 |
| 7 | **Privacy First.** PII-санітизація при введенні текстових полів. | Стаття 4 |
| 8 | **Free Tier Fit.** Схема мінімальна; жодних jsonb-масивів великого обсягу; індекси только необхідні; агрегати → materialized views. | Free Tier |
| 9 | **Dual Audit Identity.** Кожна зміна фіксує як заявлений оператор (`changed_by` з клієнта), так і реальний БД-користувач (`session_user` автоматично). | Security (v1.1) |

---

## 3. Архітектура

### Потік даних

```
                          ┌─────────────────────────────────────────┐
                          │  Investigation (людський аналіз)        │
                          │  Workflow: Investigating → Resolved     │
                          │  (Стаття 23)                            │
                          └────────────────┬────────────────────────┘
                                           │ дія "Create Knowledge"
                                           ▼
                          ┌─────────────────────────────────────────┐
                          │  knowledge_entries (Draft)              │
                          │  + knowledge_references                 │
                          │  + knowledge_version_history (audit:    │
                          │    changed_by + session_user)           │
                          └────────────────┬────────────────────────┘
                                           │ Workflow: Draft → Reviewed → Verified
                                           ▼
┌─────────────────────┐         ┌────────────────────────────────────┐
│ Новий інцидент      │         │  knowledge_entries (Verified)      │
│ INC-YYYY-NNNNN      │         │  indexed by fingerprint_hash       │
│                     │         │                                    │
│ fingerprint_key     │────────▶│  Knowledge Match Engine            │
│ component·op·signal │         │  (детерміновані правила пріоритету │
│ ·release-segment    │         │   v1.1: 3 рівні)                   │
└─────────────────────┘         │                                    │
                                └────────────────┬───────────────────┘
                                                 │ match result
                                                 ▼
                          ┌─────────────────────────────────────────┐
                          │  Operator Hint:                         │
                          │  "Known Solution" у Incident Details    │
                          │  + підказка у Trace Explorer            │
                          │  + coverage у Release Health (matview)  │
                          └─────────────────────────────────────────┘
```

### Ізоляція від фундаменту RC1

| Фундамент RC1 | Knowledge Engine — чи торкається? |
|---|---|
| `telemetry_events` | НІ (лише читає `signal` через VIEWs) |
| `telemetry_incidents` | НІ (лише читає `fingerprint_key`, `component`, `signal`) |
| `incident_policy` | НІ |
| `notification_queue` / `notification_attempts` | НІ (але TYPE `KnowledgeVerified` додається у §10 — additive) |
| Workflow функції (transition/note/owner) | НІ (нові функції додаються окремо) |
| `control_center.*` VIEWs | ДОПОВНЕННЯ (нові VIEWs, не зміна існуючих) |
| Роль `cc_readonly` | ДОПОВНЕННЯ (EXECUTE на нові Knowledge-функції — патерн Статті 23) |

Усі нові таблиці в схемі `public` з префіксом `knowledge_*`. Усі нові VIEWs у схемі `control_center`.

---

## 4. Модель KnowledgeEntry

### Логічна модель

| Поле | Тип | Призначення | Обов'язкове |
|---|---|---|---|
| `KnowledgeId` | bigint (ідентифікатор) | Сурогатний PK | ✅ |
| `FingerprintKey` | text | Зв'язок з інцидентом (Стаття 21). Формат: `component\|operation\|signal\|release-segment` | ✅ |
| `FingerprintHash` | uuid | Hash fingerprint_key (для індексу, стабільний) | ✅ |
| `Title` | text | Короткий заголовок (1 рядок). Приклад: "CERT_E_UNTRUSTEDROOT при встановленні L.I.A" | ✅ |
| `Symptoms` | text | Що бачить користувач/оператор (повідомлення, симптоми). Багаторядкове | ⬜ |
| `KnownCause` | text | Встановлена причина. Корінь проблеми. | ✅ |
| `Workaround` | text | Обхідне рішення для користувача (якщо є) | ⬜ |
| `PermanentFix` | text | Що виправлено в коді (опис) | ⬜ |
| `AffectedVersions` | text[] | Список версій, де проблема існує (напр., `{"1.0.0","1.0.1"}`) | ⬜ |
| `FixedVersion` | text | Версія, де виправлено (напр., `"1.0.2"`) | ⬜ |
| `Confidence` | enum | `Low` / `Medium` / `High` / `Verified` | ✅ |
| `Status` | enum | `Draft` / `Reviewed` / `Verified` / `Deprecated` / `Archived` | ✅ |
| `CreatedBy` | text | Автор (заявлений клієнтом: Discord username / GitHub login) | ✅ |
| `CreatedAt` | timestamptz | Час створення | ✅ |
| `UpdatedBy` | text | Останній редактор (заявлений) | ✅ |
| `UpdatedAt` | timestamptz | Час останньої зміни | ✅ |

### CHECK-інваріант Status ↔ Confidence (v1.1)

На рівні БД заборонені логічно нестабільні комбінації. Інваріант:

```
CHECK (
  (status = 'Verified'   AND confidence IN ('High','Verified')) OR
  (status IN ('Draft','Reviewed') AND confidence IN ('Low','Medium','High')) OR
  (status IN ('Deprecated','Archived'))
)
```

Це усуває неможливі стани на рівні схеми (як Стаття 5/25). UI не зможе створити `Verified + Medium` — БД відхилить.

### Зв'язані сутності

#### KnowledgeReference (1:N)

| Поле | Тип | Призначення |
|---|---|---|
| `ReferenceId` | bigint | PK |
| `KnowledgeId` | bigint FK | Посилання на KnowledgeEntry. **ON DELETE RESTRICT** (забороняє видалення запису з посиланнями). |
| `ReferenceType` | enum | `GitCommit` / `GitHubIssue` / `Documentation` / `External` |
| `Url` | text | Повний URL (для клікабельності в UI) |
| `Label` | text | Напр., "commit abc1234", "Issue #42", "Wiki: install troubleshooting" |

Декілька посилань на один запис — звичайна ситуація (commit + issue + doc).

#### KnowledgeVersionHistory (append-only audit, з dual-identity v1.1)

| Поле | Тип | Призначення |
|---|---|---|
| `VersionId` | bigint | PK |
| `KnowledgeId` | bigint FK | Зв'язок із записом. **ON DELETE RESTRICT** (як вище). |
| `Version` | int | Монотонний номер версії (1, 2, 3, ...) |
| `Snapshot` | jsonb | Повний snapshot запису на момент зміни |
| `ChangedBy` | text | Заявлений оператор (з клієнта: Discord username) |
| `SessionUser` | text NOT NULL DEFAULT `current_user` | **Реальний БД-користувач** (автоматично через тригер, не підробний клієнтом) |
| `ChangedAt` | timestamptz | Коли |
| `ChangeReason` | text | Короткий опис що й навіщо |

Створюється тригером на UPDATE/INSERT у `knowledge_entries`. Невидаляється.

**Dual-identity принцип:** якщо завтра виникне питання «хто насправді змінив цей запис» — `SessionUser` дає незаперечну відповідь (роль підключення), а `ChangedBy` показує що оператор стверджував. Якщо вони різні — ознака підозрілої активності.

---

## 5. Життєвий цикл

### Стани

```
                ┌─────────┐
                │  Draft  │  ◄── створення оператором (Create Knowledge)
                └────┬────┘
                     │ Publish (review)
                     ▼
                ┌──────────┐
        ┌──────►│ Reviewed │
        │       └────┬─────┘
        │            │ Verify (вимагає Confidence ≥ High — інваріант §4)
        │            ▼
        │     ┌──────────┐
        │     │ Verified │  ◄── основний "робочий" стан; тільки цей показується
        │     └────┬─────┘      як Known Solution у підказках
        │          │ Deprecate
        │          ▼
        │     ┌───────────┐
        │     │ Deprecated│  ◄── знайдено неточність або замінено новим записом
        │     └────┬──────┘
        │          │ Archive (фінал)
        │          ▼
        │     ┌──────────┐
        │     │ Archived │  ◄── невидаляється; доступний для історії
        │     └──────────┘
        │
        └────────── (Reopen: Deprecated → Reviewed, коли новий аналіз виправляє запис)
```

### Допустимі переходи

| З → У | Дія | Хто | Передумова |
|---|---|---|---|
| Draft → Reviewed | `Publish` | будь-хто з правом Create (cc_readonly) | Заповнені `Title`, `KnownCause` |
| Draft → Archived | `Archive` | автор або адмін | без умов (видалення без тесту) |
| Reviewed → Verified | `Verify` | адмін або auto через release_health | **`Confidence ≥ High`** (заборонено БД-інваріантом §4) |
| Reviewed → Draft | `ReturnForRevision` | рецензент | коментар обов'язковий |
| Verified → Deprecated | `Deprecate` | адмін | `ChangeReason` обов'язковий |
| Deprecated → Reviewed | `Reopen` | адмін | новий аналіз описано |
| Deprecated → Archived | `Archive` | адмін | без умов |
| Verified → Reviewed | `ReopenForRevision` | адмін | якщо нове розслідування виявило неточність |

### Заборонені переходи

- ❌ `Archived → *` (будь-що). Архів фінальний.
- ❌ Будь-який → `Draft` окрім `Reviewed → Draft` (тільки рецензент повертає).
- ❌ Будь-яке зниження `Confidence` при `Status = 'Verified'` — БД відхилить через інваріант §4. Спочатку Reopen (Verified → Reviewed), потім зміна Confidence.

### Правила показу підказки

Тільки записи зі `Status = Verified` показуються як **"Known Solution"** у деталях інциденту.
Записи `Draft`/`Reviewed` видно лише на сторінці Knowledge Management адміна.
Записи `Deprecated`/`Archived` — у історії.

---

## 6. Confidence

Шкала довіри до **змісту** запису (окремо від Status — workflow публікації).

| Рівень | Критерій | Як показується в підказці |
|---|---|---|
| `Low` | Гіпотеза, заснована на одному інциденті, без підтвердження в коді. | Застереження: "Непідтверджена гіпотеза" |
| `Medium` | Підтверджена причина, але без commit/issue-посилання, або з неповним workaround. | Без застереження, але без позначки "Verified" |
| `High` | Встановлена причина + посилання на commit/issue + описане виправлення. | Позначка "Висока довіра" |
| `Verified` | **Auto-verify через release_health (5 умов §6.1).** | Позначка "Перевірено релізом" |

### Правила переходу Confidence

| З → У | Умова |
|---|---|
| (початкове) → Low | автоматично при створенні |
| Low → Medium | оператором вручну після перехресної перевірки |
| Medium → High | додано `PermanentFix` + мінімум 1 `GitCommit` reference |
| High → Verified | **автоматично** через `verify_knowledge_auto()` (див. §6.1 нижче) |
| Verified → High | тільки через `ReopenForRevision` (Status Verified → Reviewed), потім оператор знижує Confidence. Пряме зниження заборонено БД-інваріантом §4. |

**Verification не робиться вручну.** Це автоматичне підвищення на основі
спостереження release_health (детермінована формула §6.1). Дозволяє знанням
"дозрівати" до високого ступеня довіри без ручної роботи оператора.

### 6.1. Формула auto-verify (v1.1 — повністю детермінована)

`verify_knowledge_auto(knowledge_id)` спрацьовує, **тільки якщо всі 5 умов виконані**:

1. `knowledge_entries.FixedVersion IS NOT NULL`.
2. Існує реліз R у `control_center.release_health` з `release = FixedVersion` і `install_count ≥ 50` за останні 14 днів (значуща вибірка).
3. `success_rate(component, operation, FixedVersion) ≥ 0.95` з `control_center.release_health_detail`.
4. Жодного інциденту з `fingerprint_key = knowledge.fingerprint_key` і `opened_at >= release_date(FixedVersion)`.
5. `knowledge_entries.Confidence = 'High'` на момент перевірки.

Якщо всі 5 — Confidence стає `Verified` (з audit записом `KnowledgeAutoVerified` у `knowledge_version_history` + нотифікація оператору через `notification_queue` тип `KnowledgeVerified` — патерн Статті 24).

Якщо хоч одна умова не виконана — запис залишається `High`. Ніякого «часткового» verify.

**Виконання:** `pg_cron` job раз на добу (Slide 5).

---

## 7. Matching Policy

Детерміновані правила пріоритету для вибору Knowledge Entry для інциденту.
**Ніякого AI, embeddings, fuzzy matching у версії 1.** Priority 3 (ILIKE по
KnownCause) прибрано у v1.1 як логічно підозрілий — семантичний пошук виноситься
у §12 (Future Extensions).

### Алгоритм Match (3 рівні у v1.1)

```
Input:  incident.fingerprint_key, incident.component, incident.signal,
        incident.release

Step 1 (Priority 1 — Exact Fingerprint):
    SELECT FROM knowledge_entries
    WHERE fingerprint_hash = hash(incident.fingerprint_key)
      AND status = 'Verified'
    LIMIT 1

    if FOUND → return (priority=1)

Step 2 (Priority 2 — Component + Signal, current release):
    SELECT FROM knowledge_entries
    WHERE component = incident.component
      AND signal = incident.signal
      AND status = 'Verified'
      AND (AffectedVersions IS NULL OR incident.release = ANY(AffectedVersions))
      AND (FixedVersion IS NULL OR FixedVersion > incident.release)
    ORDER BY UpdatedAt DESC, KnowledgeId DESC
    LIMIT 1

    if FOUND → return (priority=2)

Step 3 (Priority 3 — Manual Search):
    return (no automatic match)
    оператор шукає через Knowledge Search сторінку
```

### Коли показувати підказку

| Пріоритет матчу | UI поведінка |
|---|---|
| 1 (Exact) | Зелений блок "Known Solution" з повним контуром (Cause / Fix / Workaround / References) |
| 2 (Component+Signal) | Жовтий блок "Можлива підказка" + позначка пріоритету |
| 3 (None — Manual) | Кнопка "Search Knowledge" (відкриває Knowledge Search з prefetch параметрами) |

### Деградація при конфлікті

Якщо на Priority 1 знайдено КІЛЬКА записів (різні реліз-сегменти одного fingerprint):
вибрати `AffectedVersions` що містить `incident.release`, інакше — найновіший за
`UpdatedAt DESC, KnowledgeId DESC` (визначений tiebreaker у v1.1).

---

## 8. UI — три місця інтеграції

### 8.1. Incident Details (стр. `Incidents.razor`)

**Блок "Known Solution"** у шапці деталей інциденту (вище timeline):

```
┌──────────────────────────────────────────────────────────────┐
│ 🟢 Known Solution                          Match: Priority 1 │
├──────────────────────────────────────────────────────────────┤
│ Title:    CERT_E_UNTRUSTEDROOT при встановленні L.I.A        │
│ Cause:    Сертифікат L.I.A не довірен у системі користувача. │
│           Виникає, коли користувач відхилив root CA або      │
│           системний час зрушений.                            │
│ Fix:      v1.0.2 — автоматичне встановлення root CA через    │
│           підвищені привілеї.                                │
│ Workaround:                                                  │
│           Запустити LIA-RootCA-Installer вручну від імені    │
│           адміністратора.                                    │
│ Confidence: 🟢 Verified (Fixed in v1.0.2, auto-verified      │
│              by release_health 21d ago)                      │
│ References:                                                  │
│   • commit abc1234 (fix: auto-install root CA)               │
│   • Issue #42 (CERT_E_UNTRUSTEDROOT on LIA install)          │
│   • Wiki: LIA Troubleshooting                                │
│                                                              │
│ [Open Full Knowledge Entry]  [Search More]                   │
└──────────────────────────────────────────────────────────────┘
```

Коли матчу нема — компактна кнопка:

```
┌──────────────────────────────────────────────────────────────┐
│ 🔍 No known solution yet.  [Search Knowledge] [Create New]   │
└──────────────────────────────────────────────────────────────┘
```

### 8.2. Trace Explorer (стр. `TraceExplorer.razor`)

**Підказка в кінці trace**, якщо завершальний крок — `Failed` з відомим signal:

```
[Application.Start]  ✓  120 ms
[Auth.SignIn]        ✓  340 ms
[LIA.Install]        ✗  0x800B0109 — CERT_E_UNTRUSTEDROOT

┌──────────────────────────────────────────────────────────┐
│ 💡 Подібна помилка має відому причину.                    │
│    [Переглянути Known Solution →]                         │
└──────────────────────────────────────────────────────────┘
```

Лінк веде на окрему сторінку KnowledgeEntry або на пов'язаний інцидент.

### 8.3. Release Health (стр. `ReleaseHealth.razor`)

**Колонка Knowledge Coverage** у таблиці релізів (читається з **materialized view** `control_center.knowledge_coverage` — див. §9):

```
| Release | Success | Incidents | Knowledge Coverage |
|---------|---------|-----------|---------------------|
| 1.0.2   | 99.2%   | 1 (open)  | 0/1 (🟡 немає знань) |
| 1.0.1   | 87.4%   | 3 (closed)| 3/3 (🟢 повна)      |
| 1.0.0   | 65.1%   | 8 (closed)| 6/8 (🟡 2 без знань) |
```

Формула coverage: `incidents_with_verified_knowledge / total_closed_incidents` для релізу.

### 8.4. Нова сторінка Knowledge Management (Slice 3)

Окрема сторінка для адміністрування знань:

```
[Search] [Filter by Component ▾] [Filter by Status ▾] [+ New Knowledge]

| ID | Title                          | Component    | Status    | Confidence | Updated    |
|----|--------------------------------|--------------|-----------|------------|------------|
| 7  | CERT_E_UNTRUSTEDROOT на LIA    | LIA          | Verified  | Verified   | 2026-07-03 |
| 6  | 42501 RLS при Installation     | Installation | Reviewed  | High       | 2026-07-01 |
| 5  | Timeout на Update verify       | Updater      | Draft     | Low        | 2026-06-28 |
```

Клік по рядку → повна форма редагування з Workflow-кнопками
(Publish/Verify/Deprecate/Archive) + історія версій (з visible `ChangedBy` + `SessionUser`).

---

## 9. Database (без SQL — лише опис)

### Таблиці (схема `public`)

#### `knowledge_entries`
Основна таблиця записів. Колонки згідно з §4. **CHECK-інваріант Status↔Confidence** (§4) на рівні схеми. RLS:
- `cc_readonly` — SELECT + EXECUTE на Knowledge-функції (патерн Статті 23, v1.1)
- `cc_notifier` — НІ (Worker не торкається знань)
- DELETE заборонено для всіх ролей (Append-Only History принцип §2)

**Нова роль НЕ створюється** (v1.1 Зміна #1). Усі writes через SECURITY DEFINER функції, доступні `cc_readonly` через GRANT EXECUTE — як Workflow Статті 23.

#### `knowledge_references`
1:N посилання (Git commit / Issue / Doc / External). **ON DELETE RESTRICT** на FK до `knowledge_entries` (забороняє видалення запису з посиланнями; оскільки DELETE заборонено RLS, це резервний механізм).

#### `knowledge_version_history`
Append-only аудит (§4). **`SessionUser text NOT NULL DEFAULT current_user`** (v1.1 Зміна #3). Тригер на `knowledge_entries` AFTER INSERT/UPDATE пише snapshot. RLS: лише SELECT для всіх, ніяких INSERT вручну (тільки тригер). **ON DELETE RESTRICT** на FK.

### VIEWs (схема `control_center`)

#### `control_center.knowledge_list`
Список для сторінки Knowledge Management (з join references count + last activity).

#### `control_center.knowledge_matches`
VIEW що для кожного активного інциденту показує підібраний Knowledge Entry за алгоритмом §7. Оновлюється тригером або materialized view (вибір у Slice 2).

#### `control_center.knowledge_coverage` — **MATERIALIZED VIEW** (v1.1 Зміна #6)
Per-release статистика coverage (для UI 8.3). **Обов'язково materialized**, не LIVE — агрегує incidents × knowledge × release і при LIVE-обчисленні на 10+ релізах × 1000 знань дає >100 мс (неприйнятно для Free Tier).

**REFRESH стратегія:** pg_cron раз на добу + ручний REFRESH після будь-якої Workflow-операції (Verify/Deprecate/Archive) через SECURITY DEFINER функцію.

#### `control_center.knowledge_entry_detail`
Повна деталізація одного запису з aggregated references — для форми редагування і для блоку "Known Solution".

### Індекси

- `knowledge_entries(fingerprint_hash)` — для Priority 1 match
- `knowledge_entries(component, signal) WHERE status='Verified'` — для Priority 2
- `knowledge_entries(status, updated_at DESC, knowledge_id DESC)` — для списку Knowledge Management (визначений tiebreaker §7)
- `knowledge_references(knowledge_id)` — FK lookup

### Міграції (попереджувальний план)

- `00019_knowledge_engine_core.sql` — таблиці + тригер audit (з `session_user`) + CHECK-інваріант + базові VIEWs + RLS + GRANT EXECUTE на cc_readonly
- `00020_knowledge_match_view.sql` — VIEW для матчингу + `knowledge_coverage` materialized view + pg_cron REFRESH
- (далі за потребою в Slice 3-5)

**Примітка:** роль `cc_knowledge_editor` НЕ створюється (v1.1 Зміна #1). Міграція `00020_knowledge_role.sql` з v1.0 прибрана.

### закладка повнотекстового пошуку (опціонально, Future)

При досягненні 5000+ записів у `knowledge_entries` — мігрувати `search_knowledge` на Postgres FTS:
додати колонку `search_vector tsvector` (additive) + GIN index. Не вимагає зміни існуючих запитів — розширення.

---

## 10. API

Без REST. Усі операції — через SECURITY DEFINER функції (як Workflow Стаття 23),
викликувані **`cc_readonly`** через GRANT EXECUTE (v1.1 Зміна #1).

### Функції

| Функція | Дія | Параметри | Повертає |
|---|---|---|---|
| `create_knowledge_entry` | Створити Draft | fingerprint_key, title, known_cause, optional fields, created_by | new KnowledgeId |
| `update_knowledge_entry` | Редагувати з audit (dual-identity) | knowledge_id, fields, changed_by, change_reason | void (snapshot у history з `session_user=current_user`) |
| `publish_knowledge_entry` | Draft → Reviewed | knowledge_id, changed_by | void |
| `verify_knowledge_entry` | Reviewed → Verified (ручне, напр. ad-hoc) | knowledge_id, changed_by | void (вимагає Confidence ≥ High — інваріант §4) |
| `verify_knowledge_auto` | pg_cron: авто-Verify за формулою §6.1 | (без параметрів) | count of auto-verified |
| `deprecate_knowledge_entry` | Verified → Deprecated | knowledge_id, changed_by, change_reason | void |
| `archive_knowledge_entry` | * → Archived | knowledge_id, changed_by | void (заборонено з Archived) |
| `reopen_knowledge_entry` | Deprecated → Reviewed | knowledge_id, changed_by, change_reason | void |
| `add_knowledge_reference` | Додати посилання | knowledge_id, ref_type, url, label, added_by | reference_id |
| `remove_knowledge_reference` | Прибрати посилання | reference_id, removed_by | void |
| `search_knowledge` | Пошук для Manual Search (з пагінацією) | query, component?, status?, confidence?, limit, offset | set of (knowledge_id, title, status, confidence, relevance_rank) |
| `match_knowledge_for_incident` | Автоматичний матчинг (§7) | incident_id | (knowledge_id, priority, confidence) or null |

### Автоматизація

`verify_knowledge_auto()` — `pg_cron` job (раз на добу, Slide 5), що реалізує формулу §6.1 з 5 умов. При кожному спрацьовуванні — нотифікація оператору через `notification_queue` з типом `KnowledgeVerified` (патерн Статті 24, additive extension).

### Зворотна сумісність

Усі функції адитивні. Жодна не модифікує існуючі таблиці RC1. Розширення `notification_queue.notification_type` CHECK-enum значенням `KnowledgeVerified` — additive (Стаття 13).

---

## 11. Rollout Plan — Slices

Кожен слайс — тонкий вертикальний зріз: БД → код → UI → Runtime Verified → commit.
Дотримується принципу thin vertical slice (як RC1-RC6).

### Slice 1 — Display (мінімальна цінність)

**Мета:** показати Knowledge Entry у деталях інциденту, якщо вона існує.

| Що | Деталі |
|---|---|
| БД | `knowledge_entries` + `knowledge_references` + `knowledge_version_history` (з `session_user`) + тригер audit + CHECK-інваріант + GRANT EXECUTE на cc_readonly + VIEW `control_center.knowledge_entry_detail`. Без match VIEW. |
| Код | Repository-метод `GetKnownSolutionAsync(incidentId)` (точний match по fingerprint — Priority 1). Без Priority 2 у Slice 1. |
| UI | Блок "Known Solution" у `Incidents.razor`. Без форми створення. |
| Дані для тесту | Вручну INSERT Knowledge Entry з fingerprint тестового інциденту (через SQL, не функції — для тесту). |
| Критерій готовності | Відкриття інциденту з існуючим Knowledge Entry показує блок; інцидент без нього показує "No known solution yet". |

**Немає:** створення/редагування/пошук/workflow/auto-verify. Усе через SQL для тесту.

### Slice 2 — Search + Match Priority 2

**Мета:** оператор може шукати знання вручну; матчинг розширено до Priority 2.

| Що | Деталі |
|---|---|
| БД | VIEW `control_center.knowledge_list` + функція `search_knowledge` (з пагінацією) + функція `match_knowledge_for_incident` (Priority 1 + 2). |
| Код | `IKnowledgeRepository` + `KnowledgeRepository`. |
| UI | Сторінка `Knowledge.razor` зі списком + фільтрами + пошуком. |
| Критерій | Пошук за keyword + filter по component/status/confidence працює. Match Priority 2 активується при відсутності Priority 1. |

### Slice 3 — Workflow (Create / Update / Verify / Deprecate / Archive)

**Мета:** повний життєвий цикл через UI.

| Що | Деталі |
|---|---|
| БД | Усі SECURITY DEFINER функції §10 (крім `verify_knowledge_auto`). `add_knowledge_reference` + `remove_knowledge_reference`. |
| Код | Сервіс Knowledge Workflow (викликає функції). |
| UI | Форма створення/редагування. Кнопки Publish/Verify/Deprecate/Archive на сторінці деталізації. Кнопка "Create Knowledge" у `Incidents.razor` (із prefetch fingerprint). Історія версій з visible `ChangedBy` + `SessionUser`. |
| Критерій | Повний цикл Draft → Reviewed → Verified через UI, з audit у `knowledge_version_history` (з `session_user`). Спроба змінити Status/Confidence у неможливу комбінацію → БД відхиляє. |

### Slice 4 — Version History UI

**Мета:** показувати повну історію змін запису з diff ключових полів.

| Що | Деталі |
|---|---|
| БД | (вже є з Slice 1 — `knowledge_version_history`) |
| Код | `GetKnowledgeHistoryAsync(knowledgeId)` з diff обчисленням. |
| UI | Розділ "History" на сторінці деталізації: timeline версій з diff ключових полів. Видно `ChangedBy` (заявлений) + `SessionUser` (реальний). |
| Критерій | Будь-яка зміна запису з'являється в історії за ≤ 1 с. |

### Slice 5 — Release Integration (auto-verify + coverage)

**Мета:** автоматичне підвищення Confidence до `Verified` через `release_health` (§6.1).

| Що | Деталі |
|---|---|
| БД | `pg_cron` job з `verify_knowledge_auto()` (5 умов §6.1). Materialized view `control_center.knowledge_coverage` + REFRESH job. Розширення `notification_queue.notification_type` CHECK `KnowledgeVerified`. |
| Код | (мінімум — переважно БД) |
| UI | Колонка Knowledge Coverage у Release Health. Позначка "Verified by release_health" у KnowledgeEntry. |
| Критерій | Після 14 днів без рецидивів + наявності FixedVersion + 5 умов §6.1 — High автоматично стає Verified (з audit + нотифікація). |

### Пріоритети між слайсами

```
Slice 1 (Display)            — дає цінність навіть без UI створення
   ↓
Slice 3 (Workflow)           — замикає цикл "Investigation → Knowledge"
   ↓
Slice 2 (Search + Match 2)   — оператор знаходить знання вручну + relaxed matching
   ↓
Slice 4 (History)            — прозорість змін
   ↓
Slice 5 (Release Integration)— знання "дозрівають" автоматично
```

Кожен слайс самодостатній. Можна зупинитись після будь-якого.

---

## 12. Future Extensions (за межами v1)

Свідомо виноситься за межі першої реалізації. Буде розглянуто в Phase 7+
після накопичення production-даних.

### AI / LLM-розширення

- **Semantic Search (Priority 4+):** embeddings `Symptoms + KnownCause` для пошуку
  подібних випадків навіть без точного fingerprint match. Замінює прибраний Priority 3 ILIKE.
- **Auto-Draft:** LLM пропонує Draft Knowledge Entry з контексту інциденту
  (timeline + notes + forensic). Оператор затверджує або відхиляє.
  Не замінює ручне створення, лише прискорює.
- **Cross-Fingerprint Knowledge Transfer:** якщо новий fingerprint семантично близький
  до існуючого Verified — пропонує "перенести" знання з перевіркою оператором.

### Розширення платформи

- **Knowledge Graph:** зв'язки між записами (causes / related / superseded-by).
- **Auto-Deprecation:** при виявленні суперечності (новий інцидент з тим самим
  fingerprint, але з іншим симптомом) — пропонує Deprecate старий запис.
- **Knowledge Export/Import:** JSON для обміну між середовищами (dev/staging/prod)
  або резервного копіювання.
- **Public Knowledge Base:** опціонально публікувати вибрані Verified записи як
  сторінку типу "Troubleshooting" для кінцевих користувачів.
- **Supabase Auth Integration:** повноцінна автентифікація операторів замість
  shared `cc_readonly`. Тоді `ChangedBy` стає надійним без dual-identity.

### Чому НЕ в v1

- LLM-розширення потребують зовнішнього API → порушує Free Tier-фокус.
- Embeddings потребують pgvector або зовнішнього сховища → складність.
- Auto-draft ризикує породити шум → конфлікт зі Статтею 28 (підтверджене людиною).
- Семантичний match (Priority 3 ILIKE) — підозрілий без нормалізації даних.
- Будь-яке AI-розширення має спочатку довести цінність на реальних даних.

---

## Додаток A — Мапа статей Конституції → реалізація

| Стаття | Як реалізується в Knowledge Engine |
|---|---|
| 1 (Isolation) | Knowledge Engine не кидає винятки в бізнес-код; UI degrade gracefully при відсутності match |
| 4 (No PII) | Санітизація текстових полів при введенні (Symptoms/KnownCause/Workaround) |
| 5 (Append-Only) | `knowledge_version_history` — append-only audit; DELETE заборонено RLS на всіх knowledge-таблицях |
| 6 (Idempotent Retries) | Appendix via `knowledge_version_history` — кожна зміна незнищенна |
| 9 (Trace Recovery) | KnowledgeEntry пов'язується з fingerprint, через який відновлюється trace-контекст інциденту |
| 12 (Single Ingestion / Additive) | Усі нові таблиці/VIEWs/функції — без зміни RC1 |
| 13 (Additive-Only Schema) | CHECK-розширення notification_type — additive; нові таблиці — additive |
| 17 (DB Verification) | Кожен слайс верифікується SQL-тестом через реальну роль `cc_readonly` (через SECURITY DEFINER функції, не прямі writes — адаптований чеклист) |
| 18 (Production Pending) | Auto-verify через release_health отримає цей статус до появи реальних релізів |
| 20 (Dashboard Purity) | `cc_readonly` SELECT на VIEWs + EXECUTE на Knowledge-функціях (як Стаття 23). Прямі writes заборонені. |
| 21 (Incident Identity) | Матчинг через fingerprint_key, не INC-ID |
| 22 (Immutable History) | `knowledge_version_history` append-only |
| 23 (Workflow Access) | Усі переходи через SECURITY DEFINER функції, доступні `cc_readonly` через GRANT EXECUTE |
| 24 (Notification Independence) | Auto-verify → `notification_queue` тип `KnowledgeVerified` (additive enum) |
| **28 (Knowledge Preservation)** | Реалізується повністю — ручне створення, матчинг, підказки |

---

## Додаток B — Контрольні питання перед стартом Slice 1

Перед реалізацією Slice 1 переконатися, що відповіді на ці питання не потребують
повторного дизайну:

- [ ] Якщо дві KnowledgeEntry мають однаковий fingerprint (різні AffectedVersions), яка виграє match? → §7 «Деградація при конфлікті» (вибір по AffectedVersions, потім UpdatedAt DESC, KnowledgeId DESC)
- [ ] Що відбувається з KnowledgeEntry, коли інцидент переведено в Archived (Workflow), для якого вона була створена? → Запис незмінний (зв'язок через fingerprint, не INC-ID)
- [ ] Чи можна створити KnowledgeEntry без пов'язаного інциденту? → Так, через Knowledge Management сторінку (Slice 3) з ручним fingerprint
- [ ] Хто має права на Knowledge функції? → `cc_readonly` через GRANT EXECUTE (патерн Статті 23). Окремої ролі не потрібно (v1.1 Зміна #1).
- [ ] Якщо FixedVersion опубліковано, але рецидив стався — що відбувається з Confidence? → Формула §6.1 умова 4 не виконається → залишається `High`. Нотифікація типу `KnowledgeVerified` не відправляється. Оператор може вручну Reopen.
- [ ] Як розрізнити оператора у спільному підключенні cc_readonly? → Dual-identity: `ChangedBy` (заявлений клієнтом) + `SessionUser` (реальний БД-користувач). Повна надійність — після Supabase Auth (Future).

---

## Додаток C — Журнал правок v1.0 → v1.1

| # | Правка | Розділ | Критичність | Зміст |
|---|---|---|---|---|
| 1 | Модель ролей | §3, §9, §10 | CRITICAL | Прибрати `cc_knowledge_editor`. Розширити `cc_readonly` EXECUTE на Knowledge-функції (патерн Статті 23). Узгоджено зі Статтею 20. |
| 2 | Status/Confidence інваріант | §4, §5, §6 | CRITICAL | CHECK-обмеження на БД: `status='Verified' → confidence IN ('High','Verified')`. Усуває неможливі комбінації на рівні схеми. |
| 3 | Dual-identity audit | §4, §11 | CRITICAL | `session_user text NOT NULL DEFAULT current_user` у `knowledge_version_history`. Ненадійний `changed_by` доповнено реальним БД-користувачем. |
| 4 | Priority 3 прибрано | §7 | HIGH | Повністю прибрано ILIKE match (семантичний пошук виноситься у §12 Future). 3 рівні matching: exact / component+signal / manual. |
| 5 | Auto-verify формула | §6.1, §10 | HIGH | 5 детермінованих умов замість «без рецидивів 14 днів»: FixedVersion + реліз ≥50 installs + success_rate ≥95% + 0 рецидивів + Confidence=High. |
| 6 | knowledge_coverage matview | §9, §11 | HIGH | Materialized view замість LIVE VIEW. REFRESH через pg_cron + після Workflow-операцій. Відповідь Free Tier performance. |

---

## Design Freeze v1.1

> **Цей розділ заморожує дизайн як контракт Phase 6.**

Після погодження v1.1 документ стає **базовим контрактом** Phase 6 Knowledge Engine.

**Правила заморожування:**

1. **Документ є контрактом.** Knowledge Engine Slices 1–5 реалізуються виключно за цією специфікацією. Slice не може вводити архітектуру, що суперечить §1–§12.

2. **Адитивні зміни тільки.** У процесі реалізації слайсу дозволяється:
   - додавати нові функції (адитивно до §10);
   - додавати нові VIEWs (адитивно до §9);
   - додавати нові поля в `knowledge_entries` (additive, nullable, з DEFAULT).
   
   Забороняється:
   - змінювати моделі Status/Confidence та їх інваріант (§4/§5/§6);
   - змінювати алгоритм Matching Policy (§7);
   - змінювати формулу auto-verify (§6.1);
   - змінювати патерн ролей (тільки `cc_readonly` + SECURITY DEFINER).

3. **Будь-яка структурна зміна — через новий ADR або поправку до Конституції.**
   Якщо під час реалізації виявлено, що дизайн v1.1 потребує зміни:
   - зупинити реалізацію слайсу;
   - оформити Architecture Decision Record (ADR) з описом проблеми та альтернатив;
   - якщо зміна зачіпає Конституцію — поправка до неї через PR зі стандартним процесом;
   - тільки після погодження — bump документа до v1.2 + новий Freeze.

4. **Молчазне відхилення неприпустиме.** Якщо реалізація змушена відхилитись від
   дизайну — це сигнал або недоробки дизайну (треба ADR), або помилки реалізації
   (треба виправити код). Мовчазне "тут зробив інакше" неприпустиме (як і у
   Конституції: "Мовчазне порушення неприпустимо").

**Дата заморожування:** 2026-07-04  
**Версія:** 1.1 (Frozen)  
**Базис:** RC1 (тег `v0.9.0-observability-rc1`), Constitution v28, Architecture Review v1.0

---

## Статус документа

- **Версія:** 1.1 (Frozen)
- **Дата:** 2026-07-04
- **Базис:** RC1 (тег `v0.9.0-observability-rc1`), Constitution v28, Knowledge-Engine-Design-Review v1.0
- **Наступний крок:** погодження v1.1 → старт Slice 1 (Display)
