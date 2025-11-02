using StarCitizenUA.Interfaces;
using StarCitizenUA.Models;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarCitizenUA.Services
{
    public sealed class LocalizationInstaller : ILocalizationInstaller
    {
        private const string UserCfgFileName = "user.cfg";
        private const string GlobalIniFileName = "global.ini";
        private const string ReleasesApiUrl = "https://api.github.com/repos/Vova-Bob/SC_localization_UA/releases";
        private static readonly string[] LocalizationPathSegments = { "Data", "Localization", "korean_(south_korea)" };
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly Encoding UserCfgEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

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

            // Завантажуємо global.ini одразу в папку локалізації
            var globalIniPath = Path.Combine(localizationDir, GlobalIniFileName);
            await DownloadAssetAsync(asset.DownloadUrl!, globalIniPath, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Створюємо user.cfg в корені середовища, якщо його немає
            string? userCfgPathCreated = null;
            var userCfgPath = Path.Combine(environmentFolder, UserCfgFileName);
            if (!File.Exists(userCfgPath))
            {
                await File.WriteAllTextAsync(userCfgPath, BuildUserCfgContent(), UserCfgEncoding, cancellationToken);
                userCfgPathCreated = userCfgPath;
            }

            var message = userCfgPathCreated is null
                ? $"Локалізацію для {environmentName} оновлено з релізу {release.TagName ?? "невідомого"}."
                : $"Локалізацію для {environmentName} встановлено з релізу {release.TagName ?? "невідомого"}. Файл user.cfg створено.";

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

        private static async Task DownloadAssetAsync(string url, string destinationPath, CancellationToken ct)
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var destStream = File.Create(destinationPath);
            await using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
            await sourceStream.CopyToAsync(destStream, ct);
        }

        private static async Task<ReleasePayload?> GetReleaseAsync(string envName, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct);
            var releases = await JsonSerializer.DeserializeAsync<List<ReleasePayload>>(stream, cancellationToken: ct);

            if (releases == null || releases.Count == 0) return null;

            bool prereleaseNeeded = envName.Contains("PTU", StringComparison.OrdinalIgnoreCase);
            return releases.FirstOrDefault(r => r.Prerelease == prereleaseNeeded && r.Assets?.Any(a => a.Name == GlobalIniFileName) == true);
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
            [property: JsonPropertyName("name")] string Name,
            [property: JsonPropertyName("browser_download_url")] string? DownloadUrl);
    }
}