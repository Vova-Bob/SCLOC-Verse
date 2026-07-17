using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models;
using SCLOCVerse.Models.ApplicationUpdate;
using SCLOCVerse.Models.Observability;
using SCLOCVerse.Services.Observability;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SCLOCVerse.Services.ApplicationUpdate
{
    /// <summary>
    /// Єдиний оркестратор фонових перевірок оновлень.
    ///
    /// Один DispatcherTimer (UpdateConstants.BackgroundUpdateCheckInterval = 10 хв),
    /// один цикл: послідовно App → Localization → LIA. SemaphoreSlim TryEnter guard
    /// проти накладання циклів (якщо попередній ще працює — тихо виходимо).
    ///
    /// Localization перевіряється лише за _preferencesService.GetAutoUpdateLocalization() == true
    /// та наявності game folder. Оновлює ВСІ знайдені середовища (LIVE/PTU/EPTU/HOTFIX),
    /// що існують на диску — InstallAsync сам обирає release/prerelease канал
    /// через StarCitizenEnvironments.IsPrereleaseChannel.
    ///
    /// LIA перевіряється завжди (GetStatusAsync легка, кеш ETag).
    ///
    /// Телеметрія: лише UpdateFound/Failed per-env. Не логуємо успішні перевірки без оновлень.
    ///
    /// Примітка: InstallAsync виконує conditional GET (304 → skip без запису).
    /// Семантично "install", але behavior = "check + apply якщо зміни".
    /// Backlog (post-Етап E): розділити на CheckAsync/InstallAsync для явної моделі.
    /// </summary>
    public class BackgroundUpdateMonitor : IBackgroundUpdateMonitor, IDisposable
    {
        private readonly IApplicationUpdateService _updateService;
        private readonly ILocalizationInstaller _localizationInstaller;
        private readonly IUpdater _updater;
        private readonly ISettingsService _settingsService;
        private readonly IPreferencesService _preferencesService;
        private readonly ITelemetryService _telemetry;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _timer;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private bool _disposed;

        public BackgroundUpdateMonitor(
            IApplicationUpdateService updateService,
            ILocalizationInstaller localizationInstaller,
            IUpdater updater,
            ISettingsService settingsService,
            IPreferencesService preferencesService,
            ITelemetryService telemetry)
        {
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _localizationInstaller = localizationInstaller ?? throw new ArgumentNullException(nameof(localizationInstaller));
            _updater = updater ?? throw new ArgumentNullException(nameof(updater));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
            _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            _dispatcher = Dispatcher.CurrentDispatcher;

            _timer = new DispatcherTimer
            {
                Interval = UpdateConstants.BackgroundUpdateCheckInterval
            };
            _timer.Tick += async (s, e) => await CheckOnceAsync(CancellationToken.None).ConfigureAwait(false);
        }

        public event EventHandler<UpdateCycleResult>? UpdateCycleCompleted;
        public event EventHandler<Exception>? CheckFailed;

        public void Start(bool runImmediately = false)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BackgroundUpdateMonitor));

            _timer.Start();

            // Негайна перша перевірка: Toast про нові Localization/LIA одразу після старту,
            // а не через 1 годину першого тику таймера. Fire-and-forget — безпечний через
            // SemaphoreSlim TryEnter guard у CheckOnceAsync (не накладатиметься з Tick).
            if (runImmediately)
                _ = CheckOnceAsync(CancellationToken.None);
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public async Task CheckOnceAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BackgroundUpdateMonitor));

            // Неблокуючий TryEnter — якщо попередній цикл ще працює, виходимо без дублювання HTTP.
            var entered = await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false);
            if (!entered)
                return;

#if DEBUG
            var sw = Stopwatch.StartNew();
            Debug.WriteLine("[Orchestrator] цикл почато");
