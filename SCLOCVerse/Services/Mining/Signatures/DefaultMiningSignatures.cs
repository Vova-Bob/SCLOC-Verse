using SCLOCVerse.Models.Mining;

namespace SCLOCVerse.Services.Mining.Signatures
{
    /// <summary>
    /// Реальні сигнатури Star Citizen для mining.
    ///
    /// Принцип: signature = base × clusterSize.
    /// Відома лише сигнатура 1 камінця (base). Усі інші обчислюються:
    /// cluster = raw / base.
    ///
    /// Source of Truth = реальний HUD Star Citizen.
    /// Формат: кома як роздільник тисяч (напр. "3,385").
    /// </summary>
    public static class DefaultMiningSignatures
    {
        /// <summary>
        /// Базові сигнатури (1 камінь) для 26 матеріалів.
        /// (Назва, Категорія, Base signature).
        /// Відсортовано за base DESCENDING — щоб уникнути колізій
        /// (напр. 19200 = Astatine×5, а не Savrilium×6).
        /// </summary>
        public static readonly (string Name, string Category, int Base)[] Materials =
        {
            ("Ice",           "Mineral", 4300),
            ("Aluminium",     "Metal",   4285),
            ("Iron",          "Metal",   4270),
            ("Silicon",       "Mineral", 4255),
            ("Copper",        "Metal",   4240),
            ("Corundum",      "Mineral", 4225),
            ("Quartz",        "Mineral", 4210),
            ("Tin",           "Metal",   4195),
            ("Hephaestanite", "Mineral", 4180),
            ("Torite",        "Mineral", 3900),
            ("Agricium",      "Metal",   3885),
            ("Tungsten",      "Metal",   3870),
            ("Titanium",      "Metal",   3855),
            ("Astatine",      "Gas",     3840),
            ("Laranite",      "Metal",   3825),
            ("Bexalite",      "Mineral", 3600),
            ("Gold",          "Metal",   3585),
            ("Borase",        "Mineral", 3570),
            ("Taranite",      "Mineral", 3555),
            ("Beryl",         "Mineral", 3540),
            ("Lindinium",     "Mineral", 3400),
            ("Riccite",       "Mineral", 3385),
            ("Uratite",       "Mineral", 3370),
            ("Savrilium",     "Mineral", 3200),
            ("Stileron",      "Mineral", 3185),
            ("Quantainium",   "Mineral", 3170),
        };

        /// <summary>ROC Mineables — фіксовані значення (не множник base).</summary>
        public static readonly int[] RocSignatures = { 4000, 8000, 12000, 16000, 20000, 24000, 28000 };

        /// <summary>FPS Mineables — фіксовані значення.</summary>
        public static readonly int[] FpsSignatures = { 3000, 6000, 9000, 12000, 15000, 18000, 21000, 24000, 27000, 30000 };

        /// <summary>Salvage — фіксовані значення.</summary>
        public static readonly int[] SalvageSignatures = { 2000, 4000, 6000, 8000, 10000, 12000, 14000, 16000, 18000, 20000, 22000, 24000, 26000, 28000, 30000 };
    }
}