using OpenCvSharp;

namespace SCLOCVerse.Models.OcrPlatform
{
    /// <summary>
    /// Діапазон HSV (Hue, Saturation, Value) для Color Masking.
    /// Використовується для ізоляції кольорового тексту SC HUD (жовтий/помаранчевий).
    ///
    /// OpenCV HSV: H ∈ [0, 179], S ∈ [0, 255], V ∈ [0, 255].
    /// </summary>
    public sealed record HsvRange
    {
        /// <summary>Нижня межа Hue (0-179).</summary>
        public double LowH { get; init; }

        /// <summary>Нижня межа Saturation (0-255).</summary>
        public double LowS { get; init; }

        /// <summary>Нижня межа Value (0-255).</summary>
        public double LowV { get; init; }

        /// <summary>Верхня межа Hue (0-179).</summary>
        public double HighH { get; init; }

        /// <summary>Верхня межа Saturation (0-255).</summary>
        public double HighS { get; init; }

        /// <summary>Верхня межа Value (0-255).</summary>
        public double HighV { get; init; }

        /// <summary>
        /// SC HUD жовтий текст (типовий mining material amount).
        /// H ≈ 20-35 (жовтий-помаранчевий), S > 100 (насичений), V > 100 (яскравий).
        /// </summary>
        public static HsvRange ScYellowHud { get; } = new()
        {
            LowH = 20, LowS = 100, LowV = 100,
            HighH = 35, HighS = 255, HighV = 255
        };

        /// <summary>Конвертує в OpenCV Scalar для Low bound.</summary>
        public Scalar ToLowScalar() => new(LowH, LowS, LowV);

        /// <summary>Конвертує в OpenCV Scalar для High bound.</summary>
        public Scalar ToHighScalar() => new(HighH, HighS, HighV);
    }
}
