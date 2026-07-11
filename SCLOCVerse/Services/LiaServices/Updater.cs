using Newtonsoft.Json;
using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.LiaModels;
using SCLOCVerse.Models.LiaServices;
using SCLOCVerse.Models.Observability;
using SCLOCVerse.Services.Observability;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SCLOCVerse.Services.LiaServices
{
    public class Updater : IUpdater
    {
        private const string RepositoryId = "AlexLiberty/StarCitizen_VoicePack_Releases";

        private static readonly HttpClient Client = CreateHttpClient();
        private readonly ITelemetryService? _telemetry;

        // In-memory кеш remote-статусу LIA з TTL — зменшує GitHub-запити при частих викликах
        // (Assistant_Click, оркестратор, ручні кнопки). ETag lia.meta.json залишається,
        // але RAM-кеш прибирає навіть 304-запити протягом TTL.
        // Static — бо Updater один екземпляр у сесії (патерн автора з static Client).
        private sealed record CachedStatus(LiaInstallStatus Status, DateTimeOffset FetchedAt)
        {
            public bool IsFresh => DateTimeOffset.UtcNow - FetchedAt < GitHubCacheDefaults.StatusTtl;
        }

        private static CachedStatus? _cachedStatus;
        private static readonly SemaphoreSlim _statusSemaphore = new(1, 1);

        public Updater(ITelemetryService? telemetry = null)
        {
            _telemetry = telemetry;
        }

        public async Task<LiaInstallStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            // Швидкий шлях: кеш свіжий — повертаємо без Semaphore, без мережі.
            if (_cachedStatus is { IsFresh: true } fresh)
            {
#if DEBUG
                Debug.WriteLine($"[LIA Cache][{RepositoryId}] HIT (Age={DateTimeOffset.UtcNow - fresh.FetchedAt:hh\\:mm\\:ss})");
#endif
                return fresh.Status;
            }

            // Чекаємо на Semaphore повністю — щоб 20 потоків не отримали прострочені дані,
            // поки один оновлює кеш. Timeout 15с + fallback на кеш.
            var entered = await _statusSemaphore.WaitAsync(GitHubCacheDefaults.SemaphoreTimeout, cancellationToken).ConfigureAwait(false);
            if (!entered)
            {
                if (_cachedStatus is { } stale && DateTimeOffset.UtcNow - stale.FetchedAt < GitHubCacheDefaults.MaximumStaleAge)
                {
#if DEBUG
                    Debug.WriteLine($"[LIA Cache][{RepositoryId}] SEMAPHORE_TIMEOUT → fallback (Age={DateTimeOffset.UtcNow - stale.FetchedAt:hh\\:mm\\:ss})");
#endif
                    return stale.Status;
                }
                throw new TimeoutException("LIA status check timed out waiting for another check to complete.");
            }

            try
            {
                // Double-check: поки чекали, кеш міг обновитись іншим потоком.
                if (_cachedStatus is { IsFresh: true } stillFresh)
                {
#if DEBUG
                    Debug.WriteLine($"[LIA Cache][{RepositoryId}] HIT (Age={DateTimeOffset.UtcNow - stillFresh.FetchedAt:hh\\:mm\\:ss})");
#endif
                    return stillFresh.Status;
                }

#if DEBUG
                Debug.WriteLine(_cachedStatus is null
                    ? $"[LIA Cache][{RepositoryId}] MISS (Empty)"
                    : $"[LIA Cache][{RepositoryId}] MISS (Expired)");
#endif

                var installedVersion = await GetInstalledVersionAsync(cancellationToken).ConfigureAwait(false);
                var release = await TryGetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
                var latestVersion = TryParseVersion(release?.TagName, out var remoteVersion) ? remoteVersion : null;

                // 304 → release == null, але LastKnownVersion є в кеші — використовуємо (bug fix).
                if (release is null && latestVersion is null)
                {
                    var lastKnownStr = ReadReleaseMetadata()?.LastKnownVersion;
                    if (TryParseVersion(lastKnownStr, out var cachedRemote))
                        latestVersion = cachedRemote;
                }

                var status = BuildStatus(installedVersion, latestVersion);

                // Оновлюємо кеш з новим FetchedAt — продовжуємо TTL навіть при 304.
                _cachedStatus = new CachedStatus(status, DateTimeOffset.UtcNow);

#if DEBUG
                Debug.WriteLine(release is null && latestVersion is not null
                    ? $"[LIA Cache][{RepositoryId}] REFRESH (HTTP 304)"
                    : $"[LIA Cache][{RepositoryId}] REFRESH (HTTP 200)");
#endif
                return status;
            }
            catch (HttpRequestException) when (_cachedStatus is { } stale && DateTimeOffset.UtcNow - stale.FetchedAt < GitHubCacheDefaults.MaximumStaleAge)
            {
#if DEBUG
                Debug.WriteLine($"[LIA Cache][{RepositoryId}] GITHUB_ERROR → fallback (Age={DateTimeOffset.UtcNow - stale.FetchedAt:hh\\:mm\\:ss})");
#endif
                return stale.Status;
            }
            catch (TaskCanceledException) when (_cachedStatus is { } stale && DateTimeOffset.UtcNow - stale.FetchedAt < GitHubCacheDefaults.MaximumStaleAge)
            {
#if DEBUG
                Debug.WriteLine($"[LIA Cache][{RepositoryId}] GITHUB_ERROR → fallback (Age={DateTimeOffset.UtcNow - stale.FetchedAt:hh\\:mm\\:ss})");
#endif
                return stale.Status;
            }
            catch (TimeoutException) when (_cachedStatus is { } stale && DateTimeOffset.UtcNow - stale.FetchedAt < GitHubCacheDefaults.MaximumStaleAge)
            {
#if DEBUG
                Debug.WriteLine($"[LIA Cache][{RepositoryId}] GITHUB_ERROR → fallback (Age={DateTimeOffset.UtcNow - stale.FetchedAt:hh\\:mm\\:ss})");
#endif
                return stale.Status;
            }
            finally
            {
                _statusSemaphore.Release();
            }
        }

        /// <summary>
        /// Чиста функція побудови статусу LIA з встановленої та remote-версії.
        /// Без стану, без залежностей — винесено з GetStatusAsync для читабельності.
        /// </summary>
        private static LiaInstallStatus BuildStatus(Version? installedVersion, Version? latestVersion)
        {
            if (installedVersion == null)
            {
                return new LiaInstallStatus(
                    false,
                    false,
                    null,
                    latestVersion,
                    "Голосовий асистент Л.І.А не встановлено.",
                    LiaStatusColor.Red);
            }

            if (latestVersion == null)
            {
                return new LiaInstallStatus(
                    true,
                    false,
                    installedVersion,
                    null,
                    $"Встановлена версія Л.І.А: {installedVersion}. Не вдалося перевірити оновлення.",
                    LiaStatusColor.Orange);
            }

            if (latestVersion.CompareTo(installedVersion) > 0)
            {
                return new LiaInstallStatus(
                    true,
                    true,
                    installedVersion,
                    latestVersion,
                    $"Доступне оновлення версії {latestVersion}. Встановлено: {installedVersion}",
                    LiaStatusColor.Red);
            }

            return new LiaInstallStatus(
                true,
                false,
                installedVersion,
                latestVersion,
                $"Встановлена актуальна версія Л.І.А: {installedVersion}",
                LiaStatusColor.Green);
        }

        /// <summary>
        /// Інвалідує in-memory кеш статусу LIA. Викликається після Install/Uninstall,
        /// щоб наступний GetStatusAsync підтвердив нову версію з мережі.
        /// </summary>
        internal static void InvalidateStatusCache()
        {
            _cachedStatus = null;
#if DEBUG
            Debug.WriteLine($"[LIA Cache][{RepositoryId}] INVALIDATED");
#endif
        }

        public async Task InstallLatestAsync(
            Action<string>? onProgress = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LiaEvents.Track(_telemetry, "Install", "Started", orchestrationPhase: "Download", level: TelemetryLevel.Diagnostic);

            try
            {
                var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
                var installerAsset = SelectInstallerAsset(release.Assets)
                    ?? throw new InvalidOperationException("У релізі не знайдено інсталятор Л.І.А.");
                var certificateAsset = SelectCertificateAsset(release.Assets);

                Directory.CreateDirectory(AppSettings.UpdatesDirectory);

                onProgress?.Invoke($"Знайдено реліз: {release.TagName ?? release.Name ?? "невідомо"}");

                // --- Download ---
                // Кожен Download.Started зобов'язаний мати terminal (інваріант платформи).
                // EnsureAssetDownloadedAsync кидає → обов'язково Download.Failed + FlushAsync до throw.
                LiaEvents.Track(_telemetry, "Download", "Started", orchestrationPhase: "InstallerAsset", level: TelemetryLevel.Diagnostic);
                var downloadSw = System.Diagnostics.Stopwatch.StartNew();
                string installerPath;
                try
                {
                    installerPath = await EnsureAssetDownloadedAsync(installerAsset, progress, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception dlEx)
                {
                    LiaEvents.Track(_telemetry, "Download", "Failed", downloadSw.ElapsedMilliseconds, dlEx, orchestrationPhase: "InstallerAsset");
                    if (_telemetry is not null)
                        await _telemetry.FlushAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                    throw;
                }
                LiaEvents.Track(_telemetry, "Download", "Succeeded", downloadSw.ElapsedMilliseconds, orchestrationPhase: "InstallerAsset", level: TelemetryLevel.Diagnostic);

                string? certificatePath = null;

                if (certificateAsset != null)
                {
                    onProgress?.Invoke("Завантаження сертифіката...");
                    LiaEvents.Track(_telemetry, "Download", "Started", orchestrationPhase: "CertificateAsset", level: TelemetryLevel.Diagnostic);
                    var certSw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        certificatePath = await EnsureAssetDownloadedAsync(certificateAsset, null, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception certEx)
                    {
                        LiaEvents.Track(_telemetry, "Download", "Failed", certSw.ElapsedMilliseconds, certEx, orchestrationPhase: "CertificateAsset");
                        if (_telemetry is not null)
                            await _telemetry.FlushAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                        throw;
                    }
                    LiaEvents.Track(_telemetry, "Download", "Succeeded", certSw.ElapsedMilliseconds, orchestrationPhase: "CertificateAsset", level: TelemetryLevel.Diagnostic);
                }

                // --- Install ---
                // Granular telemetry: RunInstallerScript — окрема operation, щоб Dashboard одразу
                // показував «де саме обірвався L.I.A.» (CertificateImport / AddAppxPackage),
                // а не лише факт «Install.Failed десь усередині». detail.phase несе точне місце.
                onProgress?.Invoke("Запуск інсталяції Л.І.А...");
                LiaEvents.Track(_telemetry, "Install", "Started", orchestrationPhase: "RunInstallerScript",
                    installerType: GetInstallerType(installerPath), certificatePresent: certificatePath != null,
                    packageVersion: release.TagName, level: TelemetryLevel.Diagnostic);

                var installSw = System.Diagnostics.Stopwatch.StartNew();
                await RunInstallerScriptAsync(installerPath, certificatePath, cancellationToken).ConfigureAwait(false);
                LiaEvents.Track(_telemetry, "Install", "Succeeded", installSw.ElapsedMilliseconds, orchestrationPhase: "Complete", level: TelemetryLevel.Diagnostic);

                onProgress?.Invoke("Інсталяцію завершено.");
            }
            catch (Exception ex)
            {
                // ErrorContextExtractor дістане hresult/signal/cert/activity_id з LiaInstallException;
                // для інших винятків — стандартний контекст (Network/CLR).
                LiaEvents.Track(_telemetry, "Install", "Failed", sw.ElapsedMilliseconds, ex);

                // Стаття 16 — Terminal Flush. Гарантуємо, що Failed-подія покине чергу ДО throw.
                // Інакше secondary exception у caller finally (напр. UpdateLiaVersionAsync)
                // може вбити процес без виклику D15 Dispose → черга втрачається.
                if (_telemetry is not null)
                    await _telemetry.FlushAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

                throw; // Zero Regression.
            }
            finally
            {
                // Після встановлення (успішного чи ні) — інвалідуємо кеш статусу,
                // щоб наступний GetStatusAsync підтвердив нову версію з мережі.
                InvalidateStatusCache();
            }
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

            try
            {
                var result = await RunPowerShellAsync(script, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0)
                    throw new InvalidOperationException(result.Error.Trim());

                onProgress?.Invoke("Л.І.А видалено.");
            }
            finally
            {
                // Після видалення — інвалідуємо кеш статусу (встановлена версія змінилась).
                InvalidateStatusCache();
            }
        }

        public async Task LaunchAsync(CancellationToken cancellationToken = default)
        {
            // Локальний запуск встановленого пакунка Л.І.А через Windows AppX activation.
            // Мережа не використовується: Get-AppxPackage читає локальний реєстр AppX,
            // shell:AppsFolder активує пакунок через Windows Shell API.
            var script = $$"""
                $package = Get-AppxPackage -Name '{{AppSettings.PackageName}}' | Sort-Object Version -Descending | Select-Object -First 1
                if (-not $package) {
                    Write-Output 'NOT_INSTALLED'
                    exit 0
                }

                $appId = '{{AppSettings.AppId}}'
                $shellPath = "shell:AppsFolder\$($package.PackageFamilyName)!$appId"
                Start-Process "explorer.exe" -ArgumentList $shellPath
                Write-Output 'LAUNCHED'
                """;

            var result = await RunPowerShellAsync(script, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(result.Error.Trim());

            // NOT_INSTALLED — пакунок відсутній локально (не мережева помилка).
            // Кинимо InvalidOperationException з українським повідомленням.
            var output = result.Output.Trim();
            if (output == "NOT_INSTALLED" || output.Contains("NOT_INSTALLED"))
            {
                throw new InvalidOperationException("Голосовий асистент Л.І.А не встановлено. Спочатку встановіть пакунок.");
            }

            // LAUNCHED — успішна активація. Windows Shell асинхронна, тож факт появи
            // вікна Л.І.А не гарантовано миттєво (KISS — не перевіряємо процес).
        }

        private static async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            // Для встановлення потрібен ПОВНИЙ release з Assets (інсталятор, сертифікат).
            // ETag conditional GET непридатний — 304 не містить Assets.
            // Тому запит без If-None-Match/If-Modified-Since — гарантований 200 з повним тілом.
            try
            {
                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, AppSettings.GitHubReleasesUrl);

                // Retry через єдиний HttpRetryHelper (429/403/5xx + Retry-After ≤10с + backoff).
                using var response = await HttpRetryHelper.SendWithRetryAsync(Client, request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var release = JsonConvert.DeserializeObject<GitHubRelease>(json)
                    ?? throw new InvalidOperationException("Не вдалося отримати останній реліз з GitHub.");

                // Зберігаємо оновлені ETag/Last-Modified для наступного Conditional GET (GetStatusAsync).
                var updatedMetadata = new LiaReleaseMetadata
                {
                    ETag = response.Headers.ETag?.Tag,
                    LastModified = response.Content.Headers.LastModified,
                    LastKnownVersion = release.TagName
                };
                WriteReleaseMetadata(updatedMetadata);

                return release;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                throw new InvalidOperationException("Не вдалося отримати останній реліз з GitHub.");
            }
        }

        private static async Task<GitHubRelease?> TryGetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Conditional GET: читаємо кеш ETag/Last-Modified (патерн LocalizationInstaller).
                var metadata = ReadReleaseMetadata();

                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, AppSettings.GitHubReleasesUrl);
                if (metadata?.ETag is not null)
                    request.Headers.TryAddWithoutValidation("If-None-Match", metadata.ETag);
                if (metadata?.LastModified is not null)
                    request.Headers.IfModifiedSince = metadata.LastModified;

                // Retry через єдиний HttpRetryHelper (429/403/5xx + Retry-After ≤10с + backoff).
                using var response = await HttpRetryHelper.SendWithRetryAsync(Client, request, cancellationToken).ConfigureAwait(false);

                // 304 Not Modified — дані не змінились. Повертаємо null: для GetStatusAsync
                // цього достатньо (LastKnownVersion зчитується з lia.meta.json окремо),
                // а InstallLatestAsync вимагатиме повного release з Assets — null кидає
                // InvalidOperationException, що коректно (не можна встановити з кешу 304).
                if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var release = JsonConvert.DeserializeObject<GitHubRelease>(json);

                // Зберігаємо оновлені ETag/Last-Modified для наступного Conditional GET.
                if (release is not null)
                {
                    var updatedMetadata = new LiaReleaseMetadata
                    {
                        ETag = response.Headers.ETag?.Tag,
                        LastModified = response.Content.Headers.LastModified,
                        LastKnownVersion = release.TagName
                    };
                    WriteReleaseMetadata(updatedMetadata);
                }

                return release;
            }
            catch
            {
                // Зберігаємо існуючу поведінку автора: проковтування → null.
                return null;
            }
        }

        /// <summary>
        /// Шлях до кешу метаданих LIA: %LOCALAPPDATA%\SCLOCVerse\cache\lia.meta.json.
        /// Ідентично LocalizationInstaller.GetMetadataPath.
        /// </summary>
        private static string GetMetadataPath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cacheDir = Path.Combine(localAppData, "SCLOCVerse", "cache");
            Directory.CreateDirectory(cacheDir);
            return Path.Combine(cacheDir, "lia.meta.json");
        }

        private static LiaReleaseMetadata? ReadReleaseMetadata()
        {
            try
            {
                var path = GetMetadataPath();
                if (!File.Exists(path))
                    return null;

                var json = File.ReadAllText(path, Encoding.UTF8);
                return JsonConvert.DeserializeObject<LiaReleaseMetadata>(json);
            }
            catch
            {
                return null;
            }
        }

        private static void WriteReleaseMetadata(LiaReleaseMetadata metadata)
        {
            try
            {
                var path = GetMetadataPath();
                var json = JsonConvert.SerializeObject(metadata);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch
            {
                // Метадані — не критичні. Проковтуємо.
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

        private async Task RunInstallerScriptAsync(string installerPath, string? certificatePath, CancellationToken cancellationToken)
        {
            // Granular terminal telemetry для PowerShell-фази. Якщо процес обірветься тут
            // (AddAppxPackage впав на 0x800B0109, AV вбив process тощо) — Dashboard одразу
            // покаже LIA.RunInstallerScript.Failed замість «Install.Failed десь усередині».
            // detail.phase несе точне місце (CertificateImport / AddAppxPackage) з forensic.
            var scriptSw = System.Diagnostics.Stopwatch.StartNew();
            LiaEvents.Track(_telemetry, "RunInstallerScript", "Started", level: TelemetryLevel.Diagnostic);

            PowerShellResult result;
            try
            {
                // Commit 2: requireElevation=true — перевірка механізму UAC elevation.
                // BuildInstallerScript недоторканий (cert все ще в CurrentUser\TrustedPeople),
                // тому 0x800B0109 очікувано залишиться. Мета — підтвердити працездатність elevation.
                Debug.WriteLine("[LIA] RunInstallerScript: elevation requested");
                result = await RunPowerShellAsync(
                    BuildInstallerScript(installerPath, certificatePath),
                    cancellationToken,
                    requireElevation: true).ConfigureAwait(false);
                Debug.WriteLine($"[LIA] RunInstallerScript: elevation completed, ExitCode={result.ExitCode}");
            }
            catch (Exception ex)
            {
                // Process.Start/WaitForExitAsync/IO винятки — PowerShell навіть не стартував
                // або стартував і впав до completion. Фаза невідома (ExitCode відсутній).
                LiaEvents.Track(_telemetry, "RunInstallerScript", "Failed", scriptSw.ElapsedMilliseconds, ex,
                    orchestrationPhase: "ProcessExecution", level: TelemetryLevel.Diagnostic);
                if (_telemetry is not null)
                    await _telemetry.FlushAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                throw;
            }

            if (result.ExitCode != 0)
            {
                // Структурований forensic-контракт: шукаємо ##SCLOC_FORENSIC## + JSON.
                var forensic = LiaForensicParser.TryParse(result.Output);
                if (forensic != null)
                {
                    forensic.InstallerType ??= GetInstallerType(installerPath);
                    var liaEx = new LiaInstallException(forensic, result.ExitCode, result.Error);
                    LiaEvents.Track(_telemetry, "RunInstallerScript", "Failed", scriptSw.ElapsedMilliseconds, liaEx, level: TelemetryLevel.Diagnostic);
                    if (_telemetry is not null)
                        await _telemetry.FlushAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                    throw liaEx;
                }

                // Forensic-блок відсутній — fallback на InvalidOperationException (Zero Regression).
                var fallbackEx = new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim());
                LiaEvents.Track(_telemetry, "RunInstallerScript", "Failed", scriptSw.ElapsedMilliseconds, fallbackEx,
                    orchestrationPhase: "UnknownExitCode", level: TelemetryLevel.Diagnostic);
                if (_telemetry is not null)
                    await _telemetry.FlushAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                throw fallbackEx;
            }

            LiaEvents.Track(_telemetry, "RunInstallerScript", "Succeeded", scriptSw.ElapsedMilliseconds, level: TelemetryLevel.Diagnostic);
        }

        private static string GetInstallerType(string installerPath)
        {
            var ext = System.IO.Path.GetExtension(installerPath).ToLowerInvariant();
            return ext switch
            {
                ".appinstaller" => "AppInstaller",
                ".msix" or ".msixbundle" => "MSIX",
                ".msi" => "MSI",
                ".exe" => "EXE",
                _ => "Unknown"
            };
        }

        private static string BuildInstallerScript(string installerPath, string? certificatePath)
        {
            var escapedInstallerPath = EscapePowerShellString(installerPath);
            var escapedCertificatePath = EscapePowerShellString(certificatePath ?? string.Empty);

            // Діагностика elevation (debug build) — підтверджує, що PowerShell працює elevated
            // після UAC. Прибрати після стабілізації Commit 3 або залишити під #if DEBUG.
            // Write-Output "ELEVATED=True/False" — не впливає на логіку, лише діагностичний рядок.
#if DEBUG
            const string ElevationDiagnostic = """
                $liaPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
                $liaIsAdmin = $liaPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
                Write-Output "ELEVATED=$liaIsAdmin"
                """;
#else
            const string ElevationDiagnostic = "";
#endif

            return $$"""
                $ErrorActionPreference = 'Stop'
                {{ElevationDiagnostic}}
                $installerPath = '{{escapedInstallerPath}}'
                $certificatePath = '{{escapedCertificatePath}}'

                # installerType обчислюємо раніше, щоб forensic CertificateImport catch
                # міг включити його в structured JSON (інакше $null на момент помилки імпорту).
                $extension = [System.IO.Path]::GetExtension($installerPath).ToLowerInvariant()
                $installerType = if ($extension -eq '.appinstaller') { 'AppInstaller' } elseif ($extension -eq '.msix' -or $extension -eq '.msixbundle') { 'MSIX' } elseif ($extension -eq '.msi') { 'MSI' } elseif ($extension -eq '.exe') { 'EXE' } else { 'Unknown' }

                Write-Output "LIA installer path: $installerPath"
                Write-Output "LIA certificate path: $certificatePath"
                Write-Output "Certificate file exists: $(Test-Path -LiteralPath $certificatePath)"

                if ($certificatePath -and (Test-Path -LiteralPath $certificatePath)) {
                    try {
                        # Commit 3: cert import переведено на LocalMachine\Root + LocalMachine\TrustedPeople
                        # (еквівалент BAT автора: certutil -addstore -f Root + certutil -addstore -f TrustedPeople).
                        # AppX deployment trust verification перевіряє лише LocalMachine store (Microsoft docs),
                        # тому CurrentUser\TrustedPeople був недостатнім → 0x800B0109.
                        # Вимагає elevation (requireElevation=true в RunInstallerScriptAsync, Commit 2).
                        Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
                        Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
                        Write-Output "Certificate imported to LocalMachine\Root and LocalMachine\TrustedPeople."
                    } catch {
                        # Forensic-контракт фази CertificateImport — дзеркально до AddAppxPackage catch.
                        # phase='CertificateImport' дозволяє ErrorContextExtractor встановити точне
                        # місце обриву в detail.phase (більше не "десь у PowerShell").
                        $certHr = '0x{0:X8}' -f $_.Exception.HResult
                        $certSubject = ''
                        $certThumb = ''
                        try {
                            $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
                            $certSubject = $cert.Subject
                            $certThumb = $cert.Thumbprint
                        } catch {}

                        # Import-Certificate НЕ створює AppX Activity, але $activityId залишаємо
                        # порожнім (структурна симетрія з AddAppxPackage forensic). appxLog теж
                        # порожній — для cert-import це несемантично.
                        $certActivityId = ''
                        $certAppxLog = ''

                        $certForensic = @{ hresult = $certHr; phase = 'CertificateImport'; message = $_.Exception.Message; installerType = $installerType; certificatePresent = $true; certificateSubject = $certSubject; certificateThumbprint = $certThumb; activityId = $certActivityId; appxLog = $certAppxLog }
                        $certForensicJson = $certForensic | ConvertTo-Json -Compress -Depth 3
                        Write-Output '##SCLOC_FORENSIC##'
                        Write-Output $certForensicJson
                        throw "Failed to import L.I.A certificate (HRESULT: $certHr): $($_.Exception.Message)"
                    }
                }

                try {
                    if ($extension -eq '.appinstaller') {
                        Add-AppxPackage -AppInstallerFile $installerPath
                    } elseif ($extension -eq '.msi') {
                        Start-Process msiexec.exe -ArgumentList "/i `"$installerPath`"" -Wait
                    } elseif ($extension -eq '.exe') {
                        Start-Process $installerPath -Wait
                    } else {
                        Add-AppxPackage -Path $installerPath -ForceUpdateFromAnyVersion
                    }
                } catch {
                    $hresult = '0x{0:X8}' -f $_.Exception.HResult
                    $certPresent = if ($certificatePath -and (Test-Path -LiteralPath $certificatePath)) { $true } else { $false }
                    $certSubject = ''
                    $certThumb = ''
                    if ($certPresent) {
                        try {
                            $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
                            $certSubject = $cert.Subject
                            $certThumb = $cert.Thumbprint
                        } catch {}
                    }

                    # Реальний ActivityId від AppX Deployment API (System.Exception.Activity).
                    # Раніше генерувався NewGuid — НЕВОЗМОЖНО було корелювати з Get-AppxLog.
                    # AppX deployment exceptions носять Activity як властивість типу System.Guid,
                    # іноді через InnerException.Activity. Null-safe reflection-chain.
                    $activityId = ''
                    try {
                        if ($_.Exception.Activity) { $activityId = $_.Exception.Activity.ToString() }
                        elseif ($_.Exception.InnerException -and $_.Exception.InnerException.Activity) {
                            $activityId = $_.Exception.InnerException.Activity.ToString()
                        }
                    } catch {}

                    # Get-AppxLog повертає розгорнутий Event Viewer dump для цього ActivityId —
                    # stack deployment-помилки, недоступний у $_.Exception.Message. Цей текст
                    # локалізаційно-незалежний на рівні HRESULT-кодів, тож Control Center може
                    # діагностувати навіть якщо PowerShell message пошкоджено mojibake.
                    $appxLog = ''
                    if ($activityId) {
                        try {
                            $appxLog = ((Get-AppxLog -ActivityId $activityId -ErrorAction SilentlyContinue) | Out-String)
                            if ($appxLog) { $appxLog = $appxLog.Trim() }
                        } catch {}
                    }

                    $forensic = @{ hresult = $hresult; phase = 'AddAppxPackage'; message = $_.Exception.Message; installerType = $installerType; certificatePresent = $certPresent; certificateSubject = $certSubject; certificateThumbprint = $certThumb; activityId = $activityId; appxLog = $appxLog }
                    $forensicJson = $forensic | ConvertTo-Json -Compress -Depth 3
                    Write-Output '##SCLOC_FORENSIC##'
                    Write-Output $forensicJson
                    if ($_.Exception.HResult -eq -2146762487) {
                        throw "The L.I.A package could not be installed because its signing certificate is not trusted. HRESULT: $hresult. Please install the included .cer file manually or contact the developer."
                    }
                    throw "Add-AppxPackage failed with HRESULT: $hresult. $($_.Exception.Message)"
                }
                """;
        }

        private static async Task<PowerShellResult> RunPowerShellAsync(
            string script,
            CancellationToken cancellationToken,
            bool requireElevation = false)
        {
            Directory.CreateDirectory(AppSettings.UpdatesDirectory);
            var scriptPath = Path.Combine(AppSettings.UpdatesDirectory, $"lia-{Guid.NewGuid():N}.ps1");

            // UNICODE-цілісність PowerShell output (Стаття 17 — Verbatim Diagnostics).
            //
            // PowerShell 5.1 без консолі (CreateNoWindow=true + RedirectStandardOutput=true)
            // за замовчуванням серіалізує stdout/stderr через OEM code page системи — на укр/рос
            // Windows це CP1251 або CP866. Клієнт читає з Encoding.UTF8 (StandardOutputEncoding),
            // тож кирилиця перетворюється на невалідні байти → заміна на U+FFFD ('?') →
            // користувач бачить «HRESULT 0x80131500. [пошкоджений текст]...» замість оригінального
            // локалізованого повідомлення Add-AppxPackage / Import-Certificate.
            //
            // Override [Console]::OutputEncoding + $OutputEncoding на початку скрипта
            // зобов'язує PowerShell писати UTF-8 у pipe. У PowerShell 5.1 [Console]::OutputEncoding
            // визначає кодування ОБИДВА потоків (stdout і stderr) — окрема [Console]::ErrorEncoding
            // з'явилася лише в .NET 5+ і кидає PropertyNotFound у PS 5.1. Один fix у RunPowerShellAsync
            // покриває всі caller'и: BuildInstallerScript, UninstallAsync, GetInstalledVersionAsync.
            const string EncodingPreamble = """
                [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                $OutputEncoding = [System.Text.Encoding]::UTF8
                """;
            var fullScript = EncodingPreamble + "\n" + script;

            await File.WriteAllTextAsync(scriptPath, fullScript, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

            // --- ELEVATED ШЛЯХ ---
            // UseShellExecute=true + Verb="runas" — єдиний спосіб UAC elevation в .NET.
            // RedirectStandardOutput несумісний з UseShellExecute=true, тому транспорт
            // реалізовано через wrapper.ps1, який стартує дочірній powershell з .NET Process
            // (RedirectStandardOutput=true) і записує stdout/stderr у файли UTF-8 без BOM.
            // Дочірній powershell успадковує admin token від wrapper. Контракт PowerShellResult
            // однаковий в обох режимах — caller не знає про механізм транспорту.
            if (requireElevation)
            {
                return await RunElevatedAsync(scriptPath, cancellationToken).ConfigureAwait(false);
            }

            // --- NON-ELEVATED ШЛЯХ (існуюча логіка, біт-в-біт) ---
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
                TryDeleteFile(scriptPath);
            }
        }

        /// <summary>
        /// Elevated-режим RunPowerShellAsync: UAC через Verb=runas + прозорий транспорт
        /// stdout/stderr через тимчаскові файли (wrapper.ps1 + .NET Process з RedirectStandardOutput).
        /// </summary>
        /// <remarks>
        /// UseShellExecute=true забороняє RedirectStandardOutput (Windows API обмеження),
        /// тому wrapper.ps1 стартує дочірній powershell.exe через .NET ProcessStartInfo з
        /// RedirectStandardOutput=true + StandardOutputEncoding=UTF8 і записує результати у
        /// файли через [System.IO.File]::WriteAllText з UTF8Encoding(false) (без BOM).
        /// PowerShell 5.1 оператор > пише UTF-16LE — тому .NET API обов'язкове.
        /// Дочірній процес успадковує elevated token від wrapper — Add-AppxPackage та
        /// Import-Certificate в LocalMachine\* працюють без додаткового elevation.
        /// </remarks>
        private static async Task<PowerShellResult> RunElevatedAsync(string scriptPath, CancellationToken cancellationToken)
        {
            var wrapperPath = Path.Combine(AppSettings.UpdatesDirectory, $"lia-wrapper-{Guid.NewGuid():N}.ps1");
            var stdoutPath = Path.Combine(AppSettings.UpdatesDirectory, $"lia-stdout-{Guid.NewGuid():N}.txt");
            var stderrPath = Path.Combine(AppSettings.UpdatesDirectory, $"lia-stderr-{Guid.NewGuid():N}.txt");

            // Wrapper: стартує оригінальний скрипт через .NET Process з контрольованим UTF-8.
            // $psi.Arguments використовує PowerShell-інтерполяцію (подвійні лапки з $scriptPath).
            var wrapperScript = $$"""
                $ErrorActionPreference = 'Continue'
                [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                $OutputEncoding = [System.Text.Encoding]::UTF8

                $scriptPath = '{{EscapePowerShellString(scriptPath)}}'
                $stdoutPath = '{{EscapePowerShellString(stdoutPath)}}'
                $stderrPath = '{{EscapePowerShellString(stderrPath)}}'

                $psi = New-Object System.Diagnostics.ProcessStartInfo
                $psi.FileName = 'powershell.exe'
                $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`""
                $psi.UseShellExecute = $false
                $psi.CreateNoWindow = $true
                $psi.RedirectStandardOutput = $true
                $psi.RedirectStandardError = $true
                $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
                $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

                $child = New-Object System.Diagnostics.Process
                $child.StartInfo = $psi
                [void]$child.Start()

                $stdout = $child.StandardOutput.ReadToEnd()
                $stderr = $child.StandardError.ReadToEnd()
                $child.WaitForExit()
                $code = $child.ExitCode

                $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
                [System.IO.File]::WriteAllText($stdoutPath, $stdout, $utf8NoBom)
                [System.IO.File]::WriteAllText($stderrPath, $stderr, $utf8NoBom)

                exit $code
                """;

            await File.WriteAllTextAsync(wrapperPath, wrapperScript, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{wrapperPath}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };

                try
                {
                    process.Start();
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED — користувач відхилив UAC.
                {
                    // Контракт ElevationDeclined: ExitCode=-1, Output порожній.
                    // RunInstallerScriptAsync сформує LiaInstallException з Phase="ElevationDeclined".
                    return new PowerShellResult(-1, string.Empty, "Elevation declined by user");
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                var output = File.Exists(stdoutPath)
                    ? await File.ReadAllTextAsync(stdoutPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false)
                    : string.Empty;
                var error = File.Exists(stderrPath)
                    ? await File.ReadAllTextAsync(stderrPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false)
                    : string.Empty;

                return new PowerShellResult(process.ExitCode, output, error);
            }
            finally
            {
                TryDeleteFile(scriptPath);
                TryDeleteFile(wrapperPath);
                TryDeleteFile(stdoutPath);
                TryDeleteFile(stderrPath);
            }
        }

        // best-effort видалення тимчаскових файлів (не кидає при помилці).
        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Тимчаскові файли в %LOCALAPPDATA% — ОС очистити при перезавантаженні.
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            // Єдиний User-Agent для всіх GitHub-запитів SCLOC-Verse (через HttpRetryHelper).
            client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpRetryHelper.UserAgent);
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
