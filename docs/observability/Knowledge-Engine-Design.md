# Knowledge Engine — Design Specification

> **Специфікація Phase 6 SCLOC Observability Platform.**
>
> Цей документ — контрактом для майбутньої реалізації. Реєстрація слайсів (Phase 6
> Slices 1–5) виконується за цією специфікацією без повторного перепроєктування.
>
> Спирається на:
> - [`Observability-Constitution.md`](./Observability-Constitution.md) (Стаття 28 насамперед)
> - [`Observability-RC1-Release.md`](./Observability-RC1-Release.md) (фундамент RC1)
>
> **Статус:** Design (no code). После погодження — розбивка на Slices у Roadmap.

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
| 2 | **Append-Only History.** Будь-яка зміна залишає запис у `knowledge_version_history`. | Стаття 6 |
| 3 | **Version Aware.** Записи враховують версію релізу (`AffectedVersions`, `FixedVersion`) — знання може бути істинним для 1.0.0 і застарілим для 1.1.0. | Стаття 9 |
| 4 | **Fingerprint Driven.** Матчинг іде через `fingerprint_key` інциденту (Стаття 21), а не через INC-ID. Рецидив → одразу підказка. | Стаття 21 |
| 5 | **Zero Regression.** Knowledge Engine не модифікує існуючі підсистеми (Telemetry/Trace/Incident Engine/Workflow/Notification Engine). Тільки доповнення. | Стаття 12 |
| 6 | **Deterministic Matching.** Ніяких AI-евристик у першій версії — лише детерміновані правила пріоритету. | Дизайн v1 |
| 7 | **Privacy First.** PII-санітизація при введенні текстових полів. | Стаття 4 |
| 8 | **Free Tier Fit.** Схема мінімальна; жодних jsonb-масивів великого обсягу; індекси тільки необхідні. | Free Tier |

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
                          │  + knowledge_version_history (audit)    │
                          └────────────────┬────────────────────────┘
                                           │ Workflow: Draft → Reviewed → Verified
                                           ▼
┌─────────────────────┐         ┌────────────────────────────────────┐
│ Новий інцидент      │         │  knowledge_entries (Verified)      │
│ INC-YYYY-NNNNN      │         │  indexed by fingerprint_key        │
│                     │         │                                    │
│ fingerprint_key     │────────▶│  Knowledge Match Engine            │
│ component·op·signal │         │  (детерміновані правила пріоритету)│
│ ·release-segment    │         │                                    │
└─────────────────────┘         └────────────────┬───────────────────┘
                                                 │ match result
                                                 ▼
                          ┌─────────────────────────────────────────┐
                          │  Operator Hint:                         │
                          │  "Known Solution" у Incident Details    │
                          │  + підказка у Trace Explorer            │
                          │  + coverage у Release Health            │
                          └─────────────────────────────────────────┘
