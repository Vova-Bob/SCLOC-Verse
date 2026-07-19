using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Валідатор результатів OCR Platform.
    /// Стабілізує висновки OCR через 4 шари (SC-Toolbox pattern):
    /// <list type="number">
    /// <item>Confidence filter (відкидає low-confidence matches).</item>
    /// <item>Multi-frame consensus (5-frame rolling buffer, majority vote).</item>
    /// <item>Field-value locking (skip OCR поки crop не змінюється).</item>
    /// <item>Lexicon tiebreaker (future — known material codes preferred).</item>
    /// </list>
    ///
    /// Головна мета: "show nothing before wrong number" — краще показати
    /// стабільний минулий результат, ніж нестабільний поточний.
    /// </summary>
    public interface IResultValidator
    {
        /// <summary>
        /// Валідувати результат OCR для конкретного регіону.
        /// Має бути stateful per region — внутри тримає consensus buffer + lock для regionId.
        /// </summary>
        /// <param name="regionId">Ідентифікатор регіону (для tracking buffer/lock).</param>
        /// <param name="result">Сирий результат OCR Engine.</param>
        /// <param name="cropFingerprint">Optional fingerprint поточного crop (для field lock). Null = lock disabled.</param>
        /// <param name="options">Опції розпізнавання (містять MinConfidence).</param>
        /// <returns>Стабілізований результат (може бути порожнім, якщо низька впевненість).</returns>
        OcrResult Validate(string regionId, OcrResult result, long? cropFingerprint, OcrOptions options);
    }
}
