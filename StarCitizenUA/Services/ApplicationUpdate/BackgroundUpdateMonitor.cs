using StarCitizenUA.Helpers;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Models.ApplicationUpdate;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace StarCitizenUA.Services.ApplicationUpdate
{
    public class BackgroundUpdateMonitor : IBackgroundUpdateMonitor, IDisposable
    {
        private readonly IApplicationUpdateService _updateService;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _timer;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private Version? _lastNotifiedVersion;
        private bool _disposed;

        public BackgroundUpdateMonitor(IApplicationUpdateService updateService)
        {
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _dispatcher = Dispatcher.CurrentDispatcher;

            _timer = new DispatcherTimer
            {
                Interval = UpdateConstants.BackgroundUpdateCheckInterval
            };
            _timer.Tick += async (s, e) => await CheckOnceAsync(CancellationToken.None).ConfigureAwait(false);
        }

        public event EventHandler<UpdateCheckResult>? UpdateAvailable;
        public event EventHandler<Exception>? CheckFailed;

        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BackgroundUpdateMonitor));

            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public async Task CheckOnceAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BackgroundUpdateMonitor));

            var entered = await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false);
            if (!entered)
                return;

            try
            {
                var result = await _updateService.CheckForUpdatesAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);

                if (result.Status == UpdateCheckStatus.UpdateAvailable && result.LatestVersion != null)
                {
                    if (_lastNotifiedVersion == null || result.LatestVersion > _lastNotifiedVersion)
                    {
                        _lastNotifiedVersion = result.LatestVersion;
                        _dispatcher.Invoke(() => UpdateAvailable?.Invoke(this, result));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackgroundUpdateMonitor] Check failed: {ex}");
                _dispatcher.Invoke(() => CheckFailed?.Invoke(this, ex));
            }
            finally
            {
                _semaphore.Release();
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
