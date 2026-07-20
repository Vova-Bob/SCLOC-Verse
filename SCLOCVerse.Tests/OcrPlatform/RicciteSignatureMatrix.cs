using SCLOCVerse.Services.Mining.Signatures;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Тести математичної сигнатурної бази.
    /// Принцип: signature = base × cluster. Відома лише база (1 камінь).
    /// </summary>
    public class RicciteSignatureMatrix
    {
        private readonly ITestOutputHelper _output;
        public RicciteSignatureMatrix(ITestOutputHelper output) => _output = output;

        [Fact]
        public void AllMaterials_HudAndLegacy_LookupCorrect()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var cases = BuildTestCases();
            var pass = 0;

            _output.WriteLine($"{"RAW",-8} {"HUD",-10} {"MATERIAL",-15} {"CLUSTER",-20} PASS");
            _output.WriteLine(new string('─', 65));

            foreach (var (raw, name, cluster) in cases)
            {
                var hud = raw.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
                var legacy = raw.ToString();

                // Перевірити HUD-формат
                var m = db.Lookup(hud);
                var ok = m is not null && m.Name == name && m.ClusterFormat == $"Cluster: {cluster} Rocks";
                if (!ok) _output.WriteLine($"HUD FAIL: {hud} → {m?.Name ?? "null"}");

                // Перевірити Legacy-формат
                var m2 = db.Lookup(legacy);
                var ok2 = m2 is not null && m2.Name == name && m2.ClusterFormat == $"Cluster: {cluster} Rocks";
                if (!ok2) _output.WriteLine($"LEGACY FAIL: {legacy} → {m2?.Name ?? "null"}");

                if (ok && ok2) pass++;
                _output.WriteLine($"{raw,-8} {hud,-10} {m?.Name ?? "NOT FOUND",-15} {m?.ClusterFormat ?? "—",-20} {(ok && ok2 ? "✅" : "❌")}");
            }

            _output.WriteLine(new string('─', 65));
            _output.WriteLine($"Passed: {pass}/{cases.Count}");
            Assert.True(pass == cases.Count, $"{cases.Count - pass} lookups failed");
        }

        [Fact]
        public void GenericCategories_LookupCorrect()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);

            // ROC
            var roc = db.Lookup("4,000");
            Assert.NotNull(roc);
            Assert.Equal("ROC Mineable", roc!.Name);
            Assert.Equal("ROC", roc.Category);

            // FPS
            var fps = db.Lookup("3,000");
            Assert.NotNull(fps);
            Assert.Equal("FPS Mineable", fps!.Name);

            // Salvage
            var sal = db.Lookup("2,000");
            Assert.NotNull(sal);
            Assert.Equal("Salvage", sal!.Name);

            _output.WriteLine("✅ ROC + FPS + Salvage categories lookup correct");
        }

        [Fact]
        public void Lookup_UnknownSignature_ReturnsNull()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            Assert.Null(db.Lookup("99999"));
            Assert.Null(db.Lookup("1234"));
            Assert.Null(db.Lookup(""));
        }

        /// <summary>
        /// Тест-кейси: (raw, назва, cluster).
        /// Обчислюються з базових сигнатур (base × cluster).
        /// </summary>
        private static List<(int Raw, string Name, int Cluster)> BuildTestCases()
        {
            var list = new List<(int, string, int)>();
            var bases = new (string Name, int Base, int Max)[]
            {
                ("Quantainium",   3170, 2),
                ("Stileron",      3185, 2),
                ("Savrilium",     3200, 2),
                ("Uratite",       3370, 3),
                ("Riccite",       3385, 3),
                ("Lindinium",     3400, 3),
                ("Beryl",         3540, 4),
                ("Taranite",      3555, 4),
                ("Borase",        3570, 4),
                ("Gold",          3585, 4),
                ("Bexalite",      3600, 4),
                ("Laranite",      3825, 5),
                ("Astatine",      3840, 5),
                ("Titanium",      3855, 5),
                ("Tungsten",      3870, 5),
                ("Agricium",      3885, 5),
                ("Torite",        3900, 5),
                ("Hephaestanite", 4180, 6),
                ("Tin",           4195, 6),
                ("Quartz",        4210, 6),
                ("Corundum",      4225, 6),
                ("Copper",        4240, 6),
                ("Silicon",       4255, 6),
                ("Iron",          4270, 6),
                ("Aluminium",     4285, 6),
                ("Ice",           4300, 6),
            };

            foreach (var (name, baseSig, max) in bases)
            {
                for (var c = 1; c <= max; c++)
                {
                    list.Add((baseSig * c, name, c));
                }
            }

            return list;
        }
    }
}