using System.Windows;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Models.OcrPlatform
{
    /// <summary>
    /// Реєстрація одного регіону екрана для OCR Platform.
    /// Приклад: "mining.material_amount" — регіон 80×24 px з digits-only фільтром.
    /// </summary>
    public sealed record OcrRegion
    {
        /// <summary>Унікальний ідентифікатор регіону (напр. "mining.material_amount").</summary>
        public required string Id { get; init; }

        /// <summary>Людино-читабельне ім'я (для UI / debug логів).</summary>
        public required string Name { get; init; }

        /// <summary>Прямокутник на екрані у фізичних пікселях.</summary>
        public required Rect ScreenRect { get; init; }

        /// <summary>Опції OCR для цього регіону (digits-only, confidence threshold, etc.).</summary>
        public required OcrOptions OcrOptions { get; init; }

        /// <summary>Опції попередньої обробки (greyscale, resize, threshold).</summary>
        public ImagePipelineOptions PipelineOptions { get; init; } = ImagePipelineOptions.DefaultForScHud;

        /// <summary>Чи активний регіон (Coordinator пропускає disabled).</summary>
        public bool Enabled { get; init; } = true;
    }

    /// <summary>
    /// Результат OCR для конкретного регіону.
    /// </summary>
    public sealed record OcrRegionResult
    {
        /// <summary>Ідентифікатор регіону (для matchup з OcrRegion.Id).</summary>
        public required string RegionId { get; init; }

        /// <summary>Стабілізований результат (після ResultValidator).</summary>
        public required OcrResult Result { get; init; }

        /// <summary>UTC timestamp розпізнавання.</summary>
        public required DateTime CapturedAtUtc { get; init; }
    }
}
