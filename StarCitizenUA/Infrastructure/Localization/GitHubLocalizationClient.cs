using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using StarCitizenUA.Domain.Localization;

namespace StarCitizenUA.Infrastructure.Localization;

/// <summary>
/// Клієнт GitHub для отримання релізів та asset-ів локалізації.
/// </summary>
internal sealed class GitHubLocalizationClient
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/Vova-Bob/SC_localization_UA/releases";
    private const string GlobalIniFileName = "global.ini";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<ReleaseAssetDescriptor?> GetLatestAssetAsync(string environmentName, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var releases = await JsonSerializer.DeserializeAsync<List<ReleasePayload>>(stream, cancellationToken: ct).ConfigureAwait(false);
        if (releases is null || releases.Count == 0)
        {
            return null;
        }

        var prerelease = environmentName.Contains("PTU", StringComparison.OrdinalIgnoreCase);
        foreach (var release in releases)
        {
            if (release.Prerelease != prerelease)
            {
                continue;
            }

            var asset = release.Assets?.FirstOrDefault(a => a.Name.Equals(GlobalIniFileName, StringComparison.OrdinalIgnoreCase));
            if (asset is null || string.IsNullOrWhiteSpace(asset.DownloadUrl))
            {
                continue;
            }

            return new ReleaseAssetDescriptor(asset.Id, asset.Name, asset.DownloadUrl!, asset.ContentType, asset.Size, release.TagName, release.Prerelease);
        }

        return null;
    }

    public async Task<ConditionalRequestResult> SendConditionalRequestAsync(string downloadUrl, LocalizationMetadata? metadata, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        if (metadata is not null)
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

        var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            response.Dispose();
            return ConditionalRequestResult.NotModified();
        }

        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            response.Dispose();
            return ConditionalRequestResult.PreconditionFailed();
        }

        if (response.StatusCode == (HttpStatusCode)429)
        {
            var retryAfter = ParseRetryAfter(response);
            response.Dispose();
            return ConditionalRequestResult.RateLimited(retryAfter);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            response.Dispose();
            return ConditionalRequestResult.Forbidden(string.IsNullOrWhiteSpace(error) ? null : error);
        }

        response.EnsureSuccessStatusCode();
        return ConditionalRequestResult.Success(response);
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { Delta: { } delta })
        {
            return delta;
        }

        if (response.Headers.RetryAfter is { Date: { } date })
        {
            var delta = date - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : TimeSpan.FromSeconds(5);
        }

        return TimeSpan.FromSeconds(5);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(100)
        };

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SCLocalizationUA/2.0");
        client.DefaultRequestHeaders.AcceptEncoding.Clear();
        return client;
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
        [property: JsonPropertyName("size")] long? Size);
}
