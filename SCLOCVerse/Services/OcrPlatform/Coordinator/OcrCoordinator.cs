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
        /// Кеш останніх fingerprint per region — для skip OCR якщо екран не змінився.
        /// Key = regionId, Value = fingerprint hash.
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _lastFingerprints = new();

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

            // Крок 2: Compute crop fingerprint (для Field Lock + skip-if-unchanged).
            var fingerprint = ComputeFingerprint(inputMat);

            // Opt 3: Skip OCR якщо екран не змінився — економія CPU коли HUD статичний.
            if (_lastFingerprints.TryGetValue(region.Id, out var lastFp) && fingerprint == lastFp)
            {
                Debug.WriteLine("[OcrCoordinator] '{0}': SKIP (fingerprint unchanged)", region.Id);
                return;
            }
            _lastFingerprints[region.Id] = fingerprint;

            // Pre-filter: не запускати OCR якщо область очевидно порожня.
            // SC HUD сигнатура — білі/бірюзові цифри на темному фоні.
            // Якщо яскравих пікселів < minFraction — пропускаємо OCR, повертаємо Empty.
            // Це зменшує навантаження на ~90% коли Scan HUD не активний (режим V закритий).
            if (!HasContent(inputMat))
            {
                Debug.WriteLine("[OcrCoordinator] '{0}': SKIP (no content — empty region)", region.Id);
                // Публікуємо порожній результат, щоб підписники (MiningRecognitionService)
                // отримали сигнал "сигнатура не виявлена" для переходу в Lost/Discovery.
                var emptyResult = new OcrRegionResult
                {
                    RegionId = region.Id,
                    Result = OcrResult.Empty,
                    CapturedAtUtc = DateTime.UtcNow
                };
                RaiseOcrRegionReady(emptyResult);
                return;
            }

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
        /// Pre-filter: перевіряє, чи містить crop достатньо яскравих пікселів
        /// для потенційного тексту (SC HUD сигнатура — білі/бірюзові цифри
        /// на темному фоні).
        ///
        /// <para><b>Принцип:</b> SC HUD цифри мають яскравість ~180-255 (білі/бірюзові).
        /// Темний фон — ~0-60. Якщо частка яскравих пікселів (&gt; <see cref="ContentBrightnessThreshold"/>)
        /// менша за <see cref="ContentMinFraction"/> — область вважається порожньою,
        /// OCR не запускається.</para>
        ///
        /// <para><b>Thresholds:</b></para>
        /// <list type="bullet">
        /// <item>Brightness &gt; 120 — відсікає темний фон (0-60) та напівтемний (60-120).</item>
        /// <item>Fraction ≥ 0.5% — мінімум 0.5% пікселів мають бути яскравими.
        /// Цифри займають ~1-5% площі HUD регіону; 0.5% — консервативний мінімум.</item>
        /// </list>
        ///
        /// <para><b>Продуктивність:</b> resize до 32×32 → grayscale → підрахунок.
        /// ~0.1мс на Mat 100×30. Набагато дешевше за OCR (~20-50мс).</para>
        ///
        /// <para><b>Тестування:</b> internal static для прямого виклику з
        /// <c>SCLOCVerse.Tests</c> (InternalsVisibleTo).</para>
        /// </summary>
        /// <returns>True якщо область містить потенційний текст; False якщо порожня.</returns>
        internal static bool HasContent(Mat mat)
        {
            // Resize до 32×32 — достатньо для оцінки заповненості, швидко.
            using var thumb = new Mat();
            Cv2.Resize(mat, thumb, new Size(32, 32), 0, 0, InterpolationFlags.Area);
            using var gray = new Mat();
            Cv2.CvtColor(thumb, gray, ColorConversionCodes.BGR2GRAY);

            var brightCount = 0;
            var totalPixels = 32 * 32;
            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    if (gray.At<byte>(y, x) > ContentBrightnessThreshold)
                        brightCount++;
                }
            }

            var fraction = (double)brightCount / totalPixels;
            return fraction >= ContentMinFraction;
        }

        /// <summary>Яскравість пікселя (0-255), вище якої він вважається "яскравим" (можливий текст).</summary>
        internal const int ContentBrightnessThreshold = 120;

        /// <summary>Мінімальна частка яскравих пікселів (0.005 = 0.5%) для визнання області непорожньою.</summary>
        internal const double ContentMinFraction = 0.005;

        /// <summary>
        /// Scan HUD detection: перевіряє, чи є на екрані бірюзові/блакитні пікселі
        /// (характерний колір SC HUD — цифри, іконки, рамки).
        ///
        /// <para><b>Принцип:</b> Star Citizen Scan HUD (режим V) використовує
        /// бірюзовий/блакитний колір для відображення сигнатур. Ping mode (TAB)
        /// не показує числові сигнатури. Якщо немає бірюзових пікселів —
        /// Scan HUD не активний, Discovery не запускається.</para>
        ///
        /// <para><b>Критерій бірюзового:</b> BGR, де Blue &gt; 150, Green &gt; 150, Red &lt; 120.
        /// Це покриває бірюзовий (100, 255, 255), блакитний (0, 200, 255),
        /// світло-блакитний (150, 220, 255).</para>
        ///
        /// <para><b>Threshold:</b> ≥ 5 пікселів з 2304 (64×36) — консервативний мінімум.
        /// Покриває невеликі HUD елементи (одна цифра), відсікає фоновий шум.</para>
        ///
        /// <para><b>Продуктивність:</b> resize 64×36 → підрахунок BGR пікселів.
        /// ~0.05мс на Mat 1920×1080. Значно дешевше за Discovery OCR (~100-200мс).</para>
        ///
        /// <para><b>Використання:</b> викликається в MiningRecognitionService.OnDiscoveryTick
        /// перед повноекранним OCR. Якщо повертає false — Discovery skip, стан = Scanning.</para>
        /// </summary>
        /// <returns>True якщо на екрані є бірюзові пікселі (Scan HUD можливо активний).</returns>
        internal static bool HasCyanContent(Mat mat)
        {
            // Resize до 64×36 — достатньо для виявлення HUD елементів.
            using var thumb = new Mat();
            Cv2.Resize(mat, thumb, new Size(64, 36), 0, 0, InterpolationFlags.Area);

            var cyanCount = 0;
            for (var y = 0; y < 36; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var px = thumb.At<Vec3b>(y, x);
                    // BGR: B=px[0], G=px[1], R=px[2].
                    if (px[0] > 150 && px[1] > 150 && px[2] < 120)
                        cyanCount++;
                }
            }

            return cyanCount >= CyanMinPixels;
        }

        /// <summary>Мінімальна кількість бірюзових пікселів (з 2304) для визнання Scan HUD активним.</summary>
        internal const int CyanMinPixels = 5;

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
