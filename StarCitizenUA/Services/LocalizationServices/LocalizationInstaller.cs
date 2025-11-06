using System;
using System.Buffers;
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
using System.Threading.Tasks;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Models;

namespace StarCitizenUA.Services;

public sealed class LocalizationInstaller : ILocalizationInstaller
{
    private const string UserCfgFileName = "user.cfg";
    private const string GlobalIniFileName = "global.ini";
    private const string ReleasesApiUrl = "https://api.github.com/repos/Vova-Bob/SC_localization_UA/releases";
    private const string CacheRootFolderName = "StarCitizenUA";
    private const string CacheSubFolderName = "cache";
    private static readonly string[] LocalizationPathSegments = { "Data", "Localization", "korean_(south_korea)" };
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly Encoding UserCfgEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions MetadataSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<LocalizationInstallResult> InstallAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default)
    {
        ValidateEnvironment(environmentFolder, environmentName);
        cancellationToken.ThrowIfCancellationRequested();

        var localizationDir = BuildLocalizationDirectory(environmentFolder);
        Directory.CreateDirectory(localizationDir);

        var release = await GetReleaseAsync(environmentName, cancellationToken)
            ?? throw new InvalidOperationException($"Не вдалося знайти реліз з файлом локалізації для {environmentName}.");

        var asset = release.Assets?.FirstOrDefault(a => a.Name.Equals(GlobalIniFileName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Реліз {release.TagName ?? "без назви"} не містить файлу {GlobalIniFileName}.");
        if (string.IsNullOrWhiteSpace(asset.DownloadUrl))
        {
            throw new InvalidOperationException($"Asset {GlobalIniFileName} має некоректне посилання для завантаження.");
        }

        var globalIniPath = Path.Combine(localizationDir, GlobalIniFileName);
        var metadataPath = GetMetadataPath(environmentName);
        var cachedMetadata = await ReadMetadataAsync(metadataPath, cancellationToken);
        var localFileExists = File.Exists(globalIniPath);

        DownloadResult? downloadResult = null;
        LocalizationMetadata? finalMetadata = cachedMetadata;

        if (!localFileExists)
        {
            using var response = await SendConditionalRequestAsync(asset.DownloadUrl, metadata: null, cancellationToken);
            downloadResult = await DownloadAndPersistAsync(response, globalIniPath, asset, cachedMetadata, cancellationToken);
            finalMetadata = downloadResult.Metadata;
        }
        else if (cachedMetadata?.AssetId == asset.Id)
        {
            using var response = await SendConditionalRequestAsync(asset.DownloadUrl, cachedMetadata, cancellationToken);
            if (response is not null)
            {
                downloadResult = await DownloadAndPersistAsync(response, globalIniPath, asset, cachedMetadata, cancellationToken);
                finalMetadata = downloadResult.Metadata;
            }
        }
        else
        {
            using var response = await SendConditionalRequestAsync(asset.DownloadUrl, metadata: null, cancellationToken);
            downloadResult = await DownloadAndPersistAsync(response, globalIniPath, asset, cachedMetadata, cancellationToken);
            finalMetadata = downloadResult.Metadata;
        }

        if (finalMetadata is null)
        {
            throw new InvalidOperationException("Не вдалося отримати метадані локалізації.");
        }

        if (downloadResult is not null)
        {
            await WriteMetadataAsync(metadataPath, finalMetadata, cancellationToken);
        }
        else if (!File.Exists(metadataPath))
        {
            await WriteMetadataAsync(metadataPath, finalMetadata, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        string? userCfgPathCreated = null;
        var userCfgPath = Path.Combine(environmentFolder, UserCfgFileName);
        if (!File.Exists(userCfgPath))
        {
            await File.WriteAllTextAsync(userCfgPath, BuildUserCfgContent(), UserCfgEncoding, cancellationToken);
            userCfgPathCreated = userCfgPath;
        }

        var localizationUpdated = downloadResult?.FileReplaced == true || !localFileExists;
        var message = localizationUpdated
            ? $"Локалізацію для {environmentName} оновлено з релізу {release.TagName ?? "невідомого"}."
            : $"Локалізація для {environmentName} вже була актуальною.";

        if (userCfgPathCreated is not null)
        {
            message += " Файл user.cfg створено.";
        }

        return new LocalizationInstallResult(true, environmentName, globalIniPath, userCfgPathCreated, message);
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

    private static async Task<ReleasePayload?> GetReleaseAsync(string envName, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var releases = await JsonSerializer.DeserializeAsync<List<ReleasePayload>>(stream, cancellationToken: ct);

        if (releases == null || releases.Count == 0) return null;

        bool prereleaseNeeded = envName.Contains("PTU", StringComparison.OrdinalIgnoreCase);
        return releases.FirstOrDefault(r => r.Prerelease == prereleaseNeeded && r.Assets?.Any(a => a.Name == GlobalIniFileName) == true);
    }

    private static async Task<LocalizationMetadata?> ReadMetadataAsync(string metadataPath, CancellationToken ct)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(metadataPath, new FileStreamOptions
            {
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Mode = FileMode.Open,
                Options = FileOptions.Asynchronous
            });

            return await JsonSerializer.DeserializeAsync<LocalizationMetadata>(stream, MetadataSerializerOptions, ct);
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

        await using var stream = new FileStream(metadataPath, new FileStreamOptions
        {
            Access = FileAccess.Write,
            Share = FileShare.None,
            Mode = FileMode.Create,
            Options = FileOptions.Asynchronous
        });

        await JsonSerializer.SerializeAsync(stream, metadata, MetadataSerializerOptions, ct);
    }

    private static async Task<HttpResponseMessage?> SendConditionalRequestAsync(string url, LocalizationMetadata? metadata, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (metadata != null)
        {
            if (!string.IsNullOrWhiteSpace(metadata.ETag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", metadata.ETag);
            }

            if (metadata.LastModified.HasValue)
            {
                request.Headers.IfModifiedSince = metadata.LastModified;
            }
        }

        var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            response.Dispose();
            return null;
        }

        response.EnsureSuccessStatusCode();
        return response;
    }

    private static async Task<DownloadResult> DownloadAndPersistAsync(HttpResponseMessage response, string destinationPath, ReleaseAssetPayload asset, LocalizationMetadata? existingMetadata, CancellationToken ct)
    {
        var tempFilePath = destinationPath + ".tmp";
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long totalBytes = 0;

        try
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
            await using var tempStream = new FileStream(tempFilePath, new FileStreamOptions
            {
                Access = FileAccess.Write,
                Share = FileShare.None,
                Mode = FileMode.Create,
                Options = FileOptions.Asynchronous
            });

            while (true)
            {
                var read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0)
                {
                    break;
                }

                hasher.AppendData(buffer, 0, read);
                await tempStream.WriteAsync(buffer.AsMemory(0, read), ct);
                totalBytes += read;
            }

            await tempStream.FlushAsync(ct);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        var headers = response.Content.Headers;
        var etag = response.Headers.ETag?.Tag;
        var lastModified = headers.LastModified;
        if (!lastModified.HasValue && response.Headers.TryGetValues("Last-Modified", out var lastModifiedValues))
        {
            var lastModifiedValue = lastModifiedValues.FirstOrDefault();
            if (lastModifiedValue != null && DateTimeOffset.TryParse(lastModifiedValue, out var parsedLastModified))
            {
                lastModified = parsedLastModified;
            }
        }
        var size = headers.ContentLength ?? totalBytes;

        bool fileReplaced = true;
        if (existingMetadata != null
            && string.Equals(existingMetadata.Sha256, hash, StringComparison.OrdinalIgnoreCase)
            && File.Exists(destinationPath))
        {
            fileReplaced = false;
            File.Delete(tempFilePath);
        }
        else
        {
            ReplaceFileAtomically(tempFilePath, destinationPath);
        }

        var metadata = new LocalizationMetadata
        {
            AssetId = asset.Id,
            Sha256 = hash,
            ETag = etag,
            LastModified = lastModified,
            FileSize = size
        };

        return new DownloadResult(metadata, fileReplaced);
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

    private static string GetMetadataPath(string environmentName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeEnvNameChars = environmentName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        var safeEnvName = new string(safeEnvNameChars).Trim();

        if (string.IsNullOrWhiteSpace(safeEnvName))
        {
            safeEnvName = "UNKNOWN";
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cacheDir = Path.Combine(localAppData, CacheRootFolderName, CacheSubFolderName);
        return Path.Combine(cacheDir, $"{safeEnvName}.meta.json");
    }

    private static void ValidateEnvironment(string folder, string name)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Шлях до папки середовища не задано.", nameof(folder));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Назва середовища не задана.", nameof(name));
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Папку середовища \"{folder}\" не знайдено.");
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

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SCLocalizationUA/1.0");
        return client;
    }

    private sealed record ReleasePayload(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] IReadOnlyList<ReleaseAssetPayload>? Assets);

    private sealed record ReleaseAssetPayload(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string? DownloadUrl);

    private sealed class LocalizationMetadata
    {
        [JsonPropertyName("assetId")]
        public long AssetId { get; init; }

        [JsonPropertyName("etag")]
        public string? ETag { get; init; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; init; }

        [JsonPropertyName("fileSize")]
        public long FileSize { get; init; }

        [JsonPropertyName("lastModified")]
        public DateTimeOffset? LastModified { get; init; }
    }

    private sealed record DownloadResult(LocalizationMetadata Metadata, bool FileReplaced);
}
