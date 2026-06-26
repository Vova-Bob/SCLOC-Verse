# Privacy Design: SCLOC-Verse

## Продуктова модель

SCLOC-Verse — це платформа для української спільноти Star Citizen, побудована навколо персонального **SCLOC Account**.

Discord OAuth використовується виключно як зовнішній Identity Provider для створення та підтвердження SCLOC Account.

## Принцип мінімізації даних

SCLOC-Verse не збирає дані "про всяк випадок".

Кожне персональне поле має відповідати трьом критеріям:

1. Використовується продуктом сьогодні або в запланованому релізі.
2. Дає зрозумілу цінність користувачу.
3. Неможливо реалізувати відповідну функцію без цього поля (або UX суттєво погіршується).

## OAuth scopes

Поточний Discord OAuth scope:

```
identify
```

### Чому лише identify

Scope `identify` повертає базовий профіль Discord-користувача. Цього достатньо для створення SCLOC Account і відображення профілю в додатку.

### Не використовуються

| Scope | Причина виключення |
|---|---|
| `email` | Не використовується для авторизації, відновлення доступу, підтримки, сповіщень, Cloud Sync, Premium чи Beta. Збільшує обсяг персональних даних без продуктової цінності. |
| `guilds` | Поточна версія продукту не використовує список Discord-спільнот. Future Capability для Community Center, яка вимагає окремого Product Review. |

### Примітка щодо email

Код SCLOC-Verse запитує у Discord лише `identify` scope. Однак Supabase GoTrue для вбудованого Discord provider формує OAuth URL з додатковим `email` scope:

```
scope=email+identify+identify
```

Це технічна особливість Supabase: вона додає email до запиту, але завдяки увімкненій опції `EXTERNAL_DISCORD_EMAIL_OPTIONAL` аутентифікація успішна навіть якщо email відсутній.

**SCLOC-Verse не зберігає email, не відображає email і не використовує email у жодній функції продукту.**

Email може бути присутній у `auth.users` таблиці Supabase як наслідок OAuth exchange, але SCLOC-Verse не звертається до нього.

## Дані, що збираються

### Identity Layer

| Поле | Джерело | Використання | Обґрунтування |
|---|---|---|---|
| `discord_user_id` | Discord OAuth `identify` | Єдиний зовнішній ідентифікатор, основа SCLOC Account. | Без нього неможливо створити акаунт і прив'язати інсталяцію. |
| `username` / `global_name` | Discord OAuth `identify` | Відображення імені в Account Dialog, кнопці акаунта в заголовку, tooltip. | Персоналізація, впізнавання власного акаунта. |
| `avatar_url` | Discord OAuth `identify` | Відображення аватара в Account Dialog і кнопці акаунта в заголовку. | Візуальна ідентифікація SCLOC Account. Fallback на ініціали існує, але avatar URL є основним візуальним елементом профілю. |

### Technical Metadata Layer

| Поле | Джерело | Використання |
|---|---|---|
| `install_id` | Локально згенерований GUID | Прив'язка інсталяції до користувача. |
| `machine_id` | `Environment.MachineName` | Діагностика та ідентифікація пристрою в межах акаунта. |
| `platform` | Константа "Windows" | Сегментація в аналітиці. |
| `os_version` | `Environment.OSVersion.VersionString` | Діагностика, аналітика сумісності. |
| `app_version` | Версія збірки SCLOC-Verse | Аналітика версій, діагностика. |
| `session_id`, `started_at`, `ended_at` | Локальний час UTC | Історія сесій, DAU/WAU/MAU, діагностика. |

## Дані, що не збираються

| Категорія | Приклади | Причина |
|---|---|---|
| Контактні дані | email, телефон | Не використовуються продуктом. |
| Адреса | фізична адреса проживання | Не потрібна. |
| Фінансові дані | банківські картки, рахунки | Не потрібні. |
| Документи | паспорт, ID-картки | Не потрібні. |
| Біометричні дані | відбитки, обличчя, голос | Не потрібні. |
| Соціальний граф | список Discord-спільнот | Future Capability, не використовується сьогодні. |

## Місце зберігання

| Дані | Місце | Примітка |
|---|---|---|
| Identity дані | Supabase `auth.users` | Керується Supabase Gotrue. |
| Installation metadata | Supabase `app_installations` | Таблиця проєкту SCLOC-Verse. |
| Sessions | Supabase `sessions` | Таблиця проєкту SCLOC-Verse. |
| Локальні токени | `%LocalAppData%\SCLOCVerse\.auth` | Зашифровано через DPAPI. |
| `install_id` | `%LocalAppData%\SCLOCVerse\install-id` + реєстр HKCU | Локальний ідентифікатор пристрою. |

## Майбутній перегляд

Повернутися до питання додаткових OAuth scopes лише після появи функціональності, яка об'єктивно їх потребує.

### Email

SCLOC-Verse не зберігає email і не використовує його в жодній функції. Email може проходити через Supabase OAuth exchange як наслідок вбудованого Discord provider, але продукт його ігнорує.

Email може бути повернений лише після впровадження:

- відновлення доступу до SCLOC Account;
- сповіщень користувачу;
- підтримки через email;
- Cloud Sync, що вимагає email як ідентифікатор.

### Guilds scope

Може бути повернений лише після впровадження:

- Community Center;
- Community Analytics;
- Discord Audience;
- Community Insights.

Кожне повернення вимагає окремого Product Review і оновлення цього документа.

## Права користувача

Користувач має право:

- отримати інформацію про свої дані;
- виправити неточні дані;
- видалити SCLOC Account і всі пов'язані дані;
- відкликати доступ SCLOC-Verse до Discord OAuth через налаштування Discord.

Процедура видалення акаунта описується в майбутньому публічному `PRIVACY.md`.

## Результати аудиту OAuth payload

### Фактичний OAuth URL

Перевірено через Playwright після зміни коду SCLOC-Verse на `Scopes = "identify"`:

```
https://discord.com/oauth2/authorize?client_id=1519140665940770946
&response_type=code
&redirect_uri=https%3A%2F%2Fnrytczdbhehiotflaagl.supabase.co%2Fauth%2Fv1%2Fcallback
&scope=email+identify+identify
&state=...
```

### Спостереження

- Код SCLOC-Verse запитує `identify`.
- Supabase GoTrue додає `email` до OAuth URL за технологічною необхідністю вбудованого провайдера.
- Опція `EXTERNAL_DISCORD_EMAIL_OPTIONAL` увімкнена в Supabase Dashboard.
- Аутентифікація працює навіть без email в обліковому записі Discord.

### Дані SCLOC-Verse

| Поле | Статус |
|---|---|
| `id` | Використовується |
| `username` / `global_name` | Використовується |
| `avatar_url` | Використовується |
| `email` | **Не використовується** |
| `guilds` | **Не використовується** |

## Відповідальність

Цей документ є внутрішнім продуктовим описом. Публічний `PRIVACY.md` створюється після стабілізації моделі SCLOC Account.
