# SCLOC-Verse — Правила розробки

> **Конституція проєкту.** Має найвищий пріоритет над KB та іншими документами.
> **Поточний стан системи**: [`docs/SCLOCVerse-Knowledge-Base.md`](docs/SCLOCVerse-Knowledge-Base.md).
> **Версія**: 1.0.2.4 Stable. **Стек**: WPF .NET 9, Blazor Server, Worker, Supabase (PostgreSQL 17).

---

## 1. Єдина база знань (SSOT)

1. Спочатку читати [`docs/SCLOCVerse-Knowledge-Base.md`](docs/SCLOCVerse-Knowledge-Base.md).
2. Якщо відповідь є там — НЕ перечитувати forensic-документи.
3. Лише якщо відповіді в KB немає — звертатись до першоджерел (список у KB §18).
4. KB — **живий документ**: дозволено оновлювати, скорочувати, переносити дані між розділами.
5. Заборонено створювати дублікати інформації та нові документи для вже описаних підсистем.

### Дочірні документи KB (deep details)

- [`docs/database/Data-Model.md`](docs/database/Data-Model.md) — повна схема БД, 15 таблиць по колонках, RLS, lifetime.
- [`docs/observability/Telemetry-Registry.md`](docs/observability/Telemetry-Registry.md) — Policy (3 рівні), 39 `.Track()` емітерів, Field Registry, Reader Validation.
- [`docs/release/Release-History.md`](docs/release/Release-History.md) — журнал релізів v1.0.0.0 → v1.0.2.4.

### KB Synchronization

Після завершення нетривіальної задачі — оновити KB:
- нові підтверджені факти → відповідні розділи;
- спростовані гіпотези → §15 Rejected;
- нові рішення → §14 Approved;
- новий борг → §16 Technical Debt;
- нові задачі → §17 Backlog.

Мета: через пів року будь-який агент відкриває один файл і бачить **поточний** стан.

---

## 2. Методологія (на вибір агента)

SCLOC-Verse не дотримується жодної формальної методології повністю. Принципи черпані з кількох джерел. Агент обирає ту, що найкраще пасує задачі:

| Методологія | Коли застосовувати | Посилання |
|---|---|---|
| **Spec Kit** (GitHub) | Markdown-first, brownfield,Spec-driven розробка | https://github.com/github/spec-kit |
| **SDD** (Specification-Driven Development) | Загальна spec-first методологія | https://specdriven.org |
| **BMAD** (Breakthrough Method of Agile AI Devs) | Multi-agent задачі (architect/PM/dev паралельно) | https://github.com/bmad-code-org/BMAD-METHOD |

**Спільні принципи для всіх:**
- **Evidence First** — рішення спираються на доказ (код, БД, EXPLAIN). Без доказу — гіпотеза (маркується HYP).
- **Root Cause First** — спочатку знайти першопричину. Симптоми не лікувати.
- **Zero Regression** — зміна не погіршує існуючу поведінку.
- **Reuse First** — перед новим кодом довести, що аналогічне рішення вже не існує.
- **Minimal Change** — змінювати лише те, що безпосередньо необхідне. Без «заодно».
- **Evolution over Revolution** — архітектура еволюціонує поступово.
- **KISS / DRY** — простота та відсутність дублювання.

**Заборонені зовнішні CLI** (OpenSpec/Specify/BMAD CLI): лише Markdown + існуюча інфраструктура.
**Заборонено паралельні структури** `openspec/`, `.specify/`, `.bmad/` — вони дублюють KB.

---

## 3. Обов'язковий порядок роботи

1. **Forensic аналіз задачі** (зона впливу, ризики, регресії, рекомендації).
2. **План змін**.
3. **Очікування погодження** (для нетривіальних задач).
4. **Резервний commit**: `git commit -m "Резервна точка перед <опис>"`.
5. **Реалізація**.
6. **Перевірка** (build, тести, runtime).
7. **Короткий звіт**.
8. **Фінальний commit** українською.

Заборонено писати код до погодження плану змін для нетривіальних задач.

### Типи задач → рівень процесу

| Тип | Цикл |
|---|---|
| Trivial (опечатка, UI-твік) | Правка → Build → Commit |
| Bug Fix | Forensic → Root Cause → Фікс → Verify → Commit |
| Feature | Forensic → Proposal → Approval → Backup → Impl → KB Sync → Commit |
| Architecture | + Decision Engine (≥3 варіанти) → ADR |
| Migration | + Migration Review + Rollback-скрипт + Test → Post-Impl Forensic |
| Security (Auth/OAuth/Crypto/Installer) | + Security Review |

### Acceptance Criteria (AC)

Кожна нетривіальна задача містить явні AC1, AC2, ... — формує їх на етапі Proposal ДО реалізації.

### Decision Engine

Нетривіальні задачі вимагають **≥2 варіантів рішення** (architectural — ≥3) з обґрунтуванням (плюси/мінуси/ризики + відповідність принципам) перед тим, як обирати.

