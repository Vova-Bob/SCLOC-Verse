# SCLOC Observability Platform — Architecture

> Технічна реалізація платформи спостережуваності SCLOC-Verse.
> Реалізується в межах **Supabase Free Tier** (500 MB PostgreSQL, 50K MAU, shared CPU/RAM).
> Підпорядкована [`Observability-Constitution.md`](./Observability-Constitution.md).

**Версія:** 1.1 · **Статус:** затверджений архітектурний контракт.

---

## 1. Призначення

Відповідати на два питання — **автоматично**, без ручного форензику:

> «Що зараз зламалося у користувачів?»
> «Після якого релізу це почалося?»

**Не логування. Не Debug. Не Diagnostics.** Саме система раннього виявлення production-проблем (класу `42501`, `0x800B0109`, масових OAuth/Updater/LIA-відмов, падінь після нового релізу).

---

## 2. Принципи

| Принцип | Значення |
|---|---|
| **KISS** | Лише те, що реально потрібно SCLOC-Verse. Не AppInsights / Sentry / OpenTelemetry. |
| **DRY** | Одне джерело правди для подій. |
| **Event Sourcing Light** | Одна append-only таблиця `telemetry_events`. Події immutable (Стаття 5). |
| **Derived Projections** | Уся аналітика — через VIEW, не через нові таблиці статистики. |
| **Free-Tier-Driven** | Кожне рішення оцінюється за споживанням 500 MB / CPU / MAU. |
| **Privacy by Design** | Санітизація PII у клієнті до черги (Стаття 4). |
| **Offline First** | Локальна черга + авто-flush (Стаття 14). |

---

## 3. Архітектура

```
┌─────────────────── КЛІЄнт (.NET 9 WPF) ───────────────────┐
│ Сервіси (Auth, LIA, Updater, Localization…)                 │
│   │  telemetry.Track(component, op, outcome, …)            │ ← sync, O(1), не блокує
│   ▼                                                         │
│ TelemetryClient ── TraceContext (session/correlation/step) │
│   │ PrivacySanitizer (ДО черги — обовʼязково)               │
│   ▼                                                         │
│ SamplingGate (dup-suppress / burst / adaptive)             │
│   ▼                                                         │
│ InMemoryQueue (bounded) → OfflineQueue (JSONL, UTF-8)       │
│   │ batch ≤100 · backoff · dedup · rate-cap                 │
│   ▼                                                         │
│ TelemetryUploader ── [endpойнт = конфіг] ───────────────────┤  Phase1: PostgREST + JWT
│ FeatureFlagService (level → env → setting → default)        │  Phase5: Edge Function (drop-in)
│ BuildInfo (AppVersion/GitCommit/Channel/TelemetryVersion)   │
│ CrashReporter + global exception handlers (App.OnStartup)   │
└───────────────────────────┬─────────────────────────────────┘
                            ▼
┌─────────────────── Supabase Free Tier ─────────────────────┐
│ telemetry_events (raw, retention 14d)  ← owner-only INSERT  │
│ telemetry_rollup_mv (MATVIEW, 2y)     ← REFRESH on demand   │
│ telemetry_incidents (lifecycle+evidence, 2y) ← service_role │
│ feature_flags (kill-switch, levels)   ← public SELECT       │
│                                                             │
│ VIEW: traces · release_health · incident_signals            │
│ pg_cron: щоденний purge (1 trivial job)                     │
│ error_reports (dormant — триаж пізніше)                      │
└───────────────────────────┬─────────────────────────────────┘
                            ▼  cc_readonly (вже існує)
                Control Center Dashboard
```

**Два шари зберігання (Free-Tier-критично):**

| Шар | Призначення | Retention |
|---|---|---|
| `telemetry_events` (raw) | форензика, trace, детекція в реальному часі (10-хв вікно) | **14 днів** |
| `telemetry_rollup_mv` (MATVIEW) | release_health, історія інцидентів на місяці/роки | **2 роки** |

---

## 4. Компоненти клієнта

Дом: `Services/Observability/`. Реєстрація — у `AppCompositionRoot`, dispose у reverse-order.

