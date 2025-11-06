using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarCitizenUA.Domain.Localization;
using StarCitizenUA.Infrastructure.Localization;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Models;

namespace StarCitizenUA.Application.Localization;

/// <summary>
/// Сервіс встановлення локалізації з оптимізованим HTTP-завантаженням та кешем метаданих.
/// </summary>
public sealed class LocalizationInstaller : ILocalizationInstaller
{
    private const string UserCfgFileName = "user.cfg";
    private const string GlobalIniFileName = "global.ini";
    private const long MaxAllowedFileSizeBytes = 8 * 1024 * 1024; // 8 MB як запобіжник проти пошкоджених asset-ів
    private const int MaxRetryAttempts = 3;
    private static readonly string[] LocalizationPathSegments = { "Data", "Localization", "korean_(south_korea)" };
    private static readonly Encoding UserCfgEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly GitHubLocalizationClient _client;
    private readonly LocalizationMetadataStore _metadataStore;

    public LocalizationInstaller(GitHubLocalizationClient client, LocalizationMetadataStore metadataStore)
    {
        _client = client;
        _metadataStore = metadataStore;
    }

    /// <summary>
    /// Подія для оновлення прогресу в UI.
    /// </summary>
    public event EventHandler<LocalizationProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Подія для показу toast-повідомлень.
    /// </summary>
    public event EventHandler<LocalizationNotificationEventArgs>? NotificationRequested;

