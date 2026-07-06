# Optimization Matrix — Telemetry + Database + Control Center

> **Тип:** Робоча матриця рішень (не звіт, не опис). Кожен рядок = одна конкретна дія.
> **Фокус:** Що викинути (🔴), об'єднати (🟡), лишити (🟢). З підрахунком економії.
> **Дата:** 2026-07-05
> **Сфера:** Observability + 14 таблиць БД + 37 запитів Control Center.

---

# ЕТАП 1 — ТЕЛЕМЕТРІЯ

## 1.1 Матриця подій (30 сигналів)

Легенда: 🟢 лишити · 🟡 об'єднати · 🔴 видалити

### Application / Auth / Installation (3 події)

| # | Signal | File:Line | detail keys | Вирок | Причина |
|---|---|---|---|:---:|---|
| 1 | `Application.Start.Started` | `App.xaml.cs:64` | — | 🟢 | Session-маркер, DAU, основа trace |
| 2 | `Auth.<op>.<outcome>` | `AuthService.cs:290` | error-context | 🟢 | Вже 1 подія/операцію — мінімально |
| 3 | `Installation.Sync.<outcome>` | `InstallationService.cs:152` | error-context, phase | 🟢 | Вже мінімально |

### Updater — App self-update (12 сигналів)

| # | Signal | File:Line | detail keys | Вирок | Причина |
|---|---|---|---|:---:|---|
| 4 | `Updater.Download.Started` | `UpdateDownloader.cs:44` | — | 🔴 | Маркер старту. Успіх підтверджується наступним кроком |
| 5 | `Updater.Download.Succeeded` | `UpdateDownloader.cs:54` | duration_ms | 🔴 | Реконструюється з терміналу `Install.Succeeded` |
| 6 | `Updater.Download.Failed` | `UpdateDownloader.cs:59` | duration_ms, error | 🟢 | Діагностично цінна — термінал збою download |
| 7 | `Updater.Verify.Started` | `UpdateVerifier.cs:34` | — | 🔴 | Маркер старту |
| 8 | `Updater.Verify.Skipped` (NoChecksum) | `UpdateVerifier.cs:40` | duration_ms, phase | 🟡 | **Перетворити** на `Verify.Failed` severity=Warning (зараз silent skip — P0 TD-4) |
| 9 | `Updater.Verify.Failed` (FileNotFound) | `UpdateVerifier.cs:46` | duration_ms, phase | 🟢 | Діагностика |
| 10 | `Updater.Verify.Failed` (checksum) | `UpdateVerifier.cs:59` | duration_ms, phase | 🟢 | Діагностика |
| 11 | `Updater.Verify.Failed` (exception) | `UpdateVerifier.cs:65` | duration_ms, error | 🟢 | Діагностика |
| 12 | `Updater.Install.Started` | `UpdateInstaller.cs:40` | — | 🔴 | Маркер старту |
| 13 | `Updater.Install.Failed` (InstallerNotFound) | `UpdateInstaller.cs:46` | duration_ms, phase | 🟢 | Термінал збою |
| 14 | `Updater.Install.Succeeded\|Failed` (launch) | `UpdateInstaller.cs:76` | duration_ms, phase | 🟢 | Термінал успіху/збою запуску |
| 15 | `Updater.Install.Failed` (exception) | `UpdateInstaller.cs:82` | duration_ms, error | 🟢 | Термінал збою |

### LIA — Voice assistant install (15 сигналів)

