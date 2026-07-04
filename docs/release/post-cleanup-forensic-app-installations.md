# Forensic-аудит: app_installations після Cleanup 1.0.0.1

> **Дата:** 2026-07-04  
> **Тригер:** Побічний ефект cleanup — 0 installations, втрата country/version у `control_center.users`.  
> **Режим:** Лише аналіз. Без коду, без SQL-змін, без виконання.

---

## 1. Хто читає `app_installations`

### 1.1. SQL VIEW (5 споживачів)

| VIEW | Тип читання | Поле з app_installations |
|---|---|---|
| `control_center.installations` | **Прямий SELECT \*** | Усі колонки (id, install_id, country, app_version, platform, first_seen, last_seen...) |
| `control_center.statistics` | `count(*)` | `total_installations` |
| `control_center.health` | `count(*) WHERE last_seen > now()-7d` | `active_installations_last_7d` |
| `public.user_analytics` | **JOIN** `auth.users LEFT JOIN app_installations` | `install_id, country, app_version, platform, machine_id, first_seen, last_seen`, агрегат `user_countries` |
| `control_center.users` | **Просуває** `user_analytics` | Усі installation-колонки у списку користувачів |

`control_center.knowledge_coverage` — **НЕ** використовує app_installations.

### 1.2. SQL функції (1)

- `public.ecosystem_stats()` — згадує app_installations у prosrc (агрегат інсталяцій).

### 1.3. C# Repository (Control Center)

| Метод | VIEW | Чи залежить від app_installations? |
|---|---|---|
| `QueryPlatformStatsAsync` | `control_center.platform_stats` | **НІ** — `active_installations` рахується з `telemetry_events` (DISTINCT install_id за 7д) |
| `GetOverviewDataAsync` | через platform_stats | НІ (опосередковано) |

**Ключове:** KPI "Installations" на Home.razor (`@_data.Statistics.ActiveInstallations`) — це **active installs з telemetry_events за 7 днів**, а не `count(*)` з `app_installations`. Після очищення telemetry → 0; після очищення app_installations додатково не падає.

### 1.4. Сайт SCLOCVerse (Control Center page Users)

- Сторінка адміністрування читає `control_center.users` → `user_analytics` → JOIN `app_installations`.
- Після cleanup: 39 користувачів з auth.users збережено, але всі installation-колонки (install_id, country, app_version, platform, user_countries) стають NULL.

---

## 2. Хто записує `app_installations`

### 2.1. Ланцюг запису

```
SCLOCVerse Application (клієнт C#)
    ↓ (login користувача)
InstallationService.SyncCurrentInstallationAsync()
    ↓
GetOrCreateInstallId()        ← файл %LOCALAPPDATA%\SCLOCVerse\install-id
    ↓                              + реєстр HKCU\Software\VALDEUS\SCLOCVerse\InstallId
install_id (32-символьний HEX)  ← СТАБІЛЬНИЙ, зберігається локально, виживає cleanup
    ↓
PostgREST UPSERT:
    - SELECT by install_id
    - якщо немає → INSERT (FirstSeen=now, CreatedAt=now)
    - якщо є    → UPDATE (LastSeen=now, UpdatedAt=now, AppVersion, MachineId, OsVersion, IsActive=true)
    ↓
BEFORE INSERT/UPDATE trigger trg_app_installations_set_country
    ↓
читає GUC request.headers → cf-ipcountry (Cloudflare)
    ↓
NEW.country := upper(cf_country)   ← АВТОМАТИЧНО при кожному запиті через PostgREST
```

### 2.2. Структура запису