```

### Ізоляція від фундаменту RC1

| Фундамент RC1 | Knowledge Engine — чи торкається? |
|---|---|
| `telemetry_events` | НІ (лише читає `signal` через VIEWs) |
| `telemetry_incidents` | НІ (лише читає `fingerprint_key`, `component`, `signal`) |
| `incident_policy` | НІ |
| `notification_queue` / `notification_attempts` | НІ |
| Workflow функції (transition/note/owner) | НІ (нові функції додаються окремо) |
| `control_center.*` VIEWs | ДОПОВНЕННЯ (нові VIEWs, не зміна існуючих) |

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
| `CreatedBy` | text | Автор (Discord username / GitHub login / system identity) | ✅ |
| `CreatedAt` | timestamptz | Час створення | ✅ |
| `UpdatedBy` | text | Останній редактор | ✅ |
| `UpdatedAt` | timestamptz | Час останньої зміни | ✅ |

### Зв'язані сутності

#### KnowledgeReference (1:N)

| Поле | Тип | Призначення |
|---|---|---|
| `ReferenceId` | bigint | PK |
| `KnowledgeId` | bigint FK | Посилання на KnowledgeEntry |
| `ReferenceType` | enum | `GitCommit` / `GitHubIssue` / `Documentation` / `External` |
| `Url` | text | Повний URL (для клікабельності в UI) |
| `Label` | text | Напр., "commit abc1234", "Issue #42", "Wiki: install troubleshooting" |

Декілька посилань на один запис — звичайна ситуація (commit + issue + doc).

#### KnowledgeVersionHistory (append-only audit)

| Поле | Тип | Призначення |
|---|---|---|
| `VersionId` | bigint | PK |
| `KnowledgeId` | bigint FK | Зв'язок із записом |
| `Version` | int | Монотонний номер версії (1, 2, 3, ...) |
| `Snapshot` | jsonb | Повний snapshot запису на момент зміни |
| `ChangedBy` | text | Хто змінив |
| `ChangedAt` | timestamptz | Коли |
| `ChangeReason` | text | Короткий опис що й навіщо |

Створюється тригером на UPDATE/INSERT у `knowledge_entries`. Невидаляється.

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
        │            │ Verify
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
| Draft → Reviewed | `Publish` | будь-хто з правом Create | Заповнені `Title`, `KnownCause` |
| Draft → Archived | `Archive` | автор або адмін | без умов (видалення без тесту) |
| Reviewed → Verified | `Verify` | адмін або auto через release_health | `Confidence ≥ High` |
| Reviewed → Draft | `ReturnForRevision` | рецензент | коментар обов'язковий |
| Verified → Deprecated | `Deprecate` | адмін | `ChangeReason` обов'язковий |
| Deprecated → Reviewed | `Reopen` | адмін | новий аналіз описано |
| Deprecated → Archived | `Archive` | адмін | без умов |
| Verified → Reviewed | `ReopenForRevision` | адмін | якщо нове розслідування виявило неточність |

### Заборонені переходи

- ❌ `Archived → *` (будь-що). Архів фінальний.
- ❌ Будь-який → `Draft` окрім `Reviewed → Draft` (тільки рецензент повертає).
- ❌ `Verified → Verified` знизом Confidence (Verification — односторонній рух вгору через докази).

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
| `Verified` | `FixedVersion` встановлено, реліз опубліковано, `release_health` показує зникнення рецидивів ≥ 14 днів. | Позначка "Перевірено релізом" |

### Правила переходу Confidence

| З → У | Умова |
|---|---|
| (початкове) → Low | автоматично при створенні |
| Low → Medium | оператором вручну після перехресної перевірки |
| Medium → High | додано `PermanentFix` + мінімум 1 `GitCommit` reference |
| High → Verified | автоматично (job або тригер) при: `FixedVersion ≤ current_release` AND `release_health` для цього fingerprint без рецидивів ≥ 14 днів |
| Verified → High | вручну адміном, якщо нове розслідування ставить під сумнів (супроводжується `ChangeReason`) |

**Verification не робиться вручну.** Це автоматичне підвищення на основі
спостереження release_health. Дозволяє знанням "дозрівати" до високого ступеня
довіри без ручної роботи оператора.

---

## 7. Matching Policy

Детерміновані правила пріоритету для вибору Knowledge Entry для інциденту.
**Ніякого AI, embeddings, fuzzy matching у версії 1.**

### Алгоритм Match

```
Input:  incident.fingerprint_key, incident.component, incident.signal,
        incident.release, optional: exception_type/hresult

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
    ORDER BY UpdatedAt DESC
    LIMIT 1

    if FOUND → return (priority=2)

Step 3 (Priority 3 — Component + ExceptionType, cross-release):
    SELECT FROM knowledge_entries
    WHERE component = incident.component
      AND KnownCause ILIKE '%' || incident.exception_type || '%'
      AND status = 'Verified'
    ORDER BY UpdatedAt DESC
    LIMIT 1

    if FOUND → return (priority=3)

Step 4 (Priority 4 — Manual Search):
    return (no automatic match)
    оператор шукає через Knowledge Search сторінку