| # | Signal | File:Line | detail keys | Вирок | Причина |
|---|---|---|---|:---:|---|
| 16 | `LIA.Install.Started` (Download) | `Updater.cs:79` | — | 🟢 | Точка входу в trace |
| 17 | `LIA.Download.Started` (InstallerAsset) | `Updater.cs:95` | — | 🔴 | Маркер старту під-операції |
| 18 | `LIA.Download.Failed` (InstallerAsset) | `Updater.cs:104` | duration_ms, error | 🟢 | Термінал збою |
| 19 | `LIA.Download.Succeeded` (InstallerAsset) | `Updater.cs:109` | duration_ms | 🔴 | Реконструюється з наступним кроком |
| 20 | `LIA.Download.Started` (CertificateAsset) | `Updater.cs:116` | — | 🔴 | Маркер старту |
| 21 | `LIA.Download.Failed` (CertificateAsset) | `Updater.cs:124` | duration_ms, error | 🟢 | Термінал збою |
| 22 | `LIA.Download.Succeeded` (CertificateAsset) | `Updater.cs:129` | duration_ms | 🔴 | Реконструюється |
| 23 | `LIA.Install.Started` (RunInstallerScript) | `Updater.cs:137` | package_version, installer_type | 🔴 | **Пряме дублювання** #25 (нижче) |
| 24 | `LIA.Install.Succeeded` (Complete) | `Updater.cs:143` | duration_ms (total) | 🟢 | Термінал успіху |
| 25 | `LIA.Install.Failed` (top-level) | `Updater.cs:151` | duration_ms, error | 🟢 | Термінал збою |
| 26 | `LIA.RunInstallerScript.Started` | `Updater.cs:274` | — | 🔴 | Дублює #23 |
| 27 | `LIA.RunInstallerScript.Failed` (PowerShell exit) | `Updater.cs:293` | forensic full | 🟢 | **Найцінніша** — повний forensic (cert, appx_log, activity_id) |
| 28 | `LIA.RunInstallerScript.Failed` (LiaInstallException) | `Updater.cs:308` | forensic full | 🟢 | Дубль #27 за іншого шляху (exception vs exit-code) |
| 29 | `LIA.RunInstallerScript.Failed` (fallback) | `Updater.cs:316` | duration_ms, error | 🟢 | Термінал збою |
| 30 | `LIA.RunInstallerScript.Succeeded` | `Updater.cs:323` | duration_ms | 🔴 | Реконструюється з `LIA.Install.Succeeded` |

## 1.2 Підсумок Етапу 1

| Категорія | Усього | 🟢 Лишити | 🟡 Перетворити | 🔴 Видалити |
|---|---:|---:|---:|---:|
| Application | 1 | 1 | 0 | 0 |
| Auth | 1 | 1 | 0 | 0 |
| Installation | 1 | 1 | 0 | 0 |
| Updater | 12 | 7 | 1 | 4 |
| LIA | 15 | 7 | 0 | 8 |
| **РАЗОМ** | **30** | **17 (57%)** | **1 (3%)** | **12 (40%)** |

## 1.3 Економія записів у БД

Типова сесія користувача (1 старт + 1 LIA install + 1 update-check):

| Сценарій | Поточно | Після | Економія |
|---|---:|---:|---:|
| LIA Install успіх | 9 подій | 2 події (Start + Success) | **−78%** |
| LIA Install збій на download | 6 подій | 2 події (Start + Failed з phase) | **−67%** |
| App self-update успіх | 6 подій | 1 подія (Install.Succeeded) | **−83%** |
| App self-update verify-failed | 4 події | 1 подія (Verify.Failed) | **−75%** |
| App self-update download-failed | 2 події | 1 подія | **−50%** |
| **Сума типової сесії** | **~22** | **~7** | **−68%** |

**За рік при 1000 DAU:** 22 × 1000 × 365 = 8.03M → 7 × 1000 × 365 = 2.55M. **Економія ≈ 5.5M записів, ~550 MB.**

---

# ЕТАП 2 — БАЗА ДАНИХ

Прохід по кожній з 14 таблиць.

## 2.1 `telemetry_events` (28 колонок + detail jsonb)

