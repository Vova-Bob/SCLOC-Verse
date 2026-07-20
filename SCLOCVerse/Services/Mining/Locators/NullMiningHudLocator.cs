using OpenCvSharp;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Mining;

namespace SCLOCVerse.Services.Mining.Locators
{
    /// <summary>
    /// Null-object реалізація <see cref="IMiningHudLocatorStrategy"/>.
    ///
    /// Завжди повертає null — HUD не знайдено.
    /// Discovery Mode залишається активним, але ніколи не переходить у Tracking.
    ///
    /// Призначення: дозволяє системі компілюватись і запускатись, поки не прийнято
    /// архітектурне рішення щодо конкретної стратегії локалізації HUD.
    ///
    /// Це НЕ stub (не заглушка з hardcoded результатом) — це легальний null-object,
    /// що повертає "не знайдено" (семантично: стратегія не обрана, пошук неможливий).
    /// </summary>
    public sealed class NullMiningHudLocator : IMiningHudLocatorStrategy
    {
        /// <inheritdoc />
        public string Name => "NullLocator (no strategy selected)";

        /// <inheritdoc />
        public MiningHudLocationResult? Locate(Mat screenshot) => null;
    }
}