using OpenCvSharp;
using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Mining;
using SCLOCVerse.Models.OcrPlatform;
using SCLOCVerse.Services.OcrPlatform.Engines;
using System.Diagnostics;
using System.Windows.Media.Imaging;

namespace SCLOCVerse.Services.Mining
{
    /// <summary>
    /// Реалізація <see cref="IMiningRecognitionService"/>:
    /// підписується на IOcrCoordinator.OcrRegionReady → lookup у IMiningSignatureDatabase →
    /// оновлює MiningState → raise StateChanged event.
    ///
    /// Архітектурний принцип:
    /// "Screen resolution is never the source of truth. The HUD layout is."
    ///
    /// Двоступенева архітектура:
    /// 1. Discovery Mode — власний timer + IMiningHudLocatorStrategy (БЕЗ OCR, дешева локалізація).
    ///    Locator знаходить HUD → OnHudLocated → реєстрація регіонів у Coordinator → Tracking.
    /// 2. Tracking Mode — Coordinator-driven OCR всередині HUD регіонів.
    ///    Confidence-based transitions (hard/soft threshold, null count).
    /// </summary>
    public sealed class MiningRecognitionService : IMiningRecognitionService, IDisposable
    {
        private readonly IMiningSignatureDatabase _database;
        private readonly IOcrCoordinator _coordinator;
        private readonly IOcrRegionRegistry _regionRegistry;
        private readonly IMiningRoiResolver _roiResolver;
        private readonly IMiningHudLocatorStrategy _locatorStrategy;
        private readonly IScreenCaptureService _screenCapture;
        private readonly IMiningOverlayService _overlay;
        private readonly IOcrEngine _ocrEngine;
        private readonly object _stateLock = new();

        /// <summary>Discovery timer interval (повільніший за Coordinator — 2с).</summary>
        private const int DiscoveryIntervalMs = 2000;

        private Timer? _discoveryTimer;
        private bool _disposed;

        public MiningRecognitionService(
            IMiningSignatureDatabase database,
            IOcrCoordinator coordinator,
            IOcrRegionRegistry regionRegistry,
            IMiningRoiResolver roiResolver,
            IMiningHudLocatorStrategy locatorStrategy,
            IScreenCaptureService screenCapture,
            IMiningOverlayService overlay,
            IOcrEngine ocrEngine)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(coordinator);
            ArgumentNullException.ThrowIfNull(regionRegistry);
            ArgumentNullException.ThrowIfNull(roiResolver);
            ArgumentNullException.ThrowIfNull(locatorStrategy);
            ArgumentNullException.ThrowIfNull(screenCapture);
            ArgumentNullException.ThrowIfNull(overlay);
            ArgumentNullException.ThrowIfNull(ocrEngine);

            _database = database;
            _coordinator = coordinator;
            _regionRegistry = regionRegistry;
            _roiResolver = roiResolver;
            _locatorStrategy = locatorStrategy;
            _screenCapture = screenCapture;
            _overlay = overlay;
            _ocrEngine = ocrEngine;
        }

        /// <inheritdoc />
        public MiningState CurrentState { get; } = new();

        /// <inheritdoc />
        public event EventHandler<MiningState>? StateChanged;

        /// <inheritdoc />
        public bool IsEnabled { get; private set; }

        /// <inheritdoc />
        public void Enable()
        {
            if (IsEnabled) return;
            _coordinator.OcrRegionReady += OnOcrRegionReady;
            IsEnabled = true;
            StartDiscoveryMode();
        }

        /// <inheritdoc />
        public void Disable()
        {
            if (!IsEnabled) return;
            _coordinator.OcrRegionReady -= OnOcrRegionReady;
            StopDiscoveryTimer();
            UnregisterAllRegions();
            _roiResolver.ResetToDiscovery();
            lock (_stateLock)
            {
                CurrentState.Material = null;
                CurrentState.RawCode = null;
                CurrentState.ClusterCount = null;
                CurrentState.Confidence = 0;
                CurrentState.LastUpdatedUtc = DateTime.UtcNow;
            }
            IsEnabled = false;
            StateChanged?.Invoke(this, CurrentState);

            // Lazy ONNX: вивантажити моделі → звільнити ~500МБ.
            if (_ocrEngine is PaddleOcrEngine paddle)
            {
                paddle.UnloadModels();
                Debug.WriteLine("[MiningRecognition] ONNX models unloaded (lazy)");
            }
        }

        /// <inheritdoc />
        public void SetManualRoi(System.Windows.Rect roi)
        {
            if (roi.Width <= 0 || roi.Height <= 0) return;

            // Створити fake location з manual bounds.
            var location = new MiningHudLocationResult
            {
                HudBounds = roi,
                Confidence = 1.0,
                StrategyName = "ManualCalibration",
                Details = "Manual ROI"
            };

            _roiResolver.OnHudLocated(location);
            TransitionToTracking();
            Debug.WriteLine("[MiningRecognition] Manual ROI set: {0}", roi);
        }

