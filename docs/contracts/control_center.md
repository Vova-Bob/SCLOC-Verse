# Control Center Contract — Supabase Schema

> Контракт між **Supabase** (SSOT) та **SCLOC Control Center**.
> SCLOC-Verse як власник міграцій створює та еволюціонує цей контракт.
> Control Center читає лише схему `control_center` і не залежить від `public.*`.

## Принципи

- **SSOT = Supabase.** Обидва продукти (SCLOC-Verse WPF і Control Center Blazor) читають одну БД.
- **Contracts First.** `control_center` — публічний контракт; `public` — внутрішня модель SCLOC-Verse.
- **Read-only.** Контракт містить лише View. Жодних таблиць, функцій, тригерів.
- **Versioned.** `control_center.contract_info` оголошує версію контракту.

## Версія

| Поле | Значення |
|---|---|
| `product` | `SCLOC-Verse` |
| `product_version` | `1.8.0` |
| `contract_version` | `1.0.0` |
| `api_version` | `1.0.0` |

## Об'єкти контракту

| View | Призначення |
|---|---|
| `control_center.contract_info` | Метадані контракту (версія, продукт) |
| `control_center.users` | Список користувачів для модуля Users |
| `control_center.installations` | Список інсталяцій |
| `control_center.statistics` | Агреговані метрики |
| `control_center.errors` | Звіти про помилки |
| `control_center.health` | Health-метрики |

## Еволюція

- Зміна внутрішньої структури `public.*` не ламає Control Center, доки View `control_center.*` залишаються стабільними.
- Нова версія контракту — окрема міграція SCLOC-Verse.
- Control Center перевіряє `contract_version`/`api_version` при старті.

## Безпека

- Роль `cc_readonly` створюється в окремій міграції (ADR-008) **після** Integration PASS.
- Вона має доступ тільки до схеми `control_center`.
- Жодних прав на `public`, `auth`, `analytics`, `storage`.
