using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Observability;
using Supabase.Postgrest;
using Supabase.Postgrest.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.Observability
{
    /// <summary>
    /// Фонова відправка подій у Supabase через той самий авторизований JWT
    /// (Конституція, Стаття 6 — idempotent; Стаття 12 — ingestion contract).
    /// </summary>
    /// <remarks>
    /// Batch insert (до 100 подій за 1 HTTP) з idempotency через client_event_id UNIQUE +
    /// resolution=ignore-duplicates (Стаття 6). Pre-auth події залишаються в черзі до появи
    /// валідного JWT (RLS вимагає user_id = auth.uid()).
    ///
    /// P0.3 — Poison eviction: Transient errors → requeue з retry-лічильником (max 3).
    /// Після вичерпання retry — подія скидається, щоб один poison не блокував чергу.
    ///
    /// P1.4 — Batch isolation: при permanent constraint violation батч ділиться навпіл
    /// (binary split) для ізоляції конкретного poison event. O(log n) додаткових запитів.
    /// </remarks>
    public sealed class TelemetryUploader : IDisposable
    {
        private const int BatchSize = 100;

        private static readonly QueryOptions InsertOptions = new()
        {
            OnConflict = "client_event_id",
            DuplicateResolution = QueryOptions.DuplicateResolutionType.IgnoreDuplicates,
            Returning = QueryOptions.ReturnType.Minimal
        };

        private readonly ISupabaseClientFactory _clientFactory;
        private readonly TelemetryEventQueue _queue;
        private readonly SemaphoreSlim _flushLock = new(1, 1);

        public TelemetryUploader(ISupabaseClientFactory clientFactory, TelemetryEventQueue queue)
        {
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        /// <summary>Відправляє один батч із черги. Ніколи не кидає (Стаття 1).</summary>
        public async Task FlushAsync()
        {
            try
            {
                await _flushLock.WaitAsync().ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            try
            {
                var client = _clientFactory.CreateClient();

                // RC-401 root cause fix (C): перевіряємо валідність сесії, а не лише CurrentUser.
                var session = client.Auth.CurrentSession;
                if (session == null || string.IsNullOrEmpty(session.AccessToken))
                    return;

                if (IsTokenExpired(session.AccessToken))
                    return;

                if (!Guid.TryParse(session.User?.Id?.ToString(), out var userId))
                    return;

                var batch = _queue.Drain(BatchSize);
                if (batch.Count == 0)
                    return;

                // Встановлюємо UserId для RLS (user_id = auth.uid()).
                var events = new List<TelemetryEvent>(batch.Count);
                foreach (var qe in batch)
                {
                    qe.Event.UserId = userId;
                    events.Add(qe.Event);
                }

                try
                {
                    // F: batch insert — 1 HTTP запит замість N (до 100× менше навантаження).
                    await client
                        .From<TelemetryEvent>()
                        .Insert(events, InsertOptions)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Telemetry] Помилка відправки батчу ({batch.Count} подій): {ex.Message}");

                    // RC-401: 401 — expired JWT. Drop всього батчу (requeue = нескінченний цикл).
                    if (ex is PostgrestException { StatusCode: 401 } ||
                        ex.InnerException is PostgrestException { StatusCode: 401 })
                        return;

                    // P1.4 — Permanent constraint violation: ізолюємо poison через binary split.
                    if (IsPermanentConstraintViolation(ex))
                    {
                        Debug.WriteLine("[Telemetry] Batch isolation: бінарний поділ для пошуку poison...");
                        await InsertWithIsolationAsync(client, events).ConfigureAwait(false);
                        return;
                    }

                    // Тимчасова помилка (500, network, timeout) — requeue з retry-лічильником.
                    // P0.3: після MaxRetries (3) події eviction-нуться (poison drop).
                    var evicted = _queue.Requeue(batch);
                    if (evicted > 0)
                        Debug.WriteLine($"[Telemetry] {evicted} подій скинуто (poison eviction після max retries).");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] Загальна помилка FlushAsync: {ex.Message}");
            }
            finally
            {
                try { _flushLock.Release(); }
                catch { /* Семфор вже відпущено — безпечно ігноруємо (Стаття 1). */ }
            }
        }

        /// <summary>
        /// P1.4 — Бінарний поділ батчу для ізоляції poison event.
        /// При constraint violation ділить батч навпіл і рекурсивно пробує кожну половину.
        /// O(log n) додаткових HTTP-запитів замість O(n) індивідуальних вставок.
        /// Похідні успішні вставки безпечні (idempotent через client_event_id UNIQUE).
        /// </summary>
        private async Task InsertWithIsolationAsync(Supabase.Client client, List<TelemetryEvent> events)
        {
            if (events.Count == 0)
                return;

            if (events.Count == 1)
            {
                // Одиночна подія вже не пройшла — підтверджений poison. Drop.
                var e = events[0];
                Debug.WriteLine($"[Telemetry] Poison event dropped: {e.Component}/{e.Operation}/{e.Outcome} (source={e.Source}, exception_type={e.ExceptionType})");
                return;
            }

            try
            {
                await client
                    .From<TelemetryEvent>()
                    .Insert(events, InsertOptions)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsPermanentConstraintViolation(ex))
            {
                // Ділимо навпіл і рекурсивно пробуємо кожну половину.
                var mid = events.Count / 2;
                await InsertWithIsolationAsync(client, events.GetRange(0, mid)).ConfigureAwait(false);
                await InsertWithIsolationAsync(client, events.GetRange(mid, events.Count - mid)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Non-permanent error під час isolation (network/500).
                // Events вже дреновані з черги — best-effort, не requeue.
                Debug.WriteLine($"[Telemetry] Isolation: non-permanent error, events втрачено: {ex.Message}");
            }
        }

        // RC-401 root cause fix (C): перевірка терміну дії JWT.
        private static bool IsTokenExpired(string accessToken)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                if (!handler.CanReadToken(accessToken))
                    return true;

                var jwt = handler.ReadJwtToken(accessToken);
                return jwt.ValidTo <= DateTime.UtcNow.AddSeconds(30);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Визначає, чи є помилка PostgreSQL постійною (constraint violation).
        /// Постійні помилки НІКОЛИ не успішні при retry — дані не змінюються.
        /// </summary>
        private static bool IsPermanentConstraintViolation(Exception ex)
        {
            var message = (ex.Message ?? string.Empty) + " " + (ex.InnerException?.Message ?? string.Empty);

            return message.Contains("violates check constraint", StringComparison.OrdinalIgnoreCase)
                || message.Contains("violates unique constraint", StringComparison.OrdinalIgnoreCase)
                || message.Contains("violates foreign key constraint", StringComparison.OrdinalIgnoreCase)
                || message.Contains("violates not-null constraint", StringComparison.OrdinalIgnoreCase)
                || message.Contains("null value in column", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate key value violates", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            _flushLock.Dispose();
        }
    }
}
