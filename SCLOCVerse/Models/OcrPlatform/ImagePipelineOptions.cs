namespace SCLOCVerse.Models.OcrPlatform
{
    /// <summary>
    /// Опції pipeline попередньої обробки зображення для OCR.
    /// Оптимальні для маленьких цифр HUD Star Citizen (10-25px висота):
    /// Greyscale → Resize × 3 → Adaptive Threshold.
    /// </summary>
    public sealed record ImagePipelineOptions
    {
        /// <summary>
        /// Чи конвертувати в Grayscale (8bpp) на старті.
        /// Default: true — усі SC HUD digits монохромні після відділення від фону.
        /// </summary>
        public bool UseGrayscale { get; init; } = true;

        /// <summary>
        /// Множник масштабування для маленьких цифр. 1 = без resize.
        /// Default: 3 — перетворює 12px digit на 36px, що добре розпізнається OCR.
        /// </summary>
        public int ResizeFactor { get; init; } = 3;

        /// <summary>
        /// Чи застосовувати Adaptive Threshold (Gaussian) після resize.
        /// Default: true — бінаризація стійка до змін освітлення HUD (спалахи, вибухи).
        /// </summary>
        public bool UseAdaptiveThreshold { get; init; } = true;

        /// <summary>
        /// Розмір вікна для Adaptive Threshold (повинен бути непарним, ≥3).
        /// Default: 21 — підібрано для SC HUD digits після Resize × 3.
        /// </summary>
        public int AdaptiveBlockSize { get; init; } = 21;

        /// <summary>
        /// Константа C, що віднімається від середнього у Adaptive Threshold.
        /// Default: 5 — типове значення для темного тексту на яскравому фоні.
        /// </summary>
        public double AdaptiveC { get; init; } = 5.0;

        /// <summary>
        /// Опції за замовчуванням для SC HUD digits (без модифікацій).
        /// </summary>
        public static ImagePipelineOptions DefaultForScHud { get; } = new();
    }
}
