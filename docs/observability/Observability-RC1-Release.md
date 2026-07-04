# SCLOC Observability Platform — RC1

**Дата:** 2026-07-04  
**Тег:** `v0.9.0-observability-rc1`  
**Гілка:** `dev`

> Свідоцтво народження платформи спостережуваності SCLOC-Verse.
> Перетворює окремі події у замкнений цикл: Production → Telemetry → Detection →
> Incident → Notification → Developer → Fix → Release → Release Health → Production.

---

## Що це

Платформа спостережуваності першого покоління для десктоп-додатка SCLOC-Verse.
Не «система логування» — повноцінний цикл, що допомагає продукту покращувати
себе самого: інциденти детектуються автоматично, повідомлення доходять до
розробника, дослідження перетворюються на знання.

Жорсткі обмеження: лише Supabase Free Tier (500 МБ PostgreSQL, спільний CPU).
Жоден компонент не вимагає платної інфраструктури.

---

## Архітектура

```
┌─────────────────────────────┐    ┌──────────────────────────┐
│ SCLOC-Verse (WPF, .NET 9)   │    │ Supabase (Free Tier)     │
│                             │    │                          │
│  TelemetryClient            │ ── │  telemetry_events        │
│  ErrorContextExtractor      │    │  (append-only, RLS,      │
│  HResultCatalog             │    │   14d retention)         │
│  PrivacySanitizer           │    │                          │
│  TelemetryEventQueue        │    │  incident_candidates_    │
│  TelemetryUploader          │    │    live / _24h (VIEWs)   │
│  BuildInfo                  │    │                          │
│  LiaForensicParser          │    │  telemetry_incidents     │
└─────────────────────────────┘    │  incident_policy         │
                                   │  notification_queue      │
┌─────────────────────────────┐    │  notification_attempts   │
│ SCLOCVerse.Notifier         │    │                          │
│ (Worker Service, 30s)       │ ←→ │  promote_incident_       │
│                             │    │    candidates()          │
│  NotificationDispatcher     │    │  transition_incident()   │
│   FOR UPDATE SKIP LOCKED    │    │  add_incident_note()     │
│  INotificationProvider      │    │  assign_incident_owner() │
│   └── DiscordNotification   │    │                          │
└─────────────────────────────┘    │  control_center.* (VIEWs)│
                                   └──────────────────────────┘
┌─────────────────────────────┐                ▲
│ SCLOCVerse.ControlCenter    │                │
│ (Blazor Server)             │ ── READ ONLY ──┘
│                             │
│  Overview · Incidents ·     │    ┌──────────────────────────┐
│  Trace Explorer · Release   │    │ SCLOCVerse.Notifications │
│  Health · Settings          │    │ (Class Library)          │
│                             │    │                          │
│  IControlCenterRepository   │    │  INotificationProvider   │
│  ITraceRepository           │    │  NotificationPayload     │
│  (cc_readonly, VIEWs only)  │    │  NotificationResult      │
└─────────────────────────────┘    │  NotificationChannel     │
                                   └──────────────────────────┘
```

**Принцип розділення:**
- **Telemetry клієнт** — єдиний вузол sanitization, version-resilient, неблокуючий.
- **Supabase** — єдина таблиця `telemetry_events` + похідні VIEWs (Free Tier стиль).
- **Worker** — автономний диспетчер сповіщень, переживає вимкнення UI.
- **Control Center** — Blazor Server, читає лише VIEWs через роль `cc_readonly`.
- **Contracts** — окрема бібліотека, ніякої залежності Worker↔UI.

---

## 5 Event Slices (телеметрія)

| Slice | Component · Operation | Статус |
|---|---|---|
| 1 | `Application.Start` (Trace) | ✅ Runtime Verified |
| 2 | `Auth.SignIn` / `Auth.RestoreSession` (OAuth) | ✅ Runtime Verified |
| 3 | `Installation.Sync` (`42501` RLS) | ✅ Runtime Verified |
| 4 | `Updater.Download` / `Verify` / `Install` | ⏳ Production Pending (Стаття 18) |
| 5 | `LIA.Download` / `Install` + forensic JSON (`0x800B0109` → `CERT_E_UNTRUSTEDROOT`) | ⏳ Production Pending (Стаття 18) |

**Інструменти клієнта:**
- `ErrorContextExtractor` — універсальна класифікація виняток (Supabase/Network/COM/PowerShell через інспекцію типу, не текст).
- `HResultCatalog` — структурований переклад HRESULT.
- `PrivacySanitizer` — єдине горло PII-очищення (Стаття 4).
- `LiaForensicParser` — контрольований PowerShell→C# протокол.

---

## Incident Engine (Detection → Lifecycle)

```
telemetry_events → incident_candidates_live (VIEW) → promote() → telemetry_incidents
                                                                    │
                                                                    ▼
                                                       notification_queue (Pending)
```

**Двошарова модель (Стаття 19/20/21):**
- `incident_candidates_live` — виявлення в реальному часі, міняється без історії.
- `telemetry_incidents` — стабільний lifecycle з INC-ID (`INC-YYYY-NNNNN`).

**Промот-функція** `promote_incident_candidates()` (SECURITY DEFINER):
- закриває прострочені інциденти (per-policy `auto_close_after_minutes`);
- для нових кандидатів обчислює severity (`Critical` / `Warning`);
- рецидив оновлює існуючий інцидент (Стаття 21);
- новий інцидент → INSERT у `notification_queue` з payload v1.

