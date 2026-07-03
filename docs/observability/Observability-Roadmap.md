# SCLOC Observability Platform — Roadmap

> Поетапне впровадження платформи спостережуваності SCLOC-Verse.
> Спирається на [`Observability-Constitution.md`](./Observability-Constitution.md) (правила)
> та [`Observability-Architecture.md`](./Observability-Architecture.md) (реалізація).

**Принцип розгортання:** кожна фаза — самодостатня цінність. Ніколи не будуємо «всю платформу одразу» (Стаття KISS). Після кожної фази — фундамент стабільний і не переробляється наступною.

---

## STATUS (live)

- **Slice 1 — Minimal Client + Trace (`Application.Start`): ✅ Runtime Verified (2026-07-03).** Реальна подія зʼявилась у `control_center.traces`; пройшла повний конвеєр клієнт→Supabase→VIEW. DB-пів доведено повністю (RLS/GRANT/authenticated/anon/RETURNING/View/cc_readonly). У процесі виявлено+виправлено другий `42501` (відсутній `GRANT SELECT` для `RETURNING`).
- **Slice 2 — OAuth: ✅ Runtime Verified.** `Auth/SignIn` + `Auth/RestoreSession`. Побудовано універсальний `ErrorContextExtractor` (source/http_status/supabase_code/hresult через інспекцію типу, не текст).
- **Slice 3 — Installation: ✅ Runtime Verified.** `Installation/Sync` Started/Succeeded/Failed + `detail.phase`. Саме ця подія зробила б `42501` видимим за хвилини.
- **Slice 4 — Updater: ⏳ Production Pending (Стаття 18).** Code ✅ Build ✅ Architecture ✅. `Updater/Download|Verify|Install`. Runtime-перевірка неможлива без реального оновлення (фейковий реліз не створюємо) → автопідвищення до Runtime Verified на першому природному update-флоу.

### Легенда статусів

| Статус | Значення |
|---|---|
| ✅ Runtime Verified | живе виконання доведено (подія у `control_center`) |
| ⏳ Production Pending | Code/Build/Architecture доведені; runtime очікує природної production-події (Стаття 18) |
| ⬜ Pending | не почато |

### Порядок слайсів (довкола реальних больових точок)

Пріоритет — спостережуваність сервісів, що вже коштували годин форензика. **Offline Queue відкладено** (не пришвидшує пошук production-багів).

| Slice | Сервіс | Статус |
|---|---|---|
| 1 | Minimal Client + Trace | ✅ Runtime Verified |
| 2 | Auth / OAuth | ✅ Runtime Verified |
| 3 | Installation (`42501`) | ✅ Runtime Verified |
| 4 | Updater (Download/Verify/Install) | ⏳ Production Pending |
| 5 | L.I.A (`0x800B0109`) | ⬜ Pending |
| (пізніше) | Offline Queue (JSONL-персистенція) | ⬜ Pending |

**Правило:** наступний слайс не починається, поки попередній не **Runtime Verified** або **Production Pending** (Стаття 18). Production Pending → Runtime Verified при першому природному виконанні (без зміни коду).

---

## Скрізний Definition-of-Done (Стаття 15 — Observable by Default)

Будь-яка фича, додана в межах будь-якої фази, не вважається завершеною без мінімуму:

```
Start · Success · Failure · Duration
```

Це чеклист ревью для кожного PR observability-шару.

---

## Phase 1 — Minimal Client + Trace

**Мета.** Запустити збір структурованих подій із tracing-ом через наявний Supabase JWT. Доказ цінності на критичних шляхах.

**Обсяг:**
- `ITelemetryService` + `TelemetryClient` (ambient `TraceContext`: `session_id` / `correlation_id` / `step`).
- `PrivacySanitizer` (єдине вузьке горло; Стаття 4).
- `SamplingGate` (duplicate-suppression + burst).
- `OfflineTelemetryQueue` (JSONL, UTF-8, lock, bounded).
- `TelemetryUploader` (Postgrest + JWT, batch ≤100, backoff, dedup `client_event_id`, rate-cap, configurable endpойнт).
- `FeatureFlagService` + `BuildInfo`.
- Міграції: `telemetry_events`, `feature_flags` (+ seed прапорів і `telemetry.level`).
- Wiring у `AppCompositionRoot`; dispose у reverse-order.
- **~9 подій:** Application.Start/Exit, Auth.SignIn, Auth.RestoreSession, Installation.Sync (← виявляє `42501`), Localization.Install, Updater.Check, LIA.Install (← виявляє `0x800B0109`), Network.Request (sampled).

**Складність:** середня. **Ризик:** низький (ізольований сервіс). **Залежності:** немає (поверх існуючого Supabase SDK).

**Критерії готовності:**
- Події дійшли до `telemetry_events` після авторизації.
- Trace реконструюється (`ORDER BY correlation_id, step`).
- Pre-auth події буферуються й флешаються після логіну.
- Вимкнення `telemetry.level=0` не змінює поведінку додатка (Стаття 10).
- Жоден збій телеметрії не впав у бізнес-код (Стаття 1).

---

## Phase 2 — Crash & Error Capture