```

### Коли показувати підказку

| Пріоритет матчу | UI поведінка |
|---|---|
| 1 (Exact) | Зелений блок "Known Solution" з повним контуром (Cause / Fix / Workaround / References) |
| 2 (Component+Signal) | Жовтий блок "Можлива підказка" + позначка пріоритету |
| 3 (Exception) | Сірий блок "Подібний випадок" (деталі доступні, але розмита релевантність) |
| 4 (None) | Кнопка "Search Knowledge" (відкриває Knowledge Search з prefetch параметрами) |

### Деградація при конфлікті

Якщо на Priority 1 знайдено КІЛЬКА записів (різні реліз-сегменти одного fingerprint):
вибрати `AffectedVersions` що містить `incident.release`, інакше — найновіший за `UpdatedAt`.

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
│ Confidence: 🟢 Verified (Fixed in v1.0.2, no recurrences 21d)│
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

┌──────────────────────────────────────────────────┐
│ 💡 Подібна помилка має відому причину.            │
│    [Переглянути Known Solution →]                 │
└──────────────────────────────────────────────────┘
```

Лінк веде на окрему сторінку KnowledgeEntry або на пов'язаний інцидент.

### 8.3. Release Health (стр. `ReleaseHealth.razor`)

**Колонка Knowledge Coverage** у таблиці релізів:

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
(Publish/Verify/Deprecate/Archive) + історія версій.

---

## 9. Database (без SQL — лише опис)

### Таблиці (схема `public`)

#### `knowledge_entries`
Основна таблиця записів. Колонки згідно з §4. RLS:
- `cc_readonly` — SELECT
- `cc_knowledge_editor` (нова роль) — SELECT/INSERT/UPDATE
- `cc_notifier` — НІ (Worker не торкається знань)

#### `knowledge_references`
1:N посилання (Git commit / Issue / Doc / External). Каскадне видалення з
`knowledge_entries`. Ті ж права що й основна таблиця.

#### `knowledge_version_history`
Append-only аудит (§4). Тригер на `knowledge_entries` AFTER INSERT/UPDATE пише
snapshot. RLS: лише SELECT для всіх, ніяких INSERT вручну (тільки тригер).

### VIEWs (схема `control_center`)

#### `control_center.knowledge_list`
Список для сторінки Knowledge Management (з join references count + last activity).

#### `control_center.knowledge_matches`
VIEW що для кожного активного інциденту показує підібраний Knowledge Entry
за алгоритмом §7. Оновлюється тригером або materialized view (вибір у Slice 2).

#### `control_center.knowledge_coverage`
Per-release статистика coverage (для UI 8.3).

#### `control_center.knowledge_entry_detail`
Повна деталізація одного запису з aggregated references — для форми редагування
і для блоку "Known Solution".

### Індекси

- `knowledge_entries(fingerprint_hash)` — для Priority 1 match
- `knowledge_entries(component, signal)` WHERE status='Verified' — для Priority 2
- `knowledge_entries(status, updated_at DESC)` — для списку Knowledge Management
- `knowledge_references(knowledge_id)` — FK lookup

### Міграції (попереджувальний план)

- `00019_knowledge_engine_core.sql` — таблиці + тригер audit + базові VIEWs + RLS
- `00020_knowledge_role.sql` — нова роль `cc_knowledge_editor`
- `00021_knowledge_match_view.sql` — VIEW для матчингу + coverage
- (далі за потребою в Slice 2-5)

---

## 10. API

Без REST. Усі операції — через SECURITY DEFINER функції (як Workflow Стаття 23),
викликувані `cc_knowledge_editor`.

### Функції

| Функція | Дія | Параметри | Повертає |
|---|---|---|---|
| `create_knowledge_entry` | Створити Draft | fingerprint_key, title, known_cause, optional fields, created_by | new KnowledgeId |
| `update_knowledge_entry` | Редагувати з audit | knowledge_id, fields, changed_by, change_reason | void (snapshot у history) |
| `publish_knowledge_entry` | Draft → Reviewed | knowledge_id, changed_by | void |
| `verify_knowledge_entry` | Reviewed → Verified | knowledge_id, changed_by | void (вимагає Confidence ≥ High) |
| `deprecate_knowledge_entry` | Verified → Deprecated | knowledge_id, changed_by, change_reason | void |
| `archive_knowledge_entry` | * → Archived | knowledge_id, changed_by | void (заборонено з Archived) |
| `reopen_knowledge_entry` | Deprecated → Reviewed | knowledge_id, changed_by, change_reason | void |
| `add_knowledge_reference` | Додати посилання | knowledge_id, ref_type, url, label, added_by | reference_id |
| `remove_knowledge_reference` | Прибрати посилання | reference_id, removed_by | void |
| `search_knowledge` | Пошук для Manual Search | query, component?, status?, confidence? | set of (knowledge_id, title, status, confidence, relevance_rank) |
| `match_knowledge_for_incident` | Автоматичний матчинг | incident_id | (knowledge_id, priority, confidence) or null |

