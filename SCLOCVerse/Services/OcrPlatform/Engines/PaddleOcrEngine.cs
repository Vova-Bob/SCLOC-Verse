using OpenCvSharp;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.OcrPlatform;
using SCLOCVerse.Services.OcrPlatform.Engines.Internal;
using System.IO;

namespace SCLOCVerse.Services.OcrPlatform.Engines
{
    /// <summary>
    /// Реалізація <see cref="IOcrEngine"/> через прямий ONNX Runtime inference
    /// PaddleOCR PP-OCRv5/v6 моделей (Detection + Recognition).
    ///
    /// Skip AngleNet — SC HUD текст завжди вертикальний (0°).
    ///
    /// Pipeline:
    /// 1. Padding (додати border для тексту біля країв)
    /// 2. PaddleDetector.Detect → список DetectedTextBoxes
    /// 3. Для кожного боксу: crop (warpPerspective) → PaddleRecognizer.Recognize
    /// 4. Фільтрація за AllowedCharacters + MinConfidence
    /// 5. Повернути OcrResult з matches
    /// </summary>
    public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
    {
        private readonly string _detModelPath;
        private readonly string _recModelPath;
        private readonly string _dictPath;
        private readonly object _loadLock = new();
        private PaddleDetector? _detector;
        private PaddleRecognizer? _recognizer;
        private bool _disposed;

        /// <summary>
        /// Створити PaddleOcrEngine з конкретними шляхами до моделей.
        /// Моделі завантажуються LAZY — при першому RecognizeAsync, не в конструкторі.
        /// Це економить ~500МБ пам'яті коли сканер вимкнено.
        /// </summary>
        public PaddleOcrEngine(string detModelPath, string recModelPath, string dictPath)
        {
            ArgumentNullException.ThrowIfNull(detModelPath);
            ArgumentNullException.ThrowIfNull(recModelPath);
            ArgumentNullException.ThrowIfNull(dictPath);

            if (!File.Exists(detModelPath))
                throw new FileNotFoundException("Detection model not found", detModelPath);
            if (!File.Exists(recModelPath))
                throw new FileNotFoundException("Recognition model not found", recModelPath);
            if (!File.Exists(dictPath))
                throw new FileNotFoundException("Dictionary not found", dictPath);

            _detModelPath = detModelPath;
            _recModelPath = recModelPath;
            _dictPath = dictPath;
        }

        /// <summary>
        /// Завантажити ONNX моделі (lazy — при першому використанні).
        /// </summary>
        private void EnsureModelsLoaded()
        {
            if (_detector is not null && _recognizer is not null) return;

            lock (_loadLock)
            {
                if (_detector is not null && _recognizer is not null) return;

                _detector = new PaddleDetector(_detModelPath);
                _recognizer = new PaddleRecognizer(_recModelPath, _dictPath);
            }
        }

        /// <summary>
        /// Вивантажити ONNX моделі (звільнити ~500МБ).
        /// Наступний RecognizeAsync завантажить їх знову.
        /// </summary>
        public void UnloadModels()
        {
            lock (_loadLock)
            {
                _detector?.Dispose();
                _recognizer?.Dispose();
                _detector = null;
                _recognizer = null;
            }
        }

        /// <inheritdoc />
        public string Name => "PaddleOCR PP-OCRv6 (Direct ONNX Runtime)";

        /// <inheritdoc />
        public Task<OcrResult> RecognizeAsync(Mat input, OcrOptions options, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(options);
            ObjectDisposedException.ThrowIf(_disposed, this);

            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                // Lazy load ONNX моделей при першому використанні.
                EnsureModelsLoaded();

                // Крок 1: Padding — додаємо border для кращої детекції тексту біля країв.
                using var padded = options.Padding > 0
                    ? OcrPreprocessing.MakePadding(input, options.Padding)
                    : input.Clone();

                ct.ThrowIfCancellationRequested();

                // Крок 2: Detection.
                var boxes = _detector.Detect(
                    padded,
                    maxSideLen: options.MaxSideLen,
                    boxScoreThresh: options.BoxScoreThresh,
                    boxThresh: options.BoxThresh,
                    unclipRatio: options.UnclipRatio);

                if (boxes.Count == 0)
                {
                    return OcrResult.Empty;
                }

                ct.ThrowIfCancellationRequested();

                // Крок 2.5: Pre-filter — відсіяти блоки, що не схожі на цільовий текст.
                // SC HUD signature: горизонтальний текст цифр, ratio 2:1..15:1.
                // Це зменшує кількість recognition викликів у 5-10 разів.
                var filteredBoxes = options.AllowedCharacters is not null
                    ? boxes.Where(b => IsLikelyDigitBox(b.BoundingRect)).ToList()
                    : boxes;

                if (filteredBoxes.Count == 0)
                {
                    return OcrResult.Empty;
                }

                // Крок 3: Recognition per box (parallel через PLINQ).
                var matches = filteredBoxes
                    .AsParallel()
                    .WithCancellation(ct)
                    .Select(box =>
                    {
                        // Crop text region via bounding rect (simpler than warpPerspective).
                        // Для SC HUD (горизонтальний текст) bounding rect достатньо.
                        var boundingRect = box.BoundingRect;

                        // Перевірка меж (після padding).
                        var cropRect = new Rect(
                            Math.Max(0, boundingRect.X),
                            Math.Max(0, boundingRect.Y),
                            Math.Min(boundingRect.Width, padded.Cols - boundingRect.X),
                            Math.Min(boundingRect.Height, padded.Rows - boundingRect.Y));
                        if (cropRect.Width <= 0 || cropRect.Height <= 0)
                        {
                            return null;
                        }

                        using var crop = new Mat(padded, cropRect);
                        var recognized = _recognizer.Recognize(crop);

                        // Скидаємо padding offset (повертаємо координати до оригіналу).
                        var originalRect = new Rect(
                            cropRect.X - options.Padding,
                            cropRect.Y - options.Padding,
                            cropRect.Width,
                            cropRect.Height);

                        return new { Recognized = recognized, Bounds = originalRect };
                    })
                    .Where(x => x != null && !string.IsNullOrEmpty(x.Recognized.Text))
                    .Select(x => CreateMatch(x!.Recognized, x.Bounds, options))
                    .Where(m => m.Confidence >= options.MinConfidence)
                    .OrderByDescending(m => m.Confidence)
                    .Take(options.MaxResults)
                    .ToList();

                return new OcrResult { Matches = matches };
            }, ct);
        }

        /// <summary>
        /// Створити OcrMatch з застосуванням AllowedCharacters filter.
        /// </summary>
        private static OcrMatch CreateMatch(RecognizedText recognized, Rect bounds, OcrOptions options)
        {
            var text = recognized.Text;
            var filtered = false;

            if (!string.IsNullOrEmpty(options.AllowedCharacters))
            {
                var filteredText = new string(text.Where(c => options.AllowedCharacters.Contains(c)).ToArray());
                filtered = filteredText != text;
                text = filteredText;
            }

            return new OcrMatch
            {
                Text = text,
                Confidence = recognized.Confidence,
                Bounds = bounds,
                Filtered = filtered
            };
        }

        /// <summary>
        /// Відфільтрувати блоки, що не схожі на цільовий текст (цифри).
        /// SC HUD: горизонтальний текст, ratio 2:1..15:1, висота 8..60px.
        /// Цей фільтр зменшує кількість recognition викликів у 5-10 разів.
        /// </summary>
        private static bool IsLikelyDigitBox(Rect rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return false;

            var ratio = (double)rect.Width / rect.Height;
            return ratio is >= 1.5 and <= 20.0    // горизонтальний текст
                && rect.Height is >= 8 and <= 60   // розмір цифр
                && rect.Width is >= 15 and <= 300; // довжина тексту
        }

        public void Dispose()
        {
            if (_disposed) return;
            UnloadModels();
            _disposed = true;
        }
    }
}
