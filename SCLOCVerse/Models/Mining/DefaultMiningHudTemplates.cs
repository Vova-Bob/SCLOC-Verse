using System.Text.RegularExpressions;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Models.Mining
{
    /// <summary>
    /// Шаблони Mining HUD Star Citizen.
    ///
    /// Відносні позиції регіонів (0.0..1.0) — частки від HUD width/height.
    /// НЕ прив'язані до роздільної здатності екрана.
    ///
    /// Стандартний Mining Scanner (права панель):
    /// ┌─────────────────────────┐
    /// │ Signature   3,385       │  ← верх панелі
    /// │ Distance    10.5km      │
    /// │ Mass        12.34       │
    /// │ Instability 1.21        │
    /// │ Resistance  1.21 Gω     │  ← низ панелі
    /// └─────────────────────────┘
    ///
    /// TODO: RelativeBounds — PLACEHOLDER, потребує калібрування на реальних SC скріншотах.
    /// Зараз архітектура готова; точні значення уточнюються після тестування.
    /// </summary>
    public static class DefaultMiningHudTemplates
    {
        /// <summary>
        /// Стандартний шаблон Mining Scanner (права панель HUD).
        /// </summary>
        public static MiningHudTemplate Default { get; } = new()
        {
            Name = "default",
            Regions =
            [
                new MiningHudRegionTemplate
                {
                    Name = MiningHudRegionNames.Signature,
                    // PLACEHOLDER: ~10% від top, ліва частина, ~30% ширини.
                    RelativeBounds = new(0.05, 0.08, 0.35, 0.06),
                    ExpectedPattern = new(@"^\d{1,3}(,\d{3})?$", RegexOptions.Compiled),
                    OcrProfile = OcrOptions.DigitsAndSeparators,
                    Required = true,
                    Description = "Material signature (напр. 3,385)"
                },
                new MiningHudRegionTemplate
                {
                    Name = MiningHudRegionNames.Distance,
                    RelativeBounds = new(0.05, 0.20, 0.30, 0.06),
                    ExpectedPattern = new(@"^\d+(\.\d+)?\s*(km|m)?$", RegexOptions.Compiled),
                    OcrProfile = OcrOptions.Decimal,
                    Required = false,
                    Description = "Distance (напр. 10.5km)"
                },
                new MiningHudRegionTemplate
                {
                    Name = MiningHudRegionNames.Mass,
                    RelativeBounds = new(0.05, 0.32, 0.25, 0.06),
                    ExpectedPattern = new(@"^\d+(\.\d+)?$", RegexOptions.Compiled),
                    OcrProfile = OcrOptions.Decimal,
                    Required = false,
                    Description = "Mass (напр. 12.34)"
                },
                new MiningHudRegionTemplate
                {
                    Name = MiningHudRegionNames.Instability,
                    RelativeBounds = new(0.05, 0.44, 0.25, 0.06),
                    ExpectedPattern = new(@"^\d+(\.\d+)?$", RegexOptions.Compiled),
                    OcrProfile = OcrOptions.Decimal,
                    Required = false,
                    Description = "Instability (напр. 1.21)"
                },
                new MiningHudRegionTemplate
                {
                    Name = MiningHudRegionNames.Resistance,
                    RelativeBounds = new(0.05, 0.56, 0.35, 0.06),
                    ExpectedPattern = null, // довільний текст (може бути "1.21 Gω", "High" тощо)
                    OcrProfile = OcrOptions.Alphanumeric,
                    Required = false,
                    Description = "Resistance (напр. 1.21 Gω)"
                }
            ]
        };
    }
}