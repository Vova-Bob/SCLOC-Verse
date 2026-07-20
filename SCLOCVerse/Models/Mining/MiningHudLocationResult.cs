using System.Windows;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Models.Mining
{
    /// <summary>
    /// Результат локалізації Mining HUD на скріншоті.
    /// Повертається стратегією <see cref="IMiningHudLocatorStrategy"/>.
    ///
    /// Містить: bounds знайденого HUD + confidence локалізації.
    /// Регіони обчислюються з шаблону (RelativeBounds × bounds) — НЕ повертаються locator'ом.
    /// </summary>
    public sealed record MiningHudLocationResult
    {
        /// <summary>Bounds знайденого Mining HUD у координатах екрана (пікселі).</summary>
        public required Rect HudBounds { get; init; }

        /// <summary>
        /// Confidence локалізації [0..1] — наскільки впевнено стратегія знайшла HUD.
        /// Відмінне від OCR confidence: це якість локалізації, а не розпізнавання тексту.
        /// </summary>
        public double Confidence { get; init; }

        /// <summary>Ім'я стратегії, яка знайшла HUD (для debug).</summary>
        public required string StrategyName { get; init; }

        /// <summary>
        /// Додаткові метадані локалізації (напр. знайдені color blobs, matched area size).
        /// null — якщо стратегія не надає метаданих.
        /// </summary>
        public string? Details { get; init; }
    }
}