#endif

            try
            {
                var appResult = await CheckAppAsync(cancellationToken).ConfigureAwait(false);
                var localizationResults = await CheckLocalizationAsync(cancellationToken).ConfigureAwait(false);
                var liaStatus = await CheckLiaAsync(cancellationToken).ConfigureAwait(false);

                var cycleResult = new UpdateCycleResult(appResult, localizationResults, liaStatus);
                _dispatcher.Invoke(() => UpdateCycleCompleted?.Invoke(this, cycleResult));

#if DEBUG
                sw.Stop();
                Debug.WriteLine($"[Orchestrator] цикл завершено за {sw.ElapsedMilliseconds} мс");
#endif
            }
            catch (OperationCanceledException)
            {
                // Скасування — не помилка, не логуємо.
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Orchestrator] цикл failed: {ex}");
                _telemetry.Track("Orchestrator", "Cycle", "Failed", ErrorContextExtractor.Extract(ex));
                _dispatcher.Invoke(() => CheckFailed?.Invoke(this, ex));
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // --- Послідовні перевірки ---

        private async Task<UpdateCheckResult?> CheckAppAsync(CancellationToken cancellationToken)
        {
            try
            {
                var result = await _updateService.CheckForUpdatesAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
                if (result?.IsUpdateAvailable == true)
                    _telemetry.Track("Orchestrator", "AppCheck", "Skipped", level: TelemetryLevel.Diagnostic);
                return result;
            }
            catch (Exception ex)
            {
                _telemetry.Track("Orchestrator", "AppCheck", "Failed", ErrorContextExtractor.Extract(ex));
                return null;
            }
        }

        private async Task<List<LocalizationInstallResult>> CheckLocalizationAsync(CancellationToken cancellationToken)
        {
            var results = new List<LocalizationInstallResult>();

            // AutoUpdate керує лише встановленням, а не перевіркою та сповіщенням:
            //   ON  → InstallAsync (оновлення global.ini + metadata.InstalledTag).
            //   OFF → CheckAsync   (лише порівняння TagName, без запису, Toast про доступне оновлення).
            var autoUpdate = _preferencesService.GetAutoUpdateLocalization();

            var gameRoot = _settingsService.GetGameFolder();
            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
                return results;

            foreach (var env in StarCitizenEnvironments.Known)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var envFolder = Path.Combine(gameRoot, env);
                if (!Directory.Exists(envFolder))
                {
#if DEBUG
                    Debug.WriteLine($"[Orchestrator] {env}: пропущено (тека не існує)");
#endif
                    continue;
                }

#if DEBUG
                Debug.WriteLine($"[Orchestrator] {env}: перевірка... (AutoUpdate={(autoUpdate ? "ON" : "OFF")})");
#endif

                try
                {
                    var result = autoUpdate
                        ? await _localizationInstaller.InstallAsync(envFolder, env, cancellationToken).ConfigureAwait(false)
                        : await _localizationInstaller.CheckAsync(envFolder, env, cancellationToken).ConfigureAwait(false);

                    if (result.HasUpdate)
                    {
                        results.Add(result);
                        var outcome = autoUpdate ? "Succeeded" : "Skipped";
                        _telemetry.Track("Orchestrator", "LocalizationCheck", outcome,
                            new TelemetryContext { Detail = new() { { "environment", env }, { "version", result.Version ?? "" } } },
                            level: TelemetryLevel.Diagnostic);
#if DEBUG
                        Debug.WriteLine($"[Orchestrator] {env}: {(autoUpdate ? "оновлено до" : "доступне оновлення")} {result.Version}");
#endif
                    }
                }
                catch (Exception ex)
                {
                    var ctx = ErrorContextExtractor.Extract(ex);
                    if (ctx is not null)
                    {
                        ctx.Detail ??= new Dictionary<string, object?>();
                        ctx.Detail["environment"] = env;
                    }
                    _telemetry.Track("Orchestrator", "LocalizationCheck", "Failed", ctx);
#if DEBUG
                    Debug.WriteLine($"[Orchestrator] {env}: помилка — {ex.Message}");
#endif
                }
            }

            return results;
        }

        private async Task<LiaInstallStatus?> CheckLiaAsync(CancellationToken cancellationToken)
        {
            try
            {
                var status = await _updater.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                if (status?.IsUpdateAvailable == true)
                    _telemetry.Track("Orchestrator", "LiaCheck", "Skipped", level: TelemetryLevel.Diagnostic);
                return status;
            }
            catch (Exception ex)
            {
                _telemetry.Track("Orchestrator", "LiaCheck", "Failed", ErrorContextExtractor.Extract(ex));
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
            _semaphore.Dispose();
        }
    }
}