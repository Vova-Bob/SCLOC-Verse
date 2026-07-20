# SCLOC-Verse Release History

> **Дочірній документ Knowledge Base.**
> Журнал релізів SCLOC-Verse з посиланнями на Release Notes та GitHub Releases.
> Джерело: KB §14.19, §14.31, §14.32, §14.33, §14.37, §14.37a (винесено 2026-07-20 при KB optimization Variant B).
> Повні Release Notes українською: `Installer/Release-Notes-<version>.md`.

---

## Актуальний Stable

| Версія | Дата | Git tag | GitHub Release | Інсталятор | Статус |
|---|---|---|---|---|---|
| **v1.0.2.4** | 2026-07-14 | `v1.0.2.4` | https://github.com/Vova-Bob/SCLOC-Verse/releases/tag/v1.0.2.4 | `SCLOC-Verse_Setup.exe` 68.4 МБ | **Latest** |

---

## Історія релізів

### v1.0.2.4 (2026-07-14) — ✅ Latest

- **Scope:** (a) Hotfix self-contained regression з v1.0.2.3 (framework-dependent → self-contained). (b) Спрощений вигляд overlay Hangar Timer (EX-Hangar compact badge).
- **Зміни:** csproj version bump; `build-installer.ps1` — очистка `publish` директорії + `-p:SelfContained=true`.
- **Build:** 0 errors, Mojibake 0. Інсталятор 68.4 МБ.
- **Release Notes:** `Installer/Release-Notes-v1.0.2.4.md`.

### v1.0.2.3 (2026-07-14) — ⚠ ЗНЯТО З LATEST (regression)

- **Scope:** RC-401 defense-in-depth (C+D+D++F), SEC-12 fix (REVOKE EXECUTE), SEC-11 (2 функції search_path), Performance Advisor (RLS init plan + FK indexes + backup schema DROP).
- **REGRESSION:** Інсталятор виявився **framework-dependent** замість self-contained (10.4 МБ замість 60+ МБ). При запуску на машинах без .NET 9 — вікно "You must install .NET". Причина: stale publish-директорія.
- **Фікс у v1.0.2.4.**
- **Release Notes:** `Installer/Release-Notes-v1.0.2.3.md`.

### v1.0.2.2 (2026-07-12) — RC-401 Hotfix

- **Production-інцидент:** 401 storm на `POST /rest/v1/telemetry_events` (~14 req/сек, 4+ години).
- **Root Cause:** `TelemetryUploader.FlushAsync:86` — `_queue.Requeue(failed)` безумовно повертав відхилені події в чергу. При expired JWT події нескінченно циркулювали: Drain → Insert → 401 → Requeue → 30с → ∞.
- **Hotfix:** `TelemetryUploader.cs:85` — при `PostgrestException { StatusCode: 401 }` event не додається в `failed` (drop замість requeue). Черга спорожніло, storm зупинено.
- **Git:** `729e951`. Інсталятор 60.7 МБ, SHA256 `2a47d982…`.
- **Release Notes:** `Installer/Release-Notes-v1.0.2.2.md`.

### v1.0.2.1 (2026-07-11) — RC-1 Hotfix

- **Scope:** Hotfix для v1.0.2.0 — виправлено 3 невалідних outcome у `BackgroundUpdateMonitor.cs` (`UpdateFound`→`Skipped`, `Updated`→`Succeeded`, `UpdateAvailable`→`Skipped`). Кожна така подія відхилялась `chk_telemetry_outcome` → requeue → нескінченний retry loop (~2880 ERROR/добу).
- **Стратегія:** Варіант A (build з HEAD `37a5a2a`, не cherry-pick) — безпечно, бо лише 1 compiled-файл з runtime-зміною.
- **Git:** tag `v1.0.2.1`. Інсталятор 60.7 МБ, SHA256 `2472ca29…`.
- **Release Notes:** `Installer/Release-Notes-v1.0.2.1.md`.

### v1.0.2.0 (2026-07-11)

- **Scope:** 68 комітів після v1.0.1.0. Нові модулі: Auto Key (SendInput з кореневим фіксом INPUT=40 байт), Anti-AFK міграція з WinForms (GetLastInputInfo замість hooks), StarCitizenForeground Foreground Gate, динамічні підказки хоткеїв SSOT. Settings Hub Phase 0.5 + Overlay live-preview.
- **Жодних breaking changes** (additive-only, Settings мігрують автоматично).
- **Build:** 0 warnings, 0 errors. MojibakeScanner: 8 false-positives (патерн «Рі» — легітимний український).
- **Git:** tag `v1.0.2.0` → коміт `c473e63`. Інсталятор 68.4 МБ, SHA256 `eaba2204…`.
- **Release Notes:** `Installer/Release-Notes-v1.0.2.0.md`.

### v1.0.1.0 (2026-07-07)

