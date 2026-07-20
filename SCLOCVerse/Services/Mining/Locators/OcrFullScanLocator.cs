using OpenCvSharp;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Mining;
using SCLOCVerse.Models.OcrPlatform;
using System.Diagnostics;

namespace SCLOCVerse.Services.Mining.Locators
{
    /// <summary>
    /// Робоча стратегія локалізації HUD через повноекранний OCR + DB lookup.
    ///
    /// Принцип: HUD знаходять не за кольором/формою/шаблоном, а за ЗМІСТОМ —
    /// шукаємо відомі сигнатури (Riccite "3,385", "6,770", ...) безпосередньо
    /// через OCR. Якщо знайдено — HUD локалізовано, повертаємо bounds.
    ///
    /// Переваги:
    /// - Не залежить від кольору (gamma/HDR/Reshade/тема HUD — байдуже).
    /// - Не залежить від роздільної здатності/DPI (OCR інваріантний).
    /// - Не потребує еталонного шаблону/моделі — лише DB signature.
    /// - Працює на будь-якій панелі, де є цифри signature.
    ///
    /// Недоліки:
    /// - Повноекранний OCR дорожчий за template/feature matching (~3с на 1440p).
    /// Але це Discovery — запускається лише для ПОШУКУ HUD, після чого система
    /// переходить у Tracking (ROI) і працює швидко.
    ///
    /// Архітектура: IMiningHudLocatorStrategy.Locate(Mat) → MiningHudLocationResult?
    /// </summary>
    public sealed class OcrFullScanLocator : IMiningHudLocatorStrategy
    {
        private readonly IOcrEngine _ocrEngine;
        private readonly IMiningSignatureDatabase _signatureDb;

        /// <inheritdoc />
        public string Name => "OcrFullScan";

        public OcrFullScanLocator(IOcrEngine ocrEngine, IMiningSignatureDatabase signatureDb)
        {
            ArgumentNullException.ThrowIfNull(ocrEngine);
            ArgumentNullException.ThrowIfNull(signatureDb);
            _ocrEngine = ocrEngine;
            _signatureDb = signatureDb;
        }

        /// <inheritdoc />
        public MiningHudLocationResult? Locate(Mat screenshot)
        {
            ArgumentNullException.ThrowIfNull(screenshot);
            if (screenshot.Empty()) return null;

            try
            {
                // OCR повного екрана з опціями Discovery (MaxResults=50, lower confidence).
                var result = _ocrEngine.RecognizeAsync(screenshot, OcrOptions.DiscoveryScan)
                    .GetAwaiter().GetResult();

                if (result?.Matches is null || result.Matches.Count == 0) return null;

                // Шукаємо перший match, що відповідає відомій сигнатурі.
                foreach (var match in result.Matches)
                {
                    if (string.IsNullOrEmpty(match.Text)) continue;

                    // Спробувати безпосередньо (HUD-формат "3,385").
                    var material = _signatureDb.Lookup(match.Text);
                    if (material is not null)
                    {
                        return BuildResult(match, screenshot);
                    }

                    // Спробувати нормалізувати (прибрати коми/пробіли → "3385").
                    var normalized = match.Text.Replace(",", "").Replace(" ", "");
                    if (normalized != match.Text)
                    {
                        material = _signatureDb.Lookup(normalized);
                        if (material is not null)
                        {
                            return BuildResult(match, screenshot);
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[OcrFullScanLocator] Exception: {0}", ex.Message);
                return null;
            }
        }

        private static MiningHudLocationResult BuildResult(OcrMatch match, Mat screenshot)
        {
            // Bounds знайденого тексту — це і є HUD signature bounds.
            // Оскільки OCR знайшов текст на повному скріншоті, координати match.Bounds
            // — абсолютні координати екрана (OpenCvSharp.Rect → System.Windows.Rect).
            var bounds = new System.Windows.Rect(match.Bounds.X, match.Bounds.Y,
                match.Bounds.Width, match.Bounds.Height);

            // Розширити bounds з невеликим padding, щоб покрити весь HUD-блок
            // (signature + distance/mass/instability/resistance зазвичай нижче).
            // Padding — як фракція висоти тексту (НЕ hardcoded пікселі).
            var padX = match.Bounds.Height * 2;  // ~2 висоти тексту зліва/справа
            var padTop = match.Bounds.Height;    // ~1 висота зверху
            var padBottom = match.Bounds.Height * 8; // ~8 висот вниз (інші регіони)

            var x = Math.Max(0, bounds.X - padX);
            var y = Math.Max(0, bounds.Y - padTop);
            var right = Math.Min(screenshot.Width, bounds.Right + padX);
            var bottom = Math.Min(screenshot.Height, bounds.Bottom + padBottom);

            var hudBounds = new System.Windows.Rect(x, y, right - x, bottom - y);

            return new MiningHudLocationResult
            {
                HudBounds = hudBounds,
                Confidence = match.Confidence,
                StrategyName = "OcrFullScan",
                Details = $"text='{match.Text}' match={bounds}"
            };
        }
    }
}