| Поле | Хто встановлює | Коли | Відновлюється після cleanup? |
|---|---|---|---|
| `install_id` | Клієнт (файл+реєстр) | Постійно стабільний | ✅ Так (унікальний для машини) |
| `user_id` | Клієнт (auth.CurrentUser) | Кожен Sync | ✅ Так (зв'язок з auth.users) |
| `app_version` | Клієнт (Assembly.Version) | Кожен Sync | ✅ Так |
| `platform`, `machine_id`, `os_version` | Клієнт | Кожен Sync | ✅ Так |
| `country` | **Тригер** з Cloudflare | INSERT/UPDATE через PostgREST | ✅ Так (через тригер) |
| `last_seen`, `updated_at`, `is_active` | Клієнт | Кожен Sync | ✅ Так ( Operational cache) |
| **`first_seen`** | Клієнт (тільки при INSERT) | **Раз — перша установка** | ❌ **НІ — втрачається назавжди** |
| **`created_at`** | Клієнт (тільки при INSERT) | **Раз — перша установка** | ❌ **НІ — втрачається назавжди** |

### 2.3. Ключовий висновок щодо відновлення

`install_id` — це **стабільний client-side identity** (файл + реєстр Windows). При наступному Sync після cleanup клієнт виконає **INSERT з тим самим install_id**, але з новими `first_seen`/`created_at`. Запис фізично "відновиться", але **історична дата першої установки втрачається безповоротно**.

`country` відновлюється автоматично через GeoIP-тригер Cloudflare при наступному INSERT/UPDATE — **але тільки після того, як користувач хоч раз запустить застосунок і залогіниться**.

---

## 3. Класифікація `app_installations`

**вердикт: ☑ Hybrid (Operational Cache + Business History)**

| Компонент | Класифікація | Обґрунтування |
|---|---|---|
| `install_id` | **Operational Cache** | Генерується клієнтом, дублюється файл+реєстр, стабільний |
| `last_seen`, `is_active`, `updated_at` | **Operational Cache** | Оновлюється щодня, не має історичної цінності |
| `country`, `app_version`, `platform` | **Operational Cache** | Відновлюється з клієнта/Cloudflare при наступному Sync |
| `user_id` | **Foreign Reference** | Зв'язок з auth.users (останній збережено) |
| **`first_seen`, `created_at`** | **Business History** | Фіксуються раз, **НЕ відновлюються** — це adoption-метрика |

Таблиця НЕ є чистим кешем. Містить невідновлювану adoption-історію.

---

## 4. Чи правильно було повністю очищати `app_installations`?

**вердикт: ❌ НІ — повне очищення втратило невідновлювані adoption-дані.**

### 4.1. Що було зроблено правильно

- ✅ Видалено тестовий `install-1` (не-UUID, ручна ін'єкція для observability-тестів).
- ✅ Видалено alpha/beta telemetry (версія 1.0.0.0) — дійсно operational cache.

### 4.2. Що було зроблено неправильно

- ❌ Видалено 35 UUID-записів alpha/beta користувачів разом з `first_seen`/`created_at`.
- ❌ Спотворено adoption-метрику: після повторного входу користувача `first_seen` стане "дата повторного входу", а не "дата справжньої першої установки".
- ❌ Тимчасово (до наступного входу) втрачено country/app_version у `control_center.users` для 39 auth.users.

### 4.3. Нова політика Cleanup (рекомендована)

```
ТЕЛЕМЕТРІЯ (events, traces, incidents, knowledge, notifications)
    → ОЧИЩАТИ ПОВНІСТЮ (operational cache + test verification)

APP_INSTALLATIONS
    → ОЧИЩАТИ ЛИШЕ ТЕСТОВІ (install_id NOT LIKE UUID-патерн)
    → ЗАЛИШАТИ РЕАЛЬНІ (UUID install_id з auth.users)

INCIDENT_POLICY, AUTH.USERS, SCHEMA_MIGRATIONS
    → НІКОЛИ НЕ ЧІПАТИ
```

Конкретний фільтр для app_installations:
```sql
DELETE FROM public.app_installations
WHERE install_id !~ '^[0-9a-f]{32}$';   -- лише не-UUID (тестові)
```

---

## 5. Вплив на Dashboard

### 5.1. Поточний стан (після cleanup)

| KPI | Джерело | Значення | Причина |
|---|---|---|---|
| Home → "Installations" | `platform_stats.active_installations` (з telemetry_events) | **0** | telemetry_events порожня |
| Home → "Open Incidents" | `platform_stats.open_incidents` | **0** | telemetry_incidents порожня |
| Health → "Active installations last 7d" | `control_center.health` (count з app_installations) | **0** | app_installations порожня |
| Statistics → "total_installations" | `control_center.statistics` | **0** | app_installations порожня |
| Users → country/version/platform | `user_analytics` JOIN app_installations | **NULL для всіх 39 користувачів** | LEFT JOIN не знаходить рядки |
| Knowledge Coverage | `control_center.knowledge_coverage` | **0.0% (0/0)** | коректно — telemetry_incidents порожня |

### 5.2. Чи відновиться автоматично?

| KPI | Відновлення | Коли |
|---|---|---|
| Home "Installations" | ✅ | Після першої telemetry події від будь-якого користувача |
| Health "active_installations_last_7d" | ✅ | Після першого Sync кожного користувача (країна + last_seen оновляться) |
| Statistics "total_installations" | ✅ | Після першого Sync (але рахунок почнеться з 0, не з 35) |
| Users country/version/platform | ✅ | Після першого входу кожного окремого користувача (post-cleanup) |
| **first_seen/created_at** (прихована adoption-метрика) | ❌ | **Втрачено безповоротно** — новий `first_seen` = дата повторного входу |

### 5.3. Ризик спотворення метрик

- Adoption curve: замість "35 установок за alpha/beta період + нові" → "0 установок → різкий стрибок після релізу 1.0.0.1". Крива adoption стає mis-leading.
- Retention: користувач, що встановив alpha і повернувся через місяць, буде виглядати як "новий installation" замість "returning user".

---

## 6. Рекомендація для Release 1.0.0.1

**вердикт: ☑ Варіант B + C**

### Варіант B — Відновити `app_installations` з backup (фільтруючи тестові)

```sql
-- Відновлення лише реальних UUID-installations з backup
INSERT INTO public.app_installations
SELECT * FROM backup_pre_1_0_0_1.app_installations
WHERE install_id ~ '^[0-9a-f]{32}$';   -- лише справжні UUID
```

Це повертає:
- 35 alpha/beta установок з історичними `first_seen`/`created_at`.
- Adoption-криву alpha/beta періоду.
- Country/platform/version для 35 користувачів у `control_center.users`.

`install-1` НЕ відновлюється (фильтр UUID).

### Варіант C — Оновити `db-cleanup.sql` назавжди

Замінити:
```sql
DELETE FROM public.app_installations;
```
на:
```sql
DELETE FROM public.app_installations
WHERE install_id !~ '^[0-9a-f]{32}$';   -- лише тестові (не-UUID)
```

Це гарантує, що майбутні cleanup'и ніколи не втратять adoption-історію.

### Чому не A

Варіант A ("ничого не відновлювати") прийнятний, якщо:
- 35 alpha/beta users вважаються "не production".
- Adoption-метрика alpha/beta не має цінності.

Але оскільки:
- 1.0.0.1 позиціонується як **Observability Release** (діагностичний) — adoption-дані alpha/beta цінні для порівняння "до/після".
- `install_id` у користувачів стабільний — відновлення з backup НЕ створить дублікатів при наступному Sync (UPDATE замість INSERT).
- Backup вже існує (`backup_pre_1_0_0_1.app_installations`).

...варіант B дає більше цінності при мінімальному ризику.

### Чому не D

Варіант D ("інше") не потрібен — B+C повністю покриває сценарій:
- B виправляє поточний стан.
- C запобігає повторенню.

---

## 7. Оцінка ризиків

| Ризик | Можливість | Вплив | Мітигація |
|---|---|---|---|
| Дублікати install_id після відновлення | Низька | Середній | install_id має UNIQUE-обмеження; при конфлікті Sync робить UPDATE |
| Спотворена adoption-крива (якщо НЕ відновити) | Високий | Середній | Варіант B повертає first_seen |
| Тимчасова втрата country у Users (якщо НЕ відновити) | Високий | Низький | Відновиться через GeoIP-тригер при наступному Sync |
| Alpha/beta users ніколи не повертаються → dormant рядки | Середній | Низький | Прийнятно — це історичний запис; `is_active=false` після 7д |

---

## 8. Підсумковий вердикт

1. **`app_installations` НЕ є чистим кешем** — містить невідновлювану adoption-історію (`first_seen`, `created_at`).
2. **Повне очищення було надмірним** — правильний підхід: фільтр лише тестових (не-UUID) install_id.
3. **Country відновлюється автоматично** через Cloudflare GeoIP-тригер при наступному Sync — тимчасовий побічний ефект.
4. **Рекомендовано B + C**: відновити 35 UUID-записів з backup + оновити cleanup script назавжди.

**Файли для оновлення (після погодження):**
- `docs/release/db-cleanup.sql` — прибрати повне DELETE з app_installations.
- `docs/release/db-cleanup-forensic-analysis.md` — зафіксувати нову політику.
- `docs/release/release-runbook-1.0.0.1.md` — додати крок "Restore app_installations".
