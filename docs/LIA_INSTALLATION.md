# L.I.A Installation — Архітектура та обґрунтування

> **Дата:** 2026-07-05
> **Версія:** 1.0.0.1+
> **Коміти:** `85ea790` → `27e4d83` → `206ae98` → `43de344`

---

## Контекст

Цей документ фіксує архітектурні рішення, прийняті під час усунення помилки
`0x800B0109` (CERT_E_UNTRUSTED_ROOT) при встановленні Голосового асистента Л.І.А.
через SCLOC-Verse. Документ орієнтований на майбутніх супровідників, щоб уникнути
повторного аналізу тих самих причин через рік.

---

## Проблема

Інсталяція L.I.A падала з `HRESULT 0x800B0109` на етапі `Add-AppxPackage`:

> A certificate chain processed, but terminated in a root certificate which is
> not trusted by the trust provider.

Сертифікат `CN=Alexuß` (thumbprint `33DD2416B9CC3DA94A84A479AD63D07C4B322833`,
self-signed) імпортувався в `Cert:\CurrentUser\TrustedPeople`, але AppX
deployment API не визнавав його довіреним.

---

## Коренева причина

**AppX/MSIX deployment trust verification перевіряє лише `LocalMachine` store,
не `CurrentUser`.**

Це задокументовано Microsoft:
- [How to troubleshoot app package signature errors](https://learn.microsoft.com/en-us/windows/win32/appxpkg/how-to-troubleshoot-app-package-signature-errors)
- [MSIX troubleshooting guide](https://learn.microsoft.com/en-us/windows/msix/msix-troubleshooting-guide)

`CurrentUser\TrustedPeople` (registry hive `HKCU`) недостатній для AppX
deployment, навіть якщо сертифікат там фізично присутній. Довіра встановлюється
лише при наявності сертифіката в `HKLM\...\SystemCertificates` (LocalMachine).

### Доказ (причинно-наслідковий зв'язок)

| Експеримент | Cert store | Результат |
|---|---|---|
| Commit `206ae98` | `CurrentUser\TrustedPeople` + elevation | `0x800B0109` відтворюється |
| Commit `43de344` | `LocalMachine\Root` + `LocalMachine\TrustedPeople` + elevation | `ADD_APPXPACKAGE_SUCCESS` |

Єдина зміна між експериментами — cert store. Механізм elevation, транспорт
forensic, `Add-AppxPackage` — ідентичні.

---

## Рішення

### Cert store: `LocalMachine\Root` + `LocalMachine\TrustedPeople`

Еквівалент BAT-інсталятора автора L.I.A:

```batch
certutil -addstore -f Root          <file.cer>
certutil -addstore -f TrustedPeople <file.cer>
```

SCLOC-Verse використовує PowerShell `Import-Certificate` (під капотом той самий
CryptoAPI `CertAddCertificateContextToStore`):

```powershell
Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

`Root` + `TrustedPeople` обидва — на рівні `LocalMachine` (registry `HKLM`).
AppX deployment знаходить сертифікат і встановлює довіру.

### UAC elevation: лише під час Install

Запис у `LocalMachine\*` вимагає прав адміністратора (доступ до `HKLM`).
SCLOC-Verse забезпечує elevation через `Process.Start` з `Verb="runas"`
**тільки під час операції Install L.I.A**, не при кожному запуску застосунку.

---

## Чому не `requireAdministrator` (app.manifest)

Принцип найменших привілеїв (least privilege):

| Підхід | UX | Ризик |
|---|---|---|
| `app.manifest` з `requireAdministrator` | UAC на КОЖНОМУ запуску SCLOCVerse | Зайві привілеї для всього застосунку (локалізація, налаштування, телеметрія) |
| `Verb="runas"` лише під час Install | UAC один раз, лише коли потрібно | Мінімальні привілеї в звичайному режимі |

SCLOCVerse обирає другий підхід: звичайна робота без admin, elevation лише для
`Import-Certificate` + `Add-AppxPackage`.

---

## Архітектура elevation

### `RunPowerShellAsync(script, ct, requireElevation)`

Єдина абстракція запуску PowerShell з двома режимами:

- **`requireElevation=false`** (за замовчуванням) — існуюча логіка:
  `UseShellExecute=false` + `RedirectStandardOutput=true`. Використовується для
  `GetInstalledVersionAsync`, `UninstallAsync` (не потребують admin).

- **`requireElevation=true`** — нова гілка через `RunElevatedAsync`:
  `UseShellExecute=true` + `Verb="runas"`. Транспорт stdout/stderr через
  тимчаскові файли (wrapper.ps1 + .NET Process з `RedirectStandardOutput`).

### Чому файл, а не pipe

`UseShellExecute=true` забороняє `RedirectStandardOutput` (Windows API
обмеження). Тому elevated-режим використовує wrapper.ps1, який стартує дочірній
powershell через .NET Process (`RedirectStandardOutput=true`) і записує
stdout/stderr у файли UTF-8 без BOM.

PowerShell 5.1 оператор `>` пише UTF-16LE, тому .NET API
(`[System.IO.File]::WriteAllText` з `UTF8Encoding(false)`) обов'язкове для
контрольованого UTF-8 — критично для української мови в forensic-повідомленнях.

### Контракт `PowerShellResult`

Однаковий в обох режимах: `(int ExitCode, string Output, string Error)`.
Caller (`RunInstallerScriptAsync`) не знає про механізм транспорту.

### Граничний випадок: відхилення UAC

`Win32Exception` з `NativeErrorCode=1223` (ERROR_CANCELLED) →
`PowerShellResult(-1, "", "Elevation declined by user")`. Усі тимчаскові файли
очищуються в `finally` через `TryDeleteFile`.

---

## Forensic-контракт

`##SCLOC_FORENSIC##` маркер + JSON залишається джерелом істини для діагностики.
PowerShell формує повний forensic (hresult, phase, message, activityId,
appxLog, cert context) у stdout; C# парсить через `LiaForensicParser.TryParse`.

### Відома вада (backlog)

`Exception.HResult` повертає CLR generic `0x80131500`, а не deployment HRESULT
(`0x800B0109`). Deployment HRESULT доступний лише в `message`. Це не впливає на
функціональність, але signal_name в телеметрії некоректний.

**Backlog:** enrichment deployment HRESULT з message (regex або ActivityId →
HRESULT mapping). Окрема задача після збору телеметрії від реальних користувачів.

---

## Посилання

- Microsoft: [How to troubleshoot app package signature errors](https://learn.microsoft.com/en-us/windows/win32/appxpkg/how-to-troubleshoot-app-package-signature-errors)
- Microsoft: [MSIX troubleshooting guide](https://learn.microsoft.com/en-us/windows/msix/msix-troubleshooting-guide)
- Microsoft: [ProcessStartInfo.Verb](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.verb)
- Microsoft: [ProcessStartInfo.RedirectStandardOutput](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.redirectstandardoutput)
- Реліз-ранбук: [docs/release/release-runbook-1.0.0.1.md](release/release-runbook-1.0.0.1.md)
