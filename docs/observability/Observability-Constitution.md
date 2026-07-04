# SCLOC Observability Platform — Constitution

> **Найвищий авторитет** для будь-якого коду спостережуваності SCLOC-Verse.
> Статті нижче незмінні. Вони мають пріоритет над зручністю, дедлайнами та «кращими практиками».
> Будь-яка зміна вимагає явного перегляду цього документу.

**Супровідні документи:**
- [`Observability-Architecture.md`](./Observability-Architecture.md) — технічна реалізація.
- [`Observability-Roadmap.md`](./Observability-Roadmap.md) — поетапне впровадження.

**Походження.** Платформа створена після production-інцидентів (`42501` permission denied на `app_installations`; `0x800B0109` під час встановлення L.I.A), які вимагали багатогодинного ручного форензику. Мета — зробити так, щоб подібні проблеми знаходились автоматично за хвилини, а не години.

---

## Як читати цей документ

Кожна стаття містить три частини:

- **Твердження** — сама незмінна норма.
- **Точне значення** — що саме заборонено/дозволено, без двозначностей.
- **Забезпечення** — механізм, що гарантує виконання (RLS, типи, ревью, інваріант на рівні схеми). Конституція без примусу — це побажання.

---

## Стаття 1 — Absolute Isolation

> Телеметрія ніколи не впливає на роботу SCLOC-Verse. Будь-яка її помилка повністю ізольована.

**Точне значення.** Жоден публічний метод платформи не має права прокинути виняток у бізнес-код. `ITelemetryService.Track(...)` повертає `void` і ніколи не кидає.

**Забезпечення.** Реалізація огортає **кожен** публічний метод у `try/catch` → fallback `Debug.WriteLine`. На ревью: жодного `throw` у публічній поверхні платформи.

## Стаття 2 — Never Block the UI

> Телеметрія не блокує UI. Жодного `await` на UI-потоку.

**Точне значення.** Гарячий шлях `Track(...)` — синхронний, O(1), лише кладе в неблокуючу чергу. Усі I/O — у фоновому потоці з `ConfigureAwait(false)`. Жодного маршалінгу на dispatcher.

**Забезпечення.** Сигнатура `void Track(...)` (не `Task`); uploader ніколи не чекає результату на потоці викликача.

## Стаття 3 — Never Break Startup

> Телеметрія ніколи не ламає запуск, навіть коли Supabase недоступний.

**Точне значення.** Конструювання `TelemetryClient` — дешеве й синхронне. Усі мережеві виклики (feature-flag, flush) — відкладені або lazy. Startup не `await` телеметрію. Бекенд недоступний → додаток іде далі (локальний збір або no-op).

**Забезпечення.** Жоден телеметрія-виклик не стоїть у критичному шляху `MainWindow_Loaded`, що гейтить UI.

## Стаття 4 — Zero PII

> Жоден event не містить персональних даних.

**Точне значення.** Заборонені у payload: токени/JWT, access/refresh tokens, email, username, Discord-дані, IP-адреси, локальні шляхи (`C:\Users\<name>\...`), `Environment.MachineName`, MAC/серійники. Stack-trace санітариться **до** потрапляння в чергу, а не «на сервері».

**Забезпечення.** `PrivacySanitizer` — **єдине вузьке горло**: жоден шлях у чергу не обходить його. Тести з відомими PII-патернами дають sanitized output. `attributes` — лише через allowlist.

## Стаття 5 — Append-Only

> Історичні події ніколи не редагуються.

**Точне значення.** На `telemetry_events` дозволено лише `INSERT`. `UPDATE` заборонено. `DELETE` дозволений **лише** для retention-purge (видалення за часом, не редагування змісту).

**Забезпечення.** RLS: authenticated отримує **тільки INSERT**; `UPDATE`/`DELETE` не грантуються жодній прикладній ролі (окрім service_role для retention). У клієнті фізично відсутнє API «оновити подію».

## Стаття 6 — Idempotent Retries

> Будь-яку подію можна відправити повторно без дублювання.

**Точне значення.** Кожна подія несе клієнтський `client_event_id` (GUID). Повторна відправка того самого event — це no-op.

