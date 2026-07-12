# RC-401 — Supabase SDK: HandleRefreshTimerTick не викликає DestroySession

> **Статус:** 🔵 HYPOTHESIS (доказ — SDK source code, не runtime-лог)
> **Пріоритет:** P1
> **Створено:** 2026-07-12 (RC-401 Hotfix)

## Проблема

`Supabase.Gotrue 6.0.3` — метод `HandleRefreshTimerTick` при non-HttpRequestException
кидає `SignedOut` event БЕЗ виклику `DestroySession()`.

Наслідок: `CurrentSession` (з expired access token) і `CurrentUser` залишаються non-null.
PostgREST продовжує відправляти expired JWT → 401.

## Доказ (SDK source)

```csharp
internal async void HandleRefreshTimerTick(object _)
{
    refreshTimer?.Dispose();
    try { await RefreshToken(); }
    catch (HttpRequestException ex)
    {
        // Network → retry 5с
        refreshTimer = new Timer(HandleRefreshTimerTick, null, 5000, -1);
    }
    catch (Exception ex)
    {
        // Non-network (вкл. 401 від refresh endpoint)
        StateChanged?.Invoke(this, new ClientStateChanged(AuthState.SignedOut));
        // ❌ DestroySession() НЕ викликано
        // ❌ Timer НЕ пересоздано
        // CurrentSession — залишається з expired token
        // CurrentUser — залишається non-null
    }
}
```

`DestroySession()` — єдиний метод, що чистить обидва:
```csharp
internal async Task DestroySession()
{
    CurrentSession = null;
    CurrentUser = null;
}
```

## Тимчасове рішення (RC-401 Hotfix)

`TelemetryUploader.cs:85` — 401 events not requeued (drop замість infinite retry).
Зупиняє 401 storm, але не лагодить root cause (stale session).

## Власність

- **SDK bug:** `HandleRefreshTimerTick` не викликає `DestroySession()` → stale `CurrentSession`
- **SCLOC gap 1:** `TelemetryUploader:50` перевіряє `CurrentUser` замість `CurrentSession`
- **SCLOC gap 2:** `AuthService.OnAuthStateChanged:359` не зупиняє telemetry timer при `SignedOut`

## Варіанти вирішення

1. **Обхід в SCLOC:** `OnAuthStateChanged` при `SignedOut` → stop telemetry timer + signal TelemetryUploader
2. **Обхід в SCLOC:** `TelemetryUploader:50` перевіряти `CurrentSession?.AccessToken` замість `CurrentUser`
3. **SDK fix (upstream):** `HandleRefreshTimerTick` catch(Exception) → додати `await DestroySession()`
4. **SDK upgrade:** перевірити чи виправлено в новіших версіях gotrue-csharp

## Джерела

- SDK source: `HandleRefreshTimerTick`, `DestroySession` (gotrue-csharp, fetched from GitHub)
- NuGet: `Supabase.Gotrue 6.0.3`, `Supabase.Postgrest 4.0.3`
- Production evidence: auth logs порожні (0 refresh attempts), `idx_notif_pending` unused
