using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace SCLOCVerse.Services.LocalizationServices
{
    public sealed class LocalizationInstaller : ILocalizationInstaller
    {
        private const string RepositoryId = "Vova-Bob/SC_localization_UA";
        private const string UserCfgFileName = "user.cfg";
        private const string GlobalIniFileName = "global.ini";
        private const string ReleasesApiUrl = "https://api.github.com/repos/Vova-Bob/SC_localization_UA/releases";
        private static readonly string[] LocalizationPathSegments = { "Data", "Localization", "korean_(south_korea)" };
        private static readonly Encoding UserCfgEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };

        // In-memory кеш списку релізів локалізації (/releases) з TTL.
        // Оркестратор викликає InstallAsync для КОЖНОГО середовища (LIVE/PTU/EPTU/HOTFIX) —
        // без кешу це 4× GET /releases за цикл. З кешем — 1× за ReleasesTtl (5 хв).
        // Instance поля — майбутнє розширення (різні репозиторії/конфігурації).
        private sealed record CachedReleases(IReadOnlyList<ReleasePayload>? Value, DateTimeOffset FetchedAt, string? ETag, DateTimeOffset? LastModified)
        {
            public bool IsFresh => Value is not null && DateTimeOffset.UtcNow - FetchedAt < GitHubCacheDefaults.ReleasesTtl;
        }

        private CachedReleases? _cachedReleases;
        private readonly SemaphoreSlim _releasesSemaphore = new(1, 1);

        public event Action<LocalizationProgressUpdate>? ProgressChanged;
        public event Action<LocalizationNotification>? NotificationRaised;

        public async Task<LocalizationInstallResult> InstallAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default)
        {
            ValidateEnvironment(environmentFolder, environmentName);
            cancellationToken.ThrowIfCancellationRequested();

            var localizationDir = BuildLocalizationDirectory(environmentFolder);
            var globalIniPath = Path.Combine(localizationDir, GlobalIniFileName);

            Directory.CreateDirectory(localizationDir);

            var metadataPath = GetMetadataPath(environmentName);
            var metadata = await ReadMetadataAsync(environmentName, cancellationToken).ConfigureAwait(false);
            bool isInstalled = TryValidateLocalizationState(metadataPath, globalIniPath, ref metadata);

            var release = (await GetReleaseAsync(environmentName, cancellationToken).ConfigureAwait(false))
                ?? throw new InvalidOperationException(LocalizationMessages.ReleaseNotFound(environmentName));

            var asset = release.Assets?.FirstOrDefault(a => a.Name.Equals(GlobalIniFileName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(LocalizationMessages.AssetMissing(release.TagName, GlobalIniFileName));

            ProgressChanged?.Invoke(LocalizationProgressUpdate.Checking());

            var conditionalResult = await TryConditionalDownloadAsync(asset, metadata, cancellationToken).ConfigureAwait(false);

            if (conditionalResult.Status == ConditionalRequestStatus.NotModified && !isInstalled)
            {
                conditionalResult = ConditionalRequestResult.ForceDownload();
            }

            bool localizationUpdated = false;
            LocalizationMetadata? updatedMetadata = null;

            if (conditionalResult.Status != ConditionalRequestStatus.NotModified)
            {
                HttpResponseMessage? response = conditionalResult.Response;
                if (response is null)
                {
                    response = await DownloadAssetAsync(asset.DownloadUrl!, cancellationToken).ConfigureAwait(false);
                }

                SaveAssetResult downloadResult;
                try
                {
                    downloadResult = await SaveAssetAsync(response, asset, globalIniPath, metadata, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    response.Dispose();
                }

                updatedMetadata = downloadResult.Metadata with { InstalledTag = release.TagName };

                if (!downloadResult.HashMatchesExisting)
                {
                    CommitTemp(downloadResult.TempPath, globalIniPath, downloadResult.DestinationExists);
                    localizationUpdated = true;
                }
                else if (!downloadResult.DestinationExists)
                {
                    CommitTemp(downloadResult.TempPath, globalIniPath, false);
                    localizationUpdated = true;
                }
                else if (File.Exists(downloadResult.TempPath))
                {
                    File.Delete(downloadResult.TempPath);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            string? userCfgPathCreated = null;
            var userCfgPath = Path.Combine(environmentFolder, UserCfgFileName);
            if (!File.Exists(userCfgPath))
            {
                await File.WriteAllTextAsync(userCfgPath, BuildUserCfgContent(), UserCfgEncoding, cancellationToken).ConfigureAwait(false);
                userCfgPathCreated = userCfgPath;
            }

            if (updatedMetadata is not null)
            {
                await WriteMetadataAsync(metadataPath, updatedMetadata, cancellationToken).ConfigureAwait(false);
            }

            var message = LocalizationMessages.InstallCompleted(environmentName, release.TagName, userCfgPathCreated != null, localizationUpdated);

            ProgressChanged?.Invoke(LocalizationProgressUpdate.Completed(localizationUpdated));
            NotificationRaised?.Invoke(LocalizationNotification.Completed(message, localizationUpdated));

            // HasUpdate = localizationUpdated: BackgroundUpdateMonitor та NotificationRouter
            // перевіряють саме HasUpdate (не Success) для вирішення «показувати toast чи ні».
            // Раніше цей параметр не передавався → default false → toast при автооновленні
            // ніколи не показувався (регресія після рефакторингу record на 7 полів).
            // Named params — запобігає повторенню пропущеного позиційного аргументу.
            return new LocalizationInstallResult(
                Success: localizationUpdated,
                EnvironmentName: environmentName,
                GlobalIniPath: globalIniPath,
                UserCfgPath: userCfgPathCreated,
                Message: message,
                Version: release.TagName,
                HasUpdate: localizationUpdated);
        }

        /// <summary>
        /// Перевірка оновлення локалізації без встановлення (AutoUpdate=OFF).
        /// Без побічних ефектів: global.ini не записується, user.cfg не створюється,
        /// metadata не оновлюється. Лише get release (з кешу) + порівняння remote
        /// TagName з metadata.InstalledTag. Null InstalledTag означає "версія невідома"
        /// → вважається, що оновлення доступне (програма має повідомити користувача).
        /// </summary>
        public async Task<LocalizationInstallResult> CheckAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default)
        {
            ValidateEnvironment(environmentFolder, environmentName);
            cancellationToken.ThrowIfCancellationRequested();

            var release = await GetReleaseAsync(environmentName, cancellationToken).ConfigureAwait(false);
            if (release is null)
            {
                return new LocalizationInstallResult(
                    Success: false,
                    environmentName,
                    GlobalIniPath: string.Empty,
                    UserCfgPath: null,
                    Message: LocalizationMessages.ReleaseNotFound(environmentName),
                    Version: null,
                    HasUpdate: false);
            }

            var metadata = await ReadMetadataAsync(environmentName, cancellationToken).ConfigureAwait(false);
            var installedTag = metadata?.InstalledTag;
            var hasUpdate = !string.Equals(installedTag, release.TagName, StringComparison.OrdinalIgnoreCase);

            return new LocalizationInstallResult(
                Success: true,
                environmentName,
                GlobalIniPath: string.Empty,
                UserCfgPath: null,
                Message: hasUpdate ? LocalizationMessages.UpdateAvailable(environmentName, release.TagName) : string.Empty,
                Version: release.TagName,
                HasUpdate: hasUpdate);
        }

        public Task<LocalizationDeleteResult> DeleteAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default)
        {
            ValidateEnvironment(environmentFolder, environmentName);
            cancellationToken.ThrowIfCancellationRequested();

            var localizationDir = BuildLocalizationDirectory(environmentFolder);
            var globalIniPath = Path.Combine(localizationDir, GlobalIniFileName);
            var userCfgPath = Path.Combine(environmentFolder, UserCfgFileName);
            var metadataPath = GetMetadataPath(environmentName);

            bool userCfgDeleted = DeleteFileIfExists(userCfgPath);
            bool globalIniDeleted = DeleteFileIfExists(globalIniPath);
            bool metadataDeleted = DeleteFileIfExists(metadataPath);

            string message = userCfgDeleted && globalIniDeleted
                ? LocalizationMessages.DeleteAll(environmentName)
                : userCfgDeleted
                    ? LocalizationMessages.DeleteUserCfg(environmentName)
                    : globalIniDeleted
                        ? LocalizationMessages.DeleteGlobalIni(environmentName)
                        : LocalizationMessages.DeleteMissing(environmentName);

            return Task.FromResult(new LocalizationDeleteResult(userCfgDeleted || globalIniDeleted || metadataDeleted, userCfgDeleted, globalIniDeleted, message));
        }

        public static bool IsLocalizationInstalled(string environmentFolder, string environmentName)
        {
            if (string.IsNullOrWhiteSpace(environmentFolder) || string.IsNullOrWhiteSpace(environmentName))
            {
                return false;
            }

            if (!Directory.Exists(environmentFolder))
            {
                return false;
            }

            var localizationDir = BuildLocalizationDirectory(environmentFolder);
            var globalIniPath = Path.Combine(localizationDir, GlobalIniFileName);
            var metadataPath = GetMetadataPath(environmentName);
            var metadata = TryReadMetadata(metadataPath);
            return TryValidateLocalizationState(metadataPath, globalIniPath, ref metadata);
        }

        private static async Task<ConditionalRequestResult> TryConditionalDownloadAsync(ReleaseAssetPayload asset, LocalizationMetadata? metadata, CancellationToken ct)
        {
            if (metadata is null || (metadata.ETag is null && metadata.LastModified is null))
            {
                return ConditionalRequestResult.ForceDownload();
            }

            var response = await SendWithRetryAsync(() => CreateConditionalRequest(asset.DownloadUrl!, metadata), ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                response.Dispose();
                return ConditionalRequestResult.NotModified();
            }

            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                response.Dispose();
                return ConditionalRequestResult.ForceDownload();
            }

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException(LocalizationMessages.HttpError(statusCode));
            }

            return ConditionalRequestResult.WithResponse(response);
        }

        private static HttpRequestMessage CreateConditionalRequest(string url, LocalizationMetadata metadata)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(metadata.ETag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", metadata.ETag);
            }

            if (metadata.LastModified is not null)
            {
                request.Headers.IfModifiedSince = metadata.LastModified;
            }

            return request;
        }

        private static Task<HttpResponseMessage> DownloadAssetAsync(string url, CancellationToken ct)
        {
            return SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        }

        private async Task<SaveAssetResult> SaveAssetAsync(HttpResponseMessage response, ReleaseAssetPayload asset, string destinationPath, LocalizationMetadata? currentMetadata, CancellationToken ct)
        {
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;

            ProgressChanged?.Invoke(LocalizationProgressUpdate.Downloading(contentLength.HasValue ? 0d : null));

            await using var sourceStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            var tempPath = CreateTempPath(destinationPath);

            await using var tempStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            // Завантажуємо global.ini одразу в папку локалізації
            long total = 0;
            var buffer = new byte[81920];
            int read;
            double? lastProgress = null;
            while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                hash.AppendData(buffer, 0, read);
                await tempStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

                if (contentLength.HasValue && contentLength.Value > 0)
                {
                    var progress = Math.Min(1d, (double)total / contentLength.Value);
                    if (lastProgress is null || progress - lastProgress.Value >= 0.05 || progress >= 1d)
                    {
                        ProgressChanged?.Invoke(LocalizationProgressUpdate.Downloading(progress));
                        lastProgress = progress;
                    }
                }
            }

            await tempStream.FlushAsync(ct).ConfigureAwait(false);
            tempStream.Flush(true);

            if (contentLength.HasValue)
            {
                ProgressChanged?.Invoke(LocalizationProgressUpdate.Downloading(1d));
            }
            else if (total > 0)
            {
                ProgressChanged?.Invoke(LocalizationProgressUpdate.Downloading(null));
            }

            var shaBytes = hash.GetCurrentHash();
            var sha = Convert.ToHexString(shaBytes);

            var destinationExists = File.Exists(destinationPath);
            var hashMatches = currentMetadata is not null && currentMetadata.Sha256 is not null && currentMetadata.Sha256.Equals(sha, StringComparison.OrdinalIgnoreCase);

            return new SaveAssetResult(hashMatches, destinationExists, BuildMetadata(asset, response, sha, total), tempPath);
        }

        private static void CommitTemp(string tempPath, string destinationPath, bool destinationExists)
        {
            try
            {
                if (destinationExists || File.Exists(destinationPath))
                {
                    File.Replace(tempPath, destinationPath, null);
                }
                else
                {
                    File.Move(tempPath, destinationPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static LocalizationMetadata BuildMetadata(ReleaseAssetPayload asset, HttpResponseMessage response, string sha, long fileSize)
        {
            var etag = response.Headers.ETag?.Tag;

            DateTimeOffset? lastModified = null;
            if (response.Content.Headers.LastModified is not null)
            {
                lastModified = response.Content.Headers.LastModified;
            }
            else if (response.Headers.TryGetValues("Last-Modified", out var rawValues))
            {
                if (DateTimeOffset.TryParse(rawValues.FirstOrDefault(), out var parsed))
                {
                    lastModified = parsed;
                }
            }

            return new LocalizationMetadata
            {
                AssetId = asset.Id,
                ETag = etag,
                Sha256 = sha,
                FileSize = fileSize,
                LastModified = lastModified
            };
        }

        private static async Task<LocalizationMetadata?> ReadMetadataAsync(string environmentName, CancellationToken ct)
        {
            var metadataPath = GetMetadataPath(environmentName);
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            try
            {
                await using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                return await JsonSerializer.DeserializeAsync<LocalizationMetadata>(stream, SerializerOptions, ct).ConfigureAwait(false);
            }
            catch
            {
                DeleteFileIfExists(metadataPath);
                return null;
            }
        }

        private static async Task WriteMetadataAsync(string metadataPath, LocalizationMetadata metadata, CancellationToken ct)
        {
            var directory = Path.GetDirectoryName(metadataPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = new FileStream(metadataPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await JsonSerializer.SerializeAsync(stream, metadata, SerializerOptions, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        private static string GetMetadataPath(string environmentName)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cacheDirectory = Path.Combine(localAppData, "SCLOCVerse", "cache");
            Directory.CreateDirectory(cacheDirectory);
            var safeName = SanitizeEnvironmentName(environmentName);
            return Path.Combine(cacheDirectory, $"{safeName}.meta.json");
        }

        private static bool TryValidateLocalizationState(string metadataPath, string globalIniPath, ref LocalizationMetadata? metadata)
        {
            if (!File.Exists(globalIniPath))
            {
                if (metadata is not null)
                {
                    DeleteFileIfExists(metadataPath);
                }
                metadata = null;
                return false;
            }

            if (metadata is null || string.IsNullOrWhiteSpace(metadata.Sha256))
            {
                if (metadata is not null)
                {
                    DeleteFileIfExists(metadataPath);
                }
                metadata = null;
                return false;
            }

            try
            {
                using var stream = new FileStream(globalIniPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var localSha = Convert.ToHexString(SHA256.HashData(stream));
                if (!localSha.Equals(metadata.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    DeleteFileIfExists(metadataPath);
                    metadata = null;
                    return false;
                }
            }
            catch
            {
                metadata = null;
                return false;
            }

            return true;
        }

        private static LocalizationMetadata? TryReadMetadata(string metadataPath)
        {
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            try
            {
                using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return JsonSerializer.Deserialize<LocalizationMetadata>(stream, SerializerOptions);
            }
            catch
            {
                DeleteFileIfExists(metadataPath);
                return null;
            }
        }

        private static string SanitizeEnvironmentName(string environmentName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(environmentName.Length);
            foreach (var ch in environmentName)
            {
                builder.Append(invalidChars.Contains(ch) ? '_' : ch);
            }

            return builder.ToString();
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

        private static void ValidateEnvironment(string folder, string name)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Шлях до папки середовища не задано.", nameof(folder));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Назва середовища не задана.", nameof(name));
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Папку середовища \"{folder}\" не знайдено.");
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpRetryHelper.UserAgent);
            return client;
        }

        private static Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
            => SendWithRetryAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, ct);

        // Streamed-варіант: великі файли (global.ini) читаємо потоком, без буферизації в пам'ять.
        private static async Task<HttpResponseMessage> SendWithRetryAsync(
            Func<HttpRequestMessage> requestFactory,
            HttpCompletionOption completionOption,
            CancellationToken ct)
        {
            // За замовчуванням ResponseHeadersRead — бо LocalizationInstaller завжди читає потоком.
            using var request = requestFactory();
            return await HttpRetryHelper.SendWithRetryAsync(HttpClient, request, completionOption, ct).ConfigureAwait(false);
        }

        private async Task<ReleasePayload?> GetReleaseAsync(string envName, CancellationToken ct)
        {
            var releases = await GetReleasesWithCacheAsync(ct).ConfigureAwait(false);
            if (releases is null || releases.Count == 0) return null;

            bool prereleaseNeeded = StarCitizenEnvironments.IsPrereleaseChannel(envName);
            return releases.FirstOrDefault(r => r.Prerelease == prereleaseNeeded
                && r.Assets?.Any(a => a.Name == GlobalIniFileName) == true);
        }

        /// <summary>
        /// Повертає список релізів з in-memory кешу (TTL 5 хв) або з GitHub.
        /// Спільний між усіма середовищами (LIVE/PTU/EPTU/HOTFIX) — 1 запит замість 4 за цикл.
        /// При GitHub-помилці + є кеш младший 24г — fallback на кеш.
        /// </summary>
        private async Task<IReadOnlyList<ReleasePayload>?> GetReleasesWithCacheAsync(CancellationToken ct)
        {
            // Швидкий шлях: кеш свіжий — без Semaphore, без мережі.
            if (_cachedReleases is { IsFresh: true } fresh)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Localization Cache][{RepositoryId}] HIT (Age={DateTimeOffset.UtcNow - fresh.FetchedAt:hh\\:mm\\:ss})");
#endif
                return fresh.Value;
            }

            var entered = await _releasesSemaphore.WaitAsync(GitHubCacheDefaults.SemaphoreTimeout, ct).ConfigureAwait(false);
            if (!entered)
            {
                if (_cachedReleases is { } stale && DateTimeOffset.UtcNow - stale.FetchedAt < GitHubCacheDefaults.MaximumStaleAge)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[Localization Cache][{RepositoryId}] SEMAPHORE_TIMEOUT → fallback (Age={DateTimeOffset.UtcNow - stale.FetchedAt:hh\\:mm\\:ss})");
#endif
                    return stale.Value;
                }
                throw new TimeoutException("Localization releases fetch timed out waiting for another fetch to complete.");
            }

            try
            {
                // Double-check: поки чекали, кеш міг обновитись іншим потоком.
                if (_cachedReleases is { IsFresh: true } stillFresh)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[Localization Cache][{RepositoryId}] HIT (Age={DateTimeOffset.UtcNow - stillFresh.FetchedAt:hh\\:mm\\:ss})");
#endif
                    return stillFresh.Value;
                }

#if DEBUG
                System.Diagnostics.Debug.WriteLine(_cachedReleases is null
                    ? $"[Localization Cache][{RepositoryId}] MISS (Empty)"
                    : $"[Localization Cache][{RepositoryId}] MISS (Expired)");
#endif

                using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl), ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

                List<ReleasePayload> releases;
                try
                {
                    releases = await JsonSerializer.DeserializeAsync<List<ReleasePayload>>(responseStream, SerializerOptions, ct).ConfigureAwait(false)
                        ?? new List<ReleasePayload>();
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(LocalizationMessages.ReleaseParseError(), ex);
                }

                // Зберігаємо з ETag/LastModified для майбутнього розширення Conditional GET.
                _cachedReleases = new CachedReleases(
                    releases,
                    DateTimeOffset.UtcNow,
                    response.Headers.ETag?.Tag,
                    response.Content.Headers.LastModified);

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Localization Cache][{RepositoryId}] REFRESH (HTTP 200)");
#endif
                return releases;
            }
            catch (HttpRequestException) when (_cachedReleases is { } stale && DateTimeOffset.UtcNow - stale.FetchedAt < GitHubCacheDefaults.MaximumStaleAge)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Localization Cache][{RepositoryId}] GITHUB_ERROR → fallback (Age={DateTimeOffset.UtcNow - stale.FetchedAt:hh\\:mm\\:ss})");
