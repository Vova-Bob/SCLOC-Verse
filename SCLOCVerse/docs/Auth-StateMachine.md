# Auth State Machine: SCLOC-Verse

## Контекст

SCLOC-Verse переходить на модель обов'язкової Discord-авторизації. Користувач не може потрапити в основний інтерфейс без SCLOC Account.

Цей документ визначає повну специфікацію станів, переходів і подій аутентифікації. Код має реалізовувати цю специфікацію, а не навпаки.

## Джерело істини

Єдине джерело істини стану авторизації:

```
AuthState

Unknown
Checking
SignedOut
SigningIn
SignedIn
Error
```

Використовується `IAuthStatusProvider.State` та подія `IAuthStatusProvider.StatusChanged`.

Жодних додаткових прапорців типу `IsAuthenticated` не додається.

## Стани застосунку

Застосунок працює в одному з двох режимів:

```
┌─────────────────────┐     ┌─────────────────────┐
│    Auth Gate Mode   │     │     Main UI Mode    │
│                     │     │                     │
│  User is not ready  │     │  User is ready      │
│  to use the app     │     │  to use the app     │
└─────────────────────┘     └─────────────────────┘
```

Перемикання між режимами відбувається виключно на основі `AuthState`.

## Стани аутентифікації

### Checking

**Коли:** Початковий стан при створенні `AuthService` та під час активної перевірки збереженої сесії в `TryRestoreSessionAsync`.

**UI:** Auth Gate Mode. Кнопка «Увійти» disabled. Показується повідомлення «Перевіряємо сесію...».

**Дозволені дії:** Немає.

### SignedOut

**Коли:** Немає збереженої сесії або сесія недійсна.

**UI:** Auth Gate Mode. Активна кнопка «Увійти через Discord».

**Дозволені дії:** Користувач може натиснути «Увійти через Discord».

### SigningIn

**Коли:** Активний OAuth flow: браузер відкрито, очікується callback.

**UI:** Auth Gate Mode. Кнопка замінена на індикатор «Відкриваємо Discord...» та кнопка «Скасувати».

**Дозволені дії:** Користувач може натиснути «Скасувати».

### SignedIn

**Коли:** OAuth успішний, сесія встановлена, профіль отримано.

**UI:** Main UI Mode. Всі функції доступні.

**Дозволені дії:** Повний функціонал додатку.

### Error

**Коли:** Сталася помилка авторизації або відновлення сесії.

**UI:** Auth Gate Mode. Показується повідомлення про помилку та кнопка «Спробувати ще раз».

**Дозволені дії:** Користувач може повторити спробу входу.

## Діаграма переходів

```
                    ┌─────────┐
                    │ Unknown │
                    └────┬────┘
                         │
                         ▼
                   ┌───────────┐
                   │  Checking │
                   └─────┬─────┘
           ┌─────────────┼─────────────┐
           │             │             │
           ▼             ▼             ▼
    ┌──────────┐  ┌──────────┐  ┌──────────┐
    │ SignedIn │  │SignedOut │  │  Error   │
    └────┬─────┘  └────┬─────┘  └────┬─────┘
         │             │              │
         │             │              │
         │             ▼              │
         │      ┌───────────┐         │
         │      │ SigningIn │         │
         │      └─────┬─────┘         │
         │            │               │
         │    ┌───────┼───────┐       │
         │    │       │       │       │
         │    ▼       ▼       ▼       │
         │ ┌──────┐ ┌─────┐ ┌─────┐   │
         │ │Signed│ │Signed│ │Error│◄──┘
         │ │ In   │ │ Out │ │     │
         │ └──┬───┘ └─────┘ └─────┘
         │    │
         └────┘
```

## Переходи

| З | Подія | До | Ініціатор | Дія |
|---|---|---|---|---|
| `Checking` | App startup | `Checking` | `MainWindow` | Виклик `TryRestoreSessionAsync`. |
| `Checking` | Session restored | `SignedIn` | `AuthService` | `TryRestoreSessionAsync` повертає `true`. |
| `Checking` | No session | `SignedOut` | `AuthService` | `TryRestoreSessionAsync` повертає `false`. |
| `Checking` | Restore failed | `Error` | `AuthService` | Виняток під час restore. |
| `SignedOut` | User clicks Sign In | `SigningIn` | `AuthGate` | Виклик `AuthService.SignInAsync`. |
| `Error` | User clicks Retry | `SigningIn` | `AuthGate` | Виклик `AuthService.SignInAsync`. |
| `SigningIn` | OAuth success | `SignedIn` | `AuthService` | `SignInAsync` повертає `Success`. |
| `SigningIn` | access_denied | `SignedOut` | `AuthService` | `SignInAsync` повертає `Cancelled`. |
| `SigningIn` | User clicks Cancel | `SignedOut` | `AuthGate` | Скасування `CancellationTokenSource`. |
| `SigningIn` | Failure / Timeout | `Error` | `AuthService` | `SignInAsync` повертає `Failure`. |
| `SignedIn` | User clicks Sign Out | `SignedOut` | `AuthService` | Виклик `AuthService.SignOutAsync`. |
| `SignedIn` | Token refresh failure | `SignedOut` | `AuthService` | `SetSession` або refresh не вдався. |

## Відображення станів в UI

| Стан | Режим застосунку | Головний вміст | Меню зліва | Кнопка акаунта |
|---|---|---|---|---|
| `Checking` | Auth Gate | «Перевіряємо сесію...» | Disabled | Неактивна |
| `SignedOut` | Auth Gate | Пояснення + «Увійти через Discord» | Disabled | Неактивна або «Увійти» |
| `SigningIn` | Auth Gate | «Відкриваємо Discord...» + Cancel | Disabled | Неактивна |
| `Error` | Auth Gate | Повідомлення про помилку + Retry | Disabled | Неактивна |
| `SignedIn` | Main UI | HomeCanvas за замовчуванням | Enabled | Показує профіль |

## Поведінка режимів

### Auth Gate Mode

- Фон додатку залишається видимим.
- Меню зліва видно, але всі кнопки disabled.
- TitleBar працює: перетягування, згортання, закриття.
- При натисканні на disabled кнопку меню нічого не відбувається.
- Кнопка акаунта або неактивна, або показує текст «Увійти» і не відкриває AccountDialog.

### Main UI Mode

- HomeCanvas видно за замовчуванням.
- Меню активне.
- Кнопка акаунта відкриває AccountDialog.
- AuthGateCanvas не видно.

## Події та їх обробка

### App Startup

1. `MainWindow` створюється.
2. `AuthService` створюється в стані `Unknown`.
3. `MainWindow` підписується на `StatusChanged`.
4. `MainWindow` показує `AuthGateCanvas`.
5. `MainWindow` викликає `_authService.TryRestoreSessionAsync`.
6. Результат визначає наступний стан.

### User Sign In

1. Користувач натискає «Увійти через Discord».
2. `AuthGateCanvas` викликає `_authService.SignInAsync`.
3. Стан змінюється на `SigningIn`.
4. `AuthGateCanvas` показує індикатор очікування.
5. Результат OAuth визначає наступний стан.

### User Cancels

1. Користувач натискає «Скасувати».
2. `AuthGateCanvas` скасовує `CancellationToken`.
3. `AuthService` повертає `AuthResult.Cancelled`.
4. Стан змінюється на `SignedOut`.
5. `AuthGateCanvas` повертається до початкового екрану.

### OAuth access_denied

1. Користувач натискає «Скасувати» в Discord або закриває вікно.
2. Discord повертає `error=access_denied`.
3. `AuthService` повертає `AuthResult.Cancelled`.
4. Стан змінюється на `SignedOut`.
5. `AuthGateCanvas` не показує діалогове вікно з помилкою.

### OAuth Failure

1. Таймаут, відсутність інтернету, помилка Supabase або Discord.
2. `AuthService` повертає `AuthResult.Failure`.
3. Стан змінюється на `Error`.
4. `AuthGateCanvas` показує повідомлення про помилку.

### Sign Out

1. Користувач натискає «Вийти» в AccountDialog.
2. `AuthService.SignOutAsync` закриває сесію.
3. Стан змінюється на `SignedOut`.
4. `MainWindow` перемикається в Auth Gate Mode.

## Інтеграція з MainWindow

`MainWindow` не зберігає власного стану авторизації. Він тільки реагує на `IAuthStatusProvider.StatusChanged`.

```
OnStatusChanged:
    switch (newState)
        case SignedIn:  ShowMainUi()
        case SignedOut: ShowAuthGate()
        case Error:     ShowAuthGateWithError()
        case SigningIn: ShowAuthGateAuthenticating()
        case Checking:  ShowAuthGateChecking()
        case Unknown:   ShowAuthGateChecking()
```

## Інтеграція з AuthGateCanvas

`AuthGateCanvas` отримує `IAuthService` і керує тільки вмістом Auth Gate:

- Початковий екран.
- Індикатор очікування.
- Повідомлення про помилку.

`AuthGateCanvas` не вирішує, чи показувати Main UI. Це робить `MainWindow`.

## Відкладені питання

Цей документ не покриває:

- Дизайн кнопок і кольорів Auth Gate.
- Точний текст повідомлень про помилки.
- Анімації переходу між режимами.
- Поведінку при оновленні додатка зі старої версії (onboarding message).

Ці питання вирішуються на етапі UI-реалізації, але поведінкова модель вже зафіксована.
