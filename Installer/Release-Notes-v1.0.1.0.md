# SCLOC-Verse v1.0.1.0 — Stable Release

Перший офіційний production-реліз після v1.0.0.0. 160 комітів, 92 файли змінено.

---

## Нове

**Системний трей**
Додано іконку в системному треї на H.NotifyIcon.Wpf. Програма тепер може згортатися в трей при закритті вікна (X-кнопка = згорнути в трей, Alt+F4 = повний вихід).

**Автозапуск з Windows**
Додано можливість автоматичного запуску SCLOC-Verse після входу до Windows через реєстр HKCU Run-ключ.

**Чекбокси налаштувань**
Додано 4 чекбокси на екрані Налаштувань:
- Запускати з Windows
- Згортати в трей при закритті
- Автооновлення локалізації
- Розширена діагностика

**Кастомні підказки (ToolTip)**
Усі чекбокси налаштувань тепер мають інтерактивні підказки в єдиному стилі додатку (темна напівпрозора картка, синя рамка, плавна поява ~250 мс). Підказки з'являються праворуч від елемента, не перекриваючи його, з автоматичним переносом тексту.

**Windows Toast-сповіщення**
Додано інтеграцію з Windows Notification Center через CommunityToolkit.WinUI.Notifications. Сповіщення про оновлення локалізації, L.I.A та додатку тепер приходять як нативні Windows toasts.

**Оркестратор фонових оновлень**
Додано єдиний BackgroundUpdateOrchestrator з NotificationRouter — централізована диспетчеризація перевірок оновлень локалізації, L.I.A та додатку з маршрутизацією сповіщень.

**Один екземпляр додатку (Single Instance)**
Додано Mutex + Named Pipe IPC — повторний запуск додатку активує існуюче вікно замість створення нового процесу.

**Прогрес-індикатор оновлення додатку**
Додано немодальне вікно AppUpdateProgressWindow для відображення прогресу оновлення SCLOC-Verse, яке не блокує взаємодію з основним інтерфейсом.

**Брендована OAuth callback сторінка**
Додано кастомна HTML-сторінка для OAuth callback з брендованим логотипом, CSP, anti-cache headers та accessibility.

**Observability-платформа**
Впроваджено повну observability-платформу: 5 слайсів телеметрії (Application.Start, OAuth, Installation/Sync, Updater, L.I.A), автоматичний pipeline через тригери, інцидент-менеджмент (candidates detection, INC-ID, workflow, timeline, notes, owner), Knowledge Engine (6 слайсів: Known Solution Display, Authoring, Lifecycle, Workflow, Refinements, Release Integration).

---

## Покращення

**GitHub API Hardening**
Впроваджено єдину HTTP-політику GitHub API: retry з exponential backoff для тимчасових помилок (429/403 secondary/5xx), Conditional GET (ETag) для L.I.A, єдиний User-Agent у форматі GitHub-рекомендації. In-memory cache з TTL для метаданих releases.

**Архітектура телеметрії — Phase 3.5 (Telemetry Policy)**
Впроваджено багаторівневу політику збору телеметрії:
- `TelemetryLevel` enum (Mandatory / Diagnostic / Analytics) — кожна подія має явний рівень.
- 39 емітерів подій класифіковано за рівнями.
- 18 L2-емітерів позначено рівнем Diagnostic.
- Залежність полів від результату події (Outcome-dependent Field Policy): поля `error_message`, `exception_type`, `hresult` заповнюються лише для Failed-подій.

**Політика Zero Noise Telemetry**
Усі non-Failed події (Started, Succeeded, Cancelled, UpdateFound, Updated, UpdateAvailable) переведені з L1 Mandatory у L2 Diagnostic. При вимкненій розширеній діагностиці телеметрія зростає виключно при реальних помилках.

**Розширена діагностика — під контролем користувача**
Чекбокс «Розширена діагностика» тепер реально керує рівнем збору: при вимкненому стані L2-події (діагностичні) не надсилаються. За замовчуванням вимкнено.

**Швидша перша перевірка оновлень**
BackgroundUpdateMonitor тепер виконує першу перевірку оновлень при старті, а не чекає на перший тик таймера.

**Перевірка локалізації без встановлення**
Додано можливість перевіряти наявність оновлень локалізації без їх автоматичного встановлення. При вимкненому автооновленні користувач отримує сповіщення про доступне оновлення.

**Інтерактивні toast-сповіщення L.I.A**
Клік на toast-сповіщення L.I.A тепер відкриває вкладку асистента з актуальним статусом.