        /// <inheritdoc />
        public void ResetManualRoi()
        {
            if (_coordinator.IsRunning) _coordinator.Stop();
            UnregisterAllRegions();
            _roiResolver.ResetToDiscovery();
            if (IsEnabled) StartDiscoveryMode();
            Debug.WriteLine("[MiningRecognition] Manual ROI reset → Discovery");
        }

        // ── Discovery Mode ──

        /// <summary>
        /// Запустити Discovery Mode — власний timer + locator (БЕЗ OCR).
        /// </summary>
        private void StartDiscoveryMode()
        {
            // Зупинити Coordinator (Tracking), якщо був активний.
            if (_coordinator.IsRunning) _coordinator.Stop();
            UnregisterAllRegions();

            // One-shot timer pattern (як OcrCoordinator H2).
            _discoveryTimer?.Dispose();
            _discoveryTimer = new Timer(OnDiscoveryTick, null,
                TimeSpan.FromMilliseconds(DiscoveryIntervalMs), Timeout.InfiniteTimeSpan);

            Debug.WriteLine("[MiningRecognition] Discovery mode started (locator: {0})",
                _locatorStrategy.Name);
        }

        /// <summary>
        /// Discovery timer callback — захопити екран + локалізувати HUD.
        /// </summary>
        private void OnDiscoveryTick(object? state)
        {
            try
            {
                if (!IsEnabled || _disposed) return;

                // Foreground gate — SC не активний, skip.
                if (!StarCitizenForeground.IsStarCitizenForeground())
                {
                    UpdateOverlayStatus("Discovery: SC не активний");
                    return;
                }

                UpdateOverlayStatus("Discovery: сканування екрана...");

                // Захопити повний екран.
                var fullScreenRect = _roiResolver.CurrentCaptureRect;
                var bitmap = _screenCapture.CaptureRegionAsync(fullScreenRect).GetAwaiter().GetResult();
                using var screenshotMat = BitmapSourceToMat(bitmap);

                // Локалізувати HUD через стратегію (повноекранний OCR + DB lookup).
                var location = _locatorStrategy.Locate(screenshotMat);
                if (location is null)
                {
                    UpdateOverlayStatus("Discovery: сигнатуру не знайдено");
                    return;
                }

                // HUD знайдено → негайно показати результат.
                UpdateOverlayStatus($"Discovery: HUD знайдено! {location.Details}");

                _roiResolver.OnHudLocated(location);

                // Негайний DB lookup з тексту, який знайшов locator.
                // Locator (OcrFullScanLocator) повертає HUD bounds навколо знайденого тексту.
                // Текст сигнатури доступний через CurrentState (оновлюється ниже).
                // Спробуємо витягнути текст з location.Details (формат: "text='3,385' match=...").
                var signatureText = ExtractSignatureFromDetails(location.Details);
                if (!string.IsNullOrEmpty(signatureText))
                {
                    lock (_stateLock)
                    {
                        UpdateMaterial(signatureText, location.Confidence);
                    }
                    StateChanged?.Invoke(this, CurrentState);
                    // Overlay НЕ слідкує за HUD — стоїть де користувач поставив.
                }

                TransitionToTracking();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MiningRecognition] Discovery exception: {0}", ex.Message);
                UpdateOverlayStatus($"Discovery: помилка — {ex.Message}");
            }
            finally
            {
                // Re-arm one-shot timer.
                if (!_disposed && IsEnabled && _roiResolver.Layout.Mode == MiningRoiMode.Discovery)
                {
                    try
                    {
                        _discoveryTimer?.Change(
                            TimeSpan.FromMilliseconds(DiscoveryIntervalMs), Timeout.InfiniteTimeSpan);
                    }
                    catch (ObjectDisposedException) { /* timer disposed */ }
                }
            }
        }

        /// <summary>
        /// Витягнути текст сигнатури з Details (формат: "text='3,385' match=...").
        /// </summary>
        private static string? ExtractSignatureFromDetails(string? details)
        {
            if (string.IsNullOrEmpty(details)) return null;
            // Формат: text='3,385' match=...
            var marker = "text='";
            var idx = details.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return null;
            var start = idx + marker.Length;
            var end = details.IndexOf('\'', start);
            if (end < 0) return null;
            return details[start..end];
        }

        /// <summary>
        /// Оновити overlay статусним текстом (для діагностики Discovery mode).
        /// </summary>
        private void UpdateOverlayStatus(string status)
        {
            // Оновлюємо RawCode щоб overlay показав статус.
            lock (_stateLock)
            {
                if (CurrentState.Material is null)
                {
                    CurrentState.RawCode = status;
                }
            }
            StateChanged?.Invoke(this, CurrentState);
        }

        /// <summary>
        /// Перейти з Discovery → Tracking: реєструвати HUD bounds у Coordinator + запустити OCR.
        /// Використовує РЕАЛЬНІ bounds від locator (НЕ placeholder template regions).
        /// </summary>
        private void TransitionToTracking()
        {
            var layout = _roiResolver.Layout;
            if (layout.HudBounds is null) return;

            // Зупинити discovery timer — більше не потрібен.
            StopDiscoveryTimer();

            // Реєструємо ОДИН регіон — HUD bounds від locator.
            // TrackingScan: MaxSideLen=960 (швидко для малого ROI).
            _regionRegistry.Register(new OcrRegion
            {
                Id = $"mining.{MiningHudRegionNames.Signature}",
                Name = "Mining Signature (HUD bounds)",
                ScreenRect = layout.HudBounds.Value,
                OcrOptions = OcrOptions.TrackingScan
            });

            // Запустити Coordinator (Tracking OCR).
            if (!_coordinator.IsRunning) _coordinator.Start();

            Debug.WriteLine("[MiningRecognition] → Tracking mode (HUD bounds: {0})",
                layout.HudBounds);
        }

        // ── Tracking Mode ──

        /// <summary>
        /// Handler для OcrRegionReady події (Tracking mode).
        /// Передає результат у ROI Resolver для confidence-based transitions.
        /// Виконує DB lookup для signature region.
        /// </summary>
        private void OnOcrRegionReady(object? sender, OcrRegionResult e)
        {
            try
            {
                if (!IsEnabled) return;

                // Мапа regionId → regionName (напр. "mining.signature" → "signature").
                var regionName = e.RegionId.StartsWith("mining.")
                    ? e.RegionId["mining.".Length..]
                    : null;

                if (regionName is null) return;

                var text = e.Result.BestMatch?.Text;
                var confidence = e.Result.Confidence;

                // Повідомити resolver про результат (confidence-based transitions).
                _roiResolver.OnRegionResult(regionName, text, confidence);

                // Якщо resolver скинувся до Discovery — перезапустити discovery mode.
                if (_roiResolver.Layout.Mode == MiningRoiMode.Discovery)
                {
                    Debug.WriteLine("[MiningRecognition] Resolver → Discovery (HUD lost)");
                    if (_coordinator.IsRunning) _coordinator.Stop();
                    UnregisterAllRegions();
                    StartDiscoveryMode();
                    return;
                }

                // DB lookup для signature region.
                if (regionName == MiningHudRegionNames.Signature && !string.IsNullOrEmpty(text))
                {
                    lock (_stateLock)
                    {
                        UpdateMaterial(text, confidence);
                    }
                    StateChanged?.Invoke(this, CurrentState);
                    // Overlay НЕ слідкує за HUD — стоїть де користувач поставив.
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MiningRecognition] OnOcrRegionReady exception: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Оновити material у state — lookup у database.
        /// </summary>
        private void UpdateMaterial(string code, double confidence)
        {
            var material = _database.Lookup(code);
            CurrentState.RawCode = code;
            CurrentState.Material = material;
            CurrentState.Confidence = confidence;
            CurrentState.LastUpdatedUtc = DateTime.UtcNow;
        }

        // ── Helpers ──

        private void StopDiscoveryTimer()
        {
            _discoveryTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _discoveryTimer?.Dispose();
            _discoveryTimer = null;
        }

        private void UnregisterAllRegions()
        {
            // Видаляємо всі mining.* регіони.
            foreach (var region in _regionRegistry.GetAllRegions())
            {
                if (region.Id.StartsWith("mining."))
                    _regionRegistry.Unregister(region.Id);
            }
        }

        /// <summary>
        /// Конвертація BitmapSource (WPF) → OpenCvSharp Mat (BGRA → BGR).
        /// Безпечно: fixed pointer + Mat з вказанням stride (без Marshal.Copy).
        /// </summary>
        private static Mat BitmapSourceToMat(BitmapSource bitmap)
        {
            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var stride = width * (bitmap.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);

            // fixed блокує GC від руху масиву — OpenCV читає напряму.
            // Mat.FromPixelData з IntPtr + step=stride — обгортає масив без копіювання.
            // CvtColor робить глибоку копію у bgr Mat (власна пам'ять OpenCV).
            unsafe
            {
                fixed (byte* ptr = pixels)
                {
                    using var bgraMat = Mat.FromPixelData(height, width, MatType.CV_8UC4, (IntPtr)ptr, stride);
                    var bgr = new Mat();
                    Cv2.CvtColor(bgraMat, bgr, ColorConversionCodes.BGRA2BGR);
                    return bgr;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { Disable(); } catch { /* ignore */ }
            StopDiscoveryTimer();
        }
    }
}