### Автоматизація

`verify_knowledge_auto()` — `pg_cron` job (раз на добу), що:
1. Шукає Knowledge Entries з `Status='Reviewed'` AND `Confidence='High'` AND `FixedVersion IS NOT NULL`.
2. Для кожного перевіряє `release_health`: чи `FixedVersion` вже реліз, чи рецидивів цього fingerprint нема ≥ 14 днів.
3. Якщо так — підвищує Confidence до `Verified` (з audit записом "auto-verified by release_health").

### Зворотна сумісність

Усі функції адитивні. Жодна не модифікує існуючі таблиці RC1.

---

## 11. Rollout Plan — Slices

Кожен слайс — тонкий вертикальний зріз: БД → код → UI → Runtime Verified → commit.
Дотримується принципу thin vertical slice (як RC1-RC6).

### Slice 1 — Display (мінімальна цінність)

**Мета:** показати Knowledge Entry у деталях інциденту, якщо вона існує.

| Що | Деталі |
|---|---|
| БД | `knowledge_entries` + `knowledge_references` + `knowledge_version_history` + тригер audit + VIEW `control_center.knowledge_entry_detail`. Без match VIEW. |
| Код | Repository-метод `GetKnownSolutionAsync(incidentId)` (точний match по fingerprint). Без Priority 2/3/4. |
| UI | Блок "Known Solution" у `Incidents.razor`. Без форми створення. |
| Дані для тесту | Вручну INSERT Knowledge Entry з fingerprint тестового інциденту. |
| Критерій готовності | Відкриття інциденту з існуючим Knowledge Entry показує блок; інцидент без нього показує "No known solution yet". |

**Немає:** створення/редагування/пошук/workflow. Усе через SQL для тесту.

### Slice 2 — Search

**Мета:** оператор може шукати знання вручну.

| Що | Деталі |
|---|---|
| БД | VIEW `control_center.knowledge_list` + функція `search_knowledge`. |
| Код | `IKnowledgeRepository` + `KnowledgeRepository`. |
| UI | Сторінка `Knowledge.razor` зі списком + фільтрами + пошуком. |
| Критерій | Пошук за keyword + filter по component/status/confidence працює. |

### Slice 3 — Workflow (Create / Update / Verify)

**Мета:** повний життєвий цикл через UI.

| Що | Деталі |
|---|---|
| БД | Усі SECURITY DEFINER функції §10. Нова роль `cc_knowledge_editor`. |
| Код | Сервіс Knowledge Workflow (викликає функції). |
| UI | Форма створення/редагування. Кнопки Publish/Verify/Deprecate/Archive на сторінці деталізації. Кнопка "Create Knowledge" у `Incidents.razor` (із prefetch fingerprint). |
| Критерій | Повний цикл Draft → Reviewed → Verified через UI, з audit у `knowledge_version_history`. |

### Slice 4 — Version History

**Мета:** показувати повну історію змін запису.

| Що | Деталі |
|---|---|
| БД | (вже є з Slice 1 — `knowledge_version_history`) |
| Код | `GetKnowledgeHistoryAsync(knowledgeId)`. |
| UI | Розділ "History" на сторінці деталізації: timeline версій з diff ключових полів. |
| Критерій | Будь-яка зміна запису з'являється в історії за ≤ 1 с. |

### Slice 5 — Release Integration

**Мета:** автоматичне підвищення Confidence до `Verified` через `release_health`.

