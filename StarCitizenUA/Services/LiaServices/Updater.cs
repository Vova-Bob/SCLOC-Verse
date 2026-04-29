using Newtonsoft.Json;
using StarCitizenUA.Interfaces;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace StarCitizenUA.Services.LiaServices
{
    public class Updater : IUpdater
    {
        private static readonly HttpClient Client = CreateHttpClient();

        public async Task<LiaInstallStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            var installedVersion = await GetInstalledVersionAsync(cancellationToken).ConfigureAwait(false);
            var release = await TryGetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            var latestVersion = TryParseVersion(release?.TagName, out var remoteVersion) ? remoteVersion : null;

            if (installedVersion == null)
            {
                return new LiaInstallStatus(
                    false,
                    false,
                    null,
                    latestVersion,
                    "Голосовий асистент Л.І.А не встановлено.",
                    Brushes.Red);
            }

            if (latestVersion == null)
            {
                return new LiaInstallStatus(
                    true,
                    false,
                    installedVersion,
                    null,
                    $"Встановлена версія Л.І.А: {installedVersion}. Не вдалося перевірити оновлення.",
                    Brushes.Orange);
            }

            if (latestVersion.CompareTo(installedVersion) > 0)
            {
                return new LiaInstallStatus(
                    true,
                    true,
                    installedVersion,
                    latestVersion,
                    $"Доступне оновлення версії {latestVersion}. Встановлено: {installedVersion}",
                    Brushes.Red);
            }

            return new LiaInstallStatus(
                true,
                false,
                installedVersion,
                latestVersion,
                $"Встановлена актуальна версія Л.І.А: {installedVersion}",
                Brushes.LimeGreen);
        }

        public async Task InstallLatestAsync(
            Action<string>? onProgress = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            var installerAsset = SelectInstallerAsset(release.Assets)
                ?? throw new InvalidOperationException("У релізі не знайдено інсталятор Л.І.А.");
            var certificateAsset = SelectCertificateAsset(release.Assets);

            Directory.CreateDirectory(AppSettings.UpdatesDirectory);

            onProgress?.Invoke($"Знайдено реліз: {release.TagName ?? release.Name ?? "невідомо"}");
            var installerPath = await EnsureAssetDownloadedAsync(installerAsset, progress, cancellationToken).ConfigureAwait(false);
            string? certificatePath = null;

            if (certificateAsset != null)
            {
                onProgress?.Invoke("Завантаження сертифіката...");
                certificatePath = await EnsureAssetDownloadedAsync(certificateAsset, null, cancellationToken).ConfigureAwait(false);
            }

            onProgress?.Invoke("Запуск інсталяції Л.І.А...");
            await RunInstallerScriptAsync(installerPath, certificatePath, cancellationToken).ConfigureAwait(false);
            onProgress?.Invoke("Інсталяцію завершено.");
        }

        public async Task UninstallAsync(Action<string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            onProgress?.Invoke("Видалення Л.І.А...");

            var script = $$"""
                $ErrorActionPreference = 'Stop'
                $packages = @(Get-AppxPackage -Name '{{AppSettings.PackageName}}' | Sort-Object Version -Descending)
                if ($packages.Count -eq 0) {
                    Write-Output 'NOT_INSTALLED'
                    exit 0
                }

                foreach ($package in $packages) {
                    Remove-AppxPackage -Package $package.PackageFullName
                }

                Write-Output 'UNINSTALLED'
                """;

            var result = await RunPowerShellAsync(script, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(result.Error.Trim());

            onProgress?.Invoke("Л.І.А видалено.");
        }

        private static async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            var release = await TryGetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            return release ?? throw new InvalidOperationException("Не вдалося отримати останній реліз з GitHub.");
        }

        private static async Task<GitHubRelease?> TryGetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var response = await Client.GetAsync(AppSettings.GitHubReleasesUrl, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return JsonConvert.DeserializeObject<GitHubRelease>(json);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Version?> GetInstalledVersionAsync(CancellationToken cancellationToken)
        {
            var script = $$"""
                $package = Get-AppxPackage -Name '{{AppSettings.PackageName}}' | Sort-Object Version -Descending | Select-Object -First 1
                if ($package) {
                    Write-Output $package.Version
                }
                """;

            var result = await RunPowerShellAsync(script, cancellationToken).ConfigureAwait(false);
            var versionText = result.Output.Trim();

            return result.ExitCode == 0 && TryParseVersion(versionText, out var version)
                ? version
                : null;
        }

        private static async Task<string> EnsureAssetDownloadedAsync(
            GitHubReleaseAsset asset,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var destinationPath = Path.Combine(AppSettings.UpdatesDirectory, SanitizeFileName(asset.Name));
            if (IsCachedAssetValid(destinationPath, asset))
                return destinationPath;

            using var response = await Client.GetAsync(
                asset.BrowserDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long downloadedBytes = 0;

            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                downloadedBytes += read;

                if (totalBytes is > 0)
                    progress?.Report((double)downloadedBytes / totalBytes.Value);
            }

            return destinationPath;
        }

        private static async Task RunInstallerScriptAsync(string installerPath, string? certificatePath, CancellationToken cancellationToken)
        {
            var result = await RunPowerShellAsync(BuildInstallerScript(installerPath, certificatePath), cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim());
        }

        private static string BuildInstallerScript(string installerPath, string? certificatePath)
        {
            var escapedInstallerPath = EscapePowerShellString(installerPath);
            var escapedCertificatePath = EscapePowerShellString(certificatePath ?? string.Empty);

            return $$"""
                $ErrorActionPreference = 'Stop'
                $installerPath = '{{escapedInstallerPath}}'
                $certificatePath = '{{escapedCertificatePath}}'

                if ($certificatePath -and (Test-Path -LiteralPath $certificatePath)) {
                    Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
                }

                $extension = [System.IO.Path]::GetExtension($installerPath).ToLowerInvariant()
                if ($extension -eq '.appinstaller') {
                    Add-AppxPackage -AppInstallerFile $installerPath
                } elseif ($extension -eq '.msi') {
                    Start-Process msiexec.exe -ArgumentList "/i `"$installerPath`"" -Wait
                } elseif ($extension -eq '.exe') {
                    Start-Process $installerPath -Wait
                } else {
                    Add-AppxPackage -Path $installerPath -ForceUpdateFromAnyVersion
                }
                """;
        }

        private static async Task<PowerShellResult> RunPowerShellAsync(string script, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(AppSettings.UpdatesDirectory);
            var scriptPath = Path.Combine(AppSettings.UpdatesDirectory, $"lia-{Guid.NewGuid():N}.ps1");
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                return new PowerShellResult(
                    process.ExitCode,
                    await outputTask.ConfigureAwait(false),
                    await errorTask.ConfigureAwait(false));
            }
            finally
            {
                try
                {
                    File.Delete(scriptPath);
                }
                catch
                {
                    // Best effort cleanup only.
                }
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("StarCitizenUA-LIA-Installer/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        private static GitHubReleaseAsset? SelectInstallerAsset(IReadOnlyCollection<GitHubReleaseAsset> assets)
        {
            var preferredExtensions = new[] { ".appinstaller", ".msixbundle", ".msix", ".exe", ".msi" };

            foreach (var extension in preferredExtensions)
            {
                var asset = assets.FirstOrDefault(item => item.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
                if (asset != null)
                    return asset;
            }

            return null;
        }

        private static GitHubReleaseAsset? SelectCertificateAsset(IReadOnlyCollection<GitHubReleaseAsset> assets)
        {
            return assets.FirstOrDefault(item => item.Name.EndsWith(".cer", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryParseVersion(string? value, out Version version)
        {
            version = new Version(0, 0, 0, 0);

            if (string.IsNullOrWhiteSpace(value))
                return false;

            var match = Regex.Match(value, @"\d+(?:\.\d+){0,3}");
            if (!match.Success)
                return false;

            var parts = match.Value
                .Split('.')
                .Select(part => int.TryParse(part, out var number) ? number : 0)
                .ToList();

            while (parts.Count < 4)
                parts.Add(0);

            version = new Version(parts[0], parts[1], parts[2], parts[3]);
            return true;
        }

        private static bool IsCachedAssetValid(string path, GitHubReleaseAsset asset)
        {
            if (!File.Exists(path))
                return false;

            return asset.Size <= 0 || new FileInfo(path).Length == asset.Size;
        }

        private static string SanitizeFileName(string fileName)
        {
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(invalidChar, '_');

            return fileName;
        }

        private static string EscapePowerShellString(string value)
        {
            return value.Replace("'", "''");
        }

        private sealed record PowerShellResult(int ExitCode, string Output, string Error);

        private sealed class GitHubRelease
        {
            [JsonProperty("tag_name")]
            public string? TagName { get; set; }

            [JsonProperty("name")]
            public string? Name { get; set; }

            [JsonProperty("assets")]
            public List<GitHubReleaseAsset> Assets { get; set; } = new();
        }

        private sealed class GitHubReleaseAsset
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;

            [JsonProperty("size")]
            public long Size { get; set; }
        }
    }
}