**Забезпечення.** `UNIQUE(client_event_id)` + `ON CONFLICT (client_event_id) DO NOTHING`. Uploader завжди використовує upsert-no-conflict.

## Стаття 7 — Single Sanctioned Sink

> Усі нові компоненти SCLOC-Verse використовують лише `ITelemetryService`. Ніяких власних логерів.

**Точне значення.** `ITelemetryService` — єдиний дозволений канал спостережуваності. `Debug.WriteLine` — лише debug (стрипається в Release), не замінник. Існуючий `InputDiagnostics` — grandfathered (тимчасовий, hotkey-scoped); нових ad-hoc логерів не створювати.

**Забезпечення.** Архітектурний ревью; `ITelemetryService` інжектиться через `AppCompositionRoot` там, де потрібно.

## Стаття 8 — Critical Failures Auto-Captured

> Будь-який Critical Failure автоматично потрапляє в телеметрію — без покладання на дисципліну розробника.

**Точне значення.** Глобальні хендлери винятків (`DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`) перехоплюють усе, що втекло з обробників. Це страхувальна сітка, що не залежить від того, чи написав розробник `catch`.

**Забезпечення.** Хендлери реєструються в `App.OnStartup` (Phase 2) і **не можуть бути прибрані** без перегляду цієї Конституції.

## Стаття 9 — Incidents Forensable From Platform Alone

> Будь-який production-інцидент відновлюється виключно по телеметрії. Без ручного збору логів у користувачів.

**Точне значення.** Trace + evidence-snapshot + rollup мають бути достатніми для root cause. **Якщо довелося просити користувача надіслати лог — платформа не виконала своє призначення.** Це планка якості, а не лише технічний інваріант.

**Забезпечення.** `telemetry_incidents.evidence` обовʼязково містить trace + signal + семпл-повідомлення, достатні для діагностики без контакту з користувачем.

## Стаття 10 — Kill-Switch Transparent

> Якщо телеметрію вимкнути — SCLOC-Verse працює абсолютно так само.

**Точне значення.** Телеметрія — чистий спостерігач. Вимкнення (remote flag / setting / env / Telemetry Level 0) змінює лише збір даних і нічого більше. Жодна фіча не залежить від успіху телеметрії.

**Забезпечення.** Бізнес-логіка ніколи не розгалужується на результат телеметрії; збій телеметрії = мовчазний no-op.

## Стаття 11 — Trace Mandatory

> Кожна подія обовʼязково несе `correlation_id` та `step`. Без trace подія не існує.

**Точне значення.** `correlation_id` (trace) та `step` (порядок у trace) — `NOT NULL`. Trace — головна одиниця аналізу: дозволяє відновити повний життєвий цикл запуску (Launch → OAuth → Installation → Localization → Updater → LIA).

**Забезпечення.** Схема: `correlation_id uuid NOT NULL`, `step int NOT NULL`. Клієнт тримає `correlation_id` як ambient-контекст з моменту `Application.Start`.

## Стаття 12 — Single Ingestion Contract

> Усі компоненти надсилають події виключно через `ITelemetryService`. Реалізація може зберігати їх у будь-якому сховищі.

**Точне значення.** Заборонено **обходити сервіс**: прямі вставки в БД поза ним, власні HTTP-клієнти телеметрії, сторонні логери як канал спостережуваності. Сам сервіс може еволюціонувати у сховищі: зараз — `telemetry_events` (Supabase); у майбутньому — crash dump storage, performance metrics, audit trail, локальний offline cache, OpenTelemetry, Sentry, Windows Event Log.

> Конституція фіксує **єдину точку входу**, а не конкретне сховище. Це дозволяє міняти backend без переписування Конституції.

**Забезпечення.** Архітектурний ревью; `ITelemetryService` — єдиний інʼєктований канал. Новий ad-hoc канал спостережуваності = порушення.

## Стаття 13 — Additive-Only Schema

> Еволюція схеми — лише додаванням. Зміна семантики існуючих колонок заборонена.

