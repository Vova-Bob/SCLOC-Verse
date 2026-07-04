# Forensic-аудит Production БД — SCLOC-Verse 1.0.0.1

> **Дата:** 2026-07-04  
> **Проєкт Supabase:** `nrytczdbhehiotflaagl`  
> **Мета:** Підготовка абсолютно чистої Production БД перед релізом 1.0.0.1.  
> **Режим:** Лише аналіз. Виконання заборонено до погодження.

---

## 1. Інвентаризація

### 1.1. Дані observability (підлягають очищенню)

| Таблиця | Записів | Класифікація |
|---|---:|---|
| `telemetry_events` | 18 | 1 тестовий (`install-1`) + 17 alpha/beta (UUID) |
| `telemetry_incidents` | 1 | INC-2026-00008 (тестовий, fingerprint `abc123`) |
| `app_installations` | 36 | 1 тестовий (`install-1`) + 35 alpha/beta (UUID) |
| `incident_status_log` | 0 | порожньо |
| `incident_notes` | 0 | порожньо |
| `knowledge_entries` | 0 | порожньо |
| `knowledge_references` | 0 | порожньо |
| `knowledge_version_history` | 0 | порожньо |
| `notification_queue` | 0 | порожньо |
| `notification_attempts` | 0 | порожньо |

### 1.2. Production конфігурація (НЕ ЧІПАТИ)

| Таблиця | Записів | Причина |
|---|---:|---|
| `incident_policy` | 5 | Пороги компонентів (production config) |
| `auth.users` | 39 | Користувачі Supabase Auth |
| `auth.sessions` / `refresh_tokens` | 39 / 177 | Сесії Auth (сервісні) |
| `auth.identities` / `mfa_amr_claims` | 38 / 39 | Auth сервіс |
| `auth.flow_state` | 12 | OAuth flow (тимчасові) |
| `supabase_migrations.schema_migrations` | 33 | Історія міграцій |
| `realtime.*`, `storage.*` | — | Системні схеми Supabase |
| `admin_audit_log` | 0 | Порожньо (аудит) |
| `error_reports` | 0 | Порожньо |
| `user_discord_guilds` | 0 | Порожньо |

### 1.3. Materialized views (потрібен REFRESH)

| Об'єкт | Тип | Дія після cleanup |
|---|---|---|
| `control_center.knowledge_coverage` | materialized | `REFRESH MATERIALIZED VIEW` |

Звичайні VIEW (`control_center.*`) оновлюються автоматично — REFRESH не потрібен.

---

## 2. Аналіз тестових даних

### 2.1. Observability verification data (явно тестові)

Створено під час Runtime Verification Knowledge Engine Slices 1–5.

| Ідентифікатор | Таблиця | Ознака тесту |
|---|---|---|
| `install-1` | `app_installations` | Не-UUID install_id, версія `1.0.0` |
| `install-1` | `telemetry_events` | 1 event, component=LIA, Failed |
| INC-2026-00008 (id=8) | `telemetry_incidents` | fingerprint_hash=`abc123`, owner=`admin` |
| (усі Knowledge ID 4–8) | `knowledge_*` | Вже видалені під час Slice-верифікацій |

### 2.2. Alpha/beta pre-production data (версія 1.0.0.0)

| Ідентифікатор | Таблиця | Характеристика |
|---|---|---|
| `46c630be2be24e4f8d09a68b59787ff5` | `app_installations` + `telemetry_events` | 17 events (Application/Auth/Installation), country=UA |
| + 34 інших UUID | `app_installations` | 0 events, country=UA, platform=Windows |

**Висновок:** Це дані від раннього тестування SCLOC-Verse alpha/beta (версія `1.0.0.0`) українськими тестувальниками. Перед релізом `1.0.0.1` — це pre-production дані, що підлягають очищенню для "абсолютно чистої" БД.

---

## 3. FK залежності (порядок DELETE)

```
app_installations (parent)
├── telemetry_events.install_id     → SET NULL
├── error_reports.install_id         → SET NULL (порожня)
└── admin_audit_log.target_install_id → SET NULL (порожня)

telemetry_events (parent)
└── telemetry_incidents.{root,last}_event_id → SET NULL

telemetry_incidents (parent)
├── incident_notes.incident_id       → CASCADE
├── incident_status_log.incident_id  → CASCADE
└── notification_queue.incident_id   → CASCADE

notification_queue (parent)
└── notification_attempts.queue_id   → CASCADE

knowledge_entries (parent)
├── knowledge_references.knowledge_id     → RESTRICT
└── knowledge_version_history.knowledge_id → RESTRICT
```

**Важливо:** `knowledge_references` та `knowledge_version_history` мають RESTRICT — видаляються ДО `knowledge_entries`.

---

## 4. Порядок очищення

1. `knowledge_version_history` (RESTRICT parent)
2. `knowledge_references` (RESTRICT parent)
3. `knowledge_entries`
4. `notification_attempts` (явно, хоча CASCADE)
5. `notification_queue`
6. `incident_notes` (явно, хоча CASCADE)
7. `incident_status_log` (явно, хоча CASCADE)
8. `telemetry_incidents`
9. `telemetry_events`
10. `app_installations` (тільки спостережувані — НЕ auth)
11. `REFRESH MATERIALIZED VIEW control_center.knowledge_coverage`

**НЕ торкаємось:** `incident_policy`, `auth.*`, міграцій, RLS, функцій, VIEW, схем.

---

## 5. Ризики

| Ризик | Можливість | Мітигація |
|---|---|---|
| Видалення production конфігу | Низька | Скрипт явно перераховує таблиці; `incident_policy` відсутній у DELETE |
| Порушення FK RESTRICT на knowledge | Низька | Порядок DELETE враховує RESTRICT |
| Втрата alpha/beta телеметрії | Свідоме рішення | Завдання вимагає "абсолютно чисту" БД |
| Materialized VIEW не оновиться | Низька | Явний REFRESH у скрипті |
| Скрипт не ідемпотентний | Низький | Усі DELETE без WHERE або з WHERE на порожній результат безпечно повторюються |

---

## 6. Файли

- [`db-cleanup.sql`](./db-cleanup.sql) — Cleanup Script (ідемпотентний, в транзакції).
- [`db-cleanup-verify.sql`](./db-cleanup-verify.sql) — Verification Script.

**Виконувати лише після погодження.**
