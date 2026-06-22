using Newtonsoft.Json;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.LiaModels;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace SCLOCVerse.Services.LiaServices
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
                    "Р“РѕР»РѕСЃРѕРІРёР№ Р°СЃРёСЃС‚РµРЅС‚ Р›.Р†.Рђ РЅРµ РІСЃС‚Р°РЅРѕРІР»РµРЅРѕ.",
                    LiaStatusColor.Red);
            }

            if (latestVersion == null)
            {
                return new LiaInstallStatus(
                    true,
                    false,
                    installedVersion,
                    null,
                    $"Р’СЃС‚Р°РЅРѕРІР»РµРЅР° РІРµСЂСЃС–СЏ Р›.Р†.Рђ: {installedVersion}. РќРµ РІРґР°Р»РѕСЃСЏ РїРµСЂРµРІС–СЂРёС‚Рё РѕРЅРѕРІР»РµРЅРЅСЏ.",
                    LiaStatusColor.Orange);
            }

            if (latestVersion.CompareTo(installedVersion) > 0)
            {
                return new LiaInstallStatus(
                    true,
                    true,
                    installedVersion,
                    latestVersion,
                    $"Р”РѕСЃС‚СѓРїРЅРµ РѕРЅРѕРІР»РµРЅРЅСЏ РІРµСЂСЃС–С— {latestVersion}. Р’СЃС‚Р°РЅРѕРІР»РµРЅРѕ: {installedVersion}",
                    LiaStatusColor.Red);
            }

            return new LiaInstallStatus(
                true,
                false,
                installedVersion,
                latestVersion,
                $"Р’СЃС‚Р°РЅРѕРІР»РµРЅР° Р°РєС‚СѓР°Р»СЊРЅР° РІРµСЂСЃС–СЏ Р›.Р†.Рђ: {installedVersion}",
                LiaStatusColor.Green);
        }

        public async Task InstallLatestAsync(
            Action<string>? onProgress = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            var installerAsset = SelectInstallerAsset(release.Assets)
                ?? throw new InvalidOperationException("РЈ СЂРµР»С–Р·С– РЅРµ Р·РЅР°Р№РґРµРЅРѕ С–РЅСЃС‚Р°Р»СЏС‚РѕСЂ Р›.Р†.Рђ.");
            var certificateAsset = SelectCertificateAsset(release.Assets);

            Directory.CreateDirectory(AppSettings.UpdatesDirectory);

            onProgress?.Invoke($"Р—РЅР°Р№РґРµРЅРѕ СЂРµР»С–Р·: {release.TagName ?? release.Name ?? "РЅРµРІС–РґРѕРјРѕ"}");
            var installerPath = await EnsureAssetDownloadedAsync(installerAsset, progress, cancellationToken).ConfigureAwait(false);
            string? certificatePath = null;

            if (certificateAsset != null)
            {
                onProgress?.Invoke("Р—Р°РІР°РЅС‚Р°Р¶РµРЅРЅСЏ СЃРµСЂС‚РёС„С–РєР°С‚Р°...");
                certificatePath = await EnsureAssetDownloadedAsync(certificateAsset, null, cancellationToken).ConfigureAwait(false);
            }

            onProgress?.Invoke("Р—Р°РїСѓСЃРє С–РЅСЃС‚Р°Р»СЏС†С–С— Р›.Р†.Рђ...");
            await RunInstallerScriptAsync(installerPath, certificatePath, cancellationToken).ConfigureAwait(false);
            onProgress?.Invoke("Р†РЅСЃС‚Р°Р»СЏС†С–СЋ Р·Р°РІРµСЂС€РµРЅРѕ.");
        }

        public async Task UninstallAsync(Action<string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            onProgress?.Invoke("Р’РёРґР°Р»РµРЅРЅСЏ Р›.Р†.Рђ...");

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

            onProgress?.Invoke("Р›.Р†.Рђ РІРёРґР°Р»РµРЅРѕ.");
        }

        private static async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            var release = await TryGetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            return release ?? throw new InvalidOperationException("РќРµ РІРґР°Р»РѕСЃСЏ РѕС‚СЂРёРјР°С‚Рё РѕСЃС‚Р°РЅРЅС–Р№ СЂРµР»С–Р· Р· GitHub.");
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
            // РЎРµСЂС‚РёС„С–РєР°С‚ Р›.Р†.Рђ вЂ” self-signed (CN=Alexuбєћ). AppX Deployment Service РїСЂР°С†СЋС”
            // СЏРє LocalSystem С– Р±Р°С‡РёС‚СЊ Р»РёС€Рµ РјР°С€РёРЅРЅС– СЃС…РѕРІРёС‰Р° СЃРµСЂС‚РёС„С–РєР°С‚С–РІ. Р†РјРїРѕСЂС‚ Сѓ
            // CurrentUser\TrustedPeople РїСЂРёР·РІРѕРґРёС‚СЊ РґРѕ 0x800B0109 (CERT_E_UNTRUSTEDROOT)
            // РЅР° РµС‚Р°РїС– Add-AppxPackage. РўРѕРјСѓ СЃРµСЂС‚РёС„С–РєР°С‚ С–РјРїРѕСЂС‚СѓС”С‚СЊСЃСЏ Р· elevation Сѓ
            // LocalMachine\Root С‚Р° LocalMachine\TrustedPeople вЂ” СЏРє РІ РµС‚Р°Р»РѕРЅРЅРѕРјСѓ
            // СЃРєСЂРёРїС‚С– Р°РІС‚РѕСЂР° First_Install_LIA_Voice_Assistent.bat.
            if (!string.IsNullOrWhiteSpace(certificatePath) && File.Exists(certificatePath))
            {
                await InstallCertificateElevatedAsync(certificatePath, cancellationToken).ConfigureAwait(false);
            }

            var result = await RunPowerShellAsync(BuildInstallerScript(installerPath), cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim());
        }

        private static async Task InstallCertificateElevatedAsync(string certificatePath, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(AppSettings.UpdatesDirectory);
            var scriptPath = Path.Combine(AppSettings.UpdatesDirectory, $"lia-cert-{Guid.NewGuid():N}.ps1");
            var logPath = Path.Combine(AppSettings.UpdatesDirectory, $"lia-cert-{Guid.NewGuid():N}.log");

            var escapedCertificatePath = EscapePowerShellString(certificatePath);
            var escapedLogPath = EscapePowerShellString(logPath);

            // certutil Р±РµР· РїСЂР°РїРѕСЂС†СЏ -user РїРёС€Рµ Сѓ СЃС…РѕРІРёС‰Р° LocalMachine, С‰Рѕ РІРёРјР°РіР°С”
            // РїСЂР°РІ Р°РґРјС–РЅС–СЃС‚СЂР°С‚РѕСЂР°. РџСЂРѕС†РµСЃ Р·Р°РїСѓСЃРєР°С”С‚СЊСЃСЏ Р· Verb=runas (UAC).
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                $certificatePath = '{{escapedCertificatePath}}'
                $logPath = '{{escapedLogPath}}'

                certutil.exe -addstore -f Root $certificatePath *>> $logPath
                $rootExit = $LASTEXITCODE

                certutil.exe -addstore -f TrustedPeople $certificatePath *>> $logPath
                $peopleExit = $LASTEXITCODE

                if ($rootExit -ne 0 -or $peopleExit -ne 0) {
                    exit 1
                }
                """;

            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };

                try
                {
                    process.Start();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // РљРѕСЂРёСЃС‚СѓРІР°С‡ СЃРєР°СЃСѓРІР°РІ UAC-Р·Р°РїРёС‚ РЅР° РїС–РґРІРёС‰РµРЅРЅСЏ РїСЂРёРІС–Р»РµС—РІ.
                    throw new InvalidOperationException("Р†РјРїРѕСЂС‚ СЃРµСЂС‚РёС„С–РєР°С‚Р° СЃРєР°СЃРѕРІР°РЅРѕ: РїРѕС‚СЂС–Р±РЅС– РїСЂР°РІР° Р°РґРјС–РЅС–СЃС‚СЂР°С‚РѕСЂР°.");
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    var log = File.Exists(logPath)
                        ? await File.ReadAllTextAsync(logPath, cancellationToken).ConfigureAwait(false)
                        : string.Empty;

                    throw new InvalidOperationException(
                        $"РќРµ РІРґР°Р»РѕСЃСЏ С–РјРїРѕСЂС‚СѓРІР°С‚Рё СЃРµСЂС‚РёС„С–РєР°С‚ Р›.Р†.Рђ (РєРѕРґ {process.ExitCode}). {log.Trim()}");
                }
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { }
                try { File.Delete(logPath); } catch { }
            }
        }

        private static string BuildInstallerScript(string installerPath)
        {
            var escapedInstallerPath = EscapePowerShellString(installerPath);

            return $$"""
                $ErrorActionPreference = 'Stop'
                $installerPath = '{{escapedInstallerPath}}'

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
                {}
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SCLOC-Verse-LIA-Installer/1.0");
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