| Колонка | Використовується? | Стан | Дія |
|---|---|---|---|
| `id` (PK) | ✓ Q9, Q22, T2 | активна | 🟢 |
| `client_event_id` (UNIQUE) | ✓ дедуп | активна | 🟢 |
| `session_id` | ✓ T3 (group) | активна | 🟢 |
| `correlation_id` | ✓ Q22, T1 (trace) | активна | 🟢 |
| `step` | ✓ Q22 ORDER BY | активна | 🟢 |
| `install_id` (FK) | ✓ Q22, views | активна | 🟢 |
| `user_id` | ✓ RLS | активна | 🟢 |
| `occurred_at` | ✓ Q22, T4 | активна | 🟢 |
| `received_at` | ✓ усі views | активна | 🟢 |
| `app_version` | ✓ release_health | активна | 🟢 |
| `git_commit` | ✓ widget | активна | 🟢 |
| `channel` | — | пишеться, не читається CC | 🟡 (тримати для audit) |
| `telemetry_version` | ✓ candidates | активна | 🟢 |
| `os_version` | ✓ widget | активна | 🟢 |
| **`country`** | ❌ **ЗАВЖДИ NULL** | мертва (див. §2.1.1) | 🔴 прибрати з C#-моделі |
| `component` | ✓ fingerprint | активна | 🟢 |
| `operation` | ✓ fingerprint | активна | 🟢 |
| `outcome` | ✓ усі views | активна | 🟢 |
| `severity` | ✓ Q5 | активна | 🟢 |
| `category` | — | пишеться, CC не фільтрує | 🟡 (тримати) |
| `source` | — | пишеться, рідко читається | 🟡 |
| `http_status` | ✓ signal-COALESCE | активна | 🟢 |
| `hresult` | ✓ signal-COALESCE | активна | 🟢 |
| `supabase_code` | ✓ signal-COALESCE | активна | 🟢 |
| `exception_type` | ✓ signal-COALESCE | активна | 🟢 |
| `error_message` | ✓ Q9 | активна | 🟢 |
| `duration_ms` | ✓ Q9, Q22 | активна | 🟢 |
| `detail` (jsonb) | ✓ Q22 | активна | 🟢 (чистка detail — §2.9) |

**§2.1.1 `country` — мертва колонка:** GeoIP-тригер `set_country_from_cf()` лише на `app_installations`. C# `TelemetryClient.BuildEvent()` ніколи не встановлює Country. Завжди NULL. **Дія:** прибрати `Country` з `TelemetryEvent.cs:58-59`. Колонку в БД не чіпати (additive-only).

### Індекси telemetry_events

| Індекс | Стан | Дія |
|---|---|---|
| PK `id` | ✓ | 🟢 |
| `uniq_telemetry_client_event_id` | ✓ дедуп | 🟢 |
| `idx_telemetry_received` (received_at DESC) | ✓ window-filter | 🟢 |
| `idx_telemetry_trace` (correlation_id, step) | ✓ trace | 🟢 |
| `idx_telemetry_detect` (component, operation, outcome, received_at) | ✓ promote | 🟢 |
| `idx_telemetry_install_id` | ✓ JOIN | 🟢 |
| `idx_telemetry_user_id` | ✓ RLS | 🟢 |
| **Partial `WHERE outcome='Failed'`** | ❌ **ВІДСУТНІЙ** | 🔴 **ДОДАТИ** — candidate-views роблять seq-scan |
| **`(app_version, received_at DESC)`** | ❌ **ВІДСУТНІЙ** | 🔴 **ДОДАТИ** — release_health |
| **`(occurred_at DESC)`** | ❌ **ВІДСУТНІЙ** | 🔴 **ДОДАТИ** — TraceRepository.SearchTraces |
| `idx_telemetry_source_signal` (source, exception_type) | ❌ 0 використань | 🟡 перевірити `pg_stat_user_indexes`, якщо 0 → DROP |

## 2.2 `telemetry_incidents` (18 колонок)

