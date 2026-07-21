using SCLOCVerse.Services.Mining.Signatures;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Regression tests для гібридного LookupGeneric → LookupGenericAll.
    ///
    /// <para>Source of Truth — фіксовані сигнатури:</para>
    /// <list type="bullet">
    /// <item><b>ROC</b> (Range Ore Collector): 4000, 8000, 12000, 16000, 20000, 24000, 28000</item>
    /// <item><b>FPS</b> (Hand Mining): 3000, 6000, 9000, 12000, 15000, 18000, 21000, 24000, 27000, 30000</item>
    /// <item><b>Salvage</b>: 2000, 4000, 6000, 8000, 10000, 12000, 14000, 16000,
    ///     18000, 20000, 22000, 24000, 26000, 28000, 30000</item>
    /// </list>
    ///
    /// <para><b>Контракт LookupAll:</b></para>
    /// <list type="bullet">
    /// <item>Неколізійний raw → 1 кандидат (поведінка як у старого Lookup).</item>
    /// <item>Колізійний raw → 2-3 кандидати (повертаються всі, порядок стабільний).</item>
    /// </list>
    ///
    /// <para><b>Контракт Lookup (зворотна сумісність):</b> повертає перший з LookupAll.</para>
    /// </summary>
    public class GenericSignatureCollisionTests
    {
        private readonly ITestOutputHelper _output;
        public GenericSignatureCollisionTests(ITestOutputHelper output) => _output = output;

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 1: Приклади з задачі користувача
        // ─────────────────────────────────────────────────────────────
        [Theory]
        // 16000 → ROC Tier 4 + Salvage Tier 8 (FPS не входить)
        [InlineData("16000", new[] { "ROC Mineable|ROC|Tier 4", "Salvage|Salvage|Tier 8" })]
        // 24000 → ROC Tier 6 + FPS Tier 8 + Salvage Tier 12
        [InlineData("24000", new[] { "ROC Mineable|ROC|Tier 6", "FPS Mineable|FPS|Tier 8", "Salvage|Salvage|Tier 12" })]
        // 28000 → ROC Tier 7 + Salvage Tier 14 (FPS не входить)
        [InlineData("28000", new[] { "ROC Mineable|ROC|Tier 7", "Salvage|Salvage|Tier 14" })]
        // 30000 → FPS Tier 10 + Salvage Tier 15 (ROC не входить — макс 28000)
        [InlineData("30000", new[] { "FPS Mineable|FPS|Tier 10", "Salvage|Salvage|Tier 15" })]
        // 12000 → повна потрійна колізія (ROC Tier 3 + FPS Tier 4 + Salvage Tier 6)
        [InlineData("12000", new[] { "ROC Mineable|ROC|Tier 3", "FPS Mineable|FPS|Tier 4", "Salvage|Salvage|Tier 6" })]
        public void LookupAll_CollisionRaw_ReturnsAllCandidates(string raw, string[] expected)
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var actual = db.LookupAll(raw);

            _output.WriteLine($"LookupAll(\"{raw}\") = {actual.Count} candidate(s):");
            foreach (var m in actual)
                _output.WriteLine($"  - {m.Name} ({m.Category}) {m.ClusterFormat}");

            Assert.Equal(expected.Length, actual.Count);
            for (var i = 0; i < expected.Length; i++)
            {
                var parts = expected[i].Split('|');
                Assert.Equal(parts[0], actual[i].Name);
                Assert.Equal(parts[1], actual[i].Category);
                Assert.Equal(parts[2], actual[i].ClusterFormat);
                Assert.False(actual[i].IsRefineryInput, "Generic не має бути refinery input");
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 2: Неколізійні generic-сигнатури — повертають 1 candidate
        // ─────────────────────────────────────────────────────────────
        [Theory]
        // Лише Salvage (не кратне 3000 чи 4000)
        [InlineData("2000",  "Salvage", "Tier 1")]   // Salvage Tier 1
        [InlineData("10000", "Salvage", "Tier 5")]   // Salvage Tier 5
        [InlineData("22000", "Salvage", "Tier 11")]  // Salvage Tier 11
        [InlineData("26000", "Salvage", "Tier 13")]
        // Лише FPS (не кратне 4000)
        [InlineData("3000",  "FPS Mineable", "Tier 1")]
        [InlineData("9000",  "FPS Mineable", "Tier 3")]
        [InlineData("15000", "FPS Mineable", "Tier 5")]
        [InlineData("21000", "FPS Mineable", "Tier 7")]
        [InlineData("27000", "FPS Mineable", "Tier 9")]
        // Лише ROC (не кратне 3000; 2000 для Salvage)
        [InlineData("4000",  "ROC Mineable", "Tier 1")] // + Salvage Tier 2 — колізія!
        [InlineData("4000",  "Salvage",      "Tier 2")]
        public void LookupAll_NonCollisionRaw_ReturnsSingleCandidate(string raw, string expectedName, string expectedCluster)
        {
            // Цей тест перевіряє, що кандидат з (expectedName, expectedCluster)
            // дійсно є серед результатів LookupAll. Не вимагає унікальності.
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var actual = db.LookupAll(raw);

            _output.WriteLine($"LookupAll(\"{raw}\") = {actual.Count} candidate(s):");
            foreach (var m in actual)
                _output.WriteLine($"  - {m.Name} ({m.Category}) {m.ClusterFormat}");

            var found = false;
            foreach (var m in actual)
            {
                if (m.Name == expectedName && m.ClusterFormat == expectedCluster)
                {
                    found = true;
                    break;
                }
            }
            Assert.True(found, $"Кандидат {expectedName} {expectedCluster} не знайдено серед {actual.Count} результатів");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 3: Повна таблиця колізій ROC ∩ FPS ∩ Salvage
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void LookupAll_FullCollisionMatrix_AllCorrect()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);

            // Побудова матриці: (raw, expectedCount, hasROC, hasFPS, hasSalvage)
            var cases = BuildCollisionMatrix();

            var pass = 0;
            _output.WriteLine("Повна матриця колізій ROC/FPS/Salvage:");
            _output.WriteLine(new string('─', 75));
            _output.WriteLine($"  {"RAW",-7} {"COUNT",-6} {"ROC?",-5} {"FPS?",-5} {"SAL?",-5} {"ACTUAL",-30} PASS");
            _output.WriteLine(new string('─', 75));

            foreach (var (raw, expectedCount, hasROC, hasFPS, hasSal) in cases)
            {
                var actual = db.LookupAll(raw.ToString());
                var actualROC = actual.Any(m => m.Category == "ROC");
                var actualFPS = actual.Any(m => m.Category == "FPS");
                var actualSAL = actual.Any(m => m.Category == "Salvage");

                var ok = actual.Count == expectedCount
                         && actualROC == hasROC
                         && actualFPS == hasFPS
                         && actualSAL == hasSal;
                if (ok) pass++;

                var actualStr = $"{actual.Count}c {(actualROC ? "R" : "-")}{(actualFPS ? "F" : "-")}{(actualSAL ? "S" : "-")}";
                _output.WriteLine($"  {raw,-7} {expectedCount,-6} {(hasROC ? "Y" : "N"),-5} {(hasFPS ? "Y" : "N"),-5} {(hasSal ? "Y" : "N"),-5} {actualStr,-30} {(ok ? "✅" : "❌")}");
            }

            _output.WriteLine(new string('─', 75));
            _output.WriteLine($"  PASSED: {pass}/{cases.Count}");
            Assert.True(pass == cases.Count, $"{cases.Count - pass} кейсів колізій не пройшли");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 4: Lookup (single) — зворотна сумісність, повертає ПЕРШИЙ
        // ─────────────────────────────────────────────────────────────
        [Theory]
        [InlineData("16000", "ROC Mineable", "Tier 4")]   // ROC перед Salvage
        [InlineData("12000", "ROC Mineable", "Tier 3")]   // повна потрійна → перший = ROC
        [InlineData("24000", "ROC Mineable", "Tier 6")]   // потрійна → ROC
        [InlineData("30000", "FPS Mineable", "Tier 10")]  // без ROC → FPS перший
        [InlineData("2000",  "Salvage",      "Tier 1")]   // лише Salvage
        [InlineData("4000",  "ROC Mineable", "Tier 1")]   // ROC + Salvage → ROC
        public void Lookup_SingleResult_ReturnsFirstFromLookupAll(string raw, string expectedName, string expectedCluster)
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var single = db.Lookup(raw);
            var all = db.LookupAll(raw);

            _output.WriteLine($"Lookup(\"{raw}\") = {single?.Name} {single?.ClusterFormat}");
            _output.WriteLine($"LookupAll(\"{raw}\")[0] = {all[0].Name} {all[0].ClusterFormat}");

            Assert.NotNull(single);
            Assert.Equal(expectedName, single!.Name);
            Assert.Equal(expectedCluster, single.ClusterFormat);

            // Lookup повинен дорівнювати першому елементу LookupAll.
            Assert.Equal(all[0].Name, single.Name);
            Assert.Equal(all[0].ClusterFormat, single.ClusterFormat);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 5: Mining materials — LookupAll повертає 1 елемент (без регресії)
        // ─────────────────────────────────────────────────────────────
        [Theory]
        [InlineData("3385",  "Riccite",    "Cluster: 1 Rocks")]  // Epic, валідний cluster 1
        [InlineData("6770",  "Riccite",    "Cluster: 2 Rocks")]  // валідний cluster 2
        [InlineData("10155", "Riccite",    "Cluster: 3 Rocks")]  // валідний cluster 3 (max Epic)
        [InlineData("6340",  "Quantainium","Cluster: 2 Rocks")]  // Legendary, max
        [InlineData("4240",  "Copper",     "Cluster: 1 Rocks")]  // Common
        [InlineData("25440", "Copper",     "Cluster: 6 Rocks")]  // Common max
        [InlineData("4300",  "Ice",        "Cluster: 1 Rocks")]
        public void LookupAll_Material_ReturnsSingleAndNoGeneric(string raw, string expectedName, string expectedCluster)
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var actual = db.LookupAll(raw);

            _output.WriteLine($"LookupAll(\"{raw}\") = {actual.Count} candidate(s):");
            foreach (var m in actual)
                _output.WriteLine($"  - {m.Name} ({m.Category}) {m.ClusterFormat}");

            Assert.Single(actual);
            Assert.Equal(expectedName, actual[0].Name);
            Assert.Equal(expectedCluster, actual[0].ClusterFormat);
            Assert.True(actual[0].IsRefineryInput, "Mining material — refinery input");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 6: Невідома сигнатура — порожній список
        // ─────────────────────────────────────────────────────────────
        [Theory]
        [InlineData("99999")]
        [InlineData("1234")]
        [InlineData("")]
        [InlineData("abc")]
        public void LookupAll_Unknown_ReturnsEmpty(string raw)
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var actual = db.LookupAll(raw);
            Assert.Empty(actual);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 7: Порядок стабільний — ROC → FPS → Salvage
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void LookupAll_Order_IsStable_RocFpsSalvage()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var actual = db.LookupAll("24000"); // повна потрійна колізія

            _output.WriteLine("LookupAll(\"24000\") — перевірка порядку:");
            for (var i = 0; i < actual.Count; i++)
                _output.WriteLine($"  [{i}] {actual[i].Category}");

            Assert.Equal(3, actual.Count);
            Assert.Equal("ROC",     actual[0].Category);
            Assert.Equal("FPS",     actual[1].Category);
            Assert.Equal("Salvage", actual[2].Category);
        }

        // ════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Будує повну матрицю очікуваних колізій для всіх generic-сигнатур.
        /// Обчислення з фактичних масивів RocSignatures / FpsSignatures / SalvageSignatures.
        /// </summary>
        private static List<(int Raw, int Count, bool ROC, bool FPS, bool SAL)> BuildCollisionMatrix()
        {
            var rocSet = new HashSet<int>(DefaultMiningSignatures.RocSignatures);
            var fpsSet = new HashSet<int>(DefaultMiningSignatures.FpsSignatures);
            var salSet = new HashSet<int>(DefaultMiningSignatures.SalvageSignatures);

            var allRaws = new HashSet<int>();
            foreach (var v in rocSet) allRaws.Add(v);
            foreach (var v in fpsSet) allRaws.Add(v);
            foreach (var v in salSet) allRaws.Add(v);

            var list = new List<(int, int, bool, bool, bool)>();
            foreach (var raw in allRaws)
            {
                var hasROC = rocSet.Contains(raw);
                var hasFPS = fpsSet.Contains(raw);
                var hasSAL = salSet.Contains(raw);
                var count = (hasROC ? 1 : 0) + (hasFPS ? 1 : 0) + (hasSAL ? 1 : 0);
                list.Add((raw, count, hasROC, hasFPS, hasSAL));
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }
    }
}