| Компонент | Відповідальність |
|---|---|
| `ITelemetryService` / `TelemetryClient` | Єдина точка `Track(...)`. Ambient `TraceContext`. Sync, O(1), ніколи не кидає (Стаття 1). |
| `TraceContext` | `session_id` (Start→Exit), `correlation_id` (trace; == session для launch-trace, форкається для дискретних дій), atomic `step` (`Interlocked.Increment`). |
| `PrivacySanitizer` | Чистить payload ДО черги (Стаття 4). Єдине вузьке горло. |
| `SamplingGate` | Duplicate-suppression + burst protection + adaptive per-category (Стаття 15 контракту, §11). |
| `OfflineTelemetryQueue` | JSONL, `%LOCALAPPDATA%\SCLOCVerse\observability\queue.jsonl`, UTF-8, lock, bounded (10K). Патерн `InputDiagnostics`. |
| `TelemetryUploader` | Batch ≤100, backoff (2с/8с/30с), dedup `client_event_id`, rate-cap, **конфігурований endpойнт**. |
| `FeatureFlagService` | Читає `feature_flags` + `telemetry.level` (старт + кожні 10 хв). Пріоритет: remote(cached) → env → setting → default. Fail-open. |
| `BuildInfo` | `AppVersion`, `GitCommit` (короткий хеш через MSBuild), `Channel`, `TelemetryVersion`. |
| `CrashReporter` [Phase 2] | Глобальні хендлери → crash-event з `detail` (санітизований stack). |

Конфіг-лестниця (ідиом автора): `SCLOCVERSE_TELEMETRY_*` env → `Settings.*` → default.

---

## 5. Схема БД

### 5.1 `telemetry_events` (lean, ~530 байт/рядок)

| Колонка | Тип | Призначення |
|---|---|---|
| `id` | uuid PK | `gen_random_uuid()` |
| `client_event_id` | uuid NOT NULL | дедуп (Стаття 6) |
| `session_id` | uuid NOT NULL | запуск (Start→Exit) |
| `correlation_id` | uuid NOT NULL | trace (Стаття 11) |
| `step` | int NOT NULL | порядок у trace |
| `install_id` | text FK | анонімна машина (`app_installations`) |
| `user_id` | uuid FK nullable | `auth.users` (null для pre-auth) |
| `occurred_at` | timestamptz NOT NULL | клієнтський UTC (форензика) |
| `received_at` | timestamptz NOT NULL DEFAULT now() | сервер (ДЕТЕКЦІЯ) |
| `app_version` | text NOT NULL | build info |
| `git_commit` | text | короткий хеш |
| `channel` | text NOT NULL DEFAULT 'stable' | release channel |
| `telemetry_version` | int NOT NULL DEFAULT 1 | версія payload (Стаття 13/16) |
| `os_version` | text | |
| `country` | text | GeoIP (trigger/PostgREST headers) |
| `component` | text NOT NULL | таксономія |
| `operation` | text NOT NULL | |
| `outcome` | text NOT NULL | Started/Succeeded/Failed/Cancelled/Skipped |
| `severity` | text NOT NULL DEFAULT 'Info' | Info/Warning/Error/Critical/Crash |
| `category` | text NOT NULL DEFAULT 'Operational' | Critical/Operational/Diagnostic/Analytics |
| `source` | text | Supabase/GitHub/PowerShell/HttpClient/CLR |
| `http_status` | int | |
| `hresult` | text | напр. `0x800B0109` |
| `supabase_code` | text | напр. `42501` |
| `exception_type` | text | `Type.Name` |
| `error_message` | text | коротке, санітизоване |
| `duration_ms` | int | |
| `detail` | jsonb | stack-trace (лише Crash), NULL для 99% |

**Інваріанти (CHECK):**
- `outcome ∈ {Started, Succeeded, Failed, Cancelled, Skipped}`
- `severity ∈ {Info, Warning, Error, Critical, Crash}`
- `category ∈ {Critical, Operational, Diagnostic, Analytics}`
- `outcome = 'Failed'` ⇒ хоча б одне з `(source, http_status, hresult, supabase_code, exception_type)` заповнене — гарантує придатність до групування для **будь-якої** невідомої помилки.

**Індекси (lean):** `UNIQUE(client_event_id)`; `(received_at DESC)`; `(correlation_id, step)`; `(component, operation, outcome, received_at DESC)`.

### 5.2 `telemetry_rollup_mv` (MATERIALIZED VIEW, ~15 MB / 2 роки)

Щоденний агрегат per `(day, app_version, channel, component, operation, signal)`: `started`, `succeeded`, `failed`, `affected_installs`. `signal = COALESCE(hresult, supabase_code, http_status::text, exception_type, '-')`.

REFRESH on-demand при відкритті дашборду (не потребує pg_cron). Харчує `release_health` та історію інцидентів.

### 5.3 `feature_flags`

`(flag text PK, enabled bool, updated_at)`. Прапори: `telemetry`, `crash_reporter`, `updater`, `guild_sync`, `geoip`, `lia`, `localization_analytics`, `telemetry.level`.

### 5.4 `telemetry_incidents` (lifecycle, ~кілька/місяць)

`(fingerprint PK, release_tag, component, operation, signal, first_seen, last_seen, peak_impact_pct, affected_installs, severity, status, evidence jsonb)`. `status`: Detected → Confirmed → Monitoring → Resolved → Closed. `evidence` — bounded-знімок (trace + signal + семпл-повідомлення) для форензику без захисту raw (Стаття 9).

