# Security Review Checklist

> **SEW (SCLOC-Verse Engineering Workflow).** Норматив: [`AGENTS.md`, розділ «SEW — Security Review»](../../AGENTS.md).
> Виконується лише якщо зміна зачіпає домен із тригер-списку.
> Для звичайних UI або локалізації Security Review **не потрібне**.
>
> Базові принципи: Конституція Observability Стаття 29 (Secret Independence), Стаття 4 (Zero PII),
> `PRIVACY.md`, `docs/architecture/Final-Architecture-Review.md` (SEC-1…SEC-12).

---

## Тригер-список доменів

Security Review виконується, якщо зміна зачіпає хоча б один:

| Домен | Приклади |
|---|---|
| OAuth / Auth | Discord OAuth, GoTrue, сесії, токени, SignIn/SignOut, PKCE |
| Installer | InnoSetup, `SCLOC-Verse.iss`, code signing, elevation |
| Auto Update | GitHub releases, `UpdateDownloader`/`UpdateInstaller`/`UpdateVerifier`, checksum |
| Network | HTTP-клієнти, PostgREST, Discord webhook, `HttpRetryHelper` |
| Registry | HKCU Run-ключ (autostart), `HKCU\Software\VALDEUS\SCLOCVerse` |
| File System | `%LocalAppData%\SCLOCVerse`, `.auth`, `install-id`, wrapper.ps1, cert-файли |
| SQL / Supabase | міграції, RLS, policies, SECURITY DEFINER функції, grants |
| API / Контракти | `cc_readonly`, `cc_notifier`, `ITelemetryService`, `INotificationProvider` |
| RLS | нові таблиці, нові policies, зміна existing policies |
| Криптографія | DPAPI, сертификати (L.I.A. self-signed), SHA256, code signing (SignPath) |

---

## Чеклист за доменами

### OAuth / Auth

```
□ OAuth state валідується (SEC-8 — зараз відхилено, але перевіряти при зміні auth).
□ PKCE збережено (verifier/challenge).
□ Scope не розширено без погодження (зараз `identify` only).
□ SignOut = global token revoke.
□ access_denied → SignedOut без витоку помилки в UI.
□ Сесійні токени: DPAPI CurrentUser (не plaintext).
□ Без `service_role` у клієнті.
□ Return URL не керується користувачем (open redirect).
```

### Installer / Auto Update

```
□ Checksum не обійдено при порожньому значенні (SEC-4: Verify.Failed, не Verify.Skipped).
□ Authenticode / publisher identity перевірено (SEC-10: не лише byte-equality SHA256).
□ TOCTOU verify→install враховано (SEC-9).
□ Code signing: SCLOCVerse.exe + SCLOC-Verse_Setup.exe через SignPath.
□ Elevation: лише Verb="runas" під час Install (не requireAdministrator).
□ Cert імпортується в LocalMachine\Root + TrustedPeople (не CurrentUser).
□ Довільний .cer без pin? Якщо так — відмітити як відомий ризик SEC-3 (відкритий борг).
□ Інсталятор має integrity check (SEC-2 — відкритий борг).
```

### Network

```
□ SQL parameterized (без конкатенації).
□ PowerShell single-quote escaping для аргументів.
□ HTML-encoding на OAuth callback.
□ User-Agent = `SCLOC-Verse/<version>` (HttpRetryHelper).
□ Retry/backoff не розкриває секрети в лог/помилки.
□ HTTPS скрізь (HTTP не дозволено, окрім loopback OAuth `http://127.0.0.1`).
□ Webhook URL / токени не у репозиторії (Стаття 29).
```

### Registry / File System

```
□ Шляхи не містять PII (`C:\Users\<name>\...` — ні; `%LOCALAPPDATA%` — так).
□ install_id НЕ MachineName / MAC / серійник.
□ DPAPI для токенів (не plaintext файл).
□ wrapper.ps1 / тимчасові файли: UTF-8 без BOM.
□ Orphaned processes при cancellation (L-A6 — відкритий борг).
```

### SQL / Supabase / RLS

Повний БД-чеклист: [`Database-Verification.md`](Database-Verification.md).

```
□ RLS enabled на нових таблицях.
□ anon: deny-all (RESTRICTIVE + перmissive deny).
□ authenticated: owner-only (user_id = auth.uid()).
□ cc_readonly: лише SELECT на control_center + EXECUTE на workflow-функції.
□ cc_notifier: least-privilege (queue/attempts only).
□ SECURITY DEFINER функції: SET search_path = public, pg_catalog (SEC-11).
□ GRANT RETURNING вимагає SELECT (інцидент 42501).
□ Additive-only (Стаття 13): без DROP COLUMN активних клієнтів.
□ Міграції створюють ролі БЕЗ PASSWORD (паролі — поза репо, Стаття 29).
```

### Криптографія

```
□ DPAPI CurrentUser для локальних секретів.
□ SHA256 для integrity (не MD5/SHA1).
□ Cert pinning розглянуто (SEC-3 відкритий).
□ Code signing не обходить build-аудит.
```

---

## Секрети (Стаття 29 — Secret Independence)

Тришарова модель. Перевіряти при будь-якій зміні, що потенційно зачіпає секрети:

```
□ Рівень 1 (Git): жодного пароля/токена/webhook URL/service_role/OAuth Client Secret/SMTP/API-ключа/сертифіката в коді/SQL/міграціях/документах/скриптах/конфігах.
□ Рівень 2 (БД): міграції створюють ролі без PASSWORD.
□ Рівень 3 (App): секрети лише через env (SCLOC_*), .NET User Secrets (Dev), CI/CD Secrets, Secret Manager.
□ appsettings*.json: порожні значення ("") для секретів.
□ .env* файли, secrets.json, *.db, *.sqlite, *.pfx, *.cer, *.key — в .gitignore.
□ anon_key (publishable) — допускається за умови RLS захисту від anon-доступу.
```

Порушення — P0 дефект: негайна ротація секрету + перепис історії (якщо коміт не публічний) або ротація + прийняття компрометації.

---

## PII (Стаття 4 — Zero PII)

```
□ Новий payload телеметрії не містить: токенів, JWT, email, username, Discord-даних, IP, локальних шляхів, MachineName, MAC, серійників.
□ PrivacySanitizer — єдине вузьке горло (жоден шлях у чергу не обходить).
□ Stack-trace санітариться ДО черги (не «на сервері»).
□ attributes — лише через allowlist.
□ Тест з відомими PII-патернами дає sanitized output.
```

---

## Severity класифікація знахідок

| Severity | Опис | Дія |
|---|---|---|
| **Critical** | Блокує deployment: auth bypass, SQL injection, RCE, privilege escalation, витік секретів | Зупинити роботу, негайне виправлення |
| **High** | Фіксувати до deployment: IDOR, stored XSS, SSRF, JWT-вразливості, session hijacking | Виправити в цьому ж релізі |
| **Medium** | Фіксувати скоро: reflected XSS, CSRF, info disclosure, misconfig | Зафіксувати в KB §16, запланувати |
| **Low** | Трекати: security headers, verbose errors, version disclosure | Зафіксувати в KB §16 |

---

## Зв'язок з іншими чеклистами

* [`Database-Verification.md`](Database-Verification.md) — БД-домен (RLS під реальною роллю).
* [`Quality-Gates.md`](Quality-Gates.md) — Security Review є одним із Extended Gates.
* `docs/observability/Observability-Constitution.md` Стаття 4 (Zero PII), Стаття 29 (Secret Independence) — нормативи.
