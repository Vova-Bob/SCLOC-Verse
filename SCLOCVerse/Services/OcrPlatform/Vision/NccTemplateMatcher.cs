using OpenCvSharp;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Services.OcrPlatform.Vision
{
    /// <summary>
    /// Реалізація <see cref="ITemplateMatcher"/> через Cv2.MatchTemplate (TM_CCOEFF_NORMED).
    ///
    /// Алгоритм:
    /// 1. cv::matchTemplate(scene, template, result, TM_CCOEFF_NORMED) —Normalized
    ///    Cross-Correlation, стійка до змін освітлення.
    /// 2. cv::threshold(result, result, threshold, 1.0, THRESH_TOZERO) — залишаємо лише високі.
    /// 3. Знаходимо локальні максимуми через Where(result ≥ threshold).
    /// 4. Non-Maximum Suppression (спрощений): скидаємо сусідні пікселі в 0.
    ///
    /// Multi-scale поки НЕ реалізовано — SC HUD icons стабільні для фіксованого resolution.
    /// У Phase 2 (Calibration) додамо multi-scale для різних resolution.
    /// </summary>
    public sealed class NccTemplateMatcher : ITemplateMatcher
    {
        /// <inheritdoc />
        public TemplateMatch[] Find(Mat scene, Mat template, double threshold = 0.85)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(template);

            if (scene.Empty() || template.Empty())
            {
                return [];
            }

            if (template.Cols > scene.Cols || template.Rows > scene.Rows)
            {
                return [];
            }

            // Обидва Mat повинні бути однакового типу (найкраще 8UC1 grayscale).
            // Якщо ні — конвертуємо.
            using var sceneGray = ToGrayscaleIfNeeded(scene);
            using var templateGray = ToGrayscaleIfNeeded(template);

            // Result Mat: розмір (W - w + 1) × (H - h + 1), тип 32FC1.
            using var result = new Mat();
            Cv2.MatchTemplate(
                image: sceneGray,
                templ: templateGray,
                result: result,
                method: TemplateMatchModes.CCoeffNormed);

            // Знаходимо ГЛОБАЛЬНИЙ максимум NCC (найкращий збіг).
            // Для SC HUD icons типово одне входження на скріншоті (Mining UI icon).
            // Multi-instance search (Numerai=FindNonZero) — future, якщо знадобиться.
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

            if (maxVal < threshold)
            {
                return [];
            }

            // Перевіряємо, чи NCC коректний (NaN/-1 можливі для поганих шаблонів).
            if (maxVal < 0 || double.IsNaN(maxVal) || double.IsInfinity(maxVal))
            {
                return [];
            }

            var templateSize = new Size(templateGray.Cols, templateGray.Rows);
            return
            [
                new TemplateMatch
                {
                    TopLeft = maxLoc,
                    TemplateSize = templateSize,
                    Confidence = maxVal
                }
            ];
        }

        /// <summary>
        /// Конвертує Mat у Grayscale 8UC1 якщо він ще не grayscale.
        /// </summary>
        private static Mat ToGrayscaleIfNeeded(Mat input)
        {
            if (input.Channels() == 1 && input.Type() == MatType.CV_8UC1)
            {
                return input.Clone();
            }

            var gray = new Mat();
            Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
            return gray;
        }
    }
}