---

## 4. Архітектурні правила

Поточна архітектура — базова, приймається без глобальних змін.

**Заборонено:**
- Переписувати проєкт на іншу архітектуру.
- Впроваджувати MVVM-фреймворки.
- Впроваджувати сторонні IoC-контейнери.
- Додавати складні патерни без необхідності.
- Глобальний рефакторинг без погодження.

**Існуючий підхід:** `AppCompositionRoot` + ручна композиція залежностей + функціональне групування папок (`Services/<Feature>/`, `Interfaces/`, `Models/`, `Controls/`) + Canvas-підхід + існуючий стиль XAML.

**Стабільний код не змінювати без прямої необхідності.**

---

## 5. Правила коду

- **Код**: тільки англійською. Професійний C#. .NET 9. Nullable Enabled.
- **Коментарі / Документація / Commit-повідомлення**: тільки українською.
- **Принципи**: DRY, KISS, SOLID без фанатизму.
- **Нові сервіси**: `Services/<Feature>/`. Інтерфейси: `Interfaces/`. Моделі: `Models/`. Canvas: `Controls/`. Залежності — через `AppCompositionRoot`.
- **Async**: суфікс `Async`; `CancellationToken` де доцільно; `ConfigureAwait(false)` вибірково; без `async void` окрім WPF event handlers.
- **Не збільшувати відповідальність MainWindow.xaml.cs без необхідності** (~903 рядки → God Class).
- **Перед новим файлом/сервісом** довести, що аналогічного ще немає.

---

## 6. Правила кодування тексту (P0)

Пошкодження українського тексту (mojibake) НЕ допускається ні в коді, ні в релізних артефактах, ні в GitHub API.

**Вимоги:**
1. Усі C#/XAML/JSON/.iss/.ps1 → UTF-8.
2. Читання файлів — тільки з `Encoding.UTF8`: `File.ReadAllText(path, Encoding.UTF8)`.
3. Запис файлів — тільки з `Encoding.UTF8`: `File.WriteAllText(path, content, Encoding.UTF8)`.
4. PowerShell запис: `Set-Content -Encoding UTF8` або `[System.Text.Encoding]::UTF8.GetBytes(...)`.
5. PowerShell веб-запити з JSON: НІКОЛИ не передавати український текст у `-Body $string`. Завжди через UTF-8 bytes:
   ```powershell
   $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
   Invoke-WebRequest -Body $bytes -ContentType "application/json; charset=utf-8"
   ```
6. GitHub release notes — UTF-8 bytes + `Content-Type: application/json; charset=utf-8`.

**Перевірка:** `dotnet build` + візуальна перевірка українських екранів. `Р›Р°СЃРєР°РІРѕ` / `??????` = P0 регресія, виправляти негайно.

---

## 7. Git правила

- Усі commit-повідомлення українською.
- Перед змінами: `git add . && git commit -m "Резервна точка перед <опис>"`.
- Після завершення: `git add . && git commit -m "<короткий опис українською>"`.
- Не комітити секрети.
- Не робити force-push, interactive rebase без погодження.

### GitIgnore

Підтримувати виключення: `.history/`, `.speckit/`, `.codex/`, `.kilocode/`, `.kilo/`, `.opencode/`, `.groupedtimelineinclude`, `.vscode/`, `.idea/`, `coverage/`, `TestResults/`.

---

## 8. Backup стратегія перед production-міграціями

Жодна міграція не виконується напряму в production без:
1. **Snapshot/Backup production** (Supabase Dashboard / pg_dump / PITR).
2. **Тестовий deployment** (окремий Supabase project або Branching).
3. **Post-Implementation Forensic** на тестовому.
4. **Production deployment** лише після успішного тесту.

Additive-only міграції з підготовленим rollback-скриптом — обов'язкові.

---

## 9. Migration Review (self-check ДО виконання)

Після написання міграції, ДО її виконання:
- Ідемпотентність (`IF EXISTS` / `IF NOT EXISTS`).
- Конфлікти з CHECK / FK / UNIQUE.
- **IMMUTABLE вимоги** для generated columns: `to_char(ts,…)` → STABLE, не підходить; `EXTRACT` для timestamptz → також STABLE; єдине рішення для timestamptz-derived computed — звичайна колонка + тригер.
- Порядок залежностей (DROP VIEW перед DROP COLUMN; `CREATE OR REPLACE` не змінює порядок колонок).
- Повнота rollback-скрипта.

---

## 10. Пріоритет джерел при конфлікті

1. `AGENTS.md` (цей файл).
2. `docs/SCLOCVerse-Knowledge-Base.md`.
3. `docs/observability/Observability-Constitution.md`.
4. Окремі forensic-документи.

---

## 11. Формат звіту після виконання задачі

```markdown
## Виконано
...

## Змінені файли
...

## Ризики
...

## Результат
...

## Commit
git commit -m "<опис українською>"
```

Коротко, технічно, без зайвої теорії.
