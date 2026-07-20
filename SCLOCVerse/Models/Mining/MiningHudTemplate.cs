using System.Windows;

namespace SCLOCVerse.Models.Mining
{
    /// <summary>
    /// Шаблон структури Mining HUD — набір регіонів з відносними позиціями.
    ///
    /// Описує, ЯКІ регіони є в HUD і де вони знаходяться відносно самого HUD
    /// (НЕ відносно екрана — відносно HUD bounds).
    ///
    /// Після локалізації HUD (locator знаходить bounds) — регіони обчислюються
    /// з RelativeBounds × HUD bounds = абсолютні координати екрана.
    ///
    /// Архітектурний принцип:
    /// "Screen resolution is never the source of truth. The HUD layout is."
    /// Жодних hardcoded пікселів, лише відносні позиції всередині HUD.
    /// </summary>
    public sealed class MiningHudTemplate
    {
        /// <summary>
        /// Унікальне ім'я шаблону (напр. "default", "scanner-v2").
        /// Дозволяє мати кілька версій шаблонів для різних режимів гри.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>Регіони шаблону (Signature, Distance, Mass, ...).</summary>
        public required IReadOnlyList<MiningHudRegionTemplate> Regions { get; init; }

        /// <summary>
        /// Обчислити абсолютні bounds регіону з relative bounds × HUD bounds.
        /// Працює на будь-якій роздільній здатності (HUD bounds = те, що locator знайшов).
        /// </summary>
        public Rect ComputeRegionBounds(MiningHudRegionTemplate region, Rect hudBounds)
        {
            var x = hudBounds.X + region.RelativeBounds.X * hudBounds.Width;
            var y = hudBounds.Y + region.RelativeBounds.Y * hudBounds.Height;
            var w = region.RelativeBounds.Width * hudBounds.Width;
            var h = region.RelativeBounds.Height * hudBounds.Height;
            return new Rect(x, y, w, h);
        }
    }
}