> `error_reports` (вже існує, deny-all) залишається **dormant** — майбутній триаж (`is_resolved`), не сховище діагностики.

---

## 6. Політики RLS

```sql
-- telemetry_events: owner-only INSERT через JWT; читання лише через cc_readonly
ALTER TABLE public.telemetry_events ENABLE ROW LEVEL SECURITY;
CREATE POLICY "deny all anon"  ON public.telemetry_events AS RESTRICTIVE FOR ALL TO anon USING(false) WITH CHECK(false);
CREATE POLICY "auth insert own" ON public.telemetry_events FOR INSERT TO authenticated WITH CHECK(user_id = auth.uid());
GRANT INSERT ON public.telemetry_events TO authenticated;   -- без anon GRANT (foot-proof)

-- rollup_mv / incidents: deny-all клієнтам, service_role only
-- feature_flags: ПУБЛІЧНИЙ read (єдина виправдана Виняток — bool, без PII)
```

> Pre-auth події (Launch, OAuth.Start) буферуються локально, флешаються після логіну з відомим `user_id`. Сліпа зона «зламався сам логін» закривається Phase 2 crash-reporter.

---

## 7. VIEW для аналітики (поверх `control_center`)

```sql
-- TRACE: повний шлях запуску
CREATE VIEW control_center.traces AS
SELECT correlation_id, session_id, install_id, app_version, step, occurred_at,
       component, operation, outcome, category,
       COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') AS signal,
       error_message, duration_ms
FROM public.telemetry_events ORDER BY correlation_id, step;

-- INCIDENT DETECTION (real-time, 10 хв, за received_at)
-- GROUP BY (release, component, operation, signal); поріг >20% failed; sample-guard ≥20
-- → Карточка інциденту: Release · Component · Problem · Affected% · Started

-- RELEASE HEALTH (довгий горизонт — з rollup_mv)
-- per app_version: success_rate, status Healthy/Degraded/Broken
```

Детальні запити — у `Observability-Roadmap.md` (Phase 3) та реалізації.

---

## 8. Offline Queue (Стаття 14)

- Формат: **JSONL**, `%LOCALAPPDATA%\SCLOCVerse\observability\queue.jsonl`, **UTF-8**, lock-guarded.
- Bounded: 10 000 подій; переповнення → drop найстаріших + лічильник `dropped` (один маркер).
- Drain: при старті + при відновленні мережі. Batch ≤100.
- Ізоляція: будь-яка помилка черги → `Debug.WriteLine`, ніколи не впливає на додаток.

---

## 9. Privacy Sanitizer (Стаття 4)

| Що | Дія |
|---|---|
| JWT / access / refresh token, `Bearer …` | видалити (regex) |
| Email, IP-адреси | видалити |
| Шляхи `C:\Users\<name>\…`, UNC | → `C:\Users\*\…` |
| `Environment.MachineName` | не передавати (identity = `install_id`) |
| Discord username/nick/avatar | не передавати |
| `detail` (stack-trace) | санітарити рекурсивно перед чергою |
| `attributes` | лише allowlist |

---

## 10. Telemetry Client API

```csharp
public interface ITelemetryService {
    void Track(string component, string operation, string outcome,
               TelemetryContext? context = null, CancellationToken ct = default);
}
```

Sync, O(1): будує подію з ambient `TraceContext` + `BuildInfo`, проганяє через `PrivacySanitizer` → `SamplingGate` → `InMemoryQueue`. Ніякого `await` на hot path. Увесь код у `try/catch` → `Debug.WriteLine`. Перевірка `telemetry.level` перед емісією.

---

## 11. Sampling + Classification

**Classification** (`category`, ортогональна до `severity`):

| Category | Приклади | Поведінка |
|---|---|---|
| Critical | Crash, OAuth.Fail, Installation.Fail, LIA.Fail, Updater.Fail | 100% |
| Operational | OAuth.Success, Installation.Sync OK, Application.Start/Exit | 100% (знаменники) |
| Diagnostic | Network request, sub-steps | sampled |
| Analytics | (зарезервовано) | default off |

**SamplingGate (клієнт):**
1. **Duplicate suppression** — однаковий fingerprint у cooldown (60с) → 1 event + `repeat_count`. GitHub 429 ×5 = 1 рядок, не 5.
2. **Burst protection** — ліміт N/хв того ж fingerprint.
3. **Adaptive** — Diagnostic = 1-in-N; Critical/Operational = 100%.

Server-side dedup: `client_event_id` + `ON CONFLICT DO NOTHING`.

---

## 12. Uploader