#endif
                return stale.Value;
            }
            catch (TaskCanceledException) when (_cachedReleases is { } stale && DateTimeOffset.UtcNow - stale.FetchedAt < GitHubCacheDefaults.MaximumStaleAge)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Localization Cache][{RepositoryId}] GITHUB_ERROR → fallback (Age={DateTimeOffset.UtcNow - stale.FetchedAt:hh\\:mm\\:ss})");
#endif
                return stale.Value;
            }
            catch (TimeoutException) when (_cachedReleases is { } stale && DateTimeOffset.UtcNow - stale.FetchedAt < GitHubCacheDefaults.MaximumStaleAge)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Localization Cache][{RepositoryId}] GITHUB_ERROR → fallback (Age={DateTimeOffset.UtcNow - stale.FetchedAt:hh\\:mm\\:ss})");
#endif
                return stale.Value;
            }
            finally
            {
                _releasesSemaphore.Release();
            }
        }

        private static string CreateTempPath(string destinationPath)
        {
            var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
            var fileName = $".{Guid.NewGuid():N}.tmp";
            return Path.Combine(directory, fileName);
        }

        private sealed record SaveAssetResult(bool HashMatchesExisting, bool DestinationExists, LocalizationMetadata Metadata, string TempPath);

        private sealed record ReleasePayload(
            [property: JsonPropertyName("tag_name")] string? TagName,
            [property: JsonPropertyName("prerelease")] bool Prerelease,
            [property: JsonPropertyName("assets")] IReadOnlyList<ReleaseAssetPayload>? Assets);

        private sealed record ReleaseAssetPayload(
            [property: JsonPropertyName("id")] long Id,
            [property: JsonPropertyName("name")] string Name,
            [property: JsonPropertyName("browser_download_url")] string? DownloadUrl,
            [property: JsonPropertyName("content_type")] string? ContentType,
            [property: JsonPropertyName("size")] long Size);

        private sealed record LocalizationMetadata
        {
            [property: JsonPropertyName("assetId")] public long AssetId { get; init; }
            [property: JsonPropertyName("etag")] public string? ETag { get; init; }
            [property: JsonPropertyName("sha256")] public string? Sha256 { get; init; }
            [property: JsonPropertyName("fileSize")] public long? FileSize { get; init; }
            [property: JsonPropertyName("lastModified")] public DateTimeOffset? LastModified { get; init; }
            // Встановлений тег релізу локалізації (ReleasePayload.TagName).
            // null означає "версія невідома" (старий metadata до цієї зміни або ще не встановлено).
            // Записується лише після успішного InstallAsync через 'with { InstalledTag = release.TagName }'.
            // CheckAsync порівнює InstalledTag з remote TagName для визначення наявності оновлення.
            [property: JsonPropertyName("installedTag")] public string? InstalledTag { get; init; }
        }

        private readonly struct ConditionalRequestResult
        {
            private ConditionalRequestResult(ConditionalRequestStatus status, HttpResponseMessage? response)
            {
                Status = status;
                Response = response;
            }

            public ConditionalRequestStatus Status { get; }
            public HttpResponseMessage? Response { get; }

            public static ConditionalRequestResult NotModified() => new(ConditionalRequestStatus.NotModified, null);
            public static ConditionalRequestResult ForceDownload() => new(ConditionalRequestStatus.ForceDownload, null);
            public static ConditionalRequestResult WithResponse(HttpResponseMessage response) => new(ConditionalRequestStatus.ShouldDownload, response);
        }

        private enum ConditionalRequestStatus
        {
            NotModified,
            ForceDownload,
            ShouldDownload
        }

        public readonly struct LocalizationProgressUpdate
        {
            private LocalizationProgressUpdate(LocalizationProgressStage stage, double? value)
            {
                Stage = stage;
                Value = value;
            }

            public LocalizationProgressStage Stage { get; }
            public double? Value { get; }

            public static LocalizationProgressUpdate Checking() => new(LocalizationProgressStage.Checking, null);
            public static LocalizationProgressUpdate Downloading(double? value) => new(LocalizationProgressStage.Downloading, value);
            public static LocalizationProgressUpdate Completed(bool updated) => new(updated ? LocalizationProgressStage.Completed : LocalizationProgressStage.Skipped, null);
        }

        public enum LocalizationProgressStage
        {
            Checking,
            Downloading,
            Completed,
            Skipped
        }

        public readonly struct LocalizationNotification
        {
            private LocalizationNotification(string message, bool success)
            {
                Message = message;
                Success = success;
            }

            public string Message { get; }
            public bool Success { get; }

            public static LocalizationNotification Completed(string message, bool success) => new(message, success);
        }
    }
}
