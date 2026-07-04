# Release Runbook — SCLOC-Verse 1.0.0.1 (Observability Release)

> **Дата:** 2026-07-04  
> **Версія:** 1.0.0.1  
> **Кодова назва:** Observability Release  
> **Проєкт Supabase:** `nrytczdbhehiotflaagl`

---

## Контекст релізу

**1.0.0.1 — це не "гаряче виправлення".** Це **Observability Release**: перший випуск,
що містить повну платформу діагностики. Вона дозволяє автоматично збирати телеметрію,
виявляти інциденти та значно пришвидшує аналіз проблем інсталяції й роботи застосунку.

Відома проблема L.I.A. (CERT_E_UNTRUSTEDROOT) залишається відкритою навмисно —
спочатку збираємо реальні діагностичні дані, потім робимо цільовий фікс у 1.0.0.2.

---

## Порядок виконання

```
1. Git tag (pre-release checkpoint)
        ↓
2. Backup Supabase SQL (повний дамп)
        ↓
3. Виконати db-cleanup.sql
        ↓
4. Виконати db-cleanup-verify.sql
        ↓
5. Підтвердити coverage = 0%
        ↓
6. Зібрати SCLOCVerse 1.0.0.1
        ↓
7. Опублікувати реліз
```

---

## Крок 1. Git tag (pre-release checkpoint)

```bash
git tag -a v1.0.0.1-rc -m "SCLOC-Verse 1.0.0.1 Release Candidate — Observability Release"
git push origin v1.0.0.1-rc
```

Тег фіксує стан коду перед релізом. Якщо cleanup піде не так — повертаємось сюди.

---

## Крок 2. Backup Supabase SQL

**Обов'язково перед cleanup.** Це точка відкату.

**Варіант A — Dashboard:**
1. Supabase Dashboard → Project `nrytczdbhehiotflaagl`
2. Database → Backups → Create manual backup
3. Дочекатись завершення, зафіксувати ім'я/час.

**Варіант B — CLI (pg_dump):**
```bash
pg_dump "$DATABASE_URL" \
  --format=custom \
  --file="scloc-verse-pre-1.0.0.1-cleanup-$(date +%Y%m%d-%H%M%S).dump"
```

Зберегти дамп у безпечне місце (не в репозиторій).

---

## Крок 3. Виконати db-cleanup.sql

Скрипт: [`docs/release/db-cleanup.sql`](./db-cleanup.sql)

**Властивості:** ідемпотентний, одна транзакція, не торкається production-конфігу.

**Виконання через Supabase SQL Editor:**
1. Dashboard → SQL Editor
2. Вставити вміст `db-cleanup.sql`
3. Run
4. Перевірити, що завершився `COMMIT` без помилок.

**Що видаляється:**
- `telemetry_events` (18: 1 тестовий + 17 alpha/beta)
- `telemetry_incidents` (1: INC-2026-00008)
- `app_installations` (36: 1 тестовий + 35 alpha/beta)
- `incident_status_log`, `incident_notes` (0)
- `knowledge_entries`, `knowledge_references`, `knowledge_version_history` (0)
- `notification_queue`, `notification_attempts` (0)
- `REFRESH MATERIALIZED VIEW control_center.knowledge_coverage`

**Що НЕ торкається:**
- `incident_policy` (5 записів — пороги компонентів)
- `auth.users` (39 — користувачі Supabase Auth)
- `supabase_migrations.schema_migrations` (33 — історія міграцій)
- RLS, функції, VIEW, схеми, розширення

---

## Крок 4. Виконати db-cleanup-verify.sql

Скрипт: [`docs/release/db-cleanup-verify.sql`](./db-cleanup-verify.sql)

**Виконання через Supabase SQL Editor:**
1. Вставити вміст `db-cleanup-verify.sql`
2. Run
3. Перевірити результати всіх 8 перевірок.

---

## Крок 5. Підтвердити критерії успіху

| Перевірка | Очікуване значення |
|---|---|
| `telemetry_events` | 0 |
| `telemetry_incidents` | 0 |
| `incident_status_log` | 0 |
| `incident_notes` | 0 |
| `knowledge_entries` | 0 |
| `knowledge_references` | 0 |
| `knowledge_version_history` | 0 |
| `notification_queue` | 0 |
| `notification_attempts` | 0 |
| `app_installations` | 0 |
| `knowledge_coverage.coverage_pct` | 0.0 |
| `knowledge_coverage.total_fingerprints` | 0 |
| `incident_policy` (production config) | 5 (ЗБЕРЕЖЕНО) |
| `auth.users` | >0 (ЗБЕРЕЖЕНО) |
| тестові INC-2026-0000X | 0 |
| тестовий fingerprint `abc123` | 0 |
| тестовий `install-1` | 0 |
| тестова версія `1.0.0` | 0 |

**Якщо ВСІ умови виконано** — БД готова. Перший користувач стане Install #1, User #1, Incident #1, Knowledge Coverage 0%.

**Якщо будь-яка умова НЕ виконано** — НЕ продовжувати. Відкатитись з backup (Крок 2).

---

## Крок 6. Зібрати SCLOCVerse 1.0.0.1

```bash
# Оновити версію в проєкті до 1.0.0.1 (якщо ще не)
dotnet build -c Release
# Запустити build-installer.ps1 для створення .iss/.exe
```

Перевірити, що інсталятор містить:
- Телеметрію (upload у Supabase `nrytczdbhehiotflaagl`).
- Consent dialog (Стаття 27 Конституції).
- Offline-first (Стаття 1).

---

## Крок 7. Опублікувати реліз

**Release Notes (рекомендований текст):**

> ## SCLOC-Verse 1.0.0.1 — Observability Release
>
> Цей випуск вперше містить повну платформу діагностики (Observability Platform).
> Вона дозволяє автоматично збирати телеметрію, виявляти інциденти та значно
> пришвидшує аналіз проблем інсталяції й роботи застосунку.
>
> ### Що нового
> - Повна Observability Platform: телеметрія → trace → incident → knowledge.
> - Автоматичне виявлення інцидентів за fingerprint.
> - База знань: Known Solution, Coverage, Auto-verify.
> - Control Center: дашборд інцидентів, релізів, покриття знань.
>
> ### Відомі проблеми
> - L.I.A.: помилка `CERT_E_UNTRUSTEDROOT` при інсталяції на свіжих Windows.
>   Діагностичні дані збираються для точкового виправлення у 1.0.0.2.

**Публікація:**
- GitHub Release з тегом `v1.0.0.1`.
- Артефакти: інсталятор `.exe`, release notes.
- Перевірити UTF-8 кодування release notes (AGENTS.md §"Правила кодування тексту" P0).

---

## Після релізу — цикл збору даних

```
Release 1.0.0.1
        ↓
реальні користувачі (Install #1, User #1)
        ↓
реальні інсталяції (з телеметрією)
        ↓
реальні 0x800B0109 / CERT_E_UNTRUSTEDROOT
        ↓
реальні traces (correlation_id, user journey)
        ↓
Incident Engine (авто-виявлення)
        ↓
Knowledge Engine (Draft → Verified)
        ↓
Fix (targeted, на основі реальних даних)
        ↓
Release 1.0.0.2
```

---

## Відкат (якщо cleanup пішов не так)

```bash
# Крок 2 backup:
pg_restore --clean --if-exists -d "$DATABASE_URL" scloc-verse-pre-1.0.0.1-cleanup-*.dump
```

Або через Supabase Dashboard → Backups → Restore.

**Після відкату:** НЕ повторювати cleanup без аналізу причини.
