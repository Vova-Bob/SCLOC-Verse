# Quality Gates Checklist

> **SEW (SCLOC-Verse Engineering Workflow).** Норматив: [`AGENTS.md`, розділ «SEW — Quality Gates»](../../AGENTS.md).
> Чеклист з двома рівнями: Core (обов'язковий для всіх змін) + Extended (для критичних доменів).
>
> Аналог до [`Database-Verification.md`](Database-Verification.md), який реалізує Extended Gates для БД-домену.

---

## Як користуватися

1. Для **будь-якої** зміни проходяться всі **Core Gates** (4 пункти).
2. **Extended Gates** додаються лише якщо зміна зачіпає критичний домен (див. «Extended Gates — домени» нижче).
3. Для тривіальних правок (опечатка, UI-твік без логіки) Core Gates зводяться до мінімуму (Zero Regression + UTF-8), але формально не пропускаються.
4. Gate не «закритий», доки не доведено фактом (код, build, runtime, коміт).

---

## Core Gates — обов'язкові для всіх змін

### Root Cause Gate

```
□ Причина проблеми підтверджена доказом (код, лог, EXPLAIN, runtime).
□ Лікується першопричина, а не симптом.
□ Розглянуто альтернативні причини (мінмум 1 альтернатива).
□ Якщо причина — гіпотеза, позначено HYP (розділ «Стани forensic-знахідок» AGENTS.md).
```

### Zero Regression Gate

```
□ Існуюча поведінка функцій не змінилась (за винятком цільової зміни).
□ dotnet build: 0 warnings, 0 errors.
□ Стабільний код не зачеплено без потреби (розділ «Правила стабільного коду» AGENTS.md).
□ Інтеграційні точки перевірені (хто ще викликає змінений код/схему/контракт).
```

### Evidence Gate

```
□ Кожен висновок відокремлено від факту.
□ Стан доведеності позначено (VER/IMPL/HYP/REJ — розділ «Стани forensic-знахідок» AGENTS.md).
□ Є посилання на джерело: код (file:line), БД (EXPLAIN ANALYZE), лог, коміт, скріншот.
□ Припущення явно позначені як HYP, не подані як VER.
```

### UTF-8 Gate

```
□ Усі нові/змінені текстові файли збережені у UTF-8 (розділ «Правила кодування тексту (P0)» AGENTS.md).
□ Українські тексти без mojibake (Р›Р°СЃРєР°РІРѕ, Ð›Ð°, ?????? — P0 дефект).
□ PowerShell-запис файлів: -Encoding UTF8.
□ C# читання/запис: явний Encoding.UTF8.
□ Перевірка: запустити dotnet build + візуально перевірити українські екрани.
```

---

## Extended Gates — лише для критичних доменів

### Домени, що вимагають Extended Gates

* База даних (схема, міграції, RLS, функції, views)
* Authentication (OAuth, сесії, токени, GoTrue)
* Installer / Auto Update (InnoSetup, GitHub releases, L.I.A. cert)
* Network (HTTP, Supabase PostgREST, Discord webhook)
* Supabase (RLS, policies, service_role, Edge Functions)
* Публічний API / Контракти (CC view-контракт, TelemetryLevel, INotificationProvider)
* Криптографія (DPAPI, cert, checksum, code signing)

### Simplicity / Anti-Abstraction Gate

```
□ Рішення найпростіше з можливих, що вирішує задачу.
□ Немає зайвих абстракцій (інтерфейс-без-реалізації, фабрика-без-потреби).
□ НЕ додано IoC-контейнер, Generic Host, MVVM-фреймворк без окремого погодження.
□ НЕ виконано прихований рефакторинг («заодно»).
□ Перед новим кодом доведено, що аналогічного рішення немає (Reuse First).
```

### Integration First Gate

```
□ Описано точку інтеграції (який файл/сервіс/контракт зачіпається).
□ Описано залежності (що залежить від зміненого, від чого залежить зміна).
□ Описано вплив на систему (чинники побічного ефекту).
□ Для БД-змін: перевірено dependency graph (views, функції, тригери, C# SQL).
□ Для контракту: перевірено всіх споживачів (C# + Blazor + Notifier).
```

### Security Review Gate

Див. [`Security-Review.md`](Security-Review.md) — повний чеклист за доменами.

```
□ Зміна зачіпає домен із тригер-списку Security Review?
   Так → пройти Security-Review.md.
   Ні → Gate пропускається.
```

---

## Специфічні чеклисти за доменами

### БД-домен

Повний чеклист: [`Database-Verification.md`](Database-Verification.md) (RLS під реальною роллю `authenticated`).

Додатково (якщо міграція):

```
□ Migration Review виконано (розділ «Migration Review» AGENTS.md).
□ Rollback-скрипт написаний ДО застосування міграції.
□ Additive-only (без DROP COLUMN активних клієнтів).
□ Generated column: IMMUTABLE вимога перевірена.
□ Post-Implementation Forensic запланований (розділ «Post-Implementation Forensic» AGENTS.md).
```

### Installer / Auto Update

```
□ Checksum / Authenticode не обійдено (SEC-4).
□ Cert → LocalMachine\Root + TrustedPeople (не CurrentUser).
□ Elevation лише під час Install (Verb="runas", не requireAdministrator).
□ Forensic-контракт ##SCLOC_FORENSIC## збережено.
□ UTF-8 без BOM у wrapper.ps1 / тимчасових файлах.
```

### Telemetry

```
□ Конституція Observability не порушена (29 статей).
□ Телеметрія — чистий спостерігач (Стаття 1/10): не кидає, не блокує, не ламає startup.
□ PrivacySanitizer — єдине вузьке горло (Стаття 4).
□ TelemetryLevel позначено явно для нових емітерів (KB §5.10 Event Registry).
```

---

## Self-Critique (перед завершенням)

```
□ Чи існує простіше рішення?
□ Чи існує безпечніше рішення?
□ Чи можна використати більше існуючого коду (Reuse First)?
□ Чи не з'явилась зайва складність?
□ Чи всі висновки підтверджені доказами?
□ Чи залишились неперевірені критичні припущення?
```

Якщо відповідь хоча б на один пункт негативна — рішення не завершено.

---

## Зв'язок з Exit Criteria

Gate вважається пройденим лише за умови підтвердження фактом (код/build/runtime/коміт). Формальний чекбокс без доказу — не закритий.

Див. AGENTS.md «SEW — Exit Criteria» для повного списку умов завершеності задачі.
