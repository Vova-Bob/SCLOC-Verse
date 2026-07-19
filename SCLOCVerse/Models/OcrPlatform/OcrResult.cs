using OpenCvSharp;

namespace SCLOCVerse.Models.OcrPlatform
{
    /// <summary>
    /// Один розпізнаний текстовий блок (text line + bounds + confidence).
    /// </summary>
    public sealed record OcrMatch
    {
        /// <summary>Розпізнаний текст (може бути відфільтрований за AllowedCharacters).</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>Середня confidence [0..1] по символах (CRNN softmax mean).</summary>
        public double Confidence { get; init; }

        /// <summary>Прямокутник розпізнаного блоку у координатах вхідного зображення.</summary>
        public Rect Bounds { get; init; }

        /// <summary>Чи всі символи Text ∈ AllowedCharacters (якщо задано).</summary>
        public bool Filtered { get; init; }
    }

    /// <summary>
    /// Результат OCR-розпізнавання одного регіону.
    /// </summary>
    public sealed record OcrResult
    {
        /// <summary>Усі розпізнані блоки (text lines), відсортовані за confidence descending.</summary>
        public IReadOnlyList<OcrMatch> Matches { get; init; } = [];

        /// <summary>Найкращий збіг (Matches[0]) або null, якщо Match порожній.</summary>
        public OcrMatch? BestMatch => Matches.Count > 0 ? Matches[0] : null;

        /// <summary>Сирий текст (об'єднаний через \n) — для дебагу.</summary>
        public string RawText => string.Join("\n", Matches.Select(m => m.Text));

        /// <summary>Confidence найкращого збігу або 0.</summary>
        public double Confidence => BestMatch?.Confidence ?? 0;

        /// <summary>Чи був знайдений хоч один валідний збіг (з confidence ≥ MinConfidence).</summary>
        public bool HasResult => Matches.Count > 0;

        /// <summary>Порожній результат (нічого не розпізнано).</summary>
        public static OcrResult Empty { get; } = new();
    }
}
