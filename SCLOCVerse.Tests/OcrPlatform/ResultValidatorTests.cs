using OpenCvSharp;
using SCLOCVerse.Models.OcrPlatform;
using SCLOCVerse.Services.OcrPlatform.Validation;
using Xunit;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Інтеграційні тести ResultValidator: Confidence Filter + Consensus + Field Lock.
    /// </summary>
    public class ResultValidatorTests
    {
        [Fact]
        public void LowConfidence_Filtered()
        {
            var validator = new ResultValidator();
            var opts = OcrOptions.DigitsOnly; // MinConfidence = 0.7
            var input = new OcrResult
            {
                Matches = new[]
                {
                    new OcrMatch { Text = "12345", Confidence = 0.5, Bounds = new Rect(0,0,80,24), Filtered = false }
                }
            };

            var result = validator.Validate("test.region", input, cropFingerprint: null, opts);

            // Confidence 0.5 < 0.7 → відкинуто → consensus порожній → OcrResult.Empty.
            Assert.False(result.HasResult);
        }

        [Fact]
        public void HighConfidence_PassesFilter_NoConsensusYet()
        {
            var validator = new ResultValidator();
            var opts = OcrOptions.DigitsOnly;
            var input = new OcrResult
            {
                Matches = new[]
                {
                    new OcrMatch { Text = "21425", Confidence = 0.95, Bounds = new Rect(0,0,80,24) }
                }
            };

            // Перший кадр — consensus потребує ≥3. Повертає Empty.
            var result1 = validator.Validate("test.region", input, cropFingerprint: null, opts);
            Assert.False(result1.HasResult);
        }

        [Fact]
        public void ThreeSameResults_ConsensusReached()
        {
            var validator = new ResultValidator();
            var opts = OcrOptions.DigitsOnly;
            var input = new OcrResult
            {
                Matches = new[]
                {
                    new OcrMatch { Text = "21425", Confidence = 0.95, Bounds = new Rect(0,0,80,24) }
                }
            };

            // 3 кадри з однаковим результатом → consensus.
            validator.Validate("test.region", input, null, opts);
            validator.Validate("test.region", input, null, opts);
            var result3 = validator.Validate("test.region", input, null, opts);

            Assert.True(result3.HasResult);
            Assert.Equal("21425", result3.BestMatch!.Text);
        }

        [Fact]
        public void FieldLock_SkipsOcrWhenFingerprintMatches()
        {
            var validator = new ResultValidator();
            var opts = OcrOptions.DigitsOnly;
            var input = new OcrResult
            {
                Matches = new[]
                {
                    new OcrMatch { Text = "21425", Confidence = 0.95, Bounds = new Rect(0,0,80,24) }
                }
            };

            // 3 кадри → consensus → field lock встановлено.
            validator.Validate("test.region", input, cropFingerprint: 10000, opts);
            validator.Validate("test.region", input, cropFingerprint: 10000, opts);
            var result3 = validator.Validate("test.region", input, cropFingerprint: 10000, opts);
            Assert.True(result3.HasResult);

            // Наступний кадр з тим самим fingerprint → повертає locked value (без OCR).
            var emptyInput = new OcrResult { Matches = new List<OcrMatch>() };
            var lockedResult = validator.Validate("test.region", emptyInput, cropFingerprint: 10000, opts);
            Assert.True(lockedResult.HasResult);
            Assert.Equal("21425", lockedResult.BestMatch!.Text);
        }

        [Fact]
        public void DifferentRegions_HaveSeparateState()
        {
            var validator = new ResultValidator();
            var opts = OcrOptions.DigitsOnly;
            var inputA = new OcrResult
            {
                Matches = new[] { new OcrMatch { Text = "11111", Confidence = 0.95, Bounds = new Rect(0,0,80,24) } }
            };
            var inputB = new OcrResult
            {
                Matches = new[] { new OcrMatch { Text = "22222", Confidence = 0.95, Bounds = new Rect(0,0,80,24) } }
            };

            // Region A: 3 кадри "11111".
            validator.Validate("regionA", inputA, null, opts);
            validator.Validate("regionA", inputA, null, opts);
            var resultA = validator.Validate("regionA", inputA, null, opts);
            Assert.Equal("11111", resultA.BestMatch!.Text);

            // Region B: 1 кадр "22222" — consensus не досягнуто (3 не вистачить).
            var resultB = validator.Validate("regionB", inputB, null, opts);
            Assert.False(resultB.HasResult);
        }

        [Fact]
        public void MixedResults_MajorityWins()
        {
            var validator = new ResultValidator();
            var opts = OcrOptions.DigitsOnly;
            var good = new OcrResult
            {
                Matches = new[] { new OcrMatch { Text = "21425", Confidence = 0.95, Bounds = new Rect(0,0,80,24) } }
            };
            var bad = new OcrResult
            {
                Matches = new[] { new OcrMatch { Text = "21426", Confidence = 0.8, Bounds = new Rect(0,0,80,24) } }
            };

            // 3 good + 2 bad → consensus "21425".
            validator.Validate("test", good, null, opts);
            validator.Validate("test", bad, null, opts);
            validator.Validate("test", good, null, opts);
            validator.Validate("test", good, null, opts);
            var result = validator.Validate("test", bad, null, opts);

            Assert.True(result.HasResult);
            Assert.Equal("21425", result.BestMatch!.Text);
        }
    }
}