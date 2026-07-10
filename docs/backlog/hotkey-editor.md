# Phase 0.5 — Редактор гарячих клавіш

> Завершує підсистему налаштувань вводу: persistence, runtime rebind, capture комбінацій, conflict resolution, reset.
> Картка-індекс: KB §17.7. Пов'язане: ADR-009, [`settings-hub.md`](settings-hub.md), [`settings-hub-design-system.md`](settings-hub-design-system.md).

## Контекст

Phase 0 реалізував read-only список 13 гарячих клавіш (Варіант A+). Forensic виявив:
- `HotkeyDefinition.CurrentGesture` — settable, але **ніколи не персистується** → restart скидає.
- `IHotkeyService` не мав API переліку (додано `GetDefinitions()`) — але **немає API rebind** (зміни жесту на льоту).
- Немає persistence для кастомних жестів взагалі.

Phase 0.5 закриває цю дірку: користувач може змінити комбінацію, вона зберігається, застосовується глобально, конфлікти розв'язуються.

## Evidence (VER)

- `HotkeyDefinition.CurrentGesture` (settable) + `EffectiveGesture = CurrentGesture ?? DefaultGesture` → rebind = оновити CurrentGesture + re-register.
- `RegisterHotkeyBackend.TryRegister/Unregister(gesture)` → rebind = Unregister(старий) + TryRegister(новий). RawInput аналогічно через `_definitionsByGesture`.
- `Settings.Designer.cs` (user.config) — 18 полів, **machine-specific**; НЕ підходить для майбутньої синхронізації (Phase 2). Немає hotkey entries.
- JSON-persistence патерн існує: `SecureSessionStorage`, `update-cache.json`, `lia.meta.json` — `%LOCALAPPDATA%\SCLOCVerse\`, `System.Text.Json` / `Newtonsoft.Json`.

## Decision Engine

### Рішення 1: Persistence-модель (≥2 варіанти)

**Варіант A: Settings.Designer.cs entries (user.config).** 13 полів `HangarToggleOverlayGesture` (string). Патерн: як HangarOverlay*.
- ➕ відповідає існуючому Settings-патерну.
- ➖ 13 полів у user.config; жорстко прив'язано до версії схеми (нова гаряча клавіша = зміна Designer + .Save); string-кодування жесту; не масштабується.
- ➖ **user.config — machine-specific → несумісний з Phase 2 (Supabase sync)**.

**Варіант B: JSON `hotkeys.json` у `%LOCALAPPDATA%\SCLOCVerse\`.** ✅
- `Dictionary<HotkeyId-string, GestureString>`. Відсутній entry = default (масштабується). Патерн: як SecureSessionStorage / update-cache.
- ➕ один файл; версіонований; нова гаряча клавіша не ламає схему; портативний (підходить для Phase 2 sync); готує ґрунт для Phase 1 (profile.json).
- ➖ новий persistence-шар (але патерн доведений у проєкті).

**Варіант C: відкласти persistence до Phase 1.** Лише in-memory rebind (не зберігається).
- ➖ порушує AC (reset, restart-стабільність); псевдо-функціонал.

**Рішення: B** — JSON `hotkeys.json`. Machine-specific user.config неприйнятний для майбутньої синхронізації.

**Правила JSON (погоджено 2026-07-10):**
1. **Override-only:** зберігає лише перевизначені (CurrentGesture ≠ null). Reset до default → запис **видаляється**, не дублюється. Спрощує міграції й Supabase Sync.
2. **Versioned root:** `{ "version": 1, "bindings": { "HangarTimer.ToggleOverlay": "Ctrl+F10", ... } }`. Версія зараз фіксована = 1; готує ґрунт для майбутніх змін формату.

### Рішення 2: Rebind API (≥2 варіанти)

**Варіант R1: `IHotkeyService.Rebind(HotkeyId, HotkeyGesture, HotkeyConflictPolicy) → RebindResult`.** ✅
- Внутрішньо: lock, конфлікт-перевірка (чи новий жест зайнятий іншою enabled-дією), за політикою (Reject/Replace), оновити CurrentGesture, re-register у бекенді.
- ➕ чіткий контракт; ізолює UI від бекенду; Handler лишається (init).

**RebindResult (детальний статус, без винятків у UI — погоджено 2026-07-10):**
```csharp
public enum RebindResult
{
    Success,          // жест змінено + персистовано + re-registered
    Conflict,         // жест зайнятий іншою enabled-дією (RequiresConfirm + ConflictingId)
    InvalidGesture,   // capture-валідація: лише модифікатори / неповна комбінація
    RegistrationFailed,// Win32 RegisterHotKey FAIL (зайнято іншим застосунком)
    Unchanged          // новий жест == поточний EffectiveGesture
}
```
- `Conflict` → UI показує діалог «Перевизначити?»; підтвердження → повторний Rebind з `HotkeyConflictPolicy.Replace`.

**Conflict policy за замовчуванням:** `Reject` (безпечне). `Replace` — лише після **явного підтвердження** користувача (діалог макет 02).

**Варіант R2: Unregister + Register через існуючий API.**
- ➖ Register кидає на конфлікт (Reject); UI маніпулює внутрішнім станом; складно; порушує інкапсуляцію.

**Рішення: R1** — additive `Rebind` API.

## Мета (Acceptance Criteria)

* **AC1 (Persistence):** `hotkeys.json` у `%LOCALAPPDATA%\SCLOCVerse\`; зберігає лише перевизначені (CurrentGesture ≠ null) жесті; відсутній = default. Restart → жесті відновлюються.
* **AC2 (Rebind):** `IHotkeyService.Rebind(id, gesture, policy)` змінює жест на льоту (Win32 Unregister+Register), Handler лишається, дія працює глобально з новим жестом одразу.
* **AC3 (Capture):** Клік на комбінацію в Hub → режим capture (локальний, PreviewKeyDown вікна); Esc/Backspace скасовує; введений жест → Rebind. **Capture-валідація:** лише повні комбінації (≥1 модифікатор + клавіша, або «голa» клавіша без модифікаторів). **Не дозволяти зберігати лише модифікатори** (Ctrl/Shift/Alt без клавіші) → `InvalidGesture`.
* **AC4 (Conflict):** Повторне використання жесту іншої дії → `RebindResult.Conflict` → діалог «Вже використовується: «X» (Y). Перевизначити?» (макет 02). Так → повторний Rebind з `Replace` (витісняє стару → null, персистується). Ні → скасувати. За замовчуванням policy = `Reject`.
* **AC5 (Reset):** Per-item ↺ → CurrentGesture = null (→ Default, **запит видаляється з JSON**). Per-category ↺ → всі. **Reset All** (API готове; UI-кнопку можна не показувати) → очищає весь JSON.
* **AC6 (Zero Regression):** Існуючі 13 жестів працюють як раніше (без перевизначень = default). Read-only fallback → editor.
* **AC7 (P5):** Без fabricated функціоналу; UI відображає лише реальні визначення.
* **AC8 (UTF-8):** Українські тексти без mojibake.

## Зона впливу

* **Нові:** `Services/InputSystem/HotkeyBindingsStore.cs` (JSON persistence); `IHotkeyService.Rebind` + `RebindResult` (additive); `HotkeysSettingsPane` capture-режим + conflict-діалог + reset.
* **Розширення (не перепис):** `HotkeyService` (Rebind impl); `HotkeyDefinition.CurrentGesture` (вже settable); `HangarTimerService.RegisterHotkeys` (завантаження збережених жестів при реєстрації).
* **Без змін:** Backend (TryRegister/Unregister існуючі); схеми БД; Auth/Installer/Network.
* **Persistence:** `hotkeys.json` — additive новий файл; не ламає user.config.

## Ризики та мітігація

| Ризик | Мітігація |
|---|---|
| Rebind під час спрацювання тієї ж гарячої клавіші | lock у HotkeyService; rebind атомарний |
| Win32 RegisterHotKey FAIL на новому жесті (зайнято іншим застосунком) | RebindResult.Failure (Win32) → UI показує «комбінація недоступна» |
| hotkeys.json псується (mojibake/corrupt) | UTF-8 + try/catch → fallback default; не crash |
| Capture перехоплює глобальні події | Capture локальний (PreviewKeyDown вікна, лише в режимі capture) |

## Статус

- ✅ **DONE — Phase 0.5 реалізовано (2026-07-10).** JSON persistence (`hotkeys.json`, override-only, versioned) + `IHotkeyService.Rebind` API (RebindResult enum) + capture (PreviewKeyDown, capture-валідація) + conflict-діалог (Reject/Replace) + reset (per-item/per-category). Build 0 warnings.
- Останнє оновлення: 2026-07-10.