| Колонка | Стан | Дія |
|---|---|---|
| `id` (PK) | ✓ | 🟢 |
| `fingerprint_key` | ✓ фільтр | 🟢 |
| `fingerprint_hash` | ✓ idx | 🟢 |
| `release` | ✓ Q7, Q8 | 🟢 |
| `component` | ✓ Q7 | 🟢 |
| `operation` | ✓ Q7 | 🟢 |
| `signal` | ✓ Q7 | 🟢 |
| `root_event_id` (FK) | ✓ Q9 | 🟢 |
| `last_event_id` (FK) | ✓ | 🟢 |
| `opened_at` | ✓ Q7 ORDER BY | 🟢 |
| `last_event_at` | ✓ auto-close | 🟢 |
| `closed_at` | ✓ | 🟢 |
| `status` | ✓ Q7, Q8 | 🟢 |
| `highest_severity` | ✓ Q7 | 🟢 |
| `peak_failure_pct` | ✓ Q8 | 🟢 |
| `affected_users` | ✓ Q7 | 🟢 |
| `affected_installs` | ✓ Q7 | 🟢 |
| `event_count` | ✓ Q7 | 🟢 |
| `owner` | ✓ Q8 | 🟢 |

**Висновок:** Таблиця здорова, дублювання виправдане (materialized агрегат). Усі колонки активні.

### Індекси

| Індекс | Стан | Дія |
|---|---|---|
| `idx_incidents_fp_open` (fingerprint_hash WHERE status!='Closed') | ✓ | 🟢 |
| `idx_incidents_opened` (opened_at DESC) | ✓ | 🟢 |
| **Partial WHERE status='Active' на (component, signal)** | ❌ | 🟡 опціонально — Q7-Q8 фільтрують |

## 2.3 `app_installations` (18 колонок) — ⚠ 6 мертвих

| Колонка | Пишеться клієнтом? | Читається CC? | Стан | Дія |
|---|---|---|---|---|
| `id` (PK) | auto | ✓ user_analytics | активна | 🟢 |
| `created_at` | DEFAULT | ✓ | активна | 🟢 |
| `install_id` | ✓ Insert/Set | ✓ FK-зв'язок | активна | 🟢 |
| `app_version` | ✓ Insert/Set | ✓ user_analytics | активна | 🟢 |
| **`localization_version`** | ❌ **НЕ в C#-моделі** | — | **ЗАВЖДИ NULL** | 🔴 прибрати з C# або активувати |
| `country` | ✓ тригер Cloudflare | ✓ user_analytics | активна | 🟢 |
| `platform` | ✓ "Windows" | ✓ user_analytics | активна | 🟢 |
| `first_seen` | ✓ Insert | ✓ user_analytics | активна | 🟢 |
| `last_seen` | ✓ Insert/Set | ✓ user_analytics | активна | 🟢 |
| `user_id` | ✓ Insert/Set | ✓ RLS | активна | 🟢 |
| `machine_id` | ✓ Insert/Set | ✓ user_analytics | активна | 🟢 |
| `os_version` | ✓ Insert/Set | ✓ user_analytics | активна | 🟢 |
| **`os_build`** | ❌ **НЕ в C#-моделі** | — | **ЗАВЖДИ NULL** | 🔴 прибрати |
| **`update_channel`** | ❌ **НЕ в C#-моделі** (хоча в Settings!) | — | **ЗАВЖДИ NULL** | 🟡 активувати (Settings.UpdateChannel є) |
| **`install_source`** | ❌ DEFAULT 'unknown' | — | ЗАВЖДИ 'unknown' | 🔴 прибрати або активувати |
| **`game_folder_path`** | ❌ **НЕ в C#-моделі** | — | **ЗАВЖДИ NULL** | 🔴 прибрати або активувати |
| **`selected_environment`** | ❌ **НЕ в C#-моделі** | — | **ЗАВЖДИ NULL** | 🔴 прибрати або активувати |
| `is_active` | ✓ Insert/Set | ✓ user_analytics | активна | 🟢 |
| `updated_at` | ✓ Insert/Set | — | активна | 🟢 |