| Що | Деталі |
|---|---|
| БД | `pg_cron` job з `verify_knowledge_auto()`. VIEW `control_center.knowledge_coverage`. |
| Код | (мінімум — переважно БД) |
| UI | Колонка Knowledge Coverage у Release Health. Позначка "Verified by release" у KnowledgeEntry. |
| Критерій | Після 14 днів без рецидивів + наявності FixedVersion — High автоматично стає Verified. |

### Пріоритети між слайсами

```
Slice 1 (Display)            — дає цінність навіть без UI створення
   ↓
Slice 3 (Workflow)           — замикає цикл "Investigation → Knowledge"
   ↓
Slice 2 (Search)             — оператор знаходить знання вручну
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

- **Auto-Draft:** LLM пропонує Draft Knowledge Entry з контексту інциденту
  (timeline + notes + forensic). Оператор затверджує або відхиляє.
  Не замінює ручне створення, лише прискорює.
- **Semantic Search:** embeddings `Symptoms + KnownCause` для пошуку подібних
  випадків навіть без точного fingerprint match.
- **Similarity Matching:** Priority 5+ у Matching Policy — fuzzy match з мінімальною
  відповідністю embedding-векторів.
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

### Чому НЕ в v1

- LLM-розширення потребують зовнішнього API → порушує Free Tier-фокус.
- Embeddings потребують pgvector або зовнішнього сховища → складність.
- Auto-draft ризикує породити шум → конфлікт зі Статтею 28 (підтверджене людиною).
- Будь-яке AI-розширення має спочатку довести цінність на реальних даних.

---

## Додаток A — Мапа статей → реалізація

| Стаття | Як реалізується в Knowledge Engine |
|---|---|
| 1 (Isolation) | Knowledge Engine не кидає винятки в бізнес-код; UI degrade gracefully при відсутності match |
| 4 (No PII) | Санітизація текстових полів при введенні (Symptoms/KnownCause/Workaround) |
| 6 (Append-Only) | `knowledge_version_history` — audit незмінний |
| 9 (Trace Recovery) | KnowledgeEntry пов'язується з fingerprint, через який відновлюється trace-контекст інциденту |
| 12 (Additive) | Усі нові таблиці/VIEWs/функції — без зміни RC1 |
| 17 (DB Validation) | Кожен слайс верифікується SQL-тестом згідно з `Database-Validation.md` |
| 18 (Production Pending) | Auto-verify через release_health отримає цей статус до появи реальних релізів |
| 21 (Incident Identity) | Матчинг через fingerprint_key, не INC-ID |
| 22 (Immutable History) | `knowledge_version_history` append-only |
| 23 (Workflow Access) | Усі переходи через SECURITY DEFINER функції, доступні `cc_knowledge_editor` |
| **28 (Knowledge Preservation)** | Реалізується повністю — ручне створення, матчинг, підказки |

---

## Додаток B — Контрольні питання перед стартом Slice 1

Перед реалізацією Slice 1 переконатися, що відповіді на ці питання не потребують
повторного дизайну:

- [ ] Якщо дві KnowledgeEntry мають однаковий fingerprint (різні AffectedVersions), яка виграє match? → §7 «Деградація при конфлікті»
- [ ] Що відбувається з KnowledgeEntry, коли інцидент переведено в Archived (Workflow), для якого вона була створена? → Запис незмінний (створено з fingerprint, не з INC-ID)
- [ ] Чи можна створити KnowledgeEntry без пов'язаного інциденту (для майбутніх проблем)? → Так, через Knowledge Management сторінку (Slice 3) з ручним fingerprint
- [ ] Хто має роль `cc_knowledge_editor`? → Спочатку — адміни; далі — будь-хто з правом Create Knowledge
- [ ] Якщо FixedVersion опубліковано, але рецидив стався — що відбувається з Confidence? → Позначається у список на Reopen, оператор отримує нотифікацію (можна в Slice 5 через `notification_queue` тип `KnowledgeInvalidated`)

---

## Статус документа

- **Версія:** 1.0 (Design)
- **Дата:** 2026-07-04
- **Базис:** RC1 (тег `v0.9.0-observability-rc1`), Конституція 28 статей
- **Наступний крок:** погодження специфікації → старт Slice 1