**Диспетчеризація кнопки L.I.A**
Запуск L.I.A відбувається лише при актуальній версії, з відповідними сповіщеннями про встановлення/оновлення.

**UI-політика відкладення промптів**
UI-промпти (діалоги, тости) відкладаються коли додаток запущено в `--minimized` режимі через IUiInteractionPolicy.

**Навігація між Canvas**
Виправлено race condition навігації між Canvas: guard, інваріант, SetActive після переходу.

---

## Виправлення

**L.I.A: CERT_E_UNTRUSTEDROOT (0x800B0109)**
Імпорт сертифікатів переведено на LocalMachine\Root+TrustedPeople — помилка 0x800B0109 усунута. Додано інфраструктуру elevation: RunPowerShellAsync.requireElevation з прозорим транспортом stdout/stderr через файли.

**L.I.A: кодування PowerShell output**
UNICODE-цілісність PowerShell output забезпечено (Стаття 17).

**Затримка закриття додатку**
Виправлено затримку при закритті: TelemetryClient.Dispose тепер використовує fire-and-forget замість Wait(5s), що усунуло блокування UI під час shutdown.

**P0 параліч Observability**
Release _flushLock у finally — усунуто P0 параліч Observability при concurrent flush.

**Обробка 304 Not Modified від GitHub API**
TryGetLatestReleaseAsync тепер коректно повертає null при 304 (ETag без змін) замість неповного об'єкта GitHubRelease.

**Діалог оновлення у фоновому режимі**
Діалог оновлення більше не намагається відкритися коли головне вікно приховано або не здатне показати модальний діалог (CanShowModalDialogs gate).

**Пошук гри: StarCitizen визнається грою лише за наявності LIVE/PTU/EPTU/HOTFX**
Виправлено логіку визначення встановленої гри Star Citizen.

**Кнопки повернення: race condition**
Виправлено name scope кнопок повернення та додано неонову стрілку з плавним диханням та OuterGlow.

---

## Оптимізація

**Індекси бази даних**
Додано partial-індекс для Failed-подій (найчастіший запит інцидентів), що скорочує час пошуку помилок.

**Код інцидентів автоматично**
incident_code (INC-YYYY-NNNNN) тепер формується тригером set_incident_code() при insert/update, замість формування в 4 views + Notifier.

**Видалення дублів індексів**
Прибрано 2 індекси-дублікати (idx_app_installations_install_id, idx_user_discord_guilds_user_id) — additive-only, без порушення контракту.

---

## База даних

**3 міграції застосовано до production (Phase 3A, 2026-07-07):**

1. `20260708030001_phase3a_add_partial_indexes.sql` — partial-індекс для Failed-подій
2. `20260708030002_phase3a_incident_code_generated.sql` — тригер set_incident_code() (замість generated column — PostgreSQL вимагає IMMUTABLE для generated, а EXTRACT з timestamptz — STABLE)
3. `20260708030003_phase3a_remove_duplicate_indexes.sql` — видалення 2 дублів-індексів

Усі міграції additive-only, з rollback-скриптами, перевірені на production-replica до deployment.

---

## Телеметрія

**Zero Noise Telemetry Policy**

Тепер статистика збирається лише тоді, коли вона справді необхідна. Розширена діагностика надсилається лише після увімкнення відповідної опції користувачем.

- **L1 Mandatory** — відправляється завжди (навіть при вимкненому чекбоксі): лише Failed-події (13 типів) + інциденти + черга сповіщень.
- **L2 Diagnostic** — лише при ввімкненому «Розширеній діагностиці»: усі non-Failed події, деталі помилок (duration_ms, detail, hresult, exception_type), LIA forensic payload.
- **Не в telemetry_events** (живе в інших таблицях): user identity в auth.users, installation activity в app_installations, adoption metrics.

Зверніть увагу: при успішному запуску з вимкненою розширеною діагностикою жодна телеметрійна подія не відправляється. Таблиця телеметрії зростає виключно при реальних помилках.

---

## Завантаження

- **SCLOC-Verse_Setup.exe** — інсталятор (Inno Setup, українською мовою)
- SHA256 обчислюється після білду

---

**Повна версія:** 1.0.1.0  
**Дата релізу:** 2026-07-07  
**Автори:** VALDEUS (Vova-Bob), AlexLiberty (Alexuß)  
**Сайт:** github.com/Vova-Bob/SCLOC-Verse