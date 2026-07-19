using OpenCvSharp;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Services.OcrPlatform.Pipeline
{
    /// <summary>
    /// Базова реалізація <see cref="IImagePipeline"/> через OpenCvSharp4.
    /// Кроки (через <see cref="ImagePipelineOptions"/>):
    /// <list type="number">
    /// <item>Greyscale (BGR → Gray, 8bpp) — опціонально, default ON.</item>
    /// <item>Resize × N (Lanczos interpolation для маленьких цифр) — опціонально, default ×3.</item>
    /// <item>Adaptive Threshold (Gaussian) — буде додано в T3.3.</item>
    /// </list>
    ///
    /// Вхідний Mat не модифікується. Викликач відповідає за Dispose результату.
    /// </summary>
    public sealed class DefaultImagePipeline : IImagePipeline
    {
        /// <inheritdoc />
        public Mat Process(Mat input, ImagePipelineOptions options)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (input.Empty())
            {
                throw new ArgumentException("Вхідний Mat порожній.", nameof(input));
            }

            // Крок 1: Greyscale. Якщо вхід вже grayscale — просто clone.
            Mat current = options.UseGrayscale
                ? ConvertToGrayscale(input)
                : input.Clone();

            // Крок 2: Resize × N (Lanczos для маленьких цифр — дає різкі краї).
            if (options.ResizeFactor > 1)
            {
                var resized = new Mat();
                Cv2.Resize(
                    src: current,
                    dst: resized,
                    dsize: new Size(current.Cols * options.ResizeFactor, current.Rows * options.ResizeFactor),
                    fx: 0,
                    fy: 0,
                    interpolation: InterpolationFlags.Lanczos4);
                current.Dispose();
                current = resized;
            }

            // Крок 3: Adaptive Threshold — буде додано в T3.3.
            // Поки що залишаємо grayscale/resized Mat як фінальний результат.

            return current;
        }

        /// <summary>
        /// Конвертує вхідний Mat у Grayscale (8bpp).
        /// Якщо вхід вже grayscale — повертає clone (без подвійної конвертації).
        /// </summary>
        private static Mat ConvertToGrayscale(Mat input)
        {
            if (input.Channels() == 1)
            {
                return input.Clone();
            }

            var gray = new Mat();
            Cv2.CvtColor(
                src: input,
                dst: gray,
                code: ColorConversionCodes.BGR2GRAY);
            return gray;
        }
    }
}
