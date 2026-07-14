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

                // RC-401 root cause fix (C): перевіряємо валідність сесії, а не лише CurrentUser.
                // SDK bug (Supabase.Gotrue 6.0.3): після failed token refresh CurrentSession
                // залишається non-null з expired JWT → PostgREST відправляє expired токен →
                // 401 → нескінченний requeue loop. Перевірка JWT expiry зупиняє storm на корені.
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
                foreach (var evt in batch)
                    evt.UserId = userId;

                try
                {
                    // F: batch insert — 1 HTTP запит замість N окремих (до 100× менше навантаження).
                    // IgnoreDuplicates + OnConflict=client_event_id → idempotent (Стаття 6).
                    // Returning=Minimal — відповідь без тіла (елементи нам не потрібні).
                    await client
                        .From<TelemetryEvent>()
                        .Insert(batch, new QueryOptions
                        {
                            OnConflict = "client_event_id",
                            DuplicateResolution = QueryOptions.DuplicateResolutionType.IgnoreDuplicates,
                            Returning = QueryOptions.ReturnType.Minimal
                        })
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Telemetry] Помилка відправки батчу ({batch.Count} подій): {ex.Message}");

                    // RC-401: 401 Unauthorized — сесія недійсна (expired JWT після failed refresh).
                    // Drop всього батчу — Requeue створить нескінченний цикл 401 (Стаття 1 — best-effort).
                    if (ex is PostgrestException { StatusCode: 401 } ||
                        ex.InnerException is PostgrestException { StatusCode: 401 })
                        return;

                    // Тимчасова помилка (500, network, timeout) — requeue для повторної спроби.
                    _queue.Requeue(batch);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] Загальна помилка FlushAsync: {ex.Message}");
            }
            finally
            {
                // P0: обовʼязковий Release семафора. Без нього будь-який вихід із критичної
                // секції (ранній return при null CurrentUser / порожній черзі / exception)
                // залишав _flushLock захопленим назавжди — усі наступні flush (Timer-tick,
                // FlushAsync з catch, Dispose flush) блокувались на WaitAsync() і ніколи
                // не відправляли events. Це пояснювало повну відсутність Failed-подій
                // у всій історії БД: будь-яка помилка виникала вже після першого flush,
                // який глухо блокував pipeline. CLR гарантує виконання finally навіть
                // при return/throw — тому ранні return залишаються як є.
                try { _flushLock.Release(); }
                catch { /* Семфор вже відпущено — безпечно ігноруємо (Стаття 1). */ }
            }
        }

        // RC-401 root cause fix (C): перевірка терміну дії JWT.
        // SDK bug залишає stale CurrentSession з expired access token.
        // Парсимо JWT напряму — це єдине авторитетне джерело дати експірації.
        private static bool IsTokenExpired(string accessToken)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                if (!handler.CanReadToken(accessToken))
                    return true;

                var jwt = handler.ReadJwtToken(accessToken);

                // Невичерпання ресурсів: jwt.ValidTo — це UTC.
                // +30с tolerance — на випадок розинхронізації годинника.
                return jwt.ValidTo <= DateTime.UtcNow.AddSeconds(30);
            }
            catch
            {
                // Не вдалось розібрати — вважаємо expired (conservative).
                return true;
            }
        }

        public void Dispose()
        {
            _flushLock.Dispose();
        }
    }
}
