using SCLOCVerse.Services.Mining.Signatures;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Тестова матриця: реальні сигнатури Star Citizen (Riccite).
    ///
    /// Цей тест перевіряє DATABASE LOOKUP — що кожна сигнатура
    /// правильно мапиться на Material + Cluster Size.
    ///
    /// Він НЕ перевіряє OCR (для цього потрібні реальні скріншоти SC HUD).
    /// OCR тестування вимагає запуску у грі.
    /// </summary>
    public class RicciteSignatureMatrix
    {
        private readonly ITestOutputHelper _output;

        public RicciteSignatureMatrix(ITestOutputHelper output) => _output = output;

        /// <summary>
        /// Реальні сигнатури Riccite з гри.
        /// signature = 3385 × clusterSize.
        /// </summary>
        public static readonly (int Cluster, string Signature)[] RicciteData =
        {
            (1,  "3385"),
            (2,  "6770"),
            (3,  "10155"),
            (4,  "13540"),
            (5,  "16925"),
            (6,  "20310"),
            (7,  "23695"),
            (8,  "27080"),
            (9,  "30465"),
            (10, "33850"),
        };

        [Fact]
        public void Riccite_AllSignatures_LookupCorrect()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);

            _output.WriteLine("═══════════════════════════════════════════════════════");
            _output.WriteLine("RICCITE SIGNATURE MATRIX — Database Lookup Test");
            _output.WriteLine("Material: Riccite | Base: 3385 | Value: 66 000 aUEC");
            _output.WriteLine("═══════════════════════════════════════════════════════");
            _output.WriteLine("");
            _output.WriteLine($"{"SIG",-8} {"PARSED",-8} {"MATERIAL",-10} {"CLUSTER",-8} {"PASS",-5}");
            _output.WriteLine(new string('─', 45));

            var allPass = true;

            foreach (var (cluster, signature) in RicciteData)
            {
                var material = db.Lookup(signature);

                var parsed = signature;
                var matName = material?.Name ?? "NOT FOUND";
                var clusterStr = material?.ClusterFormat ?? "—";
                var pass = material is not null
                    && material.Name == "Riccite"
                    && material.ClusterFormat == $"Cluster: {cluster} Rocks";

                if (!pass) allPass = false;

                _output.WriteLine($"{signature,-8} {parsed,-8} {matName,-10} {clusterStr,-8} {(pass ? "✅" : "❌")}");
            }

            _output.WriteLine(new string('─', 45));
            _output.WriteLine($"Result: {(allPass ? "✅ ALL PASS" : "❌ SOME FAILED")}");
            _output.WriteLine("");
            _output.WriteLine("ПРИМІТКА: Цей тест перевіряє Database Lookup.");
            _output.WriteLine("OCR (захоплення з екрана → текст) вимагає тестування у грі.");

            Assert.True(allPass, "Not all Riccite signatures resolved correctly");
        }

        [Theory]
        [InlineData("3385",  "Riccite", 1)]
        [InlineData("6770",  "Riccite", 2)]
        [InlineData("10155", "Riccite", 3)]
        [InlineData("13540", "Riccite", 4)]
        [InlineData("16925", "Riccite", 5)]
        [InlineData("20310", "Riccite", 6)]
        [InlineData("23695", "Riccite", 7)]
        [InlineData("27080", "Riccite", 8)]
        [InlineData("30465", "Riccite", 9)]
        [InlineData("33850", "Riccite", 10)]
        public void Lookup_Signature_ReturnsCorrectMaterialAndCluster(
            string signature, string expectedMaterial, int expectedCluster)
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var result = db.Lookup(signature);

            Assert.NotNull(result);
            Assert.Equal(expectedMaterial, result!.Name);
            Assert.Equal($"Cluster: {expectedCluster} Rocks", result.ClusterFormat);
        }

        [Fact]
        public void Lookup_UnknownSignature_ReturnsNull()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            Assert.Null(db.Lookup("99999"));
            Assert.Null(db.Lookup("1234"));
            Assert.Null(db.Lookup(""));
        }
    }
}