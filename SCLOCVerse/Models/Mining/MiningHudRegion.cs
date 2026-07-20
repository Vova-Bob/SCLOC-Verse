using System.Text.RegularExpressions;
using System.Windows;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Models.Mining
{
    /// <summary>
    /// Ідентифікатори відомих регіонів Mining HUD.
    /// </summary>
    public static class MiningHudRegionNames
    {
        public const string Signature = "signature";
        public const string Distance = "distance";
        public const string Mass = "mass";
        public const string Instability = "instability";
        public const string Resistance = "resistance";
    }

    /// <summary>
    /// Поточний стан одного регіону Mining HUD (runtime — оновлюється при кожному OCR cycle).
    /// </summary>
    public sealed class MiningHudRegionState
    {
        /// <summary>Ім'я регіону (напр. "signature", "distance").</summary>
        public required string Name { get; init; }

        /// <summary>Bounds у координатах екрана (абсолютні, обчислені з HUD bounds + template).</summary>
        public Rect Bounds { get; set; }

        /// <summary>
        /// Якість локалізації регіону [0..1] — наскільки добре locator знайшов цей регіон.
        /// Відмінне від OCR confidence: HUD знайдено добре, але OCR може читати погано (і навпаки).
        /// </summary>
        public double RegionQuality { get; set; }

        /// <summary>Confidence останнього OCR [0..1] — наскільки добре розпізнаний текст.</summary>
        public double OcrConfidence { get; set; }

        /// <summary>Останній розпізнаний текст (null = ще не було успішного OCR).</summary>
        public string? LastText { get; set; }

        /// <summary>UTC timestamp останнього успішного оновлення.</summary>
        public DateTime? LastSeenUtc { get; set; }

        /// <summary>Кількість consecutive failures (null result АБО confidence < threshold).</summary>
        public int ConsecutiveFailures { get; set; }
    }

    /// <summary>
    /// Дескриптор (шаблон) одного регіону Mining HUD — статичний опис,
    /// який дозволяє OCR Platform автоматично знати режим розпізнавання.
    ///
    /// Приклад:
    ///   Signature: pattern = ^\d{1,3}(,\d{3})?$, profile = DigitsAndSeparators, required = true
    ///   Distance:  pattern = ^\d+(\.\d+)?\s*(km|m)$, profile = Distance, required = false
    ///   Mass:      pattern = ^\d+(\.\d+)?$, profile = Decimal, required = false
    /// </summary>
    public sealed record MiningHudRegionTemplate
    {
        /// <summary>Ім'я регіону (напр. "signature", "distance").</summary>
        public required string Name { get; init; }

        /// <summary>
        /// Відносна позиція регіону всередині HUD bounds (0.0..1.0).
        /// X, Y, Width, Height — частки від HUD width/height.
        /// Наприклад: (0.05, 0.10, 0.20, 0.08) = лівий-верхній кут, 20% ширини, 8% висоти.
        /// </summary>
        public required Rect RelativeBounds { get; init; }

        /// <summary>
        /// Regex-патерн очікуваного тексту (для валідації OCR результату).
        /// null = будь-який текст (для alphanumerical регіонів).
        /// </summary>
        public Regex? ExpectedPattern { get; init; }

        /// <summary>
        /// Профіль OCR опцій для цього регіону.
        /// Напр. DigitsAndSeparators для Signature, Decimal для Mass.
        /// </summary>
        public required OcrOptions OcrProfile { get; init; }

        /// <summary>Чи обов'язковий цей регіон для Mining HUD (якщо required і не знайдено → HUD вважається втраченим).</summary>
        public bool Required { get; init; }

        /// <summary>Людино-читабельний опис (для UI/debug).</summary>
        public string Description { get; init; } = string.Empty;
    }
}