using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StarCitizenUA.Domain.Localization;
using StarCitizenUA.Infrastructure.Localization;

namespace StarCitizenUA.Tests;

/// <summary>
/// Набір мінімальних тестових заготівок для перевірки критичної логіки локалізатора.
/// </summary>
internal static class LocalizationInstallerTestStubs
{
    /// <summary>
    /// Перевірка, що зіставлення SHA256 нечутливе до регістру.
    /// </summary>
    public static void ShouldMatchHashesIgnoreCase()
    {
        var metadata = new LocalizationMetadata { Sha256 = "abc123" };
        Debug.Assert(metadata.HasMatchingHash("ABC123"), "Порівняння хешів має бути нечутливим до регістру.");
    }

    /// <summary>
    /// Імітація відповіді сервера з rate limit для перевірки обробки повторних спроб.
    /// </summary>
    public static void ShouldSignalRateLimit()
    {
        var result = ConditionalRequestResult.RateLimited(TimeSpan.FromSeconds(10));
        Debug.Assert(result.Status == ConditionalRequestStatus.RateLimited, "Очікується статус RateLimited.");
        Debug.Assert(Math.Abs((result.RetryAfter ?? TimeSpan.Zero).TotalSeconds - 10) < 0.1, "Значення RetryAfter має зберігатися.");
    }

    /// <summary>
    /// Приклад збереження та читання метаданих у тимчасовій директорії.
    /// </summary>
    public static async Task<LocalizationMetadata?> ShouldRoundTripMetadataAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "SCLocalizationUA-Tests");
        Directory.CreateDirectory(tempRoot);

        var originalLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        try
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", tempRoot);
            var store = new LocalizationMetadataStore();
            var metadata = new LocalizationMetadata
            {
                AssetId = 42,
                Sha256 = "deadbeef",
                ETag = "\"etag\"",
                FileSize = 1024,
                LastModified = DateTimeOffset.UtcNow
            };

            await store.WriteAsync("TEST", metadata, CancellationToken.None).ConfigureAwait(false);
            return await store.ReadAsync("TEST", CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", originalLocalAppData);
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Ігноруємо: це лише допоміжний тестовий артефакт.
            }
        }
    }
}
