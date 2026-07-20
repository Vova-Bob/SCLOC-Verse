using OpenCvSharp;
using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.OcrPlatform;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

            // H2: One-shot timer — period = Timeout.Infinite.
            // Timer викликає OnCycleTick ОДИН раз через _cycleInterval, потім НЕ повторює.
            // Re-arm виконується в кінці OnCycleTick після завершення Parallel.ForEach.
            // Це запобігає re-entrant overlap: якщо cycle > interval — наступний cycle
            // просто почекає завершення поточного, а не виконуватиметься паралельно.
            _cycleTimer?.Dispose();
            _cycleTimer = new Timer(OnCycleTick, null, _cycleInterval, Timeout.InfiniteTimeSpan);
            Debug.WriteLine("[OcrCoordinator] Started — interval {0}ms (one-shot)", _cycleInterval.TotalMilliseconds);
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

                // T6.3: Повний pipeline для кожного регіону (parallel).
                // Увага: Capture + OCR — CPU/GPU-bound, не на UI thread.
                Parallel.ForEach(regions, region =>
                {
                    try
                    {
                        ProcessRegion(region);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[OcrCoordinator] Region '{0}' failed: {1}", region.Id, ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                // Critical: Timer callback не повинен кидати назовні — це вбиває Timer.
                Debug.WriteLine("[OcrCoordinator] Cycle exception: {0}", ex.Message);
            }
            finally
            {
                // H2: Re-arm one-shot timer для наступного cycle.
                // Якщо Stop() був викликаний під час cycle — не re-arm (IsRunning == false).
                if (!_disposed && Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1)
                {
                    try
                    {
                        _cycleTimer?.Change(_cycleInterval, Timeout.InfiniteTimeSpan);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Timer disposed під час cycle — ignore.
                    }
                }
            }
        }

        /// <summary>
        /// Обробити один регіон: capture → preprocess → OCR → validate → publish event.
        /// H4: Stopwatch timing для кожного кроку (Debug.WriteLine).
        /// </summary>
        private void ProcessRegion(OcrRegion region)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long tCapture, tOcr, tValidate;

            // Крок 1: Capture — BitmapSource → OpenCV Mat (BGRA → BGR).
            // BitmapSource Frozen НЕ потребує Dispose (маркерований через .Freeze()).
            var capturedBitmap = _screenCapture.CaptureRegionAsync(region.ScreenRect).GetAwaiter().GetResult();
            using var inputMat = BitmapSourceToMat(capturedBitmap);
            tCapture = sw.ElapsedMilliseconds;

            // Крок 2: Compute crop fingerprint (для Field Lock).
            var fingerprint = ComputeFingerprint(inputMat);

            // Крок 3: OCR Engine — ПЕРЕДАЄМО ОРИГІНАЛЬНИЙ BGR.
            // Forensic Audit виявив: DefaultImagePipeline бінаризував зображення
            // ПЕРЕД детектором. PaddleOCR DB детектор очікує природнє BGR
            // з градієнтами та кольорами, а не бінарне (0/255).
            // PaddleOcrEngine має власну нормалізацію (ImageNet mean/std) всередині.
            // IImagePipeline залишається як utility для майбутніх спецсценаріїв,
            // але НЕ в основному шляху OCR.
            var rawResult = _ocrEngine.RecognizeAsync(inputMat, region.OcrOptions).GetAwaiter().GetResult();
            tOcr = sw.ElapsedMilliseconds - tCapture;

            // Крок 5: Validate (Confidence + Consensus + Field Lock).
            var validated = _resultValidator.Validate(region.Id, rawResult, fingerprint, region.OcrOptions);
            tValidate = sw.ElapsedMilliseconds - tCapture - tOcr;
            sw.Stop();

            // Профілювання — вивід timing per region per cycle.
            var total = tCapture + tOcr + tValidate;
            System.Diagnostics.Debug.WriteLine(
                "[OcrCoordinator] '{0}': capture={1}ms ocr={2}ms validate={3}ms total={4}ms",
                region.Id, tCapture, tOcr, tValidate, total);

            // Крок 6: Publish event з стабілізованим результатом.
            var regionResult = new OcrRegionResult
            {
                RegionId = region.Id,
                Result = validated,
                CapturedAtUtc = DateTime.UtcNow
            };
            RaiseOcrRegionReady(regionResult);
        }

        /// <summary>
        /// Конвертація BitmapSource (WPF) → OpenCvSharp Mat (BGRA → BGR).
        /// Безпечно: fixed pointer + Mat з вказанням stride (без Marshal.Copy).
        /// </summary>
        private static Mat BitmapSourceToMat(System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var stride = width * (bitmap.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);

            // fixed блокує GC від руху масиву — OpenCV читає напряму.
            // Mat з IntPtr + step=stride — обгортає масив без копіювання.
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

        /// <summary>
        /// Простий fingerprint для Field Lock: hash суми пікселів по block 8×8.
        /// Lightweight (не повний NCC) — достатньо для виявлення змін.
        /// </summary>
        private static long ComputeFingerprint(Mat mat)
        {
            // Resize до 8×8 для стабільного hash.
            using var thumb = new Mat();
            Cv2.Resize(mat, thumb, new Size(8, 8), 0, 0, InterpolationFlags.Area);
            using var gray = new Mat();
            Cv2.CvtColor(thumb, gray, ColorConversionCodes.BGR2GRAY);

            long hash = 0;
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var pixel = gray.At<byte>(y, x);
                    hash = hash * 31 + pixel;
                }
            }
            return Math.Abs(hash);
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