- Background (`ConfigureAwait(false)`): drain → batch ≤100 → POST.
- **Endpойнт конфігурується** (`SCLOCVERSE_TELEMETRY_ENDPOINT`, default Postgrest). Phase 5 Edge Function = зміна URL.
- Retry: backoff 2с/8с/30с → назад у чергу.
- Rate-cap: ≤1 запит/10с/install, ≤1000 подій/год (overflow → drop+sample).
- Авторизація: JWT авторизованого користувача.

---

## 13. Feature Flags + Telemetry Levels

`feature_flags` + `telemetry.level` (0–4, див. Конституцію). Пріоритет: **remote(cached) → env → setting → default (Level 2)**. Fail-open. Відключити телеметрію всім без релізу = один `UPDATE`.

---

## 14. Retention (мінімальна залежність від pg_cron)

| Завдання | Механізм |
|---|---|
| Rollup | **MATVIEW** + `REFRESH` при відкритті дашборду (без cron) |
| Purge (1 trivial DELETE) | **pg_cron** щоденно + ручний safety-net (адмін-SQL) |
| Чистка `cron.job_run_details` | pg_cron раз на тиждень |

**Зафіксовано з docs Supabase:** pg_cron доступний на Free Tier (Cron Module), але scheduler може вмирати, а `job_run_details` не чиститься автоматично — тому залежність мінімізована до одного тривіального purge-job + MATVIEW поза cron. Запас надійності: 14-денний raw займає ~134 MB із бюджету 250 MB → навіть кілька днів пропуску purge не загрожують 500 MB.

---

## 15. Incident Detection + Lifecycle

**Детекція** = VIEW `incident_signals` (10-хв вікно за `received_at`):
- `signal = COALESCE(hresult, supabase_code, http_status, exception_type)` — авто-ключ, нова помилка сама утворює групу.
- Поріг `failed > 20%` AND `completed ≥ 20` (sample-guard).

**Lifecycle** = `telemetry_incidents` (стан): `Detected → Confirmed (тримався ≥15 хв, анти-флап) → Monitoring → Resolved (rate < порогу ≥30 хв) → Closed`. При Detected — bounded `evidence jsonb` (Стаття 9).

| Кейс | Як виглядає |
|---|---|
| `42501` (GRANT SELECT) | `Installation/Sync`, signal=`42501`, impact≈100% → інцидент за хвилини |
| `0x800B0109` | `LIA/Install`, signal=`0x800B0109` → сплеск → інцидент |
| Масовий OAuth Fail | `Auth/SignIn`, signal=status/type, rate>20% |
| GitHub 429 / Supabase 5xx | відповідний component, signal=code |

---

## 16. Build Info & Machine Identity

- **Build info** на кожній події: `app_version`, `git_commit` (MSBuild `git rev-parse --short HEAD` → assembly metadata → `BuildInfo`), `channel`, `telemetry_version`.
- **Machine identity**: `install_id` (існуючий анонімний `Guid.NewGuid().ToString("N")`, dual-store file+registry). `Environment.MachineName` — ніколи.

---

## 17. Free-Tier Fit

| Ресурс | Ліміт | Споживання | OK? |
|---|---|---|---|
| PostgreSQL | 500 MB | raw ~134 MB (14d @ поточний DAU) + rollup ~15 MB (2y) + існуюче ~20 MB | ✅ з запасом |
| MAU | 50 000 | десятки тисяч | ✅ |
| Egress | 5 GB | telemetry = **ingress** (не рахується); egress горять лише дашборд-читанням + клієнтські завантаження | ✅ |
| CPU/RAM | shared 512 MB | INSERT-light; аналітика index-scan по recent rows | ✅ |

**Вузьке місце:** raw-обсяг при зростанні DAU. **Escape hatch (Sampling Tier 2):** якщо 14-денний raw вийде за бюджет — переключити **успіхи** Operational у aggregated-count (клієнт шле лічильник замість N подій), невдахи лишити raw. Детекція працює (по невдачах), обсяг падає ~80%. Увімкнути лише за потреби.

---

## 18. Масштабування (без переписування)

1. **Append-only журнал** — нова аналітика = новий VIEW, без зміни клієнта.
2. **Інгест замінний** — endpойнт конфігурований; PostgREST → Edge Function → будь-що = зміна URL.
3. **`telemetry_version`** — еволюція формату additive (Стаття 13/16).
4. **Rollup-vs-raw** — розділення «гаряче» (детекція) / «холодне» (історія) вписується у 500 MB.
5. **Партиціювання** — задокументований шлях при >50M рядків/рік: range-partition по `occurred_at` через `pg_partman` без повного переписування.

---

## 19. Звʼязок із Конституцією

Кожне технічне рішення тут — реалізація конкретної статті Конституції. Якщо реалізація входить у конфлікт зі статтею — пріоритет за Конституцією; цей документ оновлюється.
