namespace SCLOCVerse.Models.Mining
{
    /// <summary>
    /// Один матеріал Star Citizen для mining signature database.
    /// </summary>
    public sealed record MiningMaterial
    {
        /// <summary>Код-сигнатура матеріалу (зчитується з HUD через OCR, напр. "21425").</summary>
        public required string Code { get; init; }

        /// <summary>Локалізоване ім'я матеріалу (напр. "Aluminium").</summary>
        public required string Name { get; init; }

        /// <summary>Категорія (Metal, Mineral, Gas, Refined, etc.)</summary>
        public string? Category { get; init; }

        /// <summary>Формат виводу в Overlay (напр. "Cluster: {0} Rocks", де {0} = cluster count).</summary>
        public string? ClusterFormat { get; init; }

        /// <summary>Чи цей матеріал — інгредієнт refinery.</summary>
        public bool IsRefineryInput { get; init; }
    }
}