using OpenCvSharp;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Pipeline попередньої обробки зображення перед подачею в OCR Engine.
    /// Відповідає за: Greyscale → Resize → Adaptive Threshold (опційно).
    /// НЕ відповідає за capture (це <see cref="IScreenCaptureService"/>)
    /// та за OCR (це <c>IOcrEngine</c>).
    /// </summary>
    public interface IImagePipeline
    {
        /// <summary>
        /// Обробити вхідне зображення згідно з опціями.
        /// Повертає НОВИЙ Mat — вхідний не модифікується.
        /// </summary>
        /// <param name="input">Вхідне зображення (будь-який формат OpenCV).</param>
        /// <param name="options">Опції обробки.</param>
        /// <returns>Новий Mat із попередньо обробленим зображенням (викликач відповідає за Dispose).</returns>
        Mat Process(Mat input, ImagePipelineOptions options);
    }
}
