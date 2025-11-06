global using StarCitizenUA.Services.LocalizationServices;

using StarCitizenUA.Interfaces;
using StarCitizenUA.Models;
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

namespace StarCitizenUA.Services.LocalizationServices
{
    public sealed class LocalizationInstaller : ILocalizationInstaller
    {
        private const string UserCfgFileName = "user.cfg";
        private const string GlobalIniFileName = "global.ini";
        private const string ReleasesApiUrl = "https://api.github.com/repos/Vova-Bob/SC_localization_UA/releases";
        private const int MaxDownloadRetries = 4;
        private const long MaxIniSizeBytes = 8 * 1024 * 1024;
        private static readonly string[] LocalizationPathSegments = { "Data", "Localization", "korean_(south_korea)" };
        private static readonly Encoding UserCfgEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };

        public event Action<LocalizationProgressUpdate>? ProgressChanged;
        public event Action<LocalizationNotification>? NotificationRaised;

        public async Task<LocalizationInstallResult> InstallAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default)
        {
            ValidateEnvironment(environmentFolder, environmentName);
            cancellationToken.ThrowIfCancellationRequested();

            var localizationDir = BuildLocalizationDirectory(environmentFolder);
            Directory.CreateDirectory(localizationDir);

            var metadata = await ReadMetadataAsync(environmentName, cancellationToken).ConfigureAwait(false);

            var release = (await GetReleaseAsync(environmentName, cancellationToken).ConfigureAwait(false))
                ?? throw new InvalidOperationException(LocalizationMessages.ReleaseNotFound(environmentName));

            var asset = release.Assets?.FirstOrDefault(a => a.Name.Equals(GlobalIniFileName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(LocalizationMessages.AssetMissing(release.TagName, GlobalIniFileName));

            var globalIniPath = Path.Combine(localizationDir, GlobalIniFileName);
            var metadataPath = GetMetadataPath(environmentName);

            ProgressChanged?.Invoke(LocalizationProgressUpdate.Checking());

            var conditionalResult = await TryConditionalDownloadAsync(asset, metadata, cancellationToken).ConfigureAwait(false);

            bool localizationUpdated = false;
            LocalizationMetadata? updatedMetadata = null;

            if (conditionalResult.Status == ConditionalRequestStatus.NotModified && File.Exists(globalIniPath))
            {
                await EnsureMetadataConsistencyAsync(metadataPath, metadata, asset, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                HttpResponseMessage? response = conditionalResult.Response;
                if (response is null)
                {
                    response = await DownloadAssetAsync(asset.DownloadUrl!, cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    var downloadResult = await SaveAssetAsync(response, asset, globalIniPath, metadata, cancellationToken).ConfigureAwait(false);
                    localizationUpdated = downloadResult.Updated;
                    updatedMetadata = downloadResult.Metadata;

                    if (!localizationUpdated && !File.Exists(globalIniPath))
                    {
                        ReplaceWithTemp(downloadResult.TempPath, globalIniPath, false);
                        localizationUpdated = true;
                    }
                    else
                    {
                        File.Delete(downloadResult.TempPath);
                    }
                }
                finally
                {
                    response.Dispose();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Створюємо user.cfg в корені середовища, якщо його немає
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

            return new LocalizationInstallResult(localizationUpdated, environmentName, globalIniPath, userCfgPathCreated, message);
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
                ? LocalizationMessages.DeleteAll(environmentName)
                : userCfgDeleted
                    ? LocalizationMessages.DeleteUserCfg(environmentName)
                    : globalIniDeleted
                        ? LocalizationMessages.DeleteGlobalIni(environmentName)
                        : LocalizationMessages.DeleteMissing(environmentName);

            return Task.FromResult(new LocalizationDeleteResult(userCfgDeleted || globalIniDeleted, userCfgDeleted, globalIniDeleted, message));
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

        private static async Task EnsureMetadataConsistencyAsync(string metadataPath, LocalizationMetadata? metadata, ReleaseAssetPayload asset, CancellationToken ct)
        {
            if (metadata is null)
            {
                return;
            }

            if (metadata.AssetId == asset.Id && metadata.FileSize == asset.Size)
            {
                return;
            }

            var refreshed = new LocalizationMetadata
            {
                AssetId = asset.Id,
                ETag = metadata.ETag,
                Sha256 = metadata.Sha256,
                FileSize = asset.Size,
                LastModified = metadata.LastModified
            };
            await WriteMetadataAsync(metadataPath, refreshed, ct).ConfigureAwait(false);
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

        private async Task<(bool Updated, LocalizationMetadata Metadata, string TempPath)> SaveAssetAsync(HttpResponseMessage response, ReleaseAssetPayload asset, string destinationPath, LocalizationMetadata? currentMetadata, CancellationToken ct)
        {
            response.EnsureSuccessStatusCode();

            ValidateContentType(asset.ContentType, response.Content.Headers.ContentType);

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxIniSizeBytes)
            {
                throw new InvalidOperationException(LocalizationMessages.FileTooLarge(contentLength.Value, MaxIniSizeBytes));
            }

            ProgressChanged?.Invoke(LocalizationProgressUpdate.Downloading(contentLength.HasValue ? 0d : null));

            await using var sourceStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            var tempPath = CreateTempPath();

            await using var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            // Завантажуємо global.ini одразу в папку локалізації
            long total = 0;
            var buffer = new byte[81920];
            int read;
            double? lastProgress = null;
            while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxIniSizeBytes)
                {
                    throw new InvalidOperationException(LocalizationMessages.FileTooLarge(total, MaxIniSizeBytes));
                }

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

            if (currentMetadata is not null && currentMetadata.Sha256 is not null && currentMetadata.Sha256.Equals(sha, StringComparison.OrdinalIgnoreCase) && File.Exists(destinationPath))
            {
                return (false, BuildMetadata(asset, response, sha, total), tempPath);
            }

            ReplaceWithTemp(tempPath, destinationPath, File.Exists(destinationPath));

            return (true, BuildMetadata(asset, response, sha, total), tempPath);
        }

        private static void ReplaceWithTemp(string tempPath, string destinationPath, bool destinationExists)
        {
            try
            {
                if (destinationExists)
                {
                    File.Replace(tempPath, destinationPath, null);
                }
                else
                {
                    File.Move(tempPath, destinationPath, overwrite: true);
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
            if (string.IsNullOrEmpty(etag) && response.Content.Headers.TryGetValues("ETag", out var contentEtags))
            {
                etag = contentEtags.FirstOrDefault();
            }

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

        private static void ValidateContentType(string? expectedContentType, MediaTypeHeaderValue? actual)
        {
            var mediaType = actual?.MediaType ?? expectedContentType;
            if (mediaType is null)
            {
                return;
            }

            if (mediaType.Contains("text", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw new InvalidOperationException(LocalizationMessages.InvalidContentType(mediaType));
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
            var cacheDirectory = Path.Combine(localAppData, "StarCitizenUA", "cache");
            Directory.CreateDirectory(cacheDirectory);
            var safeName = SanitizeEnvironmentName(environmentName);
            return Path.Combine(cacheDirectory, $"{safeName}.meta.json");
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SCLocalizationUA/1.0");
            return client;
        }

        private static async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
        {
            var delay = TimeSpan.FromSeconds(1);
            for (var attempt = 0; attempt < MaxDownloadRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var request = requestFactory();
                try
                {
                    var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        if (attempt == MaxDownloadRetries - 1)
                        {
                            throw new HttpRequestException(LocalizationMessages.HttpError(response.StatusCode));
                        }

                        response.Dispose();
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
                        continue;
                    }

                    return response;
                }
                finally
                {
                    request.Dispose();
                }
            }

            throw new HttpRequestException(LocalizationMessages.HttpError(HttpStatusCode.ServiceUnavailable));
        }

        private static async Task<ReleasePayload?> GetReleaseAsync(string envName, CancellationToken ct)
        {
            using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl), ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            try
            {
                var releases = await JsonSerializer.DeserializeAsync<List<ReleasePayload>>(responseStream, SerializerOptions, ct).ConfigureAwait(false);
                if (releases == null || releases.Count == 0) return null;

                bool prereleaseNeeded = envName.Contains("PTU", StringComparison.OrdinalIgnoreCase);
                return releases.FirstOrDefault(r => r.Prerelease == prereleaseNeeded && r.Assets?.Any(a => a.Name == GlobalIniFileName) == true);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(LocalizationMessages.ReleaseParseError(), ex);
            }
        }

        private static string CreateTempPath()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "StarCitizenUA");
            Directory.CreateDirectory(tempDirectory);
            return Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        }

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
