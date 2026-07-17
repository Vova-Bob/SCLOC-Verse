using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Helpers
{
    /// <summary>
    /// Централізована політика HTTP-запитів до GitHub API:
    ///   - єдиний User-Agent у форматі GitHub-рекомендації
    ///     "SCLOC-Verse/&lt;version&gt; (+https://github.com/Vova-Bob/SCLOC-Verse)"
    ///   - Retry з exponential backoff для тимчасових помилок (429/5xx)
    ///   - 403 NOT retried: rate limit (primary/secondary) або access forbidden —
    ///     повторний запит не змінює умови, лише споживає ліміт.
    ///     Наступний цикл Background Monitor (10 хв) спробує знову.
    ///   - Retry-After: ≤10с — дочекатись; >10с — завершити без retry (desktop-застосунок,
    ///     не сервер — не тримаємо асинхронну операцію "висячою" 120 секунд)
    ///   - Без Retry-After: exponential backoff 1с → 2с → 4с
    ///
    /// Застосовується лише до МЕТАДАНИХ (releases, sha256, LIA status, localization metadata).
    /// Завантаження великих файлів (.exe, voice pack) — без автоматичного retry
    /// (наявна телеметрія + ручне повторення).
    /// </summary>
    public static class HttpRetryHelper
    {
        public const string RepositoryUrl = "https://github.com/Vova-Bob/SCLOC-Verse";

        /// <summary>
        /// Єдиний User-Agent для всіх GitHub-запитів.
        /// Використовує AssemblyInformationalVersion (відрізняє rc/preview/beta/release).
        /// </summary>
        public static string UserAgent { get; } = BuildUserAgent();

        /// <summary>Максимальний Retry-After (сек), що його дочекаємося. Більше — без retry.</summary>
        private const int MaxRetryAfterSeconds = 10;

        /// <summary>Кількість спроб exponential backoff при відсутності Retry-After.</summary>
        private const int MaxAttempts = 3;

        /// <summary>
        /// Відправляє запит з retry-політикою для тимчасових помилок GitHub API.
        /// Повертає останній response (успішний чи ні) — виклилець перевіряє EnsureSuccessStatusCode.
        /// НЕ викидає при 429/403/5xx — повертає response з відповідним статусом.
        /// </summary>
        public static async Task<HttpResponseMessage> SendWithRetryAsync(
            System.Net.Http.HttpClient client,
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken = default)
            => await SendWithRetryAsync(client, request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);

        /// <summary>
        /// Перевантаження для streamed-завантажень (великий global.ini, .exe тощо).
        /// <paramref name="completionOption"/> = ResponseHeadersRead дозволяє читати тіло
        /// потоком одразу після отримання заголовків — без буферизації всього тіла в пам'ять.
        /// </summary>
        public static async Task<HttpResponseMessage> SendWithRetryAsync(
            System.Net.Http.HttpClient client,
            System.Net.Http.HttpRequestMessage request,
            HttpCompletionOption completionOption,
            CancellationToken cancellationToken = default)
        {
            System.Net.Http.HttpResponseMessage? response = null;

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                // Clone request для повторної відправки (HttpRequestMessage не можна повторно використати).
                using var currentRequest = CloneRequest(request);

                response?.Dispose();
                response = await client.SendAsync(currentRequest, completionOption, cancellationToken).ConfigureAwait(false);

                if (!ShouldRetry(response))
                    return response;

                // Остання спроба — не чекаємо, повертаємо response як є.
                if (attempt == MaxAttempts)
                    return response;

                var delay = ComputeDelay(response, attempt);
                if (delay == null)
                {
                    // Retry-After > 10с — завершуємо без retry (desktop, не сервер).
                    // Спробуємо знову на наступному циклі Background Monitor.
                    return response;
                }

                try
                {
                    await Task.Delay(delay.Value, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    response.Dispose();
                    throw;
                }
            }

            return response!;
        }

        /// <summary>
        /// Визначає, чи треба повторити запит за статус-кодом.
        /// 429 Too Many Requests, 5xx Server Error.
        /// 403 — НЕ retry: rate limit (повтор не допоможе), access forbidden,
        /// або secondary rate limit (GitHub рекомендує почекати, а не спамити).
        /// </summary>
        private static bool ShouldRetry(System.Net.Http.HttpResponseMessage response)
        {
            var code = (int)response.StatusCode;
            return code == 429
                || (code >= 500 && code < 600);
        }

        /// <summary>
        /// Обчислює затримку перед наступною спробою.
        /// Retry-After ≤10с → дочекатись; >10с → null (не retry).
        /// Без Retry-After → exponential backoff: 1с, 2с, 4с.
        /// </summary>
        private static TimeSpan? ComputeDelay(System.Net.Http.HttpResponseMessage response, int attempt)
        {
            // GitHub повертає Retry-After (секунди) або DateTime — підтримуємо обидва.
            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                var raw = values.FirstOrDefault();
                if (raw != null && int.TryParse(raw, out var seconds))
                {
                    if (seconds <= MaxRetryAfterSeconds)
                        return TimeSpan.FromSeconds(seconds);
                    // > 10с — завершуємо без retry.
                    return null;
                }

                // HTTP-date формат (рідкісний для GitHub).
                if (raw != null
                    && DateTimeOffset.TryParse(raw, out var date)
                    && date > DateTimeOffset.UtcNow)
                {
                    var delta = date - DateTimeOffset.UtcNow;
                    if (delta.TotalSeconds <= MaxRetryAfterSeconds)
                        return delta;
                    return null;
                }
            }

            // Без Retry-After → exponential backoff: 1с, 2с, 4с.
            return TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        }

        /// <summary>
        /// Клонує HttpRequestMessage (бо його не можна повторно відправити після SendAsync).
        /// Зберігає method, uri, headers, content.
        /// </summary>
        private static System.Net.Http.HttpRequestMessage CloneRequest(System.Net.Http.HttpRequestMessage original)
        {
            var clone = new System.Net.Http.HttpRequestMessage(original.Method, original.RequestUri);

            // Копіюємо headers.
            foreach (var header in original.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            // Копіюємо content (потрібне для POST/PUT, хоча GitHub-метадані — лише GET).
            if (original.Content != null)
            {
                var contentBytes = original.Content.ReadAsByteArrayAsync(cancellationToken: default)
                    .GetAwaiter().GetResult();
                clone.Content = new System.Net.Http.ByteArrayContent(contentBytes);
                foreach (var header in original.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }

        private static string BuildUserAgent()
        {
            var version = Assembly
                .GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "unknown";
            return $"SCLOC-Verse/{version} (+{RepositoryUrl})";
        }
    }
}