    public async Task<LocalizationInstallResult> InstallAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default)
    {
        ValidateEnvironment(environmentFolder, environmentName);
        cancellationToken.ThrowIfCancellationRequested();

        NotifyProgress(environmentName, LocalizationProgressStage.Початок);

        var localizationDir = BuildLocalizationDirectory(environmentFolder);
        Directory.CreateDirectory(localizationDir);

        NotifyProgress(environmentName, LocalizationProgressStage.ОтриманняРелізу);
        var asset = await _client.GetLatestAssetAsync(environmentName, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Не знайдено реліз із файлом {GlobalIniFileName} для середовища {environmentName}.");

        var destinationPath = Path.Combine(localizationDir, GlobalIniFileName);
        var metadata = await _metadataStore.ReadAsync(environmentName, cancellationToken).ConfigureAwait(false);
        var localFileExists = File.Exists(destinationPath);

        DownloadOutcome outcome;
        if (localFileExists && metadata?.AssetId == asset.AssetId)
        {
            NotifyProgress(environmentName, LocalizationProgressStage.ПеревіркаМетаданих);
            var conditional = await SendConditionalAsync(environmentName, asset.DownloadUrl, metadata, cancellationToken).ConfigureAwait(false);
            outcome = await HandleConditionalOutcomeAsync(environmentName, destinationPath, asset, metadata, conditional, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var conditional = await SendConditionalAsync(environmentName, asset.DownloadUrl, metadata: null, cancellationToken).ConfigureAwait(false);
            outcome = await HandleConditionalOutcomeAsync(environmentName, destinationPath, asset, metadata, conditional, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        LocalizationMetadata finalMetadata;
        bool localizationUpdated;
        if (outcome.Skipped)
        {
            finalMetadata = metadata ?? outcome.Metadata ?? throw new InvalidOperationException("Не вдалося оновити метадані після відповіді сервера.");
            localizationUpdated = false;
            NotifyProgress(environmentName, LocalizationProgressStage.Пропущено);
            Notify(environmentName, LocalizationNotificationType.Інформація, LocalizationMessages.LocalizationUpToDate(environmentName, asset.ReleaseTag));
        }
        else
        {
            finalMetadata = outcome.Metadata ?? throw new InvalidOperationException("Не вдалося записати метадані після завантаження локалізації.");
            localizationUpdated = outcome.FileReplaced;
            await _metadataStore.WriteAsync(environmentName, finalMetadata, cancellationToken).ConfigureAwait(false);
            NotifyProgress(environmentName, LocalizationProgressStage.Завершено, 100);
            Notify(environmentName, LocalizationNotificationType.Успіх, LocalizationMessages.LocalizationUpdated(environmentName, asset.ReleaseTag));
        }

        var userCfgPath = Path.Combine(environmentFolder, UserCfgFileName);
        bool userCfgCreated = false;
        if (!File.Exists(userCfgPath))
        {
            await File.WriteAllTextAsync(userCfgPath, BuildUserCfgContent(), UserCfgEncoding, cancellationToken).ConfigureAwait(false);
            userCfgCreated = true;
            Notify(environmentName, LocalizationNotificationType.Інформація, LocalizationMessages.UserCfgCreated(environmentName));
        }

        var finalMessage = LocalizationMessages.InstallResult(localizationUpdated, environmentName, asset.ReleaseTag, userCfgCreated);
        return new LocalizationInstallResult(true, environmentName, destinationPath, userCfgCreated ? userCfgPath : null, finalMessage);
    }

    public Task<LocalizationDeleteResult> DeleteAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default)
    {
        ValidateEnvironment(environmentFolder, environmentName);
        cancellationToken.ThrowIfCancellationRequested();

        var localizationDir = BuildLocalizationDirectory(environmentFolder);
        var globalIniPath = Path.Combine(localizationDir, GlobalIniFileName);
        var userCfgPath = Path.Combine(environmentFolder, UserCfgFileName);

        bool userCfgDeleted = DeleteFileIfExists(userCfgPath);
        bool globalIniDeleted = DeleteFileIfExists(globalIniPath);

        string message = userCfgDeleted && globalIniDeleted
            ? $"Файли локалізації для {environmentName} видалено."
            : userCfgDeleted
                ? $"Файл user.cfg для {environmentName} видалено."
                : globalIniDeleted
                    ? $"Файл global.ini для {environmentName} видалено."
                    : $"Файли локалізації для {environmentName} не знайдено.";

        return Task.FromResult(new LocalizationDeleteResult(userCfgDeleted || globalIniDeleted, userCfgDeleted, globalIniDeleted, message));
    }

    private async Task<DownloadOutcome> HandleConditionalOutcomeAsync(string environmentName, string destinationPath, ReleaseAssetDescriptor asset, LocalizationMetadata? existingMetadata, ConditionalRequestResult conditional, CancellationToken ct)
    {
        switch (conditional.Status)
        {
            case ConditionalRequestStatus.NotModified:
                return DownloadOutcome.ForSkip(existingMetadata);
            case ConditionalRequestStatus.PreconditionFailed:
                return await ForceDownloadAsync(environmentName, destinationPath, asset, existingMetadata, ct).ConfigureAwait(false);
            case ConditionalRequestStatus.RateLimited:
                var rateMessage = LocalizationMessages.RateLimited(conditional.RetryAfter);
                Notify(environmentName, LocalizationNotificationType.Попередження, rateMessage);
                throw new HttpRequestException(rateMessage);
            case ConditionalRequestStatus.Forbidden:
                var errorMessage = LocalizationMessages.Forbidden(conditional.ErrorMessage);
                Notify(environmentName, LocalizationNotificationType.Помилка, errorMessage);
                throw new HttpRequestException(errorMessage);
            case ConditionalRequestStatus.Success:
                using (conditional.Response!)
                {
                    return await DownloadAssetAsync(environmentName, destinationPath, asset, existingMetadata, conditional.Response!, ct).ConfigureAwait(false);
                }
            default:
                throw new InvalidOperationException("Отримано невідомий статус умовного запиту.");
        }
    }

    private async Task<DownloadOutcome> ForceDownloadAsync(string environmentName, string destinationPath, ReleaseAssetDescriptor asset, LocalizationMetadata? existingMetadata, CancellationToken ct)
    {
        NotifyProgress(environmentName, LocalizationProgressStage.ЗапитДоСервера);
        var retryDelay = TimeSpan.FromSeconds(2);
        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _client.SendConditionalRequestAsync(asset.DownloadUrl, metadata: null, ct).ConfigureAwait(false);
            if (result.Status == ConditionalRequestStatus.RateLimited && attempt < MaxRetryAttempts)
            {
                await Task.Delay(result.RetryAfter ?? retryDelay, ct).ConfigureAwait(false);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
                continue;
            }

            return await HandleConditionalOutcomeAsync(environmentName, destinationPath, asset, existingMetadata, result, ct).ConfigureAwait(false);
        }

        throw new HttpRequestException("Перевищено кількість спроб завантаження asset-у після відповіді 412.");
    }

    private async Task<ConditionalRequestResult> SendConditionalAsync(string environmentName, string downloadUrl, LocalizationMetadata? metadata, CancellationToken ct)
    {
        NotifyProgress(environmentName, LocalizationProgressStage.ЗапитДоСервера);
        var retryDelay = TimeSpan.FromSeconds(2);
        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _client.SendConditionalRequestAsync(downloadUrl, metadata, ct).ConfigureAwait(false);
            if (result.Status == ConditionalRequestStatus.RateLimited && attempt < MaxRetryAttempts)
            {
                await Task.Delay(result.RetryAfter ?? retryDelay, ct).ConfigureAwait(false);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
                continue;
            }

            return result;
        }

        return ConditionalRequestResult.RateLimited(TimeSpan.FromSeconds(30));
    }

    private async Task<DownloadOutcome> DownloadAssetAsync(string environmentName, string destinationPath, ReleaseAssetDescriptor asset, LocalizationMetadata? existingMetadata, HttpResponseMessage response, CancellationToken ct)
    {
        ValidateResponseContent(response, asset);
        NotifyProgress(environmentName, LocalizationProgressStage.Завантаження, 0);

        var tempFilePath = CreateTempFilePath(destinationPath);
        try
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var tempStream = new FileStream(tempFilePath, new FileStreamOptions
            {
                Access = FileAccess.Write,
                Share = FileShare.None,
                Mode = FileMode.Create,
                Options = FileOptions.Asynchronous
            });

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            long totalBytes = 0;

            try
            {
                while (true)
                {
                    var read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    totalBytes += read;
                    if (totalBytes > MaxAllowedFileSizeBytes)
                    {
                        throw new InvalidDataException($"Розмір asset-у перевищує {MaxAllowedFileSizeBytes / (1024 * 1024)} МБ.");
                    }

                    hasher.AppendData(buffer, 0, read);
                    await tempStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

                    var percent = response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength > 0
                        ? Math.Clamp((double)totalBytes / response.Content.Headers.ContentLength.Value * 100d, 0, 100)
                        : null;
                    NotifyProgress(environmentName, LocalizationProgressStage.Завантаження, percent);
                }

                await tempStream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            NotifyProgress(environmentName, LocalizationProgressStage.ПеревіркаЦілісності, 100);

            if (existingMetadata is not null && existingMetadata.HasMatchingHash(hash) && File.Exists(destinationPath))
            {
                return DownloadOutcome.ForSkip(existingMetadata);
            }

            ReplaceFileAtomically(tempFilePath, destinationPath);

            var etag = response.Headers.ETag?.Tag;
            var lastModified = response.Content.Headers.LastModified;
            if (!lastModified.HasValue && response.Headers.TryGetValues("Last-Modified", out var values))
            {
                var candidate = values.FirstOrDefault();
                if (candidate is not null && DateTimeOffset.TryParse(candidate, out var parsed))
                {
                    lastModified = parsed;
                }
            }

            var metadata = new LocalizationMetadata
            {
                AssetId = asset.AssetId,
                Sha256 = hash,
                ETag = etag,
                LastModified = lastModified,
                FileSize = response.Content.Headers.ContentLength ?? totalBytes
            };

            return new DownloadOutcome(metadata, fileReplaced: true, skipped: false);
        }
        finally
        {
            CleanupTempFile(tempFilePath);
        }
    }

    private static void ValidateResponseContent(HttpResponseMessage response, ReleaseAssetDescriptor asset)
    {
        if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength > MaxAllowedFileSizeBytes)
        {
            throw new InvalidDataException($"Asset {asset.Name} занадто великий ({response.Content.Headers.ContentLength.Value} байт).");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? asset.ContentType;
        if (contentType is not null && !contentType.Contains("text", StringComparison.OrdinalIgnoreCase) && !contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Отримано неочікуваний Content-Type: {contentType}.");
        }
    }

    private static string BuildLocalizationDirectory(string envFolder)
        => Path.Combine(envFolder, Path.Combine(LocalizationPathSegments));

    private static string BuildUserCfgContent()
        => "g_language = korean_(south_korea)" + Environment.NewLine + "g_languageAudio = english";

    private static bool DeleteFileIfExists(string path)
    {
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private static string CreateTempFilePath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath) ?? throw new DirectoryNotFoundException("Не вдалося визначити директорію для тимчасового файлу.");
        Directory.CreateDirectory(directory);
        var tempName = Path.GetRandomFileName();
        return Path.Combine(directory, tempName + ".tmp");
    }

    private static void ReplaceFileAtomically(string tempFilePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Move(tempFilePath, destinationPath, overwrite: true);
        }
        else
        {
            File.Move(tempFilePath, destinationPath);
        }
    }

    private static void CleanupTempFile(string tempFilePath)
    {
        try
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
        catch
        {
            // Ігноруємо: файл буде перезаписано при наступному завантаженні.
        }
    }

    private static void ValidateEnvironment(string folder, string name)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Шлях до папки середовища не задано.", nameof(folder));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Назва середовища не задана.", nameof(name));
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Папку середовища \"{folder}\" не знайдено.");
    }

    private void NotifyProgress(string environmentName, LocalizationProgressStage stage, double? percent = null)
    {
        ProgressChanged?.Invoke(this, new LocalizationProgressEventArgs(environmentName, stage, percent));
    }

    private void Notify(string environmentName, LocalizationNotificationType type, string message)
    {
        NotificationRequested?.Invoke(this, new LocalizationNotificationEventArgs(environmentName, type, message));
    }

    private sealed record DownloadOutcome(LocalizationMetadata? Metadata, bool FileReplaced, bool Skipped)
    {
        public static DownloadOutcome ForSkip(LocalizationMetadata? metadata) => new(metadata, false, true);
    }
}