**Точне значення.**
- ✅ Дозволено: додати nullable-колонку з DEFAULT; додати значення в CHECK-enum; додати VIEW/INDEX; підняти `telemetry_version` у клієнті.
- ❌ Заборонено: змінити семантику колонки (замість цього — нова колонка, стара deprecated); зробити колонку NOT NULL без backfill; перейменувати/видалити колонку, яку ще пишуть active-клієнти; змінити PK/RLS-контракт (`correlation_id`, `step`, `outcome`).

Deprecated-колонки прибираються лише коли dashboard показує `0%` подій з `telemetry_version < N`.

**Забезпечення.** Міграційна дисципліна; перевірка distribution за `telemetry_version` перед чищенням.

## Стаття 14 — Offline First

> Платформа працює без Інтернету. Відсутність мережі не втрачає Critical події.

**Точне значення.** Події ставляться у локальну чергу; після відновлення мережі відправляються автоматично; `CrashReporter` пише локально навіть при crash без мережі. Жодної втрати даних через тимчасовий offline.

**Забезпечення.** Bounded JSONL-черга (`%LOCALAPPDATA%\SCLOCVerse\observability\queue.jsonl`, UTF-8, lock); flush-on-connect; локальний crash-файл (синхронний запис перед завершенням процесу).

## Стаття 15 — Observable by Default

> Будь-яка нова функція не вважається завершеною, доки не має мінімального набору подій.

**Точне значення.** Definition-of-Done кожної фічі включає мінімум спостережуваності:

```
Start
Success
Failure
Duration
```

Інакше через рік форензик знову буде неможливим — саме та прогалина, що призвела до створення цієї платформи.

**Забезпечення.** DoD-чеклист у ревью; новий сервіс/канвас без подій = не приймається до merge.

## Стаття 16 — Backward Compatibility

> Новий сервер приймає події від старих клієнтів. Старий клієнт продовжує працювати після оновлення сервера.

**Точне значення.** Auto-update не миттєвий — одночасно живуть кілька `telemetry_version`. Сервер ніколи не відхиляє за версією; клієнт толерантний до невідомих полів відповіді.

**Забезпечення.** Серверна валідація — лише за структурою, не за `telemetry_version`; CHECK-інваріанти additive (Стаття 13); клієнт ігнорує невідомі поля. Це забезпечує життя системи багато років.

---

## Стаття 17 — Production Database Verification

> Жодна нова таблиця або RLS policy не вважається завершеною, поки не пройшла повний runtime verification під реальною роллю `authenticated` (не `service_role`).

**Точне значення.** Компіляція + SQL-наявність — НЕ є Done. Done = доведено живою системою. Інцидент `42501` траплявся двічі (`app_installations`, `telemetry_events`) і обидва рази ховався в GRANT/RLS/RETURNING, а не в коді. Тому верифікація — окрема обов'язкова стадія Definition of Done, а не «бажано».

**Забезпечення.** Кожна нова таблиця проходить універсальний чеклист [`docs/checklists/Database-Verification.md`](../checklists/Database-Verification.md) під реальною роллю `authenticated`:

```
□ RLS enabled
□ authenticated INSERT
□ authenticated SELECT
□ authenticated UPDATE (якщо потрібно)
□ authenticated DELETE (якщо потрібно)
□ anon DENY (INSERT/SELECT/UPDATE/DELETE)
□ чужий SELECT → невидимі (count = 0)
□ чужий INSERT/UPDATE/DELETE → BLOCKED
□ RETURNING працює (SDK return=representation)
□ View працює
□ Control Center (cc_readonly) бачить дані
```

Поки пункт не закритий живим доказом — таблиця вважається невпровадженою.

---

## Стаття 18 — Production Pending

> Сценарії, що неможливо природно відтворити без штучного втручання (новий реліз, аварія, збій сервісу, відокремлений процес), допускають статус **Production Pending**: код готовий до production, але очікує першого природного виконання для остаточного Runtime Verification.

**Точне значення.** Не кожен шлях можна runtime-перевірити за волею розробника (напр. `Updater/Download|Verify|Install` потребує доступного оновлення; відокремлений post-shutdown інсталятор — завершення процесу). Для таких — статус **Production Pending** замість Runtime Verified. Це НЕ означає «недоведено»: Code/Build/Architecture доведені; відсутнє лише живе виконання, що залежить від реальних production-подій.

