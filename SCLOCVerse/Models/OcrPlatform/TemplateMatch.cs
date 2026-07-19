using OpenCvSharp;

namespace SCLOCVerse.Models.OcrPlatform
{
    /// <summary>
    /// Один збіг template matching у сцені.
    /// </summary>
    public sealed record TemplateMatch
    {
        /// <summary>Лівий верхній кут збігу (у координатах сцени).</summary>
        public Point TopLeft { get; init; }

        /// <summary>Розмір template, що був застосований (для multi-scale).</summary>
        public Size TemplateSize { get; init; }

        /// <summary>
        /// Нормалізована кореляція [0..1] (NCC = Normalized Cross-Correlation).
        /// 1.0 = ідеальний збіг, 0.85+ = хороша якість для SC HUD icons.
        /// </summary>
        public double Confidence { get; init; }

        /// <summary>Правий нижній кут збігу (для прямокутника виділення).</summary>
        public Point BottomRight => new(TopLeft.X + TemplateSize.Width, TopLeft.Y + TemplateSize.Height);

        /// <summary>Прямокутник збігу (TopLeft + TemplateSize).</summary>
        public Rect Rect => new(TopLeft.X, TopLeft.Y, TemplateSize.Width, TemplateSize.Height);
    }
}
