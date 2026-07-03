using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Observability;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.Observability
{
    /// <summary>
    /// Фонова відправка подій у Supabase через той самий авторизований JWT
    /// (Конституція, Стаття 6 — idempotent; Стаття 12 — ingestion contract).
    /// </summary>
    /// <remarks>
    /// Slice 1: інсерт події-за-подією через Postgrest (idempotent через client_event_id +
    /// UNIQUE). Батч-інсерт та Edge Function — Phase 5. Pre-auth події залишаються в черзі
    /// до появи JWT (RLS вимагає user_id = auth.uid()).
    /// </remarks>
    public sealed class TelemetryUploader : IDisposable
    {
        private const int BatchSize = 100;

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

                // Pre-auth події чекають на JWT (RLS: user_id = auth.uid()).
                var currentUser = client.Auth.CurrentUser;
                if (currentUser == null)
                    return;

                if (!Guid.TryParse(currentUser.Id?.ToString(), out var userId))
                    return;

                var batch = _queue.Drain(BatchSize);
                if (batch.Count == 0)
                    return;

                var failed = new System.Collections.Generic.List<TelemetryEvent>();

                foreach (var evt in batch)
                {
                    evt.UserId = userId; // обовʼязково для RLS.

                    try
                    {
                        await client
                            .From<TelemetryEvent>()
                            .Insert(evt)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (IsDuplicate(ex))
                    {
                        // Вже вставлено раніше (повторна відправка) — вважаємо успіхом (Стаття 6).
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Telemetry] Помилка відправки події: {ex.Message}");
                        failed.Add(evt);
                    }
                }

                if (failed.Count > 0)
                    _queue.Requeue(failed);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] Загальна помилка FlushAsync: {ex.Message}");
            }
        }

        // Унікальне порушення client_event_id (Postgres 23505 / HTTP 409).
        private static bool IsDuplicate(Exception ex)
        {
            var message = ex.Message ?? string.Empty;
            var inner = ex.InnerException?.Message ?? string.Empty;
            return message.Contains("23505", StringComparison.Ordinal)
                || inner.Contains("23505", StringComparison.Ordinal)
                || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                || message.Contains("409", StringComparison.Ordinal)
                || inner.Contains("409", StringComparison.Ordinal);
        }

        public void Dispose()
        {
            _flushLock.Dispose();
        }
    }
}