**Забезпечення.** Слайз із Production Pending фіксується у Roadmap із причиною. При першому природному виконанні (без зміни коду) статус підвищується до Runtime Verified, якщо подія зʼявилась у Control Center з очікуваними полями. **Заборонено:** створювати фейкові релізи/аварії/тимчасовий код виключно заради тесту.

> Ця стаття — вузький виняток, що не послаблює загальну дисципліну Code → Build → Runtime → Commit (Стаття 15/17), а чесно позначає єдиний клас сценаріїв, де runtime залежить від production, а не від розробника.

---

## Стаття 19 — Promotion Immutability

> Promotion (детекція інцидентів) тільки ЧИТАЄ `telemetry_events` і НІКОЛИ їх не змінює.

**Точне значення.** Функція `promote_incident_candidates()` та будь-яка майбутня логіка інцидентів виконує `SELECT` з `telemetry_events` і `INSERT/UPDATE` лише в `telemetry_incidents` (та похідні таблиці стану). `telemetry_events` — append-only (Стаття 5); ніхто й ніколи не модифікує історичні події, окрім retention-purge (Стаття 5).

**Забезпечення.** `promote_incident_candidates()` — `SECURITY DEFINER`, викликається лише `service_role`/`pg_cron`. `cc_readonly` не має права її викликати. `authenticated`/`anon` не мають права писати в `telemetry_incidents`.

---

## Стаття 20 — Dashboard Purity

> Control Center (Dashboard) тільки ЧИТАЄ готові VIEW. Не містить бізнес-логіки.

