using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.OcrPlatform;
using System.Diagnostics;

namespace SCLOCVerse.Services.OcrPlatform.Coordinator
{
    /// <summary>
    /// Skeleton реалізація <see cref="IOcrCoordinator"/> (T6.2):
    /// Timer-driven cycle + StarCitizenForeground gate.
    /// Pipeline OCR (capture → preprocess → OCR → validate) + event publish — в T6.3/T6.4.
    /// </summary>
    public sealed class OcrCoordinator : IOcrCoordinator
    {
        private readonly IOcrRegionRegistry _regionRegistry;
        private readonly IScreenCaptureService _screenCapture;
        private readonly IImagePipeline _imagePipeline;
        private readonly IOcrEngine _ocrEngine;
        private readonly IResultValidator _resultValidator;
        private readonly TimeSpan _cycleInterval;

        private Timer? _cycleTimer;
        private bool _disposed;
        private int _isRunning; // 0=stopped, 1=running (Interlocked)

        /// <summary>
        /// Створити OcrCoordinator з інжектованими залежностями.
        /// Усі залежності — вже реалізовані сервіси з Composition Root.
        /// </summary>
        /// <param name="cycleIntervalMs">Інтервал cycle в мс. Default 200мс = 5 Hz.</param>
        public OcrCoordinator(
            IOcrRegionRegistry regionRegistry,
            IScreenCaptureService screenCapture,
            IImagePipeline imagePipeline,
            IOcrEngine ocrEngine,
            IResultValidator resultValidator,
            int cycleIntervalMs = 200)
        {
            ArgumentNullException.ThrowIfNull(regionRegistry);
            ArgumentNullException.ThrowIfNull(screenCapture);
            ArgumentNullException.ThrowIfNull(imagePipeline);
            ArgumentNullException.ThrowIfNull(ocrEngine);
            ArgumentNullException.ThrowIfNull(resultValidator);
            if (cycleIntervalMs < 50) throw new ArgumentOutOfRangeException(nameof(cycleIntervalMs));

            _regionRegistry = regionRegistry;
            _screenCapture = screenCapture;
            _imagePipeline = imagePipeline;
            _ocrEngine = ocrEngine;
            _resultValidator = resultValidator;
            _cycleInterval = TimeSpan.FromMilliseconds(cycleIntervalMs);
        }

        /// <inheritdoc />
        public event EventHandler<OcrRegionResult>? OcrRegionReady;

        /// <inheritdoc />
        public bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;

        /// <inheritdoc />
        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Interlocked.Exchange(ref _isRunning, 1) == 1) return; // вже запущений

            _cycleTimer?.Dispose();
            _cycleTimer = new Timer(OnCycleTick, null, _cycleInterval, _cycleInterval);
            Debug.WriteLine("[OcrCoordinator] Started — interval {0}ms", _cycleInterval.TotalMilliseconds);
        }

        /// <inheritdoc />
        public void Stop()
        {
            if (Interlocked.Exchange(ref _isRunning, 0) == 0) return; // вже зупинений

            _cycleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            Debug.WriteLine("[OcrCoordinator] Stopped");
        }

        /// <summary>
        /// Timer callback — виконується на ThreadPool.
        /// T6.2: лише gate + log. T6.3: повний pipeline. T6.4: publish event.
        /// </summary>
        private void OnCycleTick(object? state)
        {
            try
            {
                // Gate 1: Coordinator зупинено.
                if (!IsRunning) return;

                // Gate 2: SC не активний — skip cycle (CPU saving, як AntiAfk/AutoKey).
                if (!StarCitizenForeground.IsStarCitizenForeground()) return;

                // Gate 3: немає активних регіонів — skip.
                var regions = _regionRegistry.GetActiveRegions();
                if (regions.Count == 0) return;

                // T6.3 — pipeline OCR для кожного регіону.
                // T6.4 — publish event з результатами.
                Debug.WriteLine("[OcrCoordinator] Cycle: {0} active regions", regions.Count);
            }
            catch (Exception ex)
            {
                // Critical: Timer callback не повинен кидати назовні — це вбиває Timer.
                Debug.WriteLine("[OcrCoordinator] Cycle exception: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Викликати подію OcrRegionReady (використовується в T6.4).
        /// </summary>
        private void RaiseOcrRegionReady(OcrRegionResult result)
        {
            try
            {
                OcrRegionReady?.Invoke(this, result);
            }
            catch (Exception ex)
            {
                // Підписник не повинен ламати Coordinator.
                Debug.WriteLine("[OcrCoordinator] OcrRegionReady subscriber exception: {0}", ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _cycleTimer?.Dispose();
            _cycleTimer = null;
            _disposed = true;
        }
    }
}
