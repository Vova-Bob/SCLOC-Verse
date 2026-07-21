using SCLOCVerse.Services.Mining.Signatures;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Regression tests для per-rarity ліміту кластерів (Minimal Fix).
    ///
    /// <para>Source of Truth — Star Citizen:</para>
    /// <list type="bullet">
    /// <item>Legendary (Quantainium/Stileron/Savrilium): max 2 Rocks</item>
    /// <item>Epic (Uratite/Riccite/Lindinium): max 3 Rocks</item>
    /// <item>Rare (Beryl/Taranite/Borase/Gold/Bexalite): max 4 Rocks</item>
    /// <item>Uncommon (Laranite/Astatine/Titanium/Tungsten/Agricium/Torite): max 5 Rocks</item>
    /// <item>Common (Hephaestanite/Tin/Quartz/Corundum/Copper/Silicon/Iron/Aluminium/Ice): max 6 Rocks</item>
    /// </list>
    ///
    /// <para>Кожна фізично неможлива сигнатура (cluster > per-rarity max) повинна
    /// повертати null або переходити до LookupGeneric.</para>
    /// </summary>
    public class MaterialClusterBoundsTests
    {
        private readonly ITestOutputHelper _output;
        public MaterialClusterBoundsTests(ITestOutputHelper output) => _output = output;

        // ── Допоміжна таблиця (Name, Base, MaxRocks) — дублює DefaultMiningSignatures
        // для self-contained тестів. MaxRocks з Source of Truth Star Citizen.
        private static readonly (string Name, int Base, int MaxRocks)[] Materials =
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

        // ════════════════════════════════════════════════════════════════
        //  ТЕСТ 1: Усі валідні сигнатури визначаються правильно
        // ════════════════════════════════════════════════════════════════
        [Fact]
        public void AllValidSignatures_DetectedCorrectly()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var pass = 0;
            var total = 0;

            _output.WriteLine("ТЕСТ 1: Валідні сигнатури (cluster ∈ [1, MaxRocks])");
            _output.WriteLine(new string('─', 70));
            _output.WriteLine($"  {"NAME",-15} {"BASE",-6} {"CL",-3} {"RAW",-8} EXPECTED        ACTUAL          PASS");
            _output.WriteLine(new string('─', 70));

            foreach (var (name, baseSig, maxRocks) in Materials)
            {
                for (var c = 1; c <= maxRocks; c++)
                {
                    total++;
                    var raw = baseSig * c;
                    var m = db.Lookup(raw.ToString());

                    var expectedName = name;
                    var expectedCluster = $"Cluster: {c} Rocks";
                    var actualName = m?.Name ?? "null";
                    var actualCluster = m?.ClusterFormat ?? "null";

                    var ok = actualName == expectedName && actualCluster == expectedCluster;
                    if (ok) pass++;

                    _output.WriteLine($"  {name,-15} {baseSig,-6} {c,-3} {raw,-8} {expectedName + " " + expectedCluster,-15} {actualName + " " + actualCluster,-15} {(ok ? "✅" : "❌")}");
                }
            }

            _output.WriteLine(new string('─', 70));
            _output.WriteLine($"  PASSED: {pass}/{total}");
            Assert.True(pass == total, $"{total - pass} валідних сигнатур не розпізнано (регресія!)");
        }

        // ════════════════════════════════════════════════════════════════
        //  ТЕСТ 2: Фізично неможливі кластери (cluster = MaxRocks + 1)
        // ════════════════════════════════════════════════════════════════
        [Fact]
        public void PhysicallyImpossibleClusters_Rejected()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var pass = 0;
            var total = 0;

            _output.WriteLine("ТЕСТ 2: Неможливі кластери (cluster = MaxRocks + 1)");
            _output.WriteLine(new string('─', 70));
            _output.WriteLine($"  {"NAME",-15} {"BASE",-6} {"CL",-3} {"RAW",-8} {"EXPECTED",-15} {"ACTUAL",-15} PASS");
            _output.WriteLine(new string('─', 70));

            foreach (var (name, baseSig, maxRocks) in Materials)
            {
                // cluster = MaxRocks + 1 — фізично неможливо
                var c = maxRocks + 1;
                total++;
                var raw = baseSig * c;
                var m = db.Lookup(raw.ToString());

                // Очікування: material НЕ повертає (name, cluster=c).
                // Якщо m != null, то це має бути ІНШИЙ матерал з коректним cluster.
                string expected;
                string actual;
                bool ok;

                if (m is null)
                {
                    expected = "null OR other";
                    actual = "null";
                    ok = true; // null — коректна відмова
                }
                else if (m.Name == name && m.ClusterFormat == $"Cluster: {c} Rocks")
                {
                    expected = "REJECT (impossible)";
                    actual = m.Name + " " + m.ClusterFormat;
                    ok = false; // повернув неможливу комбінацію — БАГ
                }
                else
                {
                    // Інший матерал з коректним cluster — це OK (математична колізія через ділення).
                    expected = "REJECT (impossible)";
                    actual = m.Name + " " + m.ClusterFormat;
                    ok = true; // не неможлива комбінація для (name, c) — коректно
                }

                if (ok) pass++;

                _output.WriteLine($"  {name,-15} {baseSig,-6} {c,-3} {raw,-8} {expected,-15} {actual,-15} {(ok ? "✅" : "❌ FAIL")}");
            }

            _output.WriteLine(new string('─', 70));
            _output.WriteLine($"  PASSED: {pass}/{total}");
            Assert.True(pass == total, $"{total - pass} неможливих кластерів повернуто як валідні (БАГ!)");
        }

        // ════════════════════════════════════════════════════════════════
        //  ТЕСТ 3: Конкретний кейс з forensic — raw = 16000
        // ════════════════════════════════════════════════════════════════
        [Fact]
        public void Forensic_16000_NotSavriliumCluster5()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var m = db.Lookup("16000");

            _output.WriteLine("ТЕСТ 3: Forensic-кейс raw=16000");
            _output.WriteLine(new string('─', 70));
            _output.WriteLine($"  Lookup(\"16000\") = {m?.Name ?? "null"} / {m?.ClusterFormat ?? "null"}");
            _output.WriteLine(new string('─', 70));

            // Savrilium cluster 5 (3200×5) — фізично неможливо, не повинен повернутись як Savrilium.
            Assert.False(
                m?.Name == "Savrilium" && m?.ClusterFormat == "Cluster: 5 Rocks",
                "REGRESІЯ: Savrilium cluster 5 неможливий (Legendary max 2)!");

            // Очікуваний результат: ROC Tier 4 (бо RocSignatures[3] = 16000).
            Assert.NotNull(m);
            Assert.Equal("ROC Mineable", m!.Name);
            Assert.Equal("Tier 4", m.ClusterFormat);
            _output.WriteLine("  ✅ Savrilium cluster 5 відхилено → ROC Tier 4 (LookupGeneric)");
        }

        // ════════════════════════════════════════════════════════════════
        //  ТЕСТ 4: Per-rarity ліміти — конкретні значення з задачі
        // ════════════════════════════════════════════════════════════════
        [Theory]
        // Savrilium (Legendary, max 2): cluster 3 → неможливо
        [InlineData("Savrilium", 3200, 3)]
        [InlineData("Savrilium", 3200, 4)]
        [InlineData("Savrilium", 3200, 5)]
        // Stileron (Legendary, max 2)
        [InlineData("Stileron", 3185, 3)]
        [InlineData("Stileron", 3185, 4)]
        // Quantainium (Legendary, max 2)
        [InlineData("Quantainium", 3170, 3)]
        [InlineData("Quantainium", 3170, 6)]
        // Riccite (Epic, max 3): cluster 4 → неможливо
        [InlineData("Riccite", 3385, 4)]
        [InlineData("Riccite", 3385, 10)]
        // Lindinium (Epic, max 3)
        [InlineData("Lindinium", 3400, 4)]
        [InlineData("Lindinium", 3400, 5)]
        // Bexalite (Rare, max 4): cluster 5 → неможливо
        [InlineData("Bexalite", 3600, 5)]
        [InlineData("Bexalite", 3600, 6)]
        // Laranite (Uncommon, max 5): cluster 6 → неможливо
        [InlineData("Laranite", 3825, 6)]
        [InlineData("Laranite", 3825, 7)]
        // Ice (Common, max 6): cluster 7 → неможливо
        [InlineData("Ice", 4300, 7)]
        [InlineData("Ice", 4300, 8)]
        public void ImpossibleCluster_NotReturnedForMaterial(string name, int baseSig, int cluster)
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var raw = baseSig * cluster;
            var m = db.Lookup(raw.ToString());

            // Матеріал (name) НЕ повинен повернутись з цим (cluster) Rocks.
            var badCombo = m?.Name == name && m?.ClusterFormat == $"Cluster: {cluster} Rocks";
            Assert.False(badCombo,
                $"REGRESІЯ: {name} × {cluster} неможлива, але повернуто! raw={raw}");
        }

        // ════════════════════════════════════════════════════════════════
        //  ТЕСТ 5: Валідні кластери на межі (cluster == MaxRocks) — коректні
        // ════════════════════════════════════════════════════════════════
        [Theory]
        [InlineData("Savrilium", 3200, 2)]   // Legendary max
        [InlineData("Stileron", 3185, 2)]
        [InlineData("Quantainium", 3170, 2)]
        [InlineData("Riccite", 3385, 3)]     // Epic max
        [InlineData("Lindinium", 3400, 3)]
        [InlineData("Bexalite", 3600, 4)]    // Rare max
        [InlineData("Gold", 3585, 4)]
        [InlineData("Laranite", 3825, 5)]    // Uncommon max
        [InlineData("Titanium", 3855, 5)]
        [InlineData("Ice", 4300, 6)]         // Common max
        [InlineData("Copper", 4240, 6)]
        public void MaxValidCluster_ReturnsCorrectMaterial(string name, int baseSig, int cluster)
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var raw = baseSig * cluster;
            var m = db.Lookup(raw.ToString());

            Assert.NotNull(m);
            Assert.Equal(name, m!.Name);
            Assert.Equal($"Cluster: {cluster} Rocks", m.ClusterFormat);
        }
    }
}