**Висновок:** **6 з 18 колонок (33%) — мертві.** `AppInstallation.cs` модель не має полів для `localization_version`, `os_build`, `update_channel`, `install_source`, `game_folder_path`, `selected_environment`.

**Дія (additive-only):**
- 🔴 Прибрати 6 колонок з міграції майбутнього (не DROP — лишити як schema-placeholder).
- 🟡 АБО активувати: додати ці поля в C#-модель і заповнювати з `Settings`/`MainWindow` (найцінніше — `update_channel`, `game_folder_path` для аналітики).

### Індекси app_installations

| Індекс | Стан | Дія |
|---|---|---|
| `idx_app_installations_user_id` | ✓ RLS, JOIN | 🟢 |
| `idx_app_installations_install_id` | ✓ (UNIQUE-єквівалент) | 🟢 |
| `idx_app_installations_machine_id` | — рідко | 🟡 перевірити використання |
| `idx_app_installations_last_seen` | ✓ user_analytics ORDER BY | 🟢 |
| **Partial WHERE is_active=true** | ❌ відсутній | 🟡 опціонально |

## 2.4 `incident_policy` (8 колонок)

Конфігураційна таблиця, 5 рядків (default + 4 component). Усі колонки активні (фильтр `promote_incident_candidates`). 🟢 Здорова.

## 2.5 `incident_status_log` (7 колонок) — append-only

Append-only журнал переходів. Усі колонки активні (Q15 timeline). 🟢 Здорова.

## 2.6 `incident_notes` (5 колонок) — append-only

Append-only нотатки. Усі колонки активні (Q16). 🟢 Здорова.

## 2.7 `notification_queue` (16 колонок)

| Колонка | Стан | Дія |
|---|---|---|
| `id` (PK) | ✓ | 🟢 |
| `incident_id` (FK) | ✓ | 🟢 |
| `notification_type` | ✓ | 🟢 |
| `provider` | ✓ DEFAULT 'Discord' | 🟢 |
| `status` | ✓ | 🟢 |
| `payload` (jsonb) | ✓ JSON з 8 ключів | 🟢 |
| `retry_count` | ✓ | 🟢 |
| `last_attempt_at` | ✓ | 🟢 |
| `delivered_at` | ✓ | 🟢 |
| `error_message` | ✓ | 🟢 |
| `created_at` | ✓ | 🟢 |
| `next_attempt_at` | ✓ retry | 🟢 |
| `max_retries` | ✓ DEFAULT 3 | 🟢 |
| `claimed_at` | ✓ zombie | 🟢 |
| `claimed_by` | ✓ zombie | 🟢 |
| `last_error` | ✓ | 🟢 |

🟢 Здорова. `payload` JSON дублює дані з incidents (component, signal, severity) — **виправдано** (snapshot на момент створення сповіщення).

## 2.8 `notification_attempts` (10 колонок) — append-only audit

Append-only аудит доставки. Усі колонки активні. 🟢 Здорова.

## 2.9 `knowledge_entries` (18 колонок) + references + version_history

Таблиця знань. Усі колонки активні (Q11-Q14 knowledge authoring). `affected_versions` (text[]) — активна. 🟢 Здорова.

`knowledge_references` (5 колонок) — active. 🟢
`knowledge_version_history` (8 колонок) — append-only audit, active. 🟢

## 2.10 `error_reports` (12 колонок) — ⚠ ПОРОЖНЯ

**Продюсер відсутній.** 0 посилань у C# коді. Таблиця створена "на виріст" (коментар міграції: "Продюсер... відсутній до появи потреби").

| Колонка | Стан |
|---|---|
| Усі 12 | ЗАВЖДИ порожня таблиця |

**Дія:**
- 🟡 Не DROP (schema готова до майбутнього crash-reporter).
- 🟡 Прибрати 5 індексів з міграції (`idx_error_*`) — вони розростаються на 0 рядків, але займають metadata-пам'ять. Або залишити (безпечно для порожньої таблиці).

## 2.11 `admin_audit_log` (7 колонок) — ⚠ ПОРОЖНЯ

**Продюсер відсутній.** 0 посилань у C#. "Для майбутньої адмін-панелі".

**Дія:** 🟡 Не DROP. 4 індекси на порожній таблиці — безпечно.

## 2.12 `user_discord_guilds` (5 колонок) — ⚠ ПРОДЮСЕР Є, АЛЕ НЕ ВИКЛИКАЄТЬСЯ

`DiscordGuildSyncService.SyncGuildsAsync` існує, інжектиться в AuthService, але **ніколи не викликається** (Architecture Review: "injected but never called").

**Дія:**
- 🟡 Або активувати виклик `SyncGuildsAsync` (рядок у AuthService після логіна).
- 🟡 Або прибрати сервіс інжект поки не потрібен (Community Center не реалізований).

## 2.13 JSON `detail` — дублювання

| JSON-ключ | Дублює SQL? | Дія |
|---|---|---|
| `phase` | НІ | 🟢 |
| **`retry_count`** | НІ, але завжди `0` | 🔴 прибрати (3 місця: LiaEvents.cs:46, UpdateEvents.cs:39, ErrorContextExtractor.cs:181) |
| `powershell_exit_code` | НІ | 🟢 |
| `installer_type` | НІ | 🟢 |
| `certificate_present` | НІ | 🟢 |
| `certificate_subject` | НІ | 🟢 |
| `certificate_thumbprint` | НІ | 🟢 |
| `activity_id` | НІ | 🟢 |
| `appx_log` | НІ | 🟢 |
| `package_version` | НІ (LIA-пакет, не app) | 🟢 |
| **`signal_name`** | **ТАК** — дублює computed `signal` у views | 🔴 прибрати (ErrorContextExtractor.cs:202-204) |

## 2.14 Підсумок Етапу 2

| Категорія | Знайдено | Дія |
|---|---:|---|
| Мертві колонки | **7** | 6 в `app_installations` + 1 в `telemetry_events` (country) |
| Мертві JSON-ключі | **2** | retry_count (завжди 0) + signal_name (дубль) |
| Мертві таблиці | **3** | error_reports, admin_audit_log, user_discord_guilds (порожні) |
| Відсутні critical індекси | **3** | partial Failed, version_window, occurred_at |
| Potential DROP індекси | **1** | idx_telemetry_source_signal (перевірити) |
| Дублювання між таблицями | 0 критичних | user_id повторюється — виправдано (RLS) |
| Нормалізація signal_id | 0 | відхилити (вигода не виправдовує) |

---

# ЕТАП 3 — CONTROL CENTER

## 3.1 Запити, що перестануть працювати після Етапу 1

**ЖОДЕН.** Усі 37 запитів CC фільтрують по `outcome` (Failed/Succeeded) та computed `signal` — **НЕ по конкретних signal-іменах** типу "Download.Started".

| Запит | File:Line | Вплив | Ламається? |
|---|---|---|:---:|
| Q1 `QueryIncidentsAsync` | Repository.cs:467 | працює з telemetry_incidents | ні |
| Q2 `QueryComponentHealthAsync` | :478 | component_health view | ні |
| Q3 `QueryReleaseHealthAsync` | :488 | release_health view | ні |
| Q4 `QueryPlatformStatsAsync` | :501 | platform_stats view | ні |
| Q5 `QueryIncidentByIdAsync` | :510 | incidents view | ні |
| Q6 `QueryEventSummaryAsync` | :535 | telemetry_events по id | ні |
| Q7 `QueryTraceAsync` | :549 | trace по correlation_id — **коротший trace** | ні (інформативніше) |
| Q8 `QueryRelatedEventsAsync` | :565 | component/operation/signal/version filter | ні |
| T1-T4 TraceRepository | TraceRepository.cs | trace коротший | ні |
| K1-K15 Knowledge | :73-345 | працює з knowledge_entries | ні |

## 3.2 View, які треба змінити

| View | Поточна проблема | Дія |
|---|---|---|
| `incident_candidates_live` | 2 CTE full-scans | 🟡 **Оптимізувати**: злити в 1 scan з `COUNT(*) FILTER` (§3.4) |
| `incident_candidates_24h` | 2 CTE full-scans | 🟡 Те саме |
| `release_health` | full-scan 7-денного вікна на page-load | 🔴 **Матеріалізувати** з CONCURRENTLY-refresh |
| `platform_stats` | full-scan events для COUNT | 🔴 **Матеріалізувати** |
| `component_health` | ланцюг view-на-view | 🔴 **Матеріалізувати** |
| `observability_health` | singleton, але kaskad | 🟡 оптимізувати after candidates |
| `incidents` | ✓ здоровий | 🟢 |
| `telemetry_events` | ✓ здоровий | 🟢 |
| `traces` | ✓ здоровий (можна прибрати, бо TraceRepository напряму) | 🟡 перевірити |
| `top_missing_knowledge` | ✓ агрегат | 🟢 |

## 3.3 Графіки/метрики, що зміняться

| Метрика на UI | Поточно | Після Етапу 1 | Дія |
|---|---|---|---|
| **"Succeeded events"** (Release Health page) | ~3× завищено (Download+Verify+Install = 3 Succeeded на 1 оновлення) | Точне число операцій | 🟡 Оновити підпис: "Successful operations" замість "Succeeded events" |
| **"Failed events"** | ~4× завищено (4 Failed-маркери на 1 збій) | Точне число збоїв | 🟡 Те саме: "Failed operations" |
| **Trace timeline** (Incident Detail) | 9 кроків для LIA install | 2 кроки | 🟢 Інформативніше, нульовий ризик |
| **Active Users 24h** | не зміниться | не зміниться | 🟢 |
| **DAU** | не зміниться (по session_id) | не зміниться | 🟢 |
| **Component Health (RED/YELLOW/GREEN)** | не зміниться (по incidents, не events) | не зміниться | 🟢 |

**Пропорція succeeded/failed у `release_health` залишається ТАКОЮ Ж** (бо Failed теж згортається з 4→1). Змінюються лише абсолютні числа.

## 3.4 Оптимізація `incident_candidates_live` (single-scan)

```sql
-- Було: 2 CTE (failed + totals) — 2 scans
-- Стало: 1 scan з FILTER
CREATE OR REPLACE VIEW control_center.incident_candidates_live AS
SELECT
    component || '|' || operation || '|' ||
        COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') || '|' ||
        app_version AS fingerprint_key,
    md5(component || '|' || operation || '|' ||
        COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') || '|' ||
        app_version) AS fingerprint_hash,
    app_version AS release,
    telemetry_version, component, operation,
    COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') AS signal,
    COUNT(*) FILTER (WHERE outcome = 'Failed') AS failed_now,
    COUNT(*) FILTER (WHERE outcome IN ('Succeeded','Failed')) AS total_now,
    ROUND(100.0 * COUNT(*) FILTER (WHERE outcome='Failed')
          / NULLIF(COUNT(*) FILTER (WHERE outcome IN ('Succeeded','Failed')), 0), 1) AS failure_pct,
    COUNT(DISTINCT install_id) FILTER (WHERE outcome='Failed') AS affected_installs,
    COUNT(DISTINCT user_id) FILTER (WHERE outcome='Failed') AS affected_users,
    MIN(received_at) FILTER (WHERE outcome='Failed') AS first_seen,
    MAX(received_at) FILTER (WHERE outcome='Failed') AS last_seen,
    CASE
        WHEN COUNT(DISTINCT install_id) FILTER (WHERE outcome='Failed') >= 10
          OR 100.0 * COUNT(*) FILTER (WHERE outcome='Failed')
             / NULLIF(COUNT(*) FILTER (WHERE outcome IN ('Succeeded','Failed')), 0) > 50
            THEN 'Critical'
        WHEN COUNT(*) FILTER (WHERE outcome='Failed') >= 5
         AND 100.0 * COUNT(*) FILTER (WHERE outcome='Failed')
             / NULLIF(COUNT(*) FILTER (WHERE outcome IN ('Succeeded','Failed')), 0) > 20
            THEN 'Warning'
        ELSE NULL
    END AS severity
FROM public.telemetry_events
WHERE received_at > now() - interval '10 minutes'
GROUP BY app_version, telemetry_version, component, operation,
         COALESCE(hresult, supabase_code, http_status::text, exception_type, '-')
HAVING COUNT(*) FILTER (WHERE outcome = 'Failed') >= 1;
```

**Економія:** −50% I/O на candidate-detection. З partial-index `idx_telemetry_failed` — ще −95%.

## 3.5 Спрощення Control Center

| Що спростити | Як |
|---|---|
| **3 views → 3 materialized views** | `release_health`, `platform_stats`, `component_health` з CONCURRENTLY-refresh раз на 1–5 хв |
| **1 view оптимізувати** | `incident_candidates_live/24h` — single-scan з FILTER |
| **Trace-кроки скоротити** | Після Етапу 1 — з 9 → 2 кроки на LIA install (автоматично) |
| **UI-підписи уточнити** | "Succeeded events" → "Successful operations" |
| **3 порожні таблиці — не чіпати** | error_reports, admin_audit_log, user_discord_guilds (schema ready) |

## 3.6 Підсумок Етапу 3

| Параметр | Значення |
|---|---|
| Запитів CC, що зламаються | **0** |
| Views треба оптимізувати | **2** (candidates live + 24h) |
| Views треба матеріалізувати | **3** (release_health, platform_stats, component_health) |
| Графіки, що змінять значення | **2** (Succeeded/Failed — абсолютні числа в 3× менші) |
| Графіки, що зламаються | **0** |
| UI-підписи оновити | **2** |

---

# ФІНАЛЬНА ЕКОНОМІЯ

| Параметр | До | Після | Економія |
|---|---:|---:|---:|
| Подій у БД (рік, 1000 DAU) | 8.03M | 2.55M | **−68%** |
| Місце (events, ~100 байт/рядок) | 800 MB | 255 MB | **−68%** |
| INSERT-roundtrip до Supabase | 8.03M | 2.55M | **−68%** |
| Candidate-detection I/O | 100% (2 scans) | 5% (1 scan + partial idx) | **−95%** |
| Dashboard page-load latency | 100% (full-scan views) | <10% (materialized) | **−90%** |
| Мертвих колонок | 7 | 0 (в C#-моделі) | **−100%** |
| Мертвих JSON-ключів | 2 | 0 | **−100%** |
| Контроль втрати діагностики | — | 0 втрат | **0% регресії** |

---

# ПОРЯДОК ВИКОНАННЯ

| Фаза | Дія | Ризик | Ефект |
|---|---|---|---|
| **0** | Додати 3 індекси (partial Failed, version_window, occurred_at) | 0 (additive) | −95% candidate-I/O |
| **1** | Видалити 12 подій + 1 перетворити (C#-код) | низький | −68% записів |
| **2** | Прибрати 7 мертвих колонок з C# + 2 JSON-ключі | 0 | чистіша модель |
| **3** | Оптимізувати 2 candidate-views + матеріалізувати 3 views | середній | −90% page-load |
| **4** | Опц.: активувати `update_channel`/`game_folder_path` в app_installations | низький | аналітика збільшується |

**Чого НЕ робити:**
- ❌ DROP COLUMN (порушить additive-only контракт)
- ❌ Нормалізація signal → signal_id
- ❌ DROP порожніх таблиць (schema ready до майбутнього)
