using OpenCvSharp;

namespace SCLOCVerse.Models.OcrPlatform
{
    /// <summary>
    /// Опції виклику OCR Engine для конкретного регіону.
    /// Наприклад, для digits-only amount — AllowedCharacters = "0123456789".
    /// </summary>
    public sealed record OcrOptions
    {
        /// <summary>
        /// Дозволені символи (whitelist). Якщо null — всі символи дозволені.
        /// Для SC HUD amount: "0123456789". Для material name: літери латиниці.
        /// </summary>
        public string? AllowedCharacters { get; init; }

        /// <summary>
        /// Мінімальна confidence [0..1] для прийняття результату.
        /// Нижчі результати відкидаються (OcrResult.Confidence = 0).
        /// Default: 0.5 — компроміс між precision/recall для SC HUD.
        /// </summary>
        public double MinConfidence { get; init; } = 0.5;

        /// <summary>
        /// Максимальна кількість результатів (text lines), що повертаються.
        /// Default: 1 — для одного регіону очікуємо один рядок (amount, name).
        /// </summary>
        public int MaxResults { get; init; } = 1;

        /// <summary>
        /// Pad зображення перед detection (додає border зboth sides).
        /// Покращує виявлення тексту біля країв (PaddleOCR рекомендує 50).
        /// Default: 50 (стандарт PaddleOCR).
        /// </summary>
        public int Padding { get; init; } = 50;

        /// <summary>
        /// Максимальна довжина сторони зображення після resize (для detection).
        /// Обмежує споживання пам'яті + прискорює inference.
        /// Default: 1024 (стандарт PaddleOCR).
        /// </summary>
        public int MaxSideLen { get; init; } = 1024;

        /// <summary>
        /// Threshold для detection box score [0..1].
        /// Default: 0.5 (стандарт PaddleOCR).
        /// </summary>
        public float BoxScoreThresh { get; init; } = 0.5f;

        /// <summary>
        /// Threshold для detection binary [0..1].
        /// Default: 0.3 (стандарт PaddleOCR).
        /// </summary>
        public float BoxThresh { get; init; } = 0.3f;

        /// <summary>
        /// Розширення text box для розпізнавання (unclip ratio).
        /// Default: 1.6 (стандарт PaddleOCR).
        /// </summary>
        public float UnclipRatio { get; init; } = 1.6f;

        /// <summary>
        /// Опції для digits-only регіонів (напр. material amount на SC HUD).
        /// </summary>
        public static OcrOptions DigitsOnly { get; } = new()
        {
            AllowedCharacters = "0123456789",
            MinConfidence = 0.7,
            MaxResults = 1
        };

        /// <summary>
        /// Опції для digits + comma/period (напр. SC HUD signature "3,385").
        /// Дозволяє цифри, кому та крапку (HUD-формат з роздільником тисяч).
        /// </summary>
        public static OcrOptions DigitsAndSeparators { get; } = new()
        {
            AllowedCharacters = "0123456789,.",
            MinConfidence = 0.7,
            MaxResults = 1
        };

        /// <summary>
        /// Опції для Discovery Mode — повноекранний пошук сигнатур.
        /// Більший MaxResults (50) щоб знайти сигнатуру серед іншого тексту на екрані.
        /// MaxSideLen=2560 — НЕ зменшувати екран (інакше дрібний текст губиться).
        /// </summary>
        public static OcrOptions DiscoveryScan { get; } = new()
        {
            AllowedCharacters = "0123456789,.",
            MinConfidence = 0.5,
            MaxResults = 50,
            MaxSideLen = 2560,
            BoxScoreThresh = 0.3f,
            BoxThresh = 0.2f,
            Padding = 0
        };

        /// <summary>
        /// Опції для Tracking Mode — OCR всередині HUD bounds (мала область).
        /// MaxSideLen=960 — достатньо для ROI, швидше за 2560.
        /// </summary>
        public static OcrOptions TrackingScan { get; } = new()
        {
            AllowedCharacters = "0123456789,.",
            MinConfidence = 0.5,
            MaxResults = 5,
            MaxSideLen = 960,
            BoxScoreThresh = 0.3f,
            BoxThresh = 0.2f,
            Padding = 0
        };

        /// <summary>
        /// Опції для десяткових чисел (напр. Mass "12.34", Distance "10.5").
        /// Дозволяє цифри та крапку.
        /// </summary>
        public static OcrOptions Decimal { get; } = new()
        {
            AllowedCharacters = "0123456789.",
            MinConfidence = 0.6,
            MaxResults = 1
        };

        /// <summary>
        /// Опції для довільного тексту (напр. Resistance "1.21 Gω", Instability "High").
        /// Без фільтрації символів.
        /// </summary>
        public static OcrOptions Alphanumeric { get; } = new()
        {
            AllowedCharacters = null,
            MinConfidence = 0.5,
            MaxResults = 1
        };

        /// <summary>
        /// Опції для текстових регіонів (напр. material name на SC HUD).
        /// </summary>
        public static OcrOptions Default { get; } = new();
    }
}