- **Version Audit:** Єдине джерело версії — `AssemblyVersion`/`Version`/`FileVersion` у `SCLOCVerse.csproj`. Усі 7 шляхів споживання читають через reflection. Hardcoded "1.0.0.0"/"1.0.0.1" в сирцях відсутні.
- **Scope:** 160 комітів `v1.0.0.0..HEAD`. Що з'явилось проти v1.0.0.0: системний трей, автозапуск, чекбокси налаштувань, кастомні ToolTip, toast-сповіщення, BackgroundUpdateOrchestrator/NotificationRouter, single-instance (Mutex+Named Pipe), AppUpdateProgressWindow, брендована OAuth callback сторінка, увесь Observability стек (TelemetryClient, ErrorContextExtractor, PrivacySanitizer, TraceContext, BuildInfo, інцидент-менеджмент, Knowledge Engine), GitHub API Hardening (HttpRetryHelper, retry/backoff, Conditional GET ETag), LIA cert LocalMachine fix (0x800B0109), LIA elevation (RunPowerShellAsync.requireElevation).
- **Блокер релізу (усунуто):** `.iss` та `build-installer.ps1` посилались на застарілий TFM-шлях. Фікс: оновлено на `net9.0-windows10.0.18362.0`.
- **Git:** tag `v1.0.1.0` → коміт `8a6a989`. Інсталятор 68.3 МБ.
- **Release Notes:** `Installer/Release-Notes-v1.0.1.0.md`.

### v1.0.0.0 (baseline)

- **Tag:** `v1.0.0.0` → коміт `404bd85` («Реліз 1.0.0.0: фінальні URLs та секція сайту»).
- БУЛО: UpdateHistoryWindow. Не БУЛО: системного трею, автозапуску, чекбоксів налаштувань, кастомних ToolTip, toast-сповіщень, повного Observability стеку.

---

## Production Incidents (resolution log)

### RC-401 (2026-07-12 → 2026-07-15) — ✅ Resolved

- **Symptom:** 401 storm на telemetry_events API від старих версій.
- **Root Cause:** SDK `Supabase.Gotrue 6.0.3` `TokenRefresh.HandleRefreshTimerTick` ковтає exception від `RefreshToken()` без очищення сесії. Stale JWT → 401 → requeue loop.
- **Mitigation (v1.0.2.2):** 401 drop замість requeue.
- **Defense-in-Depth (v1.0.2.3+):** (C) JWT expiry check; (D) Stop/Resume за auth-статом; (D+) Stop у catch-блоках `TryRestoreSessionAsync`/`SignInAsync`; (F) batch insert.
- **Storm загас 2026-07-15** природньо — користувачі перезапустились/оновились. Деталі: KB §14.33, §14.34, §14.39.

### RC-1 chk_telemetry_outcome (2026-07-11) — ✅ Resolved

- **Symptom:** ~2880 ERROR/добу `chk_telemetry_outcome` на production.
- **Root Cause:** `BackgroundUpdateMonitor` відправляв 3 невалідних outcome (`Updated`, `UpdateAvailable`, `UpdateFound`), яких немає в CHECK.
- **Fix (v1.0.2.1):** `UpdateFound`→`Skipped`, `Updated`→`Succeeded`, `UpdateAvailable`→`Skipped`. Семантика: `Skipped` не входить у success_rate.
- **Verification (2026-07-12):** 0 невалідних подій у `telemetry_events`. Деталі: KB §14.32.

### chk_telemetry_failed_has_signal (2026-07-17) — ✅ Resolved

- **Symptom:** PostgreSQL `23514 (check_violation)` на `telemetry_events` з інтервалом 30 секунд.
- **Root Cause:** 13 C# шляхів створювали Failed-event без signal-полів (BackgroundUpdateMonitor, AuthService, UpdateEvents).
- **Fix:** 3-шарова захист (Layer 1: ErrorContextExtractor.Create на 13 шляхів; Layer 2: BuildEvent централізована валідація; Layer 3: Uploader binary split + poison eviction). Деталі: KB §14.41, §14.42, §14.43.

---

## Pattern: Version Update (KB #150, #230)

Version bump виконується зміною 3 рядків у `SCLOCVerse.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`). Усі 7 шляхів споживання версії читають її з assembly через reflection:

1. `BuildInfo.ReadAppVersion` (telemetry `app_version`)
2. `ApplicationVersionProvider.GetCurrentVersion` (HomeCanvas/About/Updater)
3. `InstallationService.GetCurrentAppVersion` (`app_installations.app_version`)
4. `App.xaml.cs.GetCurrentVersionString` (Settings migration `LastAppVersion`)
5. `HttpRetryHelper.BuildUserAgent` (User-Agent `SCLOC-Verse/<version>`)
6. `Installer/SCLOC-Verse.iss` `GetFileVersion` (інсталятор читає з білда)
7. Assembly metadata (ProductVersion, FileVersion)

Hardcoded версій в сирцях немає — single-source-of-truth.