**Мета.** Закрити найбільшу прогалину — невидимі падіння (саме тут жив би `0x800B0109` як структурований сигнал).

**Обсяг:**
- Глобальні хендлери у `App.OnStartup`: `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` (Стаття 8).
- `CrashReporter`: crash-event з `detail jsonb` (санітизований stack); синхронний локальний запис перед termination; відправка наступним запуском.
- `ErrorContextExtractor`: авто-витяг `(source, http_status, hresult, supabase_code, exception_type)` з Exception/HttpResponse — без кураторного enum.
- Bridge-event апдейтера з `update-history.json` (закрити сліпу зону detached self-update).
- Розширення wiring: LIA / Updater / Localization / GitHub / Supabase невдачі → Critical-події.

**Складність:** середня. **Ризик:** середній (PII-санітизація stack/шляхів). **Залежності:** Phase 1.

**Критерії готовності:**
- Штучний невловлений виняток → crash-event у БД з санітизованим stack.
- `0x800B0109` зʼявляється як `LIA/Install` Failed, `hresult='0x800B0109'`.
- Жоден шлях у `detail` не містить `C:\Users\<name>`.

---

## Phase 3 — Dashboard

**Мета.** Побачити релізи й траси очима Control Center.

**Обсяг:**
- VIEW `control_center.traces` (timeline запуску).
- VIEW `control_center.release_health` (per-version success-rate, status Healthy/Degraded/Broken).
- Запити success-rate / impact / TOP-помилок / проблемна версія / проблемна Windows.
- `cc_readonly` підхоплює нові VIEW автоматично (вже має default privileges).

**Складність:** низька. **Ризик:** низький (read-only). **Залежності:** Phase 1–2 (дані).

**Критерії готовності:**
- Дашборд відповідає: «після якого релізу зросли падіння OAuth/Installation/Updater».
- Trace користувача відтворюється крок-за-кроком.

---

## Phase 4 — Health Monitoring & Incident Detection

**Мета.** Автоматичне виявлення масових проблем (клас `42501` / `0x800B0109`) за хвилини після релізу.

**Обсяг:**
- VIEW `control_center.health` (GREEN/YELLOW/RED/GREY per component; sample-guard).
- VIEW `control_center.incident_signals` (10-хв вікно за `received_at`; signal-групування; поріг >20% + ≥20 семплів).
- Таблиця `telemetry_incidents` (lifecycle: Detected → Confirmed → Monitoring → Resolved → Closed; анти-флап ≥15 хв; bounded `evidence jsonb`).
- Карточка інциденту: Release · Component · Problem · Affected% · Started.

**Складність:** середня. **Ризик:** низький (read-side). **Залежності:** Phase 3.

**Критерії готовності:**
- Симуляція `42501` у тестовому релізі → інцидент зʑявляється в `incident_signals` за ≤10 хв.
- Хронічний інцидент не заморожує raw безконтрольно (evidence-snapshot bounded).

---

## Phase 5 — Scale & Platform Evolution

**Мета.** Підготуватися до зростання й розширення платформи за межі телеметрії.

**Обсяг:**
- Edge Function `telemetry-ingest` (drop-in заміна endpойнта): server-side dedup/rate-limit/country-fill, прийом pre-auth подій з `install_id` (без user-JWT).
- `pg_cron` rollup-automation (опц., замість on-demand REFRESH) + retention policy.
- Sampling Tier 2 (escape hatch): успіхи Operational → aggregated-count при наближенні до ліміту 500 MB.
- `error_reports` triage-воркфлоу (`is_resolved`, призначення) — окремий від raw-журналу.
- Синхронізація `PRIVACY.md` / `README.md` з актуальним станом збору.
- Партиціювання `telemetry_events` по `occurred_at` (за потреби >50M рядків/рік).

**Складність:** висока. **Ризик:** середній (net-new Edge Function, можлива зміна схеми). **Залежності:** Phase 1–4 стабільні.

**Критерії готовності:**
- Інгест через Edge Function працює; клієнт перемикнувся зміною endpойнта (без переписування).
- Retention автоматичний; БД тримається в межах Free Tier при навантаженні.

---

## Мапа фаза → стаття Конституції

| Фаза | Які статті вперше повністю реалізує |
|---|---|
| 1 | 1, 2, 3, 4, 6, 7, 10, 11, 12, 13, 14, 16 |
| 2 | 5 (no-UPDATE гарантується кодом), 8, 9 (частково — без incidents) |
| 3 | 9 (trace відновлюється з платформи) |
| 4 | 9 (повно: incidents + evidence) |
| 5 | масштабування, additive-evolution на практиці |
| Скрізь | 15 (Observable by Default) |

---

## Порядок виконання (workflow SCLOC-Verse)

Для кожної фази:
1. Форензик аналіз задачі (зона впливу, ризики, регресії).
2. Резервний commit.
3. Реалізація (ua-коментарі, UTF-8, існуючий стиль).
4. `dotnet build` + візуальна перевірка українського тексту (P0 — без mojibake).
5. Звіт + фінальний commit українською.

**Поточний статус:** архітектурний етап завершено, три документи зафіксовано. Готово до старту Phase 1 після погодження.
