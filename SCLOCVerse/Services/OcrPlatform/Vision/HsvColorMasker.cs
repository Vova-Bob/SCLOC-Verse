using OpenCvSharp;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Services.OcrPlatform.Vision
{
    /// <summary>
    /// Реалізація <see cref="IColorMasker"/> через OpenCvSharp4.
    /// Конвертує BGR → HSV, потім Cv2.InRange з діапазоном HsvRange.
    /// </summary>
    public sealed class HsvColorMasker : IColorMasker
    {
        /// <inheritdoc />
        public Mat Mask(Mat inputBgr, HsvRange range)
        {
            ArgumentNullException.ThrowIfNull(inputBgr);
            ArgumentNullException.ThrowIfNull(range);
            if (inputBgr.Empty())
            {
                throw new ArgumentException("Вхідний Mat порожній.", nameof(inputBgr));
            }

            // Конвертація BGR → HSV. HSV має 3 канали: H ∈ [0,179], S ∈ [0,255], V ∈ [0,255].
            var hsv = new Mat();
            Cv2.CvtColor(inputBgr, hsv, ColorConversionCodes.BGR2HSV);

            // InRange повертає 8UC1 маску: 255 де в межах, 0 поза.
            var mask = new Mat();
            Cv2.InRange(
                src: hsv,
                lowerb: range.ToLowScalar(),
                upperb: range.ToHighScalar(),
                dst: mask);

            hsv.Dispose();
            return mask;
        }
    }
}
