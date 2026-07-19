using OpenCvSharp;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Універсальний контракт OCR Engine.
    /// Приймає попередньо оброблене зображення (через <see cref="IImagePipeline"/>),
    /// повертає розпізнаний текст + confidence per match.
    ///
    /// Реалізації:
    /// <list type="bullet">
    /// <item><c>PaddleOcrEngine</c> — PaddleOCR PP-OCRv5/v6 через ONNX Runtime (primary).</item>
    /// <item>Future: <c>CustomTrainedEngine</c> — fine-tuned модель під SC HUD.</item>
    /// </list>
    /// </summary>
    public interface IOcrEngine
    {
        /// <summary>Назва рушія (для логування / Settings UI).</summary>
        string Name { get; }

        /// <summary>
        /// Розпізнати текст у попередньо обробленому зображенні.
        /// </summary>
        /// <param name="preprocessed">Вхідне зображення (BGR або Grayscale, як вимагає рушій).</param>
        /// <param name="options">Опції розпізнавання (whitelist, thresholds).</param>
        /// <param name="ct">Токен скасування.</param>
        /// <returns>OcrResult з matches (може бути порожнім, якщо нічого не знайдено).</returns>
        Task<OcrResult> RecognizeAsync(Mat preprocessed, OcrOptions options, CancellationToken ct = default);
    }
}
