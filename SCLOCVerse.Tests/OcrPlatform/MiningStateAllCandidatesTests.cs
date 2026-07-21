using SCLOCVerse.Models.Mining;
using SCLOCVerse.Services.Mining.Signatures;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Тести для інтеграції MiningState.AllCandidates з MiningSignatureDatabase.LookupAll.
    ///
    /// <para>Перевіряє контракт:</para>
    /// <list type="bullet">
    /// <item><see cref="MiningState.Material"/> = <c>AllCandidates.FirstOrDefault()</c>
    ///     (зворотна сумісність).</item>
    /// <item>При колізії ROC/FPS/Salvage список містить 2-3 кандидати.</item>
    /// <item>При звичайному material — 1 кандидат.</item>
    /// <item>При невідомій сігнатурі — 0 кандидатів, Material = null.</item>
    /// </list>
    /// </summary>
    public class MiningStateAllCandidatesTests
    {
        private readonly ITestOutputHelper _output;
        public MiningStateAllCandidatesTests(ITestOutputHelper output) => _output = output;

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 1: AllCandidates = 0 → Material = null
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void EmptyCandidates_MaterialIsNull()
        {
            var state = new MiningState { AllCandidates = System.Array.Empty<MiningMaterial>() };
            Assert.Null(state.Material);
            Assert.Empty(state.AllCandidates);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 2: AllCandidates = 1 → Material = перший (єдиний)
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void SingleCandidate_MaterialIsFirstAndOnly()
        {
            var mat = new MiningMaterial { Code = "6770", Name = "Riccite", Category = "Mineral" };
            var state = new MiningState { AllCandidates = new[] { mat } };

            Assert.NotNull(state.Material);
            Assert.Same(mat, state.Material);
            Assert.Single(state.AllCandidates);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 3: AllCandidates = 3 (колізія) → Material = перший (ROC)
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void MultiCandidate_MaterialIsFirstROC()
        {
            var roc  = new MiningMaterial { Code = "12000", Name = "ROC Mineable", Category = "ROC",  ClusterFormat = "Tier 3" };
            var fps  = new MiningMaterial { Code = "12000", Name = "FPS Mineable", Category = "FPS",  ClusterFormat = "Tier 4" };
            var sal  = new MiningMaterial { Code = "12000", Name = "Salvage",      Category = "Salvage", ClusterFormat = "Tier 6" };

            var state = new MiningState { AllCandidates = new[] { roc, fps, sal } };

            Assert.NotNull(state.Material);
            Assert.Same(roc, state.Material);           // перший = ROC
            Assert.Equal("ROC Mineable", state.Material!.Name);
            Assert.Equal(3, state.AllCandidates.Count);
            Assert.Same(fps, state.AllCandidates[1]);  // другий = FPS
            Assert.Same(sal, state.AllCandidates[2]);  // третій = Salvage
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 4: Інтеграція з LookupAll — звичайний material
        // ─────────────────────────────────────────────────────────────
        [Theory]
        [InlineData("3385",  "Riccite", 1)]
        [InlineData("6770",  "Riccite", 1)]
        [InlineData("10155", "Riccite", 1)]   // Epic max
        [InlineData("6340",  "Quantainium", 1)] // Legendary max
        [InlineData("4300",  "Ice", 1)]
        public void LookupAll_RegularMaterial_ReturnsSingleCandidate(string raw, string expectedName, int expectedCount)
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var candidates = db.LookupAll(raw);
            var state = new MiningState { AllCandidates = candidates };

            _output.WriteLine($"LookupAll(\"{raw}\") = {candidates.Count} candidate(s):");
            foreach (var m in candidates)
                _output.WriteLine($"  - {m.Name} ({m.Category}) {m.ClusterFormat}");

            Assert.Equal(expectedCount, candidates.Count);
            Assert.NotNull(state.Material);
            Assert.Equal(expectedName, state.Material!.Name);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 5: Інтеграція з LookupAll — колізія ROC/FPS/Salvage
        // ─────────────────────────────────────────────────────────────
        [Theory]
        [InlineData("12000", 3, "ROC Mineable", "Tier 3")]  // повна потрійна
        [InlineData("24000", 3, "ROC Mineable", "Tier 6")]  // повна потрійна
        [InlineData("16000", 2, "ROC Mineable", "Tier 4")]  // ROC + Salvage
        [InlineData("28000", 2, "ROC Mineable", "Tier 7")]  // ROC + Salvage
        [InlineData("30000", 2, "FPS Mineable", "Tier 10")] // FPS + Salvage (без ROC)
        public void LookupAll_Collision_StoresAllInState(string raw, int expectedCount, string firstName, string firstCluster)
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var candidates = db.LookupAll(raw);
            var state = new MiningState { AllCandidates = candidates };

            _output.WriteLine($"LookupAll(\"{raw}\") = {candidates.Count} candidate(s):");
            foreach (var m in candidates)
                _output.WriteLine($"  - {m.Name} ({m.Category}) {m.ClusterFormat}");

            Assert.Equal(expectedCount, candidates.Count);
            Assert.NotNull(state.Material);
            Assert.Equal(firstName, state.Material!.Name);
            Assert.Equal(firstCluster, state.Material.ClusterFormat);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 6: Інтеграція з LookupAll — невідома сигнатура
        // ─────────────────────────────────────────────────────────────
        [Theory]
        [InlineData("99999")]
        [InlineData("1234")]
        [InlineData("")]
        [InlineData("abc")]
        public void LookupAll_Unknown_LeavesEmptyCandidates(string raw)
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var candidates = db.LookupAll(raw);
            var state = new MiningState { AllCandidates = candidates };

            Assert.Empty(candidates);
            Assert.Null(state.Material);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 7: ToString відображає кількість альтернатив при колізії
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void ToString_MultiCandidate_ShowsAltCount()
        {
            var roc = new MiningMaterial { Code = "12000", Name = "ROC Mineable", Category = "ROC", ClusterFormat = "Tier 3" };
            var fps = new MiningMaterial { Code = "12000", Name = "FPS Mineable", Category = "FPS", ClusterFormat = "Tier 4" };
            var sal = new MiningMaterial { Code = "12000", Name = "Salvage", Category = "Salvage", ClusterFormat = "Tier 6" };

            var state = new MiningState
            {
                AllCandidates = new[] { roc, fps, sal },
                Confidence = 0.95
            };

            var str = state.ToString();
            _output.WriteLine($"ToString() = \"{str}\"");

            Assert.Contains("ROC Mineable", str);
            Assert.Contains("+2 alt", str); // 3 candidates → +2 alt
        }

        [Fact]
        public void ToString_SingleCandidate_NoAltSuffix()
        {
            var mat = new MiningMaterial { Code = "6770", Name = "Riccite", Category = "Mineral", ClusterFormat = "Cluster: 2 Rocks" };
            var state = new MiningState
            {
                AllCandidates = new[] { mat },
                Confidence = 0.95
            };

            var str = state.ToString();
            _output.WriteLine($"ToString() = \"{str}\"");

            Assert.Contains("Riccite", str);
            Assert.DoesNotContain("alt", str); // 1 candidate → без alt
        }
    }
}