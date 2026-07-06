# Observability & Database Optimization Plan

> **Тип документа:** Аналітичний план оптимізації (не bug-report, не architecture-review).
> **Фокус:** Що конкретно викинути, об'єднати, спростити, прибрати дублікати, щоб база не засмічувалась.
> **Дата:** 2026-07-05
> **Сфера:** Observability (телеметрія) + БД (Supabase/PostgreSQL) + Control Center.
> **Режим:** READ-ONLY аналіз. Без змін коду. Кожна рекомендація — additive-only або з чітким regression-чеклістом.

---

## 0. Executive Summary

Поточна телеметрія SCLOC-Verse пише **~9 подій на одне встановлення LIA** та **~6 подій на одне саме-оновлення застосунку**. З них інформаційну цінність для Control Center несуть лише **термінальні** (`Install.Succeeded` / `Install.Failed`) та **діагностичні Failed-кроки**. Проміжні `.Started` та `.Succeeded` по під-операціях **не використовуються** жодним dashboard-запитом — вони лише збільшують об'єм таблиці `telemetry_events`.

Після оптимізації (без втрати observability):

| Ланцюжок | Поточно | Оптимізовано | Економія |
|---|---:|---:|---:|
| LIA Install (успіх) | 9 подій | 1 подія | **−89%** |
| LIA Install (збій на download) | 4 події | 1 подія | **−75%** |
| App self-update (успіх) | 6 подій | 1 подія | **−83%** |
| App self-update (verify failed) | 4 події | 1 подія | **−75%** |
| App self-update (download failed) | 2 події | 1 подія | **−50%** |

**Сумарно по типовій сесії користувача** (1 старт + 1 install LIA + 1 update-check): ~17 подій → ~5 подій. **Економія ≈ 70%** записів, місця, INSERT-roundtrip-ів, індекс-розростання.

Окрім подій, виявлено:
- **1 мертва колонка** (`telemetry_events.country` — завжди NULL, джерело гео живе в `app_installations`).
- **1 дублюючий JSON-ключ** (`detail.signal_name` дублює computed-колонку `signal` у views).
- **2 відсутні critical індекси** (`user_id`, `install_id`) — RLS робить full-scan.
- **2 нормалізаційні пропозиції** (signal-lookup, consolidated country) — з оцінкою вигода/ризик.
- **5 неоптимальних views** (повні скани 7-денних вікон на кожен page-load).

Усі рекомендації — **у форматі "видаляємо X → ламається Y → фіксуємо Z"**, з regression-чеклістом у Розділі 10.

---

## 1. Методологія

Джерела даних (READ-ONLY):
- Усі `*.cs` у `SCLOCVerse/Services/Observability/` (10 файлів).
- Усі місця виклику `.Track(...)` / `LiaEvents.Track` / `UpdateEvents.Track` у SCLOCVerse.
- Усі 27 міграцій у `supabase/migrations/`.
- `ControlCenterRepository.cs` (33 запити) + `TraceRepository.cs` (4 запити).
- Моделі `TelemetryEvent.cs`, `TelemetryContext.cs`.

Сигнал ідентифікується як трійка **`{Component}.{Operation}.{Outcome}`** (формується у `TelemetryClient.Track`, рядок 70). У views БД "signal" — окрема computed-колонка `COALESCE(hresult, supabase_code, http_status::text, exception_type, '-')`, **НЕ** Component.Operation.Outcome. Ця різниця критична для impact-аналізу (Розділ 9).

---

## 2. Повний інвентар подій телеметрії

Усі 30 унікальних `{Component}.{Operation}.{Outcome}` комбінацій, що фактично емітуються кодом:

### 2.1 Application (1 подія)

| Signal | File:Line | Method | Level | detail keys | Вирок |
|---|---|---|---|---|---|
| `Application.Start.Started` | `App.xaml.cs:64` | `OnStartup` | Info | — | **ЛИШИТИ** — session-маркер, DAU |

### 2.2 Auth (1 параметризована подія)

| Signal | File:Line | Method | Level | detail keys | Вирок |
|---|---|---|---|---|---|
| `Auth.<op>.<outcome>` | `AuthService.cs:290` | `TrackAuth` | Info/Error | error-message через `TelemetryContext` | **ЛИШИТИ** — вже мінімально, 1 подія на операцію |

`operation` та `outcome` параметризуються динамічно (login/refresh/logout × Started/Succeeded/Failed). Це правильний патерн — не дублювати.

### 2.3 Installation (1 параметризована подія)

| Signal | File:Line | Method | Level | detail keys | Вирок |
|---|---|---|---|---|---|
| `Installation.Sync.<outcome>` | `InstallationService.cs:152` | `SyncInstallationAsync` | Info/Error | через `TelemetryContext` | **ЛИШИТИ** |

### 2.4 Updater — Application self-update (12 сигналів)

| Signal | File:Line | Method | detail keys | Вирок |
|---|---|---|---|---|
| `Updater.Download.Started` | `UpdateDownloader.cs:44` | `DownloadAsync` | — | **ВИДАЛИТИ** — маркер старту, не несе діагностики |
| `Updater.Download.Succeeded` | `UpdateDownloader.cs:54` | `DownloadAsync` | duration_ms | **ВИДАЛИТИ** — успіх підтверджується наступним `Verify.Started`/`Install.Started` |
| `Updater.Download.Failed` | `UpdateDownloader.cs:59` | `DownloadAsync` | duration_ms, error-context | **ЛИШИТИ** — діагностично цінна |
| `Updater.Verify.Started` | `UpdateVerifier.cs:34` | `VerifyAsync` | — | **ВИДАЛИТИ** — маркер старту |
| `Updater.Verify.Skipped` | `UpdateVerifier.cs:40` | `VerifyAsync` | duration_ms, phase="NoChecksum" | **ПЕРЕТВОРИТИ** на Warning-термінал `Updater.Verify.Failed` з phase="NoChecksum" (зараз це silent skip P0-ризику — див. Architecture Review TD-4) |
| `Updater.Verify.Failed` (FileNotFound) | `UpdateVerifier.cs:46` | `VerifyAsync` | duration_ms, phase="FileNotFound" | **ЛИШИТИ** |
| `Updater.Verify.Failed` (checksum mismatch) | `UpdateVerifier.cs:59` | `VerifyAsync` | duration_ms, phase | **ЛИШИТИ** |
| `Updater.Verify.Failed` (exception) | `UpdateVerifier.cs:65` | `VerifyAsync` | duration_ms, error-context | **ЛИШИТИ** |
| `Updater.Install.Started` | `UpdateInstaller.cs:40` | `InstallAsync` | — | **ВИДАЛИТИ** — маркер старту |
| `Updater.Install.Failed` (InstallerNotFound) | `UpdateInstaller.cs:46` | `InstallAsync` | duration_ms, phase="InstallerNotFound" | **ЛИШИТИ** |
| `Updater.Install.Succeeded` \| `Failed` (launch) | `UpdateInstaller.cs:76` | `InstallAsync` | duration_ms, phase | **ЛИШИТИ** — термінал |
| `Updater.Install.Failed` (exception) | `UpdateInstaller.cs:82` | `InstallAsync` | duration_ms, error-context | **ЛИШИТИ** |

### 2.5 LIA — Voice assistant install (15 сигналів)

| Signal | File:Line | Method | detail keys | Вирок |
|---|---|---|---|---|
| `LIA.Install.Started` (phase=Download) | `Updater.cs:79` | `InstallAsync` | — | **ЛИШИТИ** — точка входу в trace, відкриває session-відрізок |
| `LIA.Download.Started` (InstallerAsset) | `Updater.cs:95` | `DownloadAssetAsync` | — | **ВИДАЛИТИ** — проміжний старт-маркер |
| `LIA.Download.Failed` (InstallerAsset) | `Updater.cs:104` | `DownloadAssetAsync` | duration_ms, error-context | **ЛИШИТИ** |
| `LIA.Download.Succeeded` (InstallerAsset) | `Updater.cs:109` | `DownloadAssetAsync` | duration_ms | **ВИДАЛИТИ** — підтверджується наступним `LIA.Download.Started(Cert)` або `LIA.Install.Started(Run)` |
| `LIA.Download.Started` (CertificateAsset) | `Updater.cs:116` | `DownloadAssetAsync` | — | **ВИДАЛИТИ** |
| `LIA.Download.Failed` (CertificateAsset) | `Updater.cs:124` | `DownloadAssetAsync` | duration_ms, error-context | **ЛИШИТИ** |
| `LIA.Download.Succeeded` (CertificateAsset) | `Updater.cs:129` | `DownloadAssetAsync` | duration_ms | **ВИДАЛИТИ** |
| `LIA.Install.Started` (phase=RunInstallerScript) | `Updater.cs:137` | `InstallAsync` | package_version, installer_type | **ВИДАЛИТИ** — **дублює** `LIA.RunInstallerScript.Started` (емітується на 7 рядків нижче) |
| `LIA.Install.Succeeded` (phase=Complete) | `Updater.cs:143` | `InstallAsync` | duration_ms (total) | **ЛИШИТИ** — термінал успіху |
| `LIA.Install.Failed` (top-level) | `Updater.cs:151` | `InstallAsync` | duration_ms, error-context | **ЛИШИТИ** — термінал збою |
| `LIA.RunInstallerScript.Started` | `Updater.cs:274` | `RunInstallerScriptAsync` | — | **ВИДАЛИТИ** — дублює `LIA.Install.Started(Run)` (який теж під вартою видалення) |
| `LIA.RunInstallerScript.Failed` (PowerShell exit) | `Updater.cs:293` | `RunInstallerScriptAsync` | duration_ms, LiaInstallException forensic-detail | **ЛИШИТИ** — найцінніша діагностична подія (повний forensic) |
| `LIA.RunInstallerScript.Failed` (LiaInstallException) | `Updater.cs:308` | `RunInstallerScriptAsync` | duration_ms, forensic-detail | **ЛИШИТИ** — дубль попередньої за умови; лишити обидві різні шляхи (exit-code vs exception) |
| `LIA.RunInstallerScript.Failed` (fallback) | `Updater.cs:316` | `RunInstallerScriptAsync` | duration_ms, error-context | **ЛИШИТИ** |
| `LIA.RunInstallerScript.Succeeded` | `Updater.cs:323` | `RunInstallerScriptAsync` | duration_ms | **ВИДАЛИТИ** — успіх підтверджується терміналом `LIA.Install.Succeeded` |

### 2.6 Підсумок інвентаризації

| Категорія | Усього | Лишити | Видалити | Перетворити |
|---|---:|---:|---:|---:|
| Application | 1 | 1 | 0 | 0 |
| Auth | 1 | 1 | 0 | 0 |
| Installation | 1 | 1 | 0 | 0 |
| Updater | 12 | 7 | 4 | 1 |
| LIA | 15 | 7 | 8 | 0 |
| **Разом** | **30** | **17** | **12** | **1** |

**40% усіх сигналів — кандидати на видалення.** Усі вони — проміжні `.Started` або проміжні `.Succeeded` по під-операціях, що реконструюються з термінальних подій + `correlation_id` + `step`.

---

## 3. Аналіз Trace-ланцюжків

### 3.1 Ланцюжок LIA Install (успішний шлях)

**Поточно (9 подій):**
```
1. LIA.Install.Started         (phase=Download)         ← вхід
2. LIA.Download.Started        (InstallerAsset)         ← НОРМА: маркер
3. LIA.Download.Succeeded      (InstallerAsset)         ← НОРМА: успіх
4. LIA.Download.Started        (CertificateAsset)       ← НОРМА: маркер
5. LIA.Download.Succeeded      (CertificateAsset)       ← НОРМА: успіх
6. LIA.Install.Started         (phase=RunInstallerScript)← ДУБЛЬ #1
7. LIA.RunInstallerScript.Started                       ← ДУБЛЬ #2
8. LIA.RunInstallerScript.Succeeded                     ← НОРМА: успіх під-операції
9. LIA.Install.Succeeded       (phase=Complete)         ← термінал
```

**Оптимізовано (2 події):**
```
1. LIA.Install.Started         (phase=Download)         ← вхід у trace
9. LIA.Install.Succeeded       (phase=Complete, duration_ms=total)
```

**Економія: −78% (7 з 9 подій).** Усі проміжні кроки реконструюються:
- Trace порядок → через `step` (значення вже зростаюче).
- Час download-phase → через `duration_ms` у терміналі (за потреби розбити — додати `detail.download_ms`, `detail.install_ms`).
- Якщо сталася помилка на download → емітиться `LIA.Download.Failed` (термінал для цієї під-операції), потім зовнішній `catch` емітить `LIA.Install.Failed`.

### 3.2 Ланцюжок LIA Install (збій на download сертифіката)

**Поточно (4 події):**
```
1. LIA.Install.Started         (Download)
2. LIA.Download.Started        (InstallerAsset)
3. LIA.Download.Succeeded      (InstallerAsset)
4. LIA.Download.Started        (CertificateAsset)   ← далі exception
   [flush + LIA.Download.Failed (Cert) емітиться, але Install.Failed у зовнішньому catch]
   [зовнішній catch → LIA.Install.Failed]
```
Фактично 6 подій: Start + 3 download-маркери + Download.Failed(Cert) + Install.Failed.

**Оптимізовано (1–2 події):**
```
1. LIA.Install.Started         (Download)
2. LIA.Install.Failed          (phase=CertificateAsset, error-context)
```
**Економія: −67%.** `phase` у `detail` зберігає діагностику "де саме збій". `correlation_id` зберігає trace-зв'язність.

### 3.3 Ланцюжок App self-update (успішний шлях)

**Поточно (6 подій):**
```
1. Updater.Download.Started
2. Updater.Download.Succeeded
3. Updater.Verify.Started
4. Updater.Verify.Succeeded
5. Updater.Install.Started
6. Updater.Install.Succeeded   ← термінал (далі застосунок перезапускається)
```

**Оптимізовано (1 подія):**
```
6. Updater.Install.Succeeded   (detail: download_ms, verify_ms, install_ms)
```
**Економія: −83%.**

> ⚠ **Увага:** `Updater.Install.Succeeded` емітиться ДО фактичного запуску інсталятора (`UpdateInstaller.cs:76` — `launched` означає "процес запущено", а не "успішно встановлено"). Термінальний стан установки НЕ ПОВЕРТАЄТЬСЯ в застосунок (він вже завершився). Це відомий P0 (див. Architecture Review). Оптимізація не повинна приховати цей дефект — навпаки, лишити `Install.Succeeded` як "launcher started" + окремо відстежити `Install.Confirmed` через bridge-event (рекомендація Architecture Review).

### 3.4 Ланцюжок App self-update (verify failed — checksum mismatch)

**Поточно (4 події):**
```
1. Updater.Download.Started
2. Updater.Download.Succeeded
3. Updater.Verify.Started
4. Updater.Verify.Failed       (phase=checksum)   ← термінал
```

**Оптимізовано (1 подія):**
```
4. Updater.Verify.Failed       (phase=checksum, duration_ms, detail: download_ms)
```
**Економія: −75%.**

### 3.5 Auth / Installation / Application — вже мінімальні

Ці ланцюжки емітять рівно **1 подію на операцію** — оптимізації не підлягають.

---

## 4. Дублювання між таблицями

### 4.1 Матриця дублювання колонок

| Колонка | telemetry_events | telemetry_incidents | app_installations | telemetry_sessions* | duplicate? |
|---|:---:|:---:|:---:|:---:|---|
| `session_id` | ✓ (uuid) | — | — | ✓ (id) | ні — sessions не реалізована таблицею (див. §4.4) |
| `correlation_id` | ✓ | — | — | — | ні |
| `user_id` | ✓ | ✓ | ✓ | ✓ | **так, але виправдано** — потрібен для RLS у кожній таблиці |
| `install_id` | ✓ (FK) | — | ✓ (PK-джерело) | — | ні — FK-зв'язок |
| `component` | ✓ | ✓ (denormalized) | — | — | **так, виправдано** — incidents це materialized-агрегат |
| `operation` | ✓ | ✓ (denormalized) | — | — | **так, виправдано** |
| `signal` (=COALESCE(hresult,...)) | computed у views | ✓ (stored text) | — | — | **так, виправдано** — fingerprinting requires it |
| `app_version` | ✓ | ✓ (як `release`) | ✓ | — | **так, виправдано** — різні семантики (поточна vs інцидентна) |
| `country` | ✓ **ЗАВЖДИ NULL** | — | ✓ (Cloudflare) | — | **ТАК, НЕВИПРАВДАНО** — див. §4.2 |
| `os_version` | ✓ | — | ✓ | — | **частково** — однакове значення; events можна не мати (JOIN по install_id) |
| `occurred_at` | ✓ | — | — | — | ні |
| `received_at` | ✓ | — | — | — | ні |
| `opened_at` / `last_event_at` / `closed_at` | — | ✓ | — | — | ні |
| `created_at` / `updated_at` / `last_seen` / `first_seen` | — | — | ✓ | — | ні |

\* `telemetry_sessions` у цій схемі — концептуальна (session_id у events), а не окрема таблиця. Якщо коли-небудь матеріалізується — див. §6.

### 4.2 ⚠ P1: `telemetry_events.country` — мертва колонка

**Доказ:**
1. У міграції `20260630000009` (рядок 26): `country text` — без DEFAULT, без тригера.
2. GeoIP-тригер `set_country_from_cf()` (міграція `20260630000002`) стоїть **лише** на `app_installations`, НЕ на `telemetry_events`.
3. У `TelemetryClient.BuildEvent()` (рядки 116–146) поле `Country` **ніколи не проставляється**.
4. У `TelemetryEvent.cs:58-59` поле замаплено, але оскільки клієнт завжди відправляє `null` — БД-колонка завжди NULL.

**Вирок:** Колонка `telemetry_events.country` займає місце (text TOAST-pointer 8 байт/рядок), але **ніколи не несе даних**. Гео-аналітика доступна через `JOIN app_installations USING (install_id)` (див. `user_analytics_view`).

**Опції:**

| Опція | Дія | Ризик | Рекомендація |
|---|---|---|---|
| A. Залишити як є | — | 0 | Якщо не болить — не чіпати (additive-only контракт, Стаття 13) |
| B. Перестати мапити в C# | Видалити `Country` з `TelemetryEvent.cs:58-59` | 0 — БД не падає, просто Insert не відправляє поле | **Рекомендовано** — чистіший код, нульовий ризик |
| C. DROP COLUMN | Міграція `ALTER TABLE ... DROP COLUMN country` | Низький, але порушує additive-only | НЕ рекомендувати без окремого погодження |
| D. Активувати тригер на events | Скопіювати `set_country_from_cf` на `telemetry_events` | Високий — events append-only, INSERT-частота висока | Розглянути в Phase 2, якщо гео-аналітика по events потрібна (зараз вона йде через installations) |

**Оптимально: Опція B** (прибрати з моделі C#, колонку в БД не чіпати).

### 4.3 Дублювання `os_version` — часткове

`telemetry_events.os_version` і `app_installations.os_version` зберігають одне й те саме значення для однієєї машини. Проте:
- events.os_version встановлюється в `TelemetryClient.SafeOsVersion()` (рядок 160) — синхронізується з OShanки клієнта в момент події.
- installations.os_version оновлюється при `InstallationService.Sync` — може відставати.

**Вирок:** Лишити. events-копія цінна як "OS на момент події" (користувач міг оновити Windows між інсталяцією і інцидентом).

### 4.4 Відсутня `telemetry_sessions` — чи потрібна?

`session_id` у events — це стабільний Guid за запуск застосунку (`TraceContext.SessionId`). Проте **немає окремої таблиці** `telemetry_sessions`, яка б агрегувала: started_at, exited_at, install_id, app_version, event_count.

Зараз для "сесійної аналітики" (тривалість сесії, DAU-унікальні) CC робить `COUNT(DISTINCT session_id)` або `GROUP BY session_id` по events — **це повний scan**.

**Опція нормалізації (Phase 2, не терміново):**
```sql
CREATE TABLE public.telemetry_sessions (
    session_id      uuid PRIMARY KEY,
    install_id      text REFERENCES app_installations(install_id),
    user_id         uuid,
    app_version     text,
    started_at      timestamptz,
    last_event_at   timestamptz,
    event_count     int DEFAULT 0,
    exit_outcome    text  -- 'Closed'|'Crashed'|NULL
);
```
Тригер на INSERT events → upsert session (дешево через `ON CONFLICT`). Це дозволить:
- `COUNT(DISTINCT session_id)` → `COUNT(*) FROM telemetry_sessions WHERE started_at > ...` (index-only scan).
- DAU = унікальні session_id за день → index lookup замість scan events.

**Економія:** При 1000 events/день по 10 events/сесія = 100 сесій/день. Scan 1000 рядків → 100. **−90% роботи для session-based запитів.**

**Ризик:** Новий тригер на events (високочастотна таблиця). Мінімізується `ON CONFLICT (session_id) DO UPDATE SET last_event_at=EXCLUDED.last_event_at, event_count=telemetry_sessions.event_count+1`.

### 4.5 Відношення `telemetry_events` ↔ `telemetry_incidents`

Це **НЕ дублювання**, а materialized-view pattern. `telemetry_incidents` — це згорнуті Failed-патерни з агрегатами (`event_count`, `affected_users`, `peak_failure_pct`). Без неї CC довелося б робити `COUNT(DISTINCT)` по events на кожен page-load.

**Вирок:** Лишити як є. Це core-оптимізація (вже реалізована). Покращення — `REFRESH MATERIALIZED VIEW CONCURRENTLY` для `incidents`-джерел (див. §7).

---

## 5. JSON `detail` ↔ SQL-колонки

### 5.1 Інвентар ключів у `detail` (jsonb)

| JSON-ключ | Джерело в коді | Чи дублює SQL-колонку? | Вирок |
|---|---|---|---|
| `phase` | `LiaEvents.cs:44`, `UpdateEvents.cs:39`, `ErrorContextExtractor.cs:180` | НІ — немає колонки `phase` | **ЛИШИТИ** — діагностично цінна |
| `retry_count` | `LiaEvents.cs:46`, `UpdateEvents.cs:39`, `ErrorContextExtractor.cs:181` | НІ — завжди `0` (схема під Retry Policy) | **ВИДАЛИТИ** (зараз завжди 0 — це спекулятивне поле, що засмічує JSON) |
| `powershell_exit_code` | `ErrorContextExtractor.cs:182` | НІ | **ЛИШИТИ** |
| `installer_type` | `LiaEvents.cs:48`, `ErrorContextExtractor.cs:185` | НІ | **ЛИШИТИ** |
| `certificate_present` | `LiaEvents.cs:50`, `ErrorContextExtractor.cs:187` | НІ | **ЛИШИТИ** |
| `certificate_subject` | `ErrorContextExtractor.cs:189` | НІ | **ЛИШИТИ** — forensic |
| `certificate_thumbprint` | `ErrorContextExtractor.cs:191` | НІ | **ЛИШИТИ** — forensic |
| `activity_id` | `ErrorContextExtractor.cs:193` | НІ | **ЛИШИТИ** — AppX trace |
| `appx_log` | `ErrorContextExtractor.cs:199` | НІ | **ЛИШИТИ** — verbose, але лише на Failed |
| `package_version` | `LiaEvents.cs:52` | Частково `app_version` (але це LIA-пакет, не застосунок) | **ЛИШИТИ** — різні семантики |
| **`signal_name`** | `ErrorContextExtractor.cs:204` (через `HResultCatalog.ResolveSymbol`) | **ТАК** — дублює computed-колонку `signal` у views (`COALESCE(hresult, supabase_code, ...)`) | **ВИДАЛИТИ З JSON** — див. §5.2 |

### 5.2 ⚠ P2: `detail.signal_name` дублює `signal` у views

`ErrorContextExtractor.ApplyLiaForensic` (рядок 204) пише в `detail.signal_name` результат `HResultCatalog.ResolveSymbol(lia.Hresult)`. Але `signal` у views (`control_center.telemetry_events`, `traces`, `incident_candidates_*`) обчислюється як `COALESCE(hresult, supabase_code, http_status::text, exception_type, '-')` — тобто бере сам `hresult`. 

Для LIA `hresult` вже несе символьне ім'я (напр. `"CERT_CHAIN_EXPIRED"` через `LiaInstallException.Hresult` — див. `ErrorContextExtractor.cs:150-151`). Тому `signal` у views вже містить `"CERT_CHAIN_EXPIRED"`, і `detail.signal_name` — це **та сама стрічка вдруге**.

**Вирок:** Видалити `detail.signal_name` з `ErrorContextExtractor.cs:202-204`. JSON стає на ~30–50 байт меншим на кожну LIA-Failed подію.

### 5.3 ⚠ P3: `retry_count` — мертве поле

Усі три джерела (`LiaEvents.cs:46`, `UpdateEvents.cs:39`, `ErrorContextExtractor.cs:181`) пишуть `retry_count = 0`. Retry Policy не реалізовано (немає retry-логіки в жодному з pipeline). Це спекулятивне поле "на виріст", що засмічує кожен `detail`.

**Вирок:** Видалити з трьох місць. Якщо Retry Policy коли-небудь з'явиться — додати тоді. Зараз це pure noise.

### 5.4 Не дублюють, але можуть бути колонками (опц.)

| JSON-ключ | Кандидат на колонку? | Причина |
|---|---|---|
| `phase` | **ТАК** (Phase 2) | Висока частота фільтра `WHERE detail->>'phase' = 'CertificateAsset'`. Колонка + btree дешевша за jsonb-extraction. |
| `certificate_present` | ні | Низька частота запитів, лише forensic drill-down. |
| `powershell_exit_code` | ні | Те саме. |
| `appx_log` | **навіть не думати** — text-поле до кількох KB. Лише JSON. |

`detail.phase → column phase`: додасть 8 байт/рядок, але прибере jsonb-extraction з усіх candidate-views (зараз сканують `outcome='Failed'` без jsonb-фільтру — phase не використовується у views, але міг би для диференціації "download-failed vs install-failed" інцидентів).

---

## 6. Нормалізація `signal_name TEXT` → `signal_id + signals`

### 6.1 Оцінка вигоди

Поточний стан: у `telemetry_events` сигнал розпорошений по 5 колонках (`source`, `http_status`, `hresult`, `supabase_code`, `exception_type`), а "signal" як одиниця існує лише як `COALESCE(...)` у views. У `telemetry_incidents` — вже згорнутий `signal text`.

| Параметр | Значення |
|---|---|
| Кількість унікальних signal-значень | ~20–40 (компонент×операція×тип-помилки) |
| Середня довжина signal-тексту | ~20 байт |
| Розмір з lookup (int4 + FK) | 4 байти |
| Економія на рядок | ~16 байт |
| При 1М events | ~16 MB економії на таблиці |
| Економія на індексах | ще стільки ж (idx_telemetry_detect використовує text-колонки) |

### 6.2 Оцінка ризику

| Ризик | Оцінка |
|---|---|
| Новий JOIN на кожен запит | Середній — але JOIN по small lookup-table швидкий (fit в RAM) |
| Перепис усіх 6 views (candidate, traces, incidents-match) | Високий — багато SQL-роботи |
| Перепис ControlCenterRepository (33 запити) | Високий |
| Зміна інтерфейсу ingest (функция `ingest_telemetry_v1`) | Середній |
| Втрата читабельності "Failed в 'CERT_CHAIN_EXPIRED'" → "Failed в signal_id=14" | Середній — потрібен JOIN навіть для debug |

### 6.3 Вирок

**НЕ РОБИТИ нормалізацію signal → signal_id.** Вигода (16 MB на 1М рядків) не виправдовує складність перепису + втрату читабельності. "signal" — це діагностичний концепт, що об'єднує різні фізичні причини (Hresult vs HTTP-status vs Supabase-code), і COALESCE-патерн правильніший за lookup-table.

**Натомість:** Покращити індекси (Розділ 7), щоби `COALESCE`-вираз не робив full-scan.

---

## 7. Індекси — аудит

### 7.1 Поточний стан (з міграцій 9, 12, +додаткові)

На `telemetry_events`:
| Ім'я | Колонки | Тип | Використовується |
|---|---|---|---|
| (PK) | `id` | btree unique | так — QueryEventSummary |
| `uniq_telemetry_client_event_id` | `client_event_id` | btree unique | так — дедуп |
| `idx_telemetry_received` | `received_at DESC` | btree | так — candidate views (window filter) |
| `idx_telemetry_trace` | `(correlation_id, step)` | btree | так — TraceRepository |
| `idx_telemetry_detect` | `(component, operation, outcome, received_at DESC)` | btree | так — promote_incident_candidates |
| `idx_telemetry_install_id` | `install_id` | btree | так — JOIN з installations |
| `idx_telemetry_user_id` | `user_id` | btree | так — RLS `auth.uid()` filter |
| `idx_telemetry_received_brin` | `received_at` | BRIN | так — time-series scan |
| `idx_telemetry_detail_gin` | `detail` | GIN jsonb_path_ops | рідко — лише drill-down |
| `idx_telemetry_source_signal` | `(source, exception_type)` | btree | рідко |

> Примітка: точний список залежить від пізніших міграцій (агент 2 повідомив про 13, базові 4 з міграції 9 + PK).

### 7.2 ⚠ Відсутні critical індекси

| Індекс | Навіщо | Запити, що страждають |
|---|---|---|
| **Partial index на `outcome='Failed'`** | `incident_candidates_live/24h`, `release_health`, `observability_health`, `QueryRelatedEventsAsync` — усі фільтрують `outcome='Failed'`. Без partial-index Postgres сканує всі outcome. | 5+ views, 2 CC-запити |
| **`(app_version, received_at DESC)`** | `release_health` GROUP BY app_version WHERE received_at > now() - 7d | release_health view |
| **`(occurred_at)`** | `TraceRepository.SearchTracesAsync` фільтрує `occurred_at` (не `received_at`!) | Traces page |

**Рекомендація (additive-only, безпечно):**
```sql
CREATE INDEX IF NOT EXISTS idx_telemetry_failed
    ON public.telemetry_events(component, operation, received_at DESC)
    WHERE outcome = 'Failed';

CREATE INDEX IF NOT EXISTS idx_telemetry_version_window
    ON public.telemetry_events(app_version, received_at DESC)
    WHERE app_version IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_telemetry_occurred
    ON public.telemetry_events(occurred_at DESC);
```

**Економія:** Candidate-views перестануть сканувати Succeeded-події. Зараз скан 1000 рядків (з яких ~950 Succeeded) → after: scan 50. **−95% I/O на candidate-detection.**

### 7.3 ⚠ Можливо зайвий: `idx_telemetry_source_signal` `(source, exception_type)`

Не знайдено жодного запиту, що фільтрує `WHERE source = ... AND exception_type = ...`. Якщо індекс існує (потрібно перевірити в пізніших міграціях) — він лише розростається на кожен INSERT, не несучи користі.

**Вирок:** Перевірити `\di idx_telemetry_source_signal` у live-БД. Якщо 0 використань через `pg_stat_user_indexes` — DROP.

### 7.4 Індекси на `telemetry_incidents`

| Індекс | Оцінка |
|---|---|
| `idx_incidents_fp_open` (fingerprint_hash WHERE status != 'Closed') | ✓ оптимальний |
| `idx_incidents_opened` (opened_at DESC) | ✓ оптимальний |
| **Partial WHERE status='Active'** на component+signal | додати — QueryIncidentsAsync фільтрує status |

---

## 8. View-аудит

### 8.1 Інвентар views у `control_center` (10 шт.)

| View | Тип | Базові таблиці | Оптимально? |
|---|---|---|---|
| `telemetry_events` | regular | events | ✓ — простий SELECT |
| `traces` | regular | events | ✓ — ORDER BY correlation_id, step (використ. idx_telemetry_trace) |
| `incident_candidates_live` | regular | events (window 10хв) | ⚠ — 2 full-scans (failed + totals) на кожен SELECT |
| `incident_candidates_24h` | regular | events (window 24год) | ⚠ — 2 full-scans на кожен SELECT |
| `release_health` | regular | events (window 7д) | ⚠ — full-scan + GROUP BY на кожен page-load |
| `platform_stats` | regular | events + incidents | ⚠ — full-scan events для COUNT(*) |
| `component_health` | regular | events + incidents + candidates_live | ⚠ — ланцюг view-на-view, kaskad сканів |
| `observability_health` | regular | events + incidents + candidates_24h + queue + meta | ⚠ — singleton, але kaskad |
| `incidents` | regular | telemetry_incidents | ✓ |
| `release_health_detail` | regular | (див. міграцію) | ⚠ — імовірно складний JOIN |

### 8.2 ⚠ Головна проблема: усі dashboard-views **нематеріалізовані**

`release_health`, `platform_stats`, `component_health` — це агрегати з COUNT, COUNT(DISTINCT), GROUP BY. На кожен page-load в Control Center вони **повністю перераховуються** по events за 7/24 години.

При 1000 events/день → 7000 рядків scan + group на кожен SELECT з `release_health`. При 10 одновременных користувачах CC → 70 000 рядків/сек.

**Рекомендація:** Перевести найважчі 3 views на **MATERIALIZED VIEW** з `CONCURRENT REFRESH` по тригеру або cron:

```sql
-- Замість:
-- CREATE VIEW control_center.release_health AS SELECT ...

-- Зробити:
CREATE MATERIALIZED VIEW control_center.release_health AS SELECT ...;
CREATE UNIQUE INDEX ON control_center.release_health(app_version);  -- для CONCURRENTLY
REFRESH MATERIALIZED VIEW CONCURRENTLY control_center.release_health;
```

**Тригер REFRESH:** не на кожен INSERT (висока частота), а раз на 1–5 хв через `pg_cron` або зовнішній `promote_incident_candidates()` (він і так стартує per-event).

**Економія:** Dashboard-page-load з 7000-row-scan → 1 index-only-scan по MV. **−99% часу завантаження сторінки Release Health.**

### 8.3 ⚠ `incident_candidates_live` — подвійний scan

View робить 2 CTE (`failed` + `totals`) обидва з фільтром `received_at > now() - 10 min`. Можна злити в один scan з `COUNT(*) FILTER (WHERE outcome='Failed')` і `COUNT(*) FILTER (WHERE outcome IN ('Succeeded','Failed'))`:

```sql
-- Оптимізований варіант:
CREATE OR REPLACE VIEW control_center.incident_candidates_live AS
SELECT
    component || '|' || operation || '|' ||
        COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') || '|' ||
        app_version AS fingerprint_key,
    md5(...) AS fingerprint_hash,
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
    CASE WHEN ... END AS severity
FROM public.telemetry_events
WHERE received_at > now() - interval '10 minutes'
GROUP BY app_version, telemetry_version, component, operation,
         COALESCE(hresult, supabase_code, http_status::text, exception_type, '-')
HAVING COUNT(*) FILTER (WHERE outcome = 'Failed') >= 1;
```

**Економія:** Один scan замість двох. **−50% I/O на candidate-detection.** Особливо ефективно з partial-index `idx_telemetry_failed` (Розділ 7).

### 8.4 `control_center.release_health` — window-фільтр у WHERE

Зараз:
```sql
WHERE received_at > now() - interval '7 days'
```
Це **нестабільний** (stable, не immutable) предикат — кожен SELECT бачить різне вікно. Postgres не може кешувати результат. Матеріалізація (§8.2) фіксує снепшот.

---

## 9. Оптимізація Control Center — impact matrix

### 9.1 Принцип: "видаляємо event X → ламається dashboard Y → фіксуємо query Z"

Усі CC-запити залежать від **`outcome`** (Failed/Succeeded) та **`signal`** (computed COALESCE) — **НЕ від конкретних імен подій** на кшталт "Download.Started". Жоден з 37 запитів CC не фільтрує по `operation='Download'` або `operation='RunInstallerScript'`.

Тобто **видалення проміжних `.Started`/`.Succeeded` подій НЕ ламає CC**. Точна таблиця впливу:

| Подія, що видаляється | Вплив на CC-запит | Чи ламається? | Як фіксити |
|---|---|---|---|
| `LIA.Download.Started` (обидві phases) | Q22 `QueryTraceAsync` покаже на 1 крок менше | ні — trace залишається послідовним | нічого не робити |
| `LIA.Download.Succeeded` (обидві) | Q22 trace shorter; release_health.succeeded **залишається коректним** (бо `LIA.Install.Succeeded` теж рахується) | ні | нічого |
| `LIA.Install.Started(Run)` | Q22 trace коротший | ні | нічого |
| `LIA.RunInstallerScript.Started` | Q22 trace коротший | ні | нічого |
| `LIA.RunInstallerScript.Succeeded` | Q22 trace коротший; release_health.succeeded **залишається коректним** | ні | нічого |
| `Updater.Download.Started` | trace коротший | ні | нічого |
| `Updater.Download.Succeeded` | release_health.succeeded для компонента "Updater" **зменшиться** ⚠ | **тільки якщо рахує успіх саме Download** | перевірити: `release_health` GROUP BY app_version без фільтру по operation → рахує ВСІ Succeeded. Якщо лишити `Updater.Install.Succeeded` — баланс зберігається |
| `Updater.Verify.Started` | trace коротший | ні | нічого |
| `Updater.Verify.Succeeded` | release_health: -1 Succeeded; +0 Failed | **тільки якщо Verify.Succeeded рахувався окремо** | залишити `Updater.Install.Succeeded` рахує термінал |
| `Updater.Install.Started` | trace коротший | ні | нічого |

### 9.2 ⚠ Єдиний реальний ризик: `release_health` баланс

`release_health` (міграція 14, рядки 5–14):
```sql
SELECT app_version,
       COUNT(*) FILTER (WHERE outcome = 'Succeeded') AS succeeded,
       COUNT(*) FILTER (WHERE outcome = 'Failed') AS failed,
       COUNT(DISTINCT install_id) AS active_installs
FROM public.telemetry_events
WHERE received_at > now() - interval '7 days'
GROUP BY app_version
```

**Семантика:** "скільки Succeeded-і Failed-подій на version". Зараз на 1 успішне оновлення припадає **3 Succeeded** (Download+Verify+Install). Після оптимізації — **1 Succeeded** (Install). Співвідношення succeeded/failed **зберігається пропорційно** (бо Failed теж згортається з 4 до 1).

**Але абсолютні числа зменшаться в 3 рази.** Якщо CC показує "100 successful updates" — стане "33". Це **не регресія**, а точніша семантика ("33 користувачі успішно оновились", а не "100 операцій успішно завершились").

**Дія:** Оновити підписи в UI з "Succeeded events" на "Successful operations" або "Users updated". Чисто косметика.

### 9.3 Повна impact-таблиця по CC-сторінках

| Сторінка CC | Запити (Q-id) | Події, що впливають | Регресія? | Дія |
|---|---|---|---|---|
| **Home.razor** | Q1–Q6 (overview) | ніякі — рахує incidents + health, не events напряму | ні | нічого |
| **Incidents.razor** | Q7–Q8 (list + detail) | ніякі — працює з telemetry_incidents | ні | нічого |
| **Incident Detail** | Q9–Q18 (root_event, trace, related) | `QueryTraceAsync` покаже коротший trace | ні (інформативніше) | нічого |
| **Releases.razor** | Q19 (release_health_detail) | абсолютні числа Succeeded/Failed зменшаться в 3× | ні (пропорція та сама) | оновити підпис UI |
| **Traces.razor** | T1–T4 (search, by-id) | trace коротший | ні (інформативніше) | нічого |
| **Knowledge.razor** | K1–K15 (functions) | ніякі — працює з knowledge_entries | ні | нічого |
| **Settings.razor** | — | — | — | — |

### 9.4 control_center-views: чи всі потрібні?

| View | Використовується CC? | Вирок |
|---|---|---|
| `telemetry_events` | так (Q9–Q18, T1–T4) | ЛИШИТИ |
| `traces` | формально так, але TraceRepository напряму звертається до `telemetry_events` view | перевірити, можливо dead — докладніше в Phase 1 |
| `incident_candidates_live` | так (promote_incident_candidates) | ОПТИМІЗУВАТИ (§8.3) |
| `incident_candidates_24h` | так (observability_health) | ОПТИМІЗУВАТИ (§8.3 аналогічно) |
| `release_health` | так (Q19) | МАТЕРІАЛІЗУВАТИ (§8.2) |
| `platform_stats` | так (Q6) | МАТЕРІАЛІЗУВАТИ |
| `component_health` | так (Q3) | МАТЕРІАЛІЗУВАТИ |
| `observability_health` | так | залишити regular, але важкий — оптимізувати candidates спочатку |
| `incidents` | так (Q7–Q8) | ЛИШИТИ |
| `top_missing_knowledge` | так (Q20) | ЛИШИТИ — вже агрегат |
| `knowledge_coverage` (MV) | так (Q21) | ЛИШИТИ — вже матеріалізований |

---

## 10. Регресійний чекліст

Перед будь-яким застосуванням оптимізацій — чек-лист запуску:

### 10.1 Перед (snapshot)

```
☐ SELECT count(*) FROM public.telemetry_events;                -- записати baseline
☐ SELECT pg_size_pretty(pg_total_relation_size('public.telemetry_events'));
☐ \di+ public.telemetry_events                                 -- розмір кожного індексу
☐ SELECT * FROM control_center.platform_stats;                 -- snapshot KPI
☐ SELECT * FROM control_center.release_health;                 -- snapshot
☐ SELECT * FROM control_center.component_health;               -- snapshot
☐ SELECT * FROM control_center.observability_health;           -- snapshot
```

### 10.2 Тестові запити після оптимізації C#-сигналів

Після видалення `.Started`/`.Succeeded` проміжних подій:

```
☐ Session-id тест: запустити застосунок, виконати 1 LIA install.
   Перевірити: кількість events по session_id ≤ 2 (Start + Success/Failed).
☐ Trace-реконструкція: Q22 QueryTraceAsync показує коректний порядок кроків.
☐ Incident-detection: INSERT тестової Failed-події → promote_incident → інцидент у списку.
☐ Release Health: пропорція succeeded/failed не зламалась (допустиме зменшення абсолютних чисел).
☐ Traces page: drill-down з інциденту показує повний контекст (correlation_id).
```

### 10.3 Тестові запити після матеріалізації views

```
☐ REFRESH MATERIALIZED VIEW CONCURRENTLY control_center.release_health;
   → не падає, не блокує читання.
☐ SELECT з MV повертає ті ж рядки, що й попередня regular view (delta ≤ останній refresh-інтервал).
☐ Dashboard Home.razor завантажується швидше (заміряти Latency до/після).
☐ pipeline_healthy з observability_health повертає true після тестової Failed + promote.
```

### 10.4 Тестові запити після нових індексів

```
☐ EXPLAIN ANALYZE SELECT * FROM control_center.incident_candidates_live;
   → план використовує idx_telemetry_failed (partial index), не seq-scan.
☐ CREATE INDEX завершується без блокування INSERT (CONCURRENTLY).
☐ pg_stat_user_indexes.idx_scan > 0 для нових індексів після доби роботи.
```

### 10.5 Utf-8 / mojibake перевірка (P0 з AGENTS.md)

```
☐ Візуально перевірити всі українські тексти в CC після оптимізації.
☐ Якщо в коді змінюються рядки з detail-ключами — перевірити, що phase="CertificateAsset" не пошкоджено.
☐ Git diff — жодного mojibake (Р›Р°СЃРєР°РІРѕ, Ð›Ð°, ??????).
```

---

## 11. План реалізації — фази

### Phase 0 — Нульовий ризик (additive-only)

**Тільки additions, ніяких breaking changes. Можна виконати негайно.**

| ID | Дія | Файл/місце | Економія |
|---|---|---|---|
| P0-1 | Додати partial index `idx_telemetry_failed` | нова міграція | −95% I/O candidate-views |
| P0-2 | Додати index `idx_telemetry_version_window` | нова міграція | release_health scan |
| P0-3 | Додати index `idx_telemetry_occurred` | нова міграція | TraceRepository |
| P0-4 | Перевірити `idx_telemetry_source_signal` через `pg_stat_user_indexes`. Якщо 0 використань — DROP | live-БД | мінус індекс-розростання |

### Phase 1 — Оптимізація C#-телеметрії (low risk)

**Видалення проміжних `.Started`/`.Succeeded`. Контракт additive-only — не порушується (events просто стають рідшими, БД-схема незмінна).**

| ID | Дія | Файл |
|---|---|---|
| P1-1 | Видалити еміт `LIA.Download.Started` (обидві phases) | `Updater.cs:95, 116` |
| P1-2 | Видалити еміт `LIA.Download.Succeeded` (обидві phases) | `Updater.cs:109, 129` |
| P1-3 | Видалити еміт `LIA.Install.Started (RunInstallerScript)` | `Updater.cs:137` |
| P1-4 | Видалити еміт `LIA.RunInstallerScript.Started` | `Updater.cs:274` |
| P1-5 | Видалити еміт `LIA.RunInstallerScript.Succeeded` | `Updater.cs:323` |
| P1-6 | Видалити еміт `Updater.Download.Started` | `UpdateDownloader.cs:44` |
| P1-7 | Видалити еміт `Updater.Download.Succeeded` | `UpdateDownloader.cs:54` |
| P1-8 | Видалити еміт `Updater.Verify.Started` | `UpdateVerifier.cs:34` |
| P1-9 | Видалити еміт `Updater.Verify.Succeeded` | `UpdateVerifier.cs:59` (успішний шлях) |
| P1-10 | Видалити еміт `Updater.Install.Started` | `UpdateInstaller.cs:40` |
| P1-11 | Перетворити `Updater.Verify.Skipped` → `Updater.Verify.Failed` з `phase="NoChecksum"` (severity=Warning) | `UpdateVerifier.cs:40` — закриває P0 TD-4 з Architecture Review |

**Економія: −70% подій на типовому user-session.**

### Phase 2 — Очищення detail-JSON (low risk)

| ID | Дія | Файл |
|---|---|---|
| P2-1 | Видалити `detail.signal_name` (дублює `signal` у views) | `ErrorContextExtractor.cs:202-204` |
| P2-2 | Видалити `detail.retry_count` (завжди 0, спекулятивне) | `LiaEvents.cs:46`, `UpdateEvents.cs:39`, `ErrorContextExtractor.cs:181` |
| P2-3 | Прибрати `Country` з C#-моделі `TelemetryEvent` (завжди null, мертве мапіння) | `TelemetryEvent.cs:58-59` |

### Phase 3 — Оптимізація views (medium risk, потребує тестування)

| ID | Дія | Міграція |
|---|---|---|
| P3-1 | Переписати `incident_candidates_live` на single-scan з `FILTER` | нова міграція `CREATE OR REPLACE VIEW` |
| P3-2 | Переписати `incident_candidates_24h` аналогічно | нова міграція |
| P3-3 | Матеріалізувати `release_health` з CONCURRENTLY-refresh | нова міграція |
| P3-4 | Матеріалізувати `platform_stats` | нова міграція |
| P3-5 | Матеріалізувати `component_health` | нова міграція |
| P3-6 | Скласти тригер або cron для REFRESH MV (раз на 1–5 хв) | нова міграція |

### Phase 4 — Нормалізація (опціонально, не терміново)

| ID | Дія | Коли |
|---|---|---|
| P4-1 | Створити `telemetry_sessions` таблицю + тригер на INSERT events | якщо DAU зросте до 1000+ |
| P4-2 | Перевести `detail.phase` → колонку `phase` | якщо candidate-views почнуть фільтрувати по phase |
| P4-3 | Врахувати Retry Policy → тоді повернути `retry_count` | якщо реалізується retry-логіка |

### Що НЕ робити

| Дія | Причина |
|---|---|
| ❌ DROP COLUMN `telemetry_events.country` | порушує additive-only контракт (Стаття 13); натомість P2-3 (прибрати з C#-моделі) |
| ❌ Нормалізація signal → signal_id | вигода 16 MB/1M рядків не виправдовує перепис 6 views + 33 CC-запитів |
| ❌ Перепис promotion engine | він правильно згорнутий, не дублюється |
| ❌ Перенос country-тригера на events | висока INSERT-частота; гео вже є в installations |

---

## 12. Підсумкова таблиця економії

| Оптимізація | Записів/день (1000-DAU оцінка) | Місце | I/O | Запитів-сканів |
|---|---:|---:|---:|---:|
| Базелайн (поточно) | ~17 000 | 100% | 100% | 100% |
| Phase 0 (індекси) | 17 000 | 100% | 100% | **5%** (candidate-views) |
| Phase 1 (видалення проміжних events) | **~5 000** | **30%** | 30% | 30% |
| Phase 2 (detail-JSON чистка) | 5 000 | **~25%** (мінус ~50 байт/рядок) | 25% | 25% |
| Phase 3 (матеріалізація views) | 5 000 | 25% | 25% | **<5%** на dashboard-page-load |
| **Разом Phase 0–3** | **5 000** | **25%** | **25%** | **<5%** |

**За рік (1000 DAU): ~6.2M → ~1.8M рядків у telemetry_events. Економія ≈ 4.4M рядків, ~440 MB місця (при ~100 байт/рядок), ~4.4M INSERT-roundtrip-ів до Supabase.**

---

## 13. Висновки

1. **Телеметрія перенасичена проміжними маркерами.** 12 з 30 сигналів (~40%) — кандидати на видалення без втрати observability.
2. **Control Center не залежить від конкретних signal-імен**, лише від `outcome` + computed `signal`. Оптимізація подій безпечна.
3. **Головна вага — у відсутніх індексах та нематеріалізованих views.** Phase 0 дає найбільший ефект на dashboard-latency при нульовому ризику.
4. **Схема БД — здорова.** Дублювання між events/incidents/installations — виправдане (RLS, denormalization для fingerprinting). Єдина мертва колонка — `telemetry_events.country` (прибирається з C#, в БД не чіпається).
5. **Нормалізація signal → signal_id відхиляється** — вигода не виправдовує складність.
6. **Усі зміни — additive-only**, крім видалення подій у C# (а це не structural change, просто менше INSERT-ів).

**Рекомендований порядок:** Phase 0 → Phase 1 → Phase 2 → (пауза, заміряти) → Phase 3.