**Workflow (Стаття 22/23):**
- `transition_incident()` — переходи статусу через SECURITY DEFINER.
- `add_incident_note()` — append-only журнал нотаток.
- `assign_incident_owner()` — призначення відповідального.
- Append-only `incident_status_log` + `incident_notes` — історія незмінна.

---

## Notification Engine (Статті 24-27)

**Тришарова архітектура:**

```
notification_queue (Pending)
        │
        ▼  poll 30s, FOR UPDATE SKIP LOCKED
SCLOCVerse.Notifier (Worker)
        │
        ▼
INotificationProvider (контракт)
        │
        └── DiscordNotificationProvider → Discord Webhook
```

**Гарантії:**
- **Автономність** (Стаття 24): Worker повністю окремий від UI. Blazor OFF → сповіщення продовжують йти.
- **Ідемпотентність** (Стаття 25): `UNIQUE(incident_id, notification_type) WHERE status != 'Failed'` — повторні промоти не породжують дублів.
- **Аудит доставки** (Стаття 26): кожна спроба → append-only запис у `notification_attempts`. Стани: `Pending → Sending → Delivered | RetryScheduled | Failed`. Жодного `Pending → (зникло)`.
- **Незалежність провайдерів** (Стаття 27): Worker знає лише `INotificationProvider`. Discord/Email/Telegram — реалізації, реєструються в DI.

**Живучість:**
- Claim через `FOR UPDATE SKIP LOCKED` — безпечний для 2+ інстансів Worker.
- Zombie Recovery: `Sending > 10 хв → RetryScheduled`.
- Retry з експоненційним backoff: `next_attempt_at = now + 2^attemptNo секунд`.
- `claimed_at` + `claimed_by` (machine:pid) для діагностики.
- `provider_message_id` для зіставлення з реальним повідомленням Discord.

---

## Control Center (Blazor Server)

5 сторінок, всі через `cc_readonly` (SELECT для VIEWs + EXECUTE для 3 workflow-функцій):

| Сторінка | Призначення |
|---|---|
| Overview | Загальна картина: релізи, інциденти, здоров'я компонентів |
| Incidents | Деталі інциденту + Workflow: timeline, notes, owner, transitions |
| Trace Explorer | Реконструкція сесії користувача крок-за-кроком |
| Release Health | Per-version success-rate, 🟢/🟡/🔴 рекомендація |
| Settings | Конфігурація / діагностика |

Жоден запит Control Center не звертається до `public.*` напряму — лише `control_center.*` VIEWs.

---

## База даних

**Міграції 00009–00018:**

| Міграція | Призначення |
|---|---|
| 00009–00012 | `telemetry_events`, базові VIEWs, RLS, retention |
| 00013 | `promote_incident_candidates()` + `telemetry_incidents` |
| 00014 | `incident_policy` (per-component пороги) |
| 00015 | Workflow tables: `incident_status_log`, `incident_notes` + SECURITY DEFINER функції |
| 00016 | `notification_queue` + контрольна VIEW |
| 00017 | Audit + retry: `notification_attempts`, `Sending`/`RetryScheduled` стани, `claimed_at/by` |
| 00018 | Роль `cc_notifier` для Worker (least privilege) |

**Ролі:**
- `cc_readonly` — Control Center. SELECT на VIEWs, EXECUTE на workflow-функції. Ніяких INSERT/UPDATE напряму.
- `cc_notifier` — Worker. INSERT/UPDATE на `notification_queue`+`notification_attempts`, SELECT на `telemetry_incidents`. Ніякого доступу до `telemetry_events` чи `control_center`.

---

## Конституція (28 статей)

| Діапазон | Тема |
|---|---|
| 1–17 | Observability core: ізоляція, PII, append-only, trace, additive schema, offline, observable-by-default, backwards-compat, DB validation, production pending |
| 18 | Production Pending (Стаття 18): сценарії, що не відтворюються природньо, отримують цей статус |
| 19–20 | Dashboard Purity / Incident Identity |
| 21–23 | Incident lifecycle: ідентичність, незмінна історія, доступ до workflow |
| 24–25 | Notification: незалежність, ідемпотентність |
| 26–27 | Audit + Provider Independence |
| **28** | **Knowledge Preservation** (для Phase 6) |

Конституція є вищим авторитетом; жоден код не відхиляється від неї.

---

## Що НЕ ввійшло в RC1

Свідомо відкладено:
- **Edge Function ingest** (Phase 5 повна) — зараз PostgREST + JWT достатньо.
- **Offline Queue** (JSONL persistence) — не пришвидшує пошук production-багів.
- **Knowledge Engine** (Phase 6) — вимагає спочатку реальних production-інцидентів.

---

## Наступний етап

Phase 6 — **Knowledge Engine**. Slice 1 мінімальний:
`knowledge_entries` (fingerprint → Known Cause/Fix/Workaround) + відображення
"Known Solution" у деталях інциденту. Без AI, без embedding — лише тонкий
вертикальний зріз з ручним створенням знань (Стаття 28).

---

## Технічні вимоги

- .NET 9, Nullable Enabled.
- C# — англійською, коментарі — українською.
- Усі файли UTF-8 (P0 правило: жодного mojibake в українських текстах).
- Supabase Free Tier (PostgreSQL 17, пулер замість прямого підключення).
- Жодного vendor lock: абстракції на кожному рівні (Supabase, Discord, тощо).
