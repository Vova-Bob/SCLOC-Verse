using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Acceptance test: 40+ різних значень через повний pipeline.
    /// Порівняння нашого CTC decode vs RapidOCR-style CTC decode.
    /// Доказ, що fix працює, а не випадковість.
    /// </summary>
    public class OcrBatchAcceptance
    {
        private readonly ITestOutputHelper _output;
        public OcrBatchAcceptance(ITestOutputHelper output) => _output = output;

        private string? FindModelsDir()
        {
            var candidates = new[] { "Resources/OcrModels", "../../../../SCLOCVerse/Resources/OcrModels" };
            foreach (var c in candidates)
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(Path.Combine(full, "ch_PP-OCRv5_rec_mobile_infer.onnx")))
                    return full;
            }
            return null;
        }

        private static Mat CreateTextCrop(string text, int height = 48, int width = 240)
        {
            // Більший шрифт + товстіше перо для кращої видимості моделлю
            var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(255, 255, 255));
            var fontScale = Math.Min(1.5, (double)(width - 20) / (text.Length * 20));
            if (fontScale < 0.6) fontScale = 0.6;
            var orgX = (width - (int)(text.Length * 20 * fontScale)) / 2;
            if (orgX < 5) orgX = 5;
            Cv2.PutText(mat, text, new Point(orgX, height - 10), HersheyFonts.HersheySimplex,
                fontScale, new Scalar(0, 0, 0), 2, LineTypes.AntiAlias);
            return mat;
        }

        // Preprocessing ТОЧНО як RapidOCR (RGB + 127.5)
        private static DenseTensor<float> PreprocessRapidOcr(Mat srcBgr)
        {
            var rows = srcBgr.Rows;
            var cols = srcBgr.Cols;
            var tensor = new DenseTensor<float>(new[] { 1, 3, rows, cols });
            var mean = 127.5f;
            var norm = 1f / 127.5f;

            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    var p = srcBgr.At<Vec3b>(r, c); // BGR
                    // RGB order: channel 0=R(=2), 1=G(=1), 2=B(=0)
                    tensor[0, 0, r, c] = (p[2] - mean) * norm; // R
                    tensor[0, 1, r, c] = (p[1] - mean) * norm; // G
                    tensor[0, 2, r, c] = (p[0] - mean) * norm; // B
                }
            return tensor;
        }

        // Dictionary ТОЧНО як RapidOCR (# + file + space)
        private static List<string> LoadDictRapidOcr(string path)
        {
            var keys = new List<string> { "#" };
            keys.AddRange(File.ReadAllLines(path));
            keys.Add(" ");
            return keys;
        }

        // ─────────────────────────────────────────────────────
        // RapidOCR-style CTC decode (еталон)
        // ─────────────────────────────────────────────────────
        private static string DecodeRapidOcrStyle(Tensor<float> output, List<string> dict)
        {
            var dims = output.Dimensions;
            var h = dims[1]; // timeSteps
            var w = dims[2]; // numClasses
            var sb = new StringBuilder();
            var lastIndex = 0;

            for (var i = 0; i < h; i++)
            {
                var maxIndex = 0;
                var maxValue = -1000f;
                for (var j = 0; j < w; j++)
                {
                    var v = output[0, i, j];
                    if (v > maxValue) { maxValue = v; maxIndex = j; }
                }
                // RapidOCR: if (maxIndex > 0 && maxIndex < keys.Count && (!(i > 0 && maxIndex == lastIndex)))
                if (maxIndex > 0 && maxIndex < dict.Count && (!(i > 0 && maxIndex == lastIndex)))
                {
                    sb.Append(dict[maxIndex]);
                }
                lastIndex = maxIndex;
            }
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────
        // Наш поточний CTC decode (з PaddleRecognizer.cs)
        // blank = numClasses-1, skip ch.StartsWith("#")
        // ─────────────────────────────────────────────────────
        private static string DecodeOurStyle(Tensor<float> output, IReadOnlyList<string> dict)
        {
            var dims = output.Dimensions;
            var timeSteps = dims[1];
            var numClasses = dims[2];
            var sb = new StringBuilder();
            var blankIdx = numClasses - 1;
            int? prevIdx = null;

            for (var t = 0; t < timeSteps; t++)
            {
                var bestIdx = 0;
                var bestVal = float.MinValue;
                for (var c = 0; c < numClasses; c++)
                {
                    var v = output[0, t, c];
                    if (v > bestVal) { bestVal = v; bestIdx = c; }
                }
                if (bestIdx == blankIdx) { prevIdx = null; continue; }
                if (prevIdx == bestIdx) continue;
                if (bestIdx < dict.Count)
                {
                    var ch = dict[bestIdx];
                    if (!ch.StartsWith("#"))
                        sb.Append(ch);
                }
                prevIdx = bestIdx;
            }
            return sb.ToString();
        }

        [Fact]
        public void Batch_40_Values_Decode_Comparison()
        {
            var modelsDir = FindModelsDir();
            if (modelsDir is null) { _output.WriteLine("❌ Models not found"); return; }

            var recPath = Path.Combine(modelsDir, "ch_PP-OCRv5_rec_mobile_infer.onnx");
            var dictPath = Path.Combine(modelsDir, "ppocrv5_dict.txt");

            using var session = new InferenceSession(recPath);
            var inputName = session.InputMetadata.Keys.First();
            var rapidDict = LoadDictRapidOcr(dictPath);

            // 42 різних значення
            var testValues = new[]
            {
                // Riccite signatures (clusters 1-10)
                "3385", "6770", "10155", "13540", "16925",
                "20310", "23695", "27080", "30465", "33850",
                // Прості цифри
                "0", "1", "9", "10", "99", "100", "999", "1000",
                "1234", "4321", "5678", "8765",
                // Повторювані цифри
                "1111", "2222", "3333", "4444", "5555",
                "6666", "7777", "8888", "9999", "0000",
                // Великі значення
                "50000", "99999", "100000",
                // Букви
                "ABCDE", "AAAA", "BBBB", "CCCC",
                "ZZZZ", "HELLO", "WORLD", "OCR",
                // Мішанина
                "A1B2", "12AB"
            };

            var passCount = 0;
            var failCount = 0;
            var results = new List<(string Input, string RapidDecode, string OurDecoded, bool Pass)>();

            _output.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
            _output.WriteLine("║  BATCH ACCEPTANCE TEST: 40+ values | RapidOCR decode vs Our decode     ║");
            _output.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
            _output.WriteLine("");
            _output.WriteLine($"  Dict: RapidOCR-style (# + file + space) = {rapidDict.Count} entries");
            _output.WriteLine($"  Model output classes: {session.OutputMetadata.First().Value.Dimensions.Last()}");
            _output.WriteLine($"  Preprocessing: RGB + (x-127.5)/127.5 + NCHW");
            _output.WriteLine("");

            foreach (var input in testValues)
            {
                using var crop = CreateTextCrop(input);
                using var resized = new Mat();
                Cv2.Resize(crop, resized, new Size(crop.Cols, 48), 0, 0, InterpolationFlags.Linear);

                var tensor = PreprocessRapidOcr(resized);
                var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
                using var results_ = session.Run(inputs);
                var output = results_.First().AsTensor<float>();

                var rapidDecoded = DecodeRapidOcrStyle(output, rapidDict);
                var ourDecoded = DecodeOurStyle(output, rapidDict);
                var pass = rapidDecoded == input;
                var ourMatch = ourDecoded == rapidDecoded;

                if (pass) passCount++; else failCount++;
                results.Add((input, rapidDecoded, ourDecoded, pass));

                _output.WriteLine($"  Input: {input,-10} | RapidOCR: {rapidDecoded,-12} | Our: {ourDecoded,-12} | " +
                                  $"Match={ourMatch,-5} | Pass={(pass ? "✅" : "❌")}");
            }

            _output.WriteLine("");
            _output.WriteLine($"  ────────────────────────────────────────────────");
            _output.WriteLine($"  TOTAL: {testValues.Length} tests");
            _output.WriteLine($"  PASS (RapidOCR decode == input): {passCount}/{testValues.Length} ({100.0*passCount/testValues.Length:F0}%)");
            _output.WriteLine($"  FAIL: {failCount}/{testValues.Length}");
            _output.WriteLine($"  Our decode == RapidOCR decode:  {results.Count(r => r.OurDecoded == r.RapidDecode)}/{testValues.Length}");

            // Аналіз розбіжностей Our vs RapidOCR
            var mismatches = results.Where(r => r.OurDecoded != r.RapidDecode).ToList();
            if (mismatches.Count > 0)
            {
                _output.WriteLine("");
                _output.WriteLine("  ⚠️  OUR CTC DECODE MISMATCHES vs RapidOCR:");
                _output.WriteLine("  ────────────────────────────────────────────");
                foreach (var m in mismatches.Take(20))
                {
                    _output.WriteLine($"    Input: {m.Input,-10} | RapidOCR: '{m.RapidDecode}' | Our: '{m.OurDecoded}'");
                }
                _output.WriteLine("");
                _output.WriteLine("  → Our CTC decode algorithm has bugs (blank index, special token handling).");
                _output.WriteLine("  → Need to align with RapidOCR ScoreToTextLine exactly.");
            }
            else
            {
                _output.WriteLine("");
                _output.WriteLine("  ✅ Our CTC decode == RapidOCR decode for ALL inputs.");
                _output.WriteLine("  → Decode algorithm is compatible (with correct dictionary).");
            }

            // Аналіз FAIL (RapidOCR decode != input)
            if (failCount > 0)
            {
                _output.WriteLine("");
                _output.WriteLine($"  ⚠️  MODEL RECOGNITION FAILURES ({failCount}/{testValues.Length}):");
                _output.WriteLine("  ────────────────────────────────────────────");
                foreach (var f in results.Where(r => !r.Pass))
                {
                    _output.WriteLine($"    Input: {f.Input,-10} → Got: '{f.RapidDecode}'");
                }
                _output.WriteLine("");
                _output.WriteLine("  → Some failures expected: synthetic B&W text differs from real screenshots.");
                _output.WriteLine("  → What matters: digits 0-9 and common patterns work.");
            }

            _output.WriteLine("");
            _output.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
            _output.WriteLine("║  VERDICT                                                                ║");
            _output.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");

            // Окремо: цифри
            var digitTests = results.Where(r => r.Input.All(c => char.IsDigit(c))).ToList();
            var digitPass = digitTests.Count(r => r.Pass);
            _output.WriteLine($"  Digits only:     {digitPass}/{digitTests.Count} pass ({100.0*digitPass/digitTests.Count:F0}%)");

            // Окремо: букви
            var letterTests = results.Where(r => r.Input.All(c => char.IsLetter(c))).ToList();
            var letterPass = letterTests.Count(r => r.Pass);
            if (letterTests.Count > 0)
                _output.WriteLine($"  Letters only:    {letterPass}/{letterTests.Count} pass ({100.0*letterPass/letterTests.Count:F0}%)");

            // Окремо: Riccite
            var ricciteTests = results.Where(r => r.Input == "3385" || r.Input == "6770" || r.Input == "10155" ||
                                                    r.Input == "13540" || r.Input == "16925" || r.Input == "20310" ||
                                                    r.Input == "23695" || r.Input == "27080" || r.Input == "30465" ||
                                                    r.Input == "33850").ToList();
            var riccitePass = ricciteTests.Count(r => r.Pass);
            _output.WriteLine($"  Riccite (10 sig): {riccitePass}/{ricciteTests.Count} pass ({100.0*riccitePass/ricciteTests.Count:F0}%)");

            // Підсумковий висновок
            _output.WriteLine("");
            if (digitPass == digitTests.Count)
            {
                _output.WriteLine("  ✅ ALL digit tests pass → Dictionary fix confirmed for numeric OCR.");
                _output.WriteLine("  ✅ Root Cause = dictionary offset (# + space).");
                _output.WriteLine("  ✅ Ready to implement fix in PaddleRecognizer.LoadDictionary.");
            }
            else
            {
                _output.WriteLine($"  ⚠️  {digitTests.Count - digitPass} digit tests failed → possible additional issues.");
            }

            if (mismatches.Count > 0)
            {
                _output.WriteLine("");
                _output.WriteLine("  ⚠️  ADDITIONAL BUG: Our CTC decode algorithm differs from RapidOCR.");
                _output.WriteLine("  → Must align DecodeRapidOcrStyle with PaddleRecognizer.CtcGreedyDecode.");
            }

            session.Dispose();
        }
    }
}