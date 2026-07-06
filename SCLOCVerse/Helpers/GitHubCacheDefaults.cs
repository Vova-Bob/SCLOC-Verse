using System;

namespace SCLOCVerse.Helpers
{
    /// <summary>
    /// Єдине місце TTL- та timeout-констант для in-memory кешів GitHub-метаданих.
    /// Зменшення частоти GitHub-запитів проти unauth rate limit (60 req/год/IP).
    ///
    /// Лише параметри кешування. HTTP-політика (retry/backoff) живе в HttpRetryHelper;
    /// винесення спільної GitHubHttpPolicy — backlog post-Етап F.
    /// </summary>
    public static class GitHubCacheDefaults
    {
        /// <summary>TTL кешу remote-статусу LIA (LiaInstallStatus), хвилини.</summary>
        public const int StatusTtlMinutes = 5;

        /// <summary>TTL кешу списку релізів локалізації (/releases), хвилини.</summary>
        public const int ReleasesTtlMinutes = 5;

        /// <summary>Timeout очікування SemaphoreSlim перед fallback на кеш, секунди.</summary>
        public const int SemaphoreTimeoutSeconds = 15;

        /// <summary>Максимальний вік кешу для fallback при GitHub-помилці, години.</summary>
        public const int MaximumStaleHours = 24;

        public static readonly TimeSpan StatusTtl = TimeSpan.FromMinutes(StatusTtlMinutes);
        public static readonly TimeSpan ReleasesTtl = TimeSpan.FromMinutes(ReleasesTtlMinutes);
        public static readonly TimeSpan SemaphoreTimeout = TimeSpan.FromSeconds(SemaphoreTimeoutSeconds);
        public static readonly TimeSpan MaximumStaleAge = TimeSpan.FromHours(MaximumStaleHours);
    }
}