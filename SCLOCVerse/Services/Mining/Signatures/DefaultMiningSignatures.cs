using SCLOCVerse.Models.Mining;

namespace SCLOCVerse.Services.Mining.Signatures
{
    /// <summary>
    /// Реальні сигнатури Star Citizen для mining.
    ///
    /// Джерело: inside game data.
    /// Формула: signature = baseSignature × clusterSize.
    ///
    /// Riccite: base = 3385, market value = 66 000 aUEC.
    /// </summary>
    public static class DefaultMiningSignatures
    {
        public static readonly IReadOnlyDictionary<string, MiningMaterial> Defaults = new Dictionary<string, MiningMaterial>
        {
            // ═══════════════════════════════════════════════════
            // RICCITE — base signature 3385, value 66 000 aUEC
            // Формула: signature = 3385 × clusterSize
            // ═══════════════════════════════════════════════════
            ["3385"]  = Create("Riccite", 1),
            ["6770"]  = Create("Riccite", 2),
            ["10155"] = Create("Riccite", 3),
            ["13540"] = Create("Riccite", 4),
            ["16925"] = Create("Riccite", 5),
            ["20310"] = Create("Riccite", 6),
            ["23695"] = Create("Riccite", 7),
            ["27080"] = Create("Riccite", 8),
            ["30465"] = Create("Riccite", 9),
            ["33850"] = Create("Riccite", 10),
        };

        /// <summary>
        /// Створити MiningMaterial для Riccite з заданим cluster size.
        /// </summary>
        private static MiningMaterial Create(string name, int clusterSize) => new()
        {
            Code = (3385 * clusterSize).ToString(),
            Name = name,
            Category = "Mineral",
            ClusterFormat = $"Cluster: {clusterSize} Rocks",
            IsRefineryInput = true
        };
    }
}