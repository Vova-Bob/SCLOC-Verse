namespace SCLOCVerse.Models.Mining
{
    /// <summary>
    /// Режим роботи ROI Resolver для Mining Module.
    ///
    /// Архітектурний принцип:
    /// "Screen resolution is never the source of truth. The HUD layout is."
    ///
    /// Discovery — повний екран, пошук HUD через OCR.
    /// Tracking — локальний ROI навколо знайденого signature.
    /// При втраті HUD — автоматичне повернення до Discovery.
    /// </summary>
    public enum MiningRoiMode
    {
        /// <summary>
        /// Повний екран — система шукає Mining HUD.
        /// Після успішного виявлення signature → Transition to Tracking.
        /// </summary>
        Discovery,

        /// <summary>
        /// Локальний ROI навколо останнього відомого signature (з margin).
        /// Продуктивніший, ніж Discovery (менша область для OCR).
        /// При N consecutive failures → Transition to Discovery.
        /// </summary>
        Tracking
    }
}