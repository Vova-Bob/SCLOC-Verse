using SCLOCVerse.Models.Mining;

namespace SCLOCVerse.Services.Mining.Signatures
{
    /// <summary>
    /// Built-in database матеріалів Star Citizen для mining.
    /// Захардкоджені коди (напр. "21425" = Aluminium), які витягуються з SC HUD.
    ///
    /// Дані — приблизні/placeholder (точні коди уточнюються після тестів на реальному SC HUD).
    /// Розширення можливе через JSON override (T7.2: MiningSignatureDatabase).
    /// </summary>
    public static class DefaultMiningSignatures
    {
        /// <summary>Усі вбудовані матеріали за кодом.</summary>
        public static readonly IReadOnlyDictionary<string, MiningMaterial> Defaults = new Dictionary<string, MiningMaterial>
        {
            // Metals — типова категорія для mining.
            ["21425"] = new MiningMaterial
            {
                Code = "21425",
                Name = "Aluminium",
                Category = "Metal",
                ClusterFormat = "Cluster: {0} Rocks",
                IsRefineryInput = true
            },
            ["33151"] = new MiningMaterial
            {
                Code = "33151",
                Name = "Copper",
                Category = "Metal",
                ClusterFormat = "Cluster: {0} Rocks",
                IsRefineryInput = true
            },
            ["22103"] = new MiningMaterial
            {
                Code = "22103",
                Name = "Iron",
                Category = "Metal",
                ClusterFormat = "Cluster: {0} Rocks",
                IsRefineryInput = true
            },
            ["11204"] = new MiningMaterial
            {
                Code = "11204",
                Name = "Titanium",
                Category = "Metal",
                ClusterFormat = "Cluster: {0} Rocks",
                IsRefineryInput = true
            },
            ["11203"] = new MiningMaterial
            {
                Code = "11203",
                Name = "Tungsten",
                Category = "Metal",
                ClusterFormat = "Cluster: {0} Rocks",
                IsRefineryInput = true
            },

            // Minerals.
            ["55001"] = new MiningMaterial
            {
                Code = "55001",
                Name = "Quartz",
                Category = "Mineral",
                ClusterFormat = "Cluster: {0} Rocks",
                IsRefineryInput = true
            },
            ["55003"] = new MiningMaterial
            {
                Code = "55003",
                Name = "Laranite",
                Category = "Mineral",
                ClusterFormat = "Cluster: {0} Rocks",
                IsRefineryInput = true
            },
            ["55006"] = new MiningMaterial
            {
                Code = "55006",
                Name = "Agricium",
                Category = "Mineral",
                ClusterFormat = "Cluster: {0} Rocks",
                IsRefineryInput = true
            },

            // Industrial.
            ["65001"] = new MiningMaterial
            {
                Code = "65001",
                Name = "Diamond",
                Category = "Industrial",
                ClusterFormat = "Cluster: {0} Rocks",
                IsRefineryInput = true
            },
            ["65002"] = new MiningMaterial
            {
                Code = "65002",
                Name = "Bexalite",
                Category = "Industrial",
                ClusterFormat = "Cluster: {0} Rocks",
                IsRefineryInput = true
            },

            // Inert — без refinery.
            ["99001"] = new MiningMaterial
            {
                Code = "99001",
                Name = "Inert Material",
                Category = "Inert",
                ClusterFormat = "Cluster: {0} Rocks",
                IsRefineryInput = false
            }
        };
    }
}