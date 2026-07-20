# Telemetry Registry — Single Source of Truth

> **Дочірній документ Knowledge Base (KB §5).**
> Містить: Policy (3 рівні), Event Registry (39 емітерів), Field Registry (outcome-dependent), Reader Validation.
> Джерело: KB §5.8–5.11 + §14.20.1 Test Matrix (винесено 2026-07-20 при KB optimization Variant B).
> Живий документ — оновлюється синхронно з кодом телеметрії.

---

## 1. Telemetry Policy — 3 рівні (Phase 3.5, IMPL 2026-07-07)

> Відповідає на 3 питання: що надсилається завжди? Що лише при Developer Diagnostics? Що ніколи не виходить із комп'ютера користувача?

| Рівень | Назва | Вимикається? | Опис |
|---|---|---|---|
| **L1** | **Mandatory** | ❌ Ні | Статистика життя продукту. Без неї неможливо оцінити release health, success rate, інциденти. Анонімізована (install_id, app_version) + технічні результати. |
| **L2** | **Diagnostic** | ✅ Так (Developer Diagnostics) | Розширена діагностика для розробника: проміжні кроки, трейси, forensic payload (cert, hresult, stack), duration_ms, detail.phase. |
| **L3** | **Local Only** | ❌ Ніколи не відправляється | Hotkeys, debug log, performance, FPS, input traces, verbose. Лише локальний журнал (якщо буде). |

### 1.1. Архітектура Policy — варіант E (Enum level)

> **Forensic (2026-07-07):** `TelemetryPolicy.Classify(component, operation)` занадто крихке — перейменування `LocalizationInstall`→`LocalizationInstaller` ламає політику. Розглянуто 5 варіантів (Attribute/StronglyTyped/Registry/ContextTag/EnumLevel). **Обрано Enum level**.

```csharp
public enum TelemetryLevel { Mandatory, Diagnostic, Local }

public interface ITelemetryService {
    Task Track(string component, string operation, string outcome,
               TelemetryContext? ctx = null,
               TelemetryLevel level = TelemetryLevel.Mandatory);  // default для backward compat
}
```

**Переваги:**
- Не крихке до перейменувань `component/operation` (Level вшитий у виклик).
- Існуючі виклики `.Track()` за замовчуванням стають L1 (default параметр) — не ламає код.
- L2 виклики додають `level: TelemetryLevel.Diagnostic` явно.
- Читається одразу в коді емітера.

**Перевірка `IsDiagnosticEnabled` всередині `Track`:**
```csharp
if (level == TelemetryLevel.Diagnostic && !settings.DeveloperDiagnostics) return;
```

### 1.2. Чекбокс «Розширена діагностика» (БЕЗ перейменування)

| Аспект | Станом |
|---|---|
| UI id | `AdvancedDiagnosticsCheckBox` — **НЕ чіпати** (частина UI, релізований контракт) |
| Label | `Content="Розширена діагностика"` — **НЕ чіпати** |
| Settings key | `Settings.Default.AdvancedDiagnostics` — лишити (внутрішнє ім'я) |
| Читання | `MainWindow.xaml.cs:403` |
| Запис | `MainWindow.xaml.cs:450-456` |
| Default | `false` (opt-in) |

**Changes лише внутрішньо:** `TelemetryClient.Track()` перевіряє `settings.AdvancedDiagnostics` перед відправкою L2. UI не змінюється.

### 1.3. Прив'язка `app_installations` FUTURE колонок до Policy

| Колонка | Policy Level | Reason |
|---|---|---|
| `app_version`, `os_version`, `machine_id`, `country`, `platform` | **L1 Mandatory** | базова статистика релізів + Release Health |
| `localization_version`, `selected_environment`, `update_channel` | **L1 Mandatory** | контекст установки, потрібен для статистики релізів |
| `game_folder_path` | **L2 Diagnostic** | немає доведеного споживача в Release Health/Incidents/Statistics → Diagnostic |
| `install_source` | **L2 Diagnostic** | не доведено, що потрібен для дашборду |
| `os_build` | **L2 Diagnostic** | деталізація OS, корисна для debug |

---

## 2. Event Registry — 39 `.Track()` емітерів

> **Single Source of Truth для кожного `.Track()` емітера.** Кожен має Level + Reason. Verified через grep `.Track(` у SCLOCVerse/**/*.cs.

### 2.1. Реєстр емітерів

| # | Component/Operation/Outcome | Level | Файл:рядок | Reason |
|---|---|---|---|---|
| 1 | Application/Start/Started | **L2** | `App.xaml.cs:81` | Pre-auth launch signal (Zero Noise Policy) |
| 2 | Auth/Login/Success | **L1** | `AuthService.cs:297` | OAuth success rate — Release Health |
| 3 | Auth/Login/Failed | **L1** | `AuthService.cs:297` | Auth failure rate — критичний інцидент |
| 4 | Installation/Sync/Success | **L1** | `InstallationService.cs:152` | Installation sync success rate |
| 5 | Installation/Sync/Failed | **L1** | `InstallationService.cs:152` | 42501 або інша помилка синхронізації (критичний) |
| 6 | Orchestrator/Cycle/Failed | **L1** | `BackgroundUpdateMonitor.cs:131` | Збій циклу оновлень — інфраструктурна проблема |
| 7 | Orchestrator/AppCheck/UpdateFound | **L2** | `BackgroundUpdateMonitor.cs:148` | Adoption нових релізів (Zero Noise) |
| 8 | Orchestrator/AppCheck/Failed | **L1** | `BackgroundUpdateMonitor.cs:153` | Не вдалося перевірити оновлення |
| 9 | Orchestrator/LocalizationCheck/{outcome} | **L1** | `BackgroundUpdateMonitor.cs:198` | Localization pipeline health |
| 10 | Orchestrator/LocalizationCheck/Failed | **L1** | `BackgroundUpdateMonitor.cs:207` | Критичний збій локалізації |
| 11 | Orchestrator/LiaCheck/UpdateFound | **L2** | `BackgroundUpdateMonitor.cs:224` | LIA adoption (Zero Noise) |
| 12 | Orchestrator/LiaCheck/Failed | **L1** | `BackgroundUpdateMonitor.cs:229` | Не вдалося перевірити LIA оновлення |
| 13 | Updater/Download/Failed (terminal) | **L1** | `UpdateDownloader.cs:69` | Критичний збій завантаження оновлення |
| 14 | Updater/Verify/Failed (terminal) | **L1** | `UpdateVerifier.cs:46,59,65` | Критичний збій перевірки checksum/Authenticode |
| 15 | Updater/Install/Failed (terminal) | **L1** | `UpdateInstaller.cs:46,76,82` | Критичний збій встановлення оновлення |
| 16 | LIA/Install/Succeeded | **L1** | `Updater.cs:265` | LIA install success rate (фінальний) |
| 17 | LIA/Install/Failed (terminal) | **L1** | `Updater.cs:273` | Критичний збій LIA встановлення (фінальний) |
| 18 | LIA/Download/Failed (terminal) | **L1** | `Updater.cs:226,246` | Не вдалося завантажити LIA package або cert |
| 19 | Updater/Download/Started | **L2** | `UpdateDownloader.cs:46` | Проміжний крок, лише для trace діагностики |
| 20 | Updater/Download/Succeeded | **L2** | `UpdateDownloader.cs:64` | Не потрібен для release health |
| 21 | Updater/Verify/Started | **L2** | `UpdateVerifier.cs:34` | Проміжний крок verify |
| 22 | Updater/Verify/Skipped | **L2** | `UpdateVerifier.cs:40` | NoChecksum — діагностична інформація |
| 23 | Updater/Verify/Succeeded | **L2** | `UpdateVerifier.cs:59` | Проміжний крок verify |
| 24 | Updater/Install/Started | **L2** | `UpdateInstaller.cs:40` | Проміжний крок install |
| 25 | Updater/Install/Succeeded | **L2** | `UpdateInstaller.cs:76` | Проміжний (cascade trace) |
| 26 | LIA/Install/Started (orchestration) | **L2** | `Updater.cs:201,259` | Проміжний крок LIA install |
| 27 | LIA/Download/Started | **L2** | `Updater.cs:217,238` | Проміжний крок LIA download (InstallerAsset, CertificateAsset) |
| 28 | LIA/Download/Succeeded | **L2** | `Updater.cs:231,251` | Проміжний крок LIA download |
| 29 | LIA/RunInstallerScript/Started | **L2** | `Updater.cs:555` | Детальний trace PowerShell script execution |
| 30 | LIA/RunInstallerScript/Succeeded | **L2** | `Updater.cs:604` | Детальний trace |
| 31 | LIA/RunInstallerScript/Failed ×3 (cascade) | **L2** | `Updater.cs:574,589,597` | Детальний trace з different fallback exception |

**Підсумок:** **21 L1 Mandatory + 18 L2 Diagnostic + 0 L3 = 39 .Track() емітерів** (+ 2 делегуючих: `LiaEvents.cs:56`, `UpdateEvents.cs:43`).

> **Zero Noise Policy (#139 KB):** L1 = **тільки Failed події** + фінальні Succeeded. Усі Started/Skipped/UpdateFound/Updated → L2 Diagnostic. При `AdvancedDiagnostics=false` успішний запуск → 0 L1 подій.

### 2.2. Level 3 — Local Only (ніколи не відправляється)

| Дані | Де | Стан |
|---|---|---|
| Hotkey events (key-up) | `HotkeyService.cs`, `RawInputBackend` | ❌ локально |
| Debug.WriteLine traces | `AuthService.cs:381`, `HotkeyService.cs:268`, `LiaEvents` debug | ❌ локально |
| InputDiagnostics (HWND, window title, key sequence) | `InputDiagnostics.cs:37` | ❌ локально (file log) |
| HangarTimer cycle ms, FPS | `HangarTimerService.cs`, `HomeCanvas.xaml.cs` | ❌ локально |
| Performance counters | (future) | ❌ локально |

**0 `.Track()` викликів.** Архітектурне правило: нова перформанс/вхідна телеметрія — лише локально.

---

## 3. Field Registry — Outcome-dependent (Phase 3.5)

> L1 розділено на **L1 Success** (мінімальний набір) та **L1 Failed** (розширений мінімальний — error context завжди, навіть без чекбокса). `error_message` входить у L1 (для Failed).

| Поле | L1 Success | L1 Failed | L2 Diagnostic | Reader | Reason |
|---|---|---|---|---|---|
| `install_id` | ✅ | ✅ | — | FK + cc.release_health + incident_candidates | Identity |
| `user_id` | ✅ | ✅ | — | FK + cc.platform_stats + cc.release_health_detail | Identity |
| `session_id` | ✅ | ✅ | — | TraceRepository.cs:73,125 + cc.unfinished_started | Trace grouping |
| `correlation_id` | ✅ | ✅ | — | TraceRepository.cs:19,37,40 + cc.traces | Trace reconstruction |
| `step` | ✅ | ✅ | — | cc.traces (ORDER BY step) | Trace ordering |
| `component` | ✅ | ✅ | — | cc.release_health + incident_candidates + fingerprint | Classification |
| `operation` | ✅ | ✅ | — | cc.release_health + incident_candidates + fingerprint | Classification |
| `outcome` | ✅ | ✅ | — | cc.release_health + trg_telemetry_failed_promote | Status |
| `severity` | ✅ | ✅ | — | cc.telemetry_events | Status |
| `app_version` | ✅ | ✅ | — | cc.release_health (GROUP BY app_version) + fingerprint | Version |
| `telemetry_version` | ✅ | ✅ | — | cc.telemetry_events | Schema |
| `channel` | ✅ | ✅ | — | cc.telemetry_events | Config |
| `occurred_at` | ✅ | ✅ | — | cc.traces + cc.unfinished_started | Time |
| `received_at` | ✅ | ✅ | — | cc.release_health + incident_candidates + platform_stats + observability_health | Time |
| `os_version` | ✅ | ✅ | — | cc.telemetry_events | Environment |
| `country` | ✅ | ✅ | — | cc.telemetry_events | Geography |
| `error_message` | ❌ | ✅ | — | ControlCenterRepository.cs:540,570 + TraceRepository.cs:111,144 | **Human-readable** (Exception.Message через PrivacySanitizer). Не дубль exception_type — дає пояснення, не тип. |
| `source` | ❌ | ✅ | — | ControlCenterRepository.cs:539,573 (COALESCE priority 1) | Incident fingerprint priority 1 (Supabase/Network/PowerShell/COM/CLR) |
| `hresult` | ❌ | ✅ | — | ControlCenterRepository.cs:539,573 (COALESCE priority 3) | Incident fingerprint priority 3 |
| `exception_type` | ❌ | ✅ | — | ControlCenterRepository.cs:539,573 (COALESCE priority 5) | Incident fingerprint fallback |
| `supabase_code` | ❌ | ❌ | ❌ (DEPRECATED) | — | 100% NULL. CHECK вимагає колонку (additive-only). |
| `http_status` | ❌ | ❌ | ❌ (DEPRECATED) | — | 100% NULL. |
| `duration_ms` | — | — | ✅ | cc.traces + cc.telemetry_events | Performance diagnostics |
| `detail.phase` | — | — | ✅ | cc.telemetry_events (jsonb) | Cascade trace |
| `detail.retry_count` | — | — | ✅ | cc.telemetry_events (jsonb) | FUTURE (Retry Policy, зараз 0) |
| `detail.certificate_present` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.certificate_subject` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.certificate_thumbprint` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.installer_type` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.package_version` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.activity_id` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.powershell_exit_code` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.appx_log` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic |
| `detail.signal_name` | — | — | ✅ | cc.telemetry_events (jsonb) | LIA forensic (0 у даних) |
| `detail` (jsonb container) | ❌ | ✅ (LIA forensic) | ✅ | cc.telemetry_events | Container для forensic keys |

### 3.1. Outcome-dependent Field Policy (модель)

```
L1 Success (мінімальний набір — 16 полів):
    install_id, user_id, session_id, correlation_id, step
    component, operation, outcome, severity, category
    app_version, telemetry_version, channel
    occurred_at, received_at
    os_version, country (trigger)
    → без error-context (немає exception)

L1 Failed (розширений мінімальний — 21 поле, навіть без чекбокса):
    + error_message (Exception.Message через PrivacySanitizer)
    + source (ErrorContextExtractor.ClassifySource: Supabase/Network/PowerShell/COM/CLR)
    + hresult (priority 3 в signal COALESCE)
    + exception_type (priority 5 fallback в signal COALESCE)
    + detail (jsonb container для LIA forensic)
    → ErrorContextExtractor.Extract(exception) — завжди для Failed

L2 Diagnostic (лише при AdvancedDiagnostics ON — будь-який outcome):
    + duration_ms
    + detail.phase (orchestrationPhase)
    + detail.retry_count (FUTURE — Retry Policy)
    + detail.certificate_present / certificate_subject / certificate_thumbprint
    + detail.installer_type, package_version
    + detail.activity_id, powershell_exit_code, appx_log
    + detail.signal_name (HResultCatalog.ResolveSymbol)
    + game_folder_path, install_source, os_build (app_installations L2)

L3 Local Only (ніколи не відправляється):
    hotkeys, debug.WriteLine, performance, FPS, input traces
    → file journal (future), 0 .Track() викликів
```

**Реалізація:** `ErrorContextExtractor.Extract(exception)` викликається лише при `exception != null` (Failed). Для Success — `ctx = null` або мінімальний. Нова модель не вимагає переписування коду — лише чітке документування.

**Перевірка `TelemetryClient.Track()`:**
```csharp
if (level == TelemetryLevel.Diagnostic && !settings.AdvancedDiagnostics) return;
// L1 events проходять завжди (Succeeded + Failed)
// L1 Failed events несуть error_message + source + hresult + exception_type (через ErrorContextExtractor)
// L2 events несуть + duration_ms + detail.* (через LiaEvents/UpdateEvents параметри)
```

---

## 4. Reader Validation — доказ споживача для L1

> **Forensic за вимогою користувача:** для кожного L1 емітера та поля — довести реального Reader. Якщо Reader = ніхто → переглянути рівень.

### 4.1. Reader Validation для L1 емітерів

> Production факт: більшість L1 Failed-емітерів НІКОЛИ не траплялись у даних (бо система працювала успішно). Але вони залишаються L1, бо при збій — потрібні для Incident Pipeline.

| Event | Reader (view/function/C#) | Mandatory because |
|---|---|---|
| Application/Start/Started | `cc.platform_stats` (events_24h), `cc.telemetry_events` | adoption rate |
| Auth/RestoreSession/Success | `cc.release_health` (Succeeded count), `cc.platform_stats` | OAuth success rate — Release Health |
| Auth/RestoreSession/Failed | `cc.incident_candidates_live/24h`, `cc.release_health`, `trg_telemetry_failed_promote` | Auth failure rate — критичний інцидент |
| Auth/SignIn/Success | `cc.release_health`, `cc.platform_stats` | Auth success rate |
| Installation/Sync/Success | `cc.release_health`, `cc.platform_stats` | Installation sync success rate |
| Installation/Sync/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | 42501 або інша помилка (критичний) |
| Orchestrator/Cycle/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | Збій циклу оновлень |
| Orchestrator/AppCheck/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | Не вдалося перевірити оновлення |
| Orchestrator/LocalizationCheck/{outcome} | `cc.platform_stats`, `cc.telemetry_events` | Localization pipeline health |
| Orchestrator/LocalizationCheck/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | Критичний збій локалізації |
| Orchestrator/LiaCheck/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | Не вдалося перевірити LIA |
| Updater/Download/Failed | `cc.incident_candidates_live/24h`, `cc.release_health`, `trg_telemetry_failed_promote` | Критичний збій завантаження |
| Updater/Verify/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | Критичний збій перевірки |
| Updater/Install/Failed | `cc.incident_candidates_live/24h`, `trg_telemetry_failed_promote` | Критичний збій встановлення |
| LIA/Install/Succeeded | `cc.release_health`, `cc.platform_stats` | LIA install success rate |
| LIA/Install/Failed | `cc.incident_candidates_live/24h`, `cc.release_health`, `trg_telemetry_failed_promote` | Критичний збій LIA |

> **Note:** `cc.incident_candidates_live/24h` читає `outcome='Failed'` + `received_at > now()-10min/24h`. `trg_telemetry_failed_promote` — тригер `AFTER INSERT WHEN outcome='Failed'`. Усі L1 Failed-події мають **мінімум 2 Readers**.

### 4.2. Reader Validation для ключових полів

| Поле | Reader (view) | Reader (C#) | Висновок |
|---|---|---|---|
| **`correlation_id`** | `cc.traces` (ORDER BY), `cc.telemetry_events` | `TraceRepository.cs:19,37,40`; `ControlCenterRepository.cs:44,538,555,568` | ✅ **L1** (trace reconstruction) |
| **`session_id`** | `cc.traces`, `cc.unfinished_started`, `cc.telemetry_events` | `TraceRepository.cs:73,91,107,125`; `TraceModels.cs:8,54` | ✅ **L1** (session grouping) |
| **`hresult`** | `cc.incident_candidates_live/24h` (COALESCE priority 3), `cc.release_health_detail`, `cc.traces` | `ControlCenterRepository.cs:539,553,569,573`; `TraceRepository.cs:110,141` | ✅ **L1 Failed** (fingerprint priority 3) |
| **`exception_type`** | `cc.incident_candidates_live/24h` (COALESCE priority 5), `cc.release_health_detail`, `cc.traces` | `ControlCenterRepository.cs:539,553,569,573`; `TraceRepository.cs:110,143` | ✅ **L1 Failed** (fingerprint fallback) |
| **`source`** | `cc.incident_candidates_live/24h` (COALESCE priority 1), `cc.release_health_detail`, `cc.traces` | `ControlCenterRepository.cs:539,553,569,573`; `TraceRepository.cs:110` | ✅ **L1 Failed** (fingerprint priority 1) |
| **`error_message`** | `cc.traces`, `cc.telemetry_events` | `ControlCenterRepository.cs:540,554,570`; `TraceRepository.cs:111,144` | ✅ **L1 Failed** (людяне пояснення помилки) |

> **Writer forensic:** `error_message` = `Exception.Message` через `PrivacySanitizer.Sanitize()`. Це НЕ дубль `exception_type` — `exception_type` дає тип ("LiaInstallException"), а `error_message` дає **людяне пояснення** ("Certificate chain invalid", "Access denied"). Без `error_message` при Failed — розробник бачить лише тип без причини.

---

## 5. Telemetry Policy Test Matrix (контракт поведінки)

> Контракт для тестування реалізації по чек-листу. Не форензик, а поведінка системи.

### 5.1. Матриця сценаріїв

| Scenario | AdvancedDiagnostics | TelemetryLevel | Очікування |
|---|---|---|---|
| Success OFF | false | Mandatory | ✅ L1 Success (16 полів) відправляється |
| Success ON | true | Mandatory | ✅ L1 Success + L2 (якщо є) |
| Failed OFF | false | Mandatory | ✅ L1 Failed (21 поле: + error_message, source, hresult, exception_type) |
| Failed ON | true | Mandatory | ✅ L1 Failed + L2 (detail forensic) |
| Diagnostic OFF | false | Diagnostic | ❌ подія НЕ відправляється |
| Diagnostic ON | true | Diagnostic | ✅ L2 відправляється (duration_ms, detail.*) |
| Telemetry Disabled | будь-який | будь-який | ❌ нічого (env `SCLOCVERSE_TELEMETRY_DISABLED`) |
| Gate not attached | — | Diagnostic | ❌ подія НЕ відправляється (`_diagnosticGate == null → false`) |
| **Gate null + Mandatory** | **null** | **Mandatory** | **✅ Відправляється** (gate null не блокує L1; KB #123) |

### 5.2. Конкретні події — що піде

| Подія | Scenario | Поля, що відправляються |
|---|---|---|
| **Auth/SignIn/Success** | OFF | install_id, user_id, session_id, correlation_id, step, component, operation, outcome, severity, app_version, telemetry_version, channel, occurred_at, received_at, os_version, country (16 = L1 Success) |
| **Auth/SignIn/Failed** | OFF | L1 Success (16) + error_message, source, hresult, exception_type, detail (21 = L1 Failed) |
| **Localization/Install/Failed** | OFF | L1 Failed (21) — error_message = "Certificate chain invalid" (PrivacySanitizer), source = "PowerShell", hresult = "0x800B0109", exception_type = "LiaInstallException" |
| **LIA/Install/Failed** | OFF | L1 Failed (21) — error_message, source, hresult, exception_type, detail (phase, retry_count=0, installer_type, certificate_present) |
| **LIA/Install/Failed** | ON | L1 Failed (21) + L2 (duration_ms, detail.certificate_subject, certificate_thumbprint, package_version, activity_id, powershell_exit_code, appx_log, signal_name) |
| **Updater/Download/Started** | OFF | ❌ НЕ відправляється (L2 Diagnostic, gate OFF) |
| **Updater/Download/Started** | ON | ✅ L2: install_id, session_id, correlation_id, step, component, operation, outcome, severity, app_version, ..., duration_ms, detail.phase, detail.retry_count |
| **Updater/Download/Failed** | OFF | ✅ L1 Failed (21) — error_message, source, hresult, exception_type |
| **Application/Start/Started** | OFF | ❌ НЕ відправляється (L2 Diagnostic, gate OFF) — Zero Noise Policy |
| **Application/Start/Started** | ON | ✅ L2: diagnostic detail (pre-auth launch signal) |

---

## 6. Архітектурні правила реалізації (KB #117–123)

- **`TelemetryLevel` ≠ `Category`** — дві різні осі. Level відповідає «коли відправляти?» (gate), Category — «що це за подія?» (бізнес-семантика). Default Category = `"Operational"`.
- **Єдина точка прийняття рішення** — `TelemetryClient.Track()` єдине місце перевірки `AdvancedDiagnostics`. НЕ 21 місце з `if (AdvancedDiagnostics)`. Gate: `_diagnosticGate?.Invoke() ?? false` (two-phase `AttachDiagnosticGate(Func<bool>)`).
- **`TelemetryLevel.Local`** — залишити в enum (0 використань, документує L3).
- **Реалізація `ITelemetryService` — одна** (`TelemetryClient`). Новий параметр у 1 інтерфейс + 1 реалізацію. Backward compatible.
- **`TelemetryLevel` лише через `.Track()` параметр.** ЗАБОРОНЕНО: `if (settings.AdvancedDiagnostics) { _telemetry.Track(...) }`.
- **Заборонити пряме читання `AdvancedDiagnostics` поза `TelemetryClient`.** Виняток: `MainWindow.xaml.cs:403,450-455` (UI читання/запис чекбокса).
- **Gate null не блокує Mandatory.** Gate not attached → Mandatory ✅ відправляється, Diagnostic ❌ блокується.

---

## 7. Пост-Impl Forensic (2026-07-07)

| # | Перевірка | Результат |
|---|---|---|
| 1 | L2 емітери з `TelemetryLevel.Diagnostic` | ✅ 18 (grep `level: TelemetryLevel.Diagnostic`) |
| 2 | `AdvancedDiagnostics` поза `TelemetryClient` | ✅ 0 у діловому коді |
| 3 | `Track()` без `level` → default Mandatory | ✅ 21 L1 виклик через default |
| 4 | `TelemetryLevel.Local` випадково | ✅ 0 використань (лише gate) |
| 5 | `AttachDiagnosticGate()` один раз | ✅ 1 виклик (`AppCompositionRoot.cs:156`) |
| 6 | `Category` не змінена | ✅ `context?.Category ?? "Operational"` |
| 7 | Incident Pipeline регресія | ✅ Failed = L1 → trigger не зачеплено |

---

## 8. Телеметрія pipeline hardening (KB §14.42, IMPL 2026-07-17)

### 8.1. Тришарова захист від Failed-without-signal

**Layer 1 (Source):** усі шляхи створення Failed-event гарантують signal через `ErrorContextExtractor.Extract(ex)` або `ErrorContextExtractor.Create(source, exceptionType, errorMessage?)`. 13 з 13 шляхів закрито.

**Layer 2 (BuildEvent):** `TelemetryClient.BuildEvent()` перевіряє інвариант `outcome == "Failed" && !HasSignal(context)` ПЕРЕД постановкою в чергу. При порушенні — авто-доповнення `Source = "CLR"`, `ExceptionType = "MissingSignalAutoFix"` + Debug.WriteLine.

**Layer 3 (Uploader):** `IsPermanentConstraintViolation(ex)` + `BatchIsolation.ExecuteAsync` (binary split) + retry counter (max 3 → poison eviction) + 401/403 drop.

### 8.2. chk_telemetry_failed_has_signal constraint

Міграція 00009 вимагає: при `outcome='Failed'` → хоча б одне з (`source`, `hresult`, `supabase_code`, `http_status`, `exception_type`) NOT NULL. `error_message` **не входить** у COALESCE. Порушення → відхилення всього батчу → retry loop (до фіксу Layer 1-3).

### 8.3. RC-401 fix (KB §14.34)

`TelemetryUploader.FlushAsync:86` — при `PostgrestException { StatusCode: 401 }` event не додається в `failed` (drop замість requeue). Черга спорожніє, storm зупинено. Транзитні помилки (500, network) — як і раніше requeue (до MaxRetries=3).

### 8.4. Stop/Resume за auth-статом (KB #254)

`ITelemetryService.Stop()`/`Resume()` — `_authStopped` flag у `TelemetryClient`. `AuthService.OnAuthStateChanged`: SignedOut → Stop, SignedIn → Resume. Також Stop у catch-блоках `TryRestoreSessionAsync` та `SignInAsync` (бо SDK не завжди генерує SignedOut event при `SetSession` failure).

---

## 9. Посилання

- **KB §5 Telemetry** — концепція, поточний стан ( Connie KB).
- **KB §14.42** — Telemetry Pipeline Hardening (3-шарова захист).
- **KB §14.41** — chk_telemetry_failed_has_signal Constraint Violation Fix.
- **KB §14.34** — RC-401 Defense-in-Depth (C+D+F).
- **`docs/observability/Observability-Constitution.md`** — 29 статей Конституції.
- **`docs/observability/FORENSIC-DATA-PIPELINE-DETAIL.md`** — повні CREATE/ALTER + всі `.Track()` з рядками сирцевого коду.
- **`docs/observability/Observability-Architecture.md`** — цільова архітектура телеметрії.
