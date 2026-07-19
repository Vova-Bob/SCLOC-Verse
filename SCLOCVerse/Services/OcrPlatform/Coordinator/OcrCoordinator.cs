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
        }

        /// <summary>
        /// Обробити один регіон: capture → preprocess → OCR → validate → publish event.
        /// </summary>
        private void ProcessRegion(OcrRegion region)
        {
            // Крок 1: Capture — BitmapSource → OpenCV Mat (BGRA → BGR).
            // BitmapSource Frozen НЕ потребує Dispose (маркерований через .Freeze()).
            var capturedBitmap = _screenCapture.CaptureRegionAsync(region.ScreenRect).GetAwaiter().GetResult();
            using var inputMat = BitmapSourceToMat(capturedBitmap);

            // Крок 2: Compute crop fingerprint (для Field Lock).
            // Простий hash: сума байтів у підрядку для швидкості.
            var fingerprint = ComputeFingerprint(inputMat);

            // Крок 3: Preprocess — Greyscale → Resize → Adaptive Threshold.
            using var preprocessed = _imagePipeline.Process(inputMat, region.PipelineOptions);

            // Крок 4: OCR Engine.
            // H1 WARNING: preprocessed Mat передається в RecognizeAsync (Task.Run).
            // Поточний код використовує .GetAwaiter().GetResult() (синхронне блокування),
            // тому Mat НЕ звільниться до завершення OCR — використання безпечне.
            // Якщо колись змінено на await — Mat буде disposed під час inference.
            // У цьому випадку: скопіювати Mat перед передачею: using var ocrMat = preprocessed.Clone();
            var rawResult = _ocrEngine.RecognizeAsync(preprocessed, region.OcrOptions).GetAwaiter().GetResult();

            // Крок 5: Validate (Confidence + Consensus + Field Lock).
            var validated = _resultValidator.Validate(region.Id, rawResult, fingerprint, region.OcrOptions);

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
        /// Frozen BitmapSource → copy pixel data → wrap у Mat через unsafe pointer.
        /// </summary>
        private static Mat BitmapSourceToMat(System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var stride = width * (bitmap.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);

            // Створюємо Mat 8UC4 вручну з піксельних даних.
            var bgraMat = new Mat(height, width, MatType.CV_8UC4);
            Marshal.Copy(pixels, 0, bgraMat.Data, pixels.Length);

            // Переводимо в BGR (відкидаємо alpha).
            var bgr = new Mat();
            Cv2.CvtColor(bgraMat, bgr, ColorConversionCodes.BGRA2BGR);
            bgraMat.Dispose();
            return bgr;
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
