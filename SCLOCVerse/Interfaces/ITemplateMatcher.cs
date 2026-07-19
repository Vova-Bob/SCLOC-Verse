using OpenCvSharp;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Пошук template (напр. іконки HUD) у сцені через Normalized Cross-Correlation.
    /// Використовується для локалізації HUD elements (Mining UI icons, label rows,
    /// panel anchors) перед OCR.
    /// </summary>
    public interface ITemplateMatcher
    {
        /// <summary>
        /// Знайти всі входження template у сцені з confidence ≥ threshold.
        /// </summary>
        /// <param name="scene">Сцена для пошуку (повний скріншот або регіон).</param>
        /// <param name="template">Шаблон для пошуку (напр. іконка Mining UI).</param>
        /// <param name="threshold">Мінімальна NCC confidence [0..1]. Default рекомендований 0.85.</param>
        /// <returns>Масив збігів (може бути порожнім, якщо нічого не знайдено).</returns>
        TemplateMatch[] Find(Mat scene, Mat template, double threshold = 0.85);
    }
}
