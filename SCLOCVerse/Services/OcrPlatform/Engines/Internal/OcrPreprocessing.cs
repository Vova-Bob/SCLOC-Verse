using OpenCvSharp;

namespace SCLOCVerse.Services.OcrPlatform.Engines.Internal
{
    /// <summary>
    /// Утиліти попередньої обробки зображень для PaddleOCR inference.
    /// ImageNet normalization + BGR→RGB + NCHW tensor packing.
    /// </summary>
    internal static class OcrPreprocessing
    {
        // ImageNet normalization constants (стандарт PaddleOCR).
        // Mean = [0.485, 0.456, 0.406] × 255
        private static readonly float[] MeanValues = { 0.485f * 255f, 0.456f * 255f, 0.406f * 255f };
        // Std = [0.229, 0.224, 0.225] → 1/(Std×255)
        private static readonly float[] NormValues =
            { 1f / (0.229f * 255f), 1f / (0.224f * 255f), 1f / (0.225f * 255f) };

        /// <summary>
        /// Resize зображення до target height × dynamic width (збереження пропорцій).
        /// Для CRNN recognizer — height фіксований (48 для PP-OCRv5_mobile_rec).
        /// </summary>
        public static Mat ResizeForRecognition(Mat input, int targetHeight)
        {
            var ratio = (float)targetHeight / input.Rows;
            var targetWidth = (int)(input.Cols * ratio);
            if (targetWidth < 1) targetWidth = 1;

            var resized = new Mat();
            Cv2.Resize(input, resized, new Size(targetWidth, targetHeight), 0, 0, InterpolationFlags.Linear);
            return resized;
        }

        /// <summary>
        /// Нормалізація для Detection model (зберігає BGR порядок PaddleOCR).
        /// Повертає NCHW tensor розміром [1, 3, H, W].
        /// </summary>
        public static float[] NormalizeForDetection(Mat srcBgr)
        {
            return SubtractMeanNormalize(srcBgr, MeanValues, NormValues, bgrToRgb: false);
        }

        /// <summary>
        /// Нормалізація для Recognition model.
        /// PP-OCRv5_rec очікує RGB порядок каналів (як усі ImageNet models).
        /// </summary>
        public static float[] NormalizeForRecognition(Mat srcBgr)
        {
            return SubtractMeanNormalize(srcBgr, MeanValues, NormValues, bgrToRgb: true);
        }

        /// <summary>
        /// Core preprocessing: subtract mean + divide by std + pack as NCHW tensor.
        /// Повертає масив [3 * H * W] у порядку channel-row-col (NCHW без batch dim).
        /// </summary>
        private static float[] SubtractMeanNormalize(Mat input, float[] mean, float[] norm, bool bgrToRgb)
        {
            // Конвертуємо в 3-channel BGR якщо потрібно.
            Mat bgr;
            if (input.Channels() == 1)
            {
                bgr = new Mat();
                Cv2.CvtColor(input, bgr, ColorConversionCodes.GRAY2BGR);
            }
            else if (input.Channels() == 4)
            {
                bgr = new Mat();
                Cv2.CvtColor(input, bgr, ColorConversionCodes.BGRA2BGR);
            }
            else
            {
                bgr = input.Clone();
            }

            try
            {
                var rows = bgr.Rows;
                var cols = bgr.Cols;
                var tensor = new float[3 * rows * cols];

                // NCHW: [c, y, x]. Для channel 0 → Blue (або Red якщо RGB), etc.
                // bgrToRgb = true означає міняємо порядок каналів: BGR → RGB.
                var channelOrder = bgrToRgb ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 }; // 0=B,1=G,2=R

                for (var y = 0; y < rows; y++)
                {
                    for (var x = 0; x < cols; x++)
                    {
                        var pixel = bgr.At<Vec3b>(y, x);
                        for (var c = 0; c < 3; c++)
                        {
                            var srcChannel = channelOrder[c];
                            var pixelValue = pixel[srcChannel];
                            var normalized = (pixelValue - mean[c]) * norm[c];
                            tensor[c * rows * cols + y * cols + x] = normalized;
                        }
                    }
                }

                return tensor;
            }
            finally
            {
                if (bgr != input)
                {
                    bgr.Dispose();
                }
            }
        }

        /// <summary>
        /// Зробити padding зображення (додати border по всіх сторонах).
        /// Використовується для detection, щоб виявити текст біля країв.
        /// </summary>
        public static Mat MakePadding(Mat src, int padding)
        {
            if (padding <= 0) return src.Clone();

            var padded = new Mat();
            Cv2.CopyMakeBorder(
                src: src,
                dst: padded,
                top: padding,
                bottom: padding,
                left: padding,
                right: padding,
                borderType: BorderTypes.Constant,
                value: new Scalar(0, 0, 0));
            return padded;
        }
    }
}