**Точне значення.** Усі рішення (severity, status, health GREEN/YELLOW/RED, failure-rate, affected%) обчислюються в SQL VIEWs. Dashboard — це буквально `SELECT * FROM control_center.<view>`. Жодних обчислень клієнтською мовою (Blazor/C#). Жодних side-effect (ніякого виклику функцій, що пишуть).

**Забезпечення.** `cc_readonly` має лише `SELECT` на об'єктах схеми `control_center`. Немає `EXECUTE` на функціях, що пишуть. Немає `INSERT/UPDATE/DELETE` ні на що. Якщо Dashboard потребує нового рішення — додається новий VIEW, а не клієнтська логіка.

---

## Стаття 21 — Incident Identity

> Інцидент визначається лише своїм `incident_id`. Fingerprint — механізм детекції, а не ідентичність.

**Точне значення.** Два інциденти з однаковим fingerprint — **різні інциденти** (різні `incident_id`, різні `opened_at`). Fingerprint групує події для детекції; `incident_id` ідентифікує конкретний випадок проблеми. Рецидив після Closed = **новий інцидент** (новий `incident_id`), а не повторне відкриття закритого.

**Забезпечення.** Promotion-функція перевіряє відкриті інциденти за `fingerprint_key`; якщо немає відкритого — створює новий з новим `id`. Закритий інцидент ніколи не «відкривається знову» — створюється новий.

---

## Стаття 22 — Incident History Is Immutable

> Будь-яка зміна стану інциденту не перезаписує попередній стан, а створює новий запис у журналі подій.

**Точне значення.** `telemetry_incidents.status` — поточний стан (кеш для швидкого читання). Кожен перехід (Detected→Confirmed→Investigating→Mitigated→Resolved→Closed) фіксується в `incident_status_log` (append-only, Стаття 5). Журнал — джерело правди; status — зручність. Історія переходів ніколи не видаляється й не перезаписується.

**Забезпечення.** `transition_incident()` — SECURITY DEFINER функція: перевіряє валідність переходу (forward-only), UPDATE status (кеш) + INSERT у журнал (immutable). Пряме UPDATE `telemetry_incidents.status` мимо функції заборонено RLS.

---

## Стаття 23 — Incident Workflow Access

> Dashboard змінює стан інцидентів лише через SECURITY DEFINER функції. Прямі table-writes заборонені.

**Точне значення.** Поправка до Статті 20 (Dashboard Purity): `cc_readonly` отримує `EXECUTE` на `transition_incident()`, `add_incident_note()`, `assign_incident_owner()` — контрольований write-path для workflow. Усі інші writes заборонені. Ці функції — єдиний спосіб зміни стану/нотаток/власника з Dashboard.

**Забезпечення.** GRANT EXECUTE лише на ці три функції. RLS deny-all на `incident_status_log` / `incident_notes` (прямі writes заборонені). Функції SECURITY DEFINER (runs as postgres, bypasses RLS). VIEWs `control_center.incident_timeline` / `incident_notes` — READ ONLY через cc_readonly.

---

## Стаття 24 — Notification Independence

> Невдала доставка будь-якого повідомлення ніколи не впливає на створення, оновлення, життєвий цикл або закриття інциденту.

**Точне значення.** Incident Engine не знає про Discord/Email/Telegram. Promotion-функція створює інцидент і (опційно) записує в `notification_queue` — але НЕ відправляє повідомлення напряму. Відправка — окремий шар (Notification Dispatcher). Timeout/відмова Discord не змінює статус інциденту. Усі помилки Notification Engine ізольовані (Стаття 1 — Absolute Isolation).

**Забезпечення.** `promote_incident_candidates()` пише лише в `telemetry_incidents` + `notification_queue` (append-only). Dispatcher читає queue, відправляє, оновлює статус доставки. Incident Engine ніколи не чекає результату відправки.

---

## Стаття 25 — Notification Idempotency

> Одне й те саме повідомлення не можна відправити двічі.

**Точне значення.** Якщо `promote_incident_candidates()` викликається 10 разів (pg_cron 5 хв), інцидент створюється один раз (Стаття 21), і повідомлення про його створення відправляється **один раз**. Дедуплікація — через `notification_queue.incident_id` + `notification_type` UNIQUE.

**Забезпечення.** `notification_queue` має `UNIQUE(incident_id, notification_type) WHERE status != 'Failed'`. Dispatcher відправляє лише рядки зі `status = 'Pending'`. Після відправки — `status = 'Delivered'` (або `Failed` + retry_count).

---

## Стаття 26 — Delivery Audit

> Жодна спроба доставки не може зникнути без сліду.

**Точне значення.** Кожна спроба відправки повідомлення провайдеру залишає окремий append-only запис у `notification_attempts` незалежно від результату: успіх, помилка, timeout, відсутність провайдера — усе фіксується.

**Перехід станів черги строго обмежений:**

```
Pending → Sending → Delivered
                   ↘ Failed (після вичерпання max_retries)
                   ↘ RetryScheduled (з next_attempt_at)

RetryScheduled → Sending → ...

Sending → (zombie > 10 хв) → RetryScheduled
```

Заборонено будь-які інші переходи. Зокрема, заборонено `Pending → (зникло)`.

**Забезпечення.**
- `notification_attempts` — append-only таблиця (Стаття 6 стиль) з FK на `notification_queue`, RLS deny-all.
- CHECK-обмеження на `notification_queue.status`: `('Pending','Sending','Delivered','Failed','RetryScheduled')`.
- CHECK-обмеження на `notification_attempts.status`: `('Sending','Delivered','Failed')`.
- Zombie Recovery: диспетчер на старті кожного циклу переводить елементи у `Sending` довше за `ZombieTimeoutMinutes` назад у `RetryScheduled` — щоб краш Worker не породжував "вічних Sending".
- Колонки `claimed_at` + `claimed_by` (machine:pid) фіксують власника кожної обробки.
- Колонка `provider_message_id` дозволяє зіставити запис у БД з реальним повідомленням у зовнішній системі (Discord message id, Email message-id тощо).

---

## Стаття 27 — Provider Independence

> Notification Engine не знає про конкретні канали. Він знає лише `INotificationProvider`.

**Точне значення.** Диспетчер оперує контрактом `INotificationProvider` з контрактної бібліотеки `SCLOCVerse.Notifications`. Discord/Email/Telegram — реалізації цього інтерфейсу, що реєструються в DI як окремі класи. Додавання нового каналу не потребує змін у диспетчері чи черзі.

**Забезпечення.**
- Контракти (`INotificationProvider`, `NotificationPayload`, `NotificationResult`, `NotificationChannel`) живуть у **окремій** бібліотеці `SCLOCVerse.Notifications`, відокремленій і від Worker, і від Control Center.
- Worker (`SCLOCVerse.Notifier`) посилається на контрактну бібліотеку.
- Control Center не має жодного коду сповіщень (окрім читання `control_center.notifications` VIEW для адмін-панелі).
- Назви каналів — константи `NotificationChannel.Discord` тощо, не "магічні рядки".
- `NotificationPayload` версіонований (`version` у jsonb) — зміна формату не ламає існуючі записи в черзі.
- Webhook URL/облікові дані провайдерів зберігаються **тільки** у user-secrets / environment variables, ніколи в БД.

**Переваги.** Будь-який шар (Worker, UI, тести) може працювати з однаковими типами, не отримуючи залежності від конкретного каналу чи движка відправки.

---

## Стаття 28 — Knowledge Preservation

> Кожен інцидент, причина якого була встановлена та підтверджена, повинен мати можливість бути перетвореним у повторно використовуване знання. Knowledge Engine не створює знання автоматично; кожен запис є результатом підтвердженого людського аналізу.

**Точне значення.** Спостережуваність без пам'яті перетворюється на безкінечне повторне дослідження тих самих симптомів. Коли розробник встановив справжню причину інциденту (через Workflow Стаття 23), цей висновок має стати матеріальною цінністю платформи: наступний інцидент з тим самим `fingerprint_key` (Стаття 21) має отримати автоматичну підказку про відому причину та відоме виправлення.

**Обов'язкові властивості:**

1. **Людське походження.** Knowledge Engine не генерує записи автоматично з телеметрії чи LLM. Створення запису — явна дія розробника у Workflow інциденту (`Investigating` → `Resolved`, дія **Create Knowledge**). Без підтвердженого аналізу запису не існує.
2. **Зв'язок через fingerprint.** Knowledge Entry прив'язується до `fingerprint_key` інциденту (component · operation · signal · release-segment), а не до конкретного `INC-ID`. Той самий рецидив (Стаття 21 — новий INC-ID, але той самий fingerprint) одразу отримує підказку.
3. **Перевага над новим INC-ID.** При відкритті нового інциденту движок перевіряє наявність Knowledge Entry для його fingerprint. Якщо існує — підказка показується на сторінці деталей інциденту (Known Solution: причина · обхідне рішення · постійне виправлення · посилання).
4. **Confidence-шкала.** Кожен запис має градацію довіри: `Low` → `Medium` → `High` → `Verified`. Лише `Verified` розглядається як остаточна відповідь; нижчі рівні супроводжують підказку застереженням.
5. **Версійність та правки.** Запис допускає редагування (саме редагування, не видалення) — нове розуміння причини замінює застаріле, зі збереженням аудиту (хто й коли змінив).
6. **Зворотний зв'язок із Release Health.** Knowledge Entry фіксує `Fixed In` (версія). Після релізу цієї версії та зникнення рецидивів confidence може бути підвищено до `Verified` за результатами спостереження release_health (Стаття 9).

**Забезпечення.** Knowledge Engine реалізується у Phase 6 як окремий шар поверх завершеного фундаменту (Telemetry + Trace + Incident Engine + Workflow + Notification Engine). Не замінює жодної існуючої підсистеми; лише доповнює деталі інциденту підказкою. Без перетворення на джерело правди — правдою залишається телеметрія.

**Чому не автоматично.** Автогенерація знань з telemetry/LLM породжує шум: непідтверджені гіпотези, що маскуються під факти. Знання корисне лише тоді, коли воно **підтверджене людиною**, яка провела аналіз. Якість важніша за кількість.

---

## Стаття 29 — Secret Independence

> Єдиний безпечний секрет у репозиторії — відсутній.

**Точне значення.** Репозиторій є публічним артефактом з моменту першого push. Будь-який секрет, що потрапив у git-історію, вважається скомпрометованим назавжди (навіть перепис історії не гарантує знищення, якщо коміт уже опинився за межами локального репозиторію).

Жоден пароль, токен, webhook URL, ключ API, OAuth Client Secret, SMTP-credential або інший секрет не може зберігатися в репозиторії — незалежно від типу файлу: код, SQL, міграції, документація, конфігурація, скрипти релізу.

**Тришарова модель.**

1. **Git — абсолютна заборона.** Заборонено: паролі, connection strings із паролями, webhook URL, `service_role`, OAuth Client Secret, SMTP-credentials, API-ключі, сертифікати (`*.pfx`, `*.cer`, `*.crt`, `*.pem`, `*.key`), `.env*`-файли, `secrets.json`, локальні бази (`*.db`, `*.sqlite`). `.gitignore` підтримує цю заборону механічно, але основний захист — політика, а не шаблон.

2. **База даних — пароль як властивість ролі, що встановлюється поза репозиторієм.** Міграції створюють ролі без `PASSWORD`. Паролі встановлюються окремо через захищений адміністративний процес (Supabase Dashboard, secure channel, deployment script із секретом у середовищі). Міграція є еталоном схеми та прав; пароль — операційною властивістю середовища.

3. **Додаток — тільки захищені канали конфігурації.** Будь-який компонент (Worker, UI, сервіс) отримує секрети виключно через:
   - Environment Variables (`SCLOC_*`);
   - .NET User Secrets (Development);
   - CI/CD Secrets (GitHub Actions, deployment pipeline);
   - Secret Manager (якщо впроваджено).

   Значення за замовчуванням у коді допускаються лише для публічних за дизайном токенів (наприклад, Supabase `anon_key` / publishable key) за умови, що RLS захищає дані від анонімного доступу (див. також Стаття 4 — Zero PII).

**Забезпечення.**
- Міграції створюють ролі без `PASSWORD`; паролі встановлюються окремо через захищений адміністративний процес.
- `appsettings*.json` містять порожні значення (`""`) для секретів; реальні значення — user-secrets / env.
- Webhook URL, connection strings з паролями, `service_role` ніколи не фігурують у коді, міграціях чи `docs/`.
- Build Audit перевіряє `publish/` output на наявність секретів перед релізом.
- Порушення цієї статті — P0-дефект, що вимагає негайної ротації секрету та перепису історії (якщо коміт ще не публічний) або ротації + прийняття факту компрометації (якщо публічний).

**Переваги.** Репозиторій можна оприлюднити, форкнути, передати новому розробнику або завантажити у CI без жодної ротації. Середовища (Dev/Staging/Production) відрізняються лише секретами у конфігурації, а не гілками чи fork'ами.

---

## Telemetry Levels

Один feature-flag `telemetry.level` (0–4) керує обсягом даних. **Головне правило: жоден рівень не відключає детекцію інцидентів** — навіть Level 1 зберігає critical-path failures та їхні знаменники.

| Level | Назва | Що збирається | Призначення |
|---|---|---|---|
| **0** | Disabled | нічого | Повне вимкнення (Стаття 10) |
| **1** | Critical | Лише critical-path lifecycle: Application, Auth, Installation, LIA, Updater, Crash (повний Start/Success/Failure) | Мінімум для детекції інцидентів + release_health критичних шляхів |
| **2** | Operational | + Localization та інший Operational | **Default.** Повний lifecycle-observability без діагностики |
| **3** | Diagnostics | + Diagnostic (sampled: network, sub-steps) | Розширений форензик |
| **4** | Verbose | + Verbose (високочастотне, без sampling) | Лише активний debug |

**Default: Level 2 (Operational).** Дає детекцію + знаменники success-rate при мінімумі обсягу — оптимально для Supabase Free Tier.

**Free-Tier тиск:** якщо проєкт упирається в ліміти — опустити рівень одним `UPDATE` (напр., 2→1). Зворотно — підняти без релізу.

---

## Як змінювати Конституцію

1. Конституція змінюється лише через явний Pull Request, що оновлює цей файл.
2. Будь-яка зміна статей 1–25 вимагає позначки **Breaking Constitutional Change** у PR.
3. Telemetry Levels є конфігурацією, а не статтею — можуть доповнюватись без перегляду статей.
4. Якщо реалізація змушена порушити статтю — це сигнал, що треба або змінити реалізацію, або свідомо переглянути Конституцію. Мовчазне порушення неприпустиме.
