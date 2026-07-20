using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System.Reflection;
using SCLOCVerse.Services.OcrPlatform.Engines.Internal;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Regression-тести: гарантують, що PaddleRecognizer ініціалізується
    /// ідентично офіційній реалізації RapidOCR.
    ///
    /// Forensic 2026-07-20: Root Cause — LoadDictionary не відповідала
    /// офіційній специфікації (відсутні CTC blank "#" та space " ").
    /// Тести запобігають регресії: ніхто через пів року не "спростить"
    /// LoadDictionary назад до File.ReadAllLines.
    /// </summary>
    public class OcrRegressionTests
    {
        private readonly ITestOutputHelper _output;
        public OcrRegressionTests(ITestOutputHelper output) => _output = output;

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

        // ─────────────────────────────────────────────────────
        // Еталон: RapidOCR CrnnNet.InitKeys
        // ─────────────────────────────────────────────────────
        private static List<string> LoadDictRapidOcrReference(string path)
        {
            var keys = new List<string> { "#" };
            keys.AddRange(File.ReadAllLines(path));
            keys.Add(" ");
            return keys;
        }

        // ─────────────────────────────────────────────────────
        // Тест 1: DictionaryInitialization_MatchesRapidOCR
        // ─────────────────────────────────────────────────────
        [Fact]
        public void DictionaryInitialization_MatchesRapidOCR()
        {
            var modelsDir = FindModelsDir();
            if (modelsDir is null) { _output.WriteLine("❌ Models not found"); return; }

            var dictPath = Path.Combine(modelsDir, "ppocrv5_dict.txt");

            // Наш код (через reflection — LoadDictionary приватний)
            var recognizerType = typeof(PaddleRecognizer);
            var loadDictMethod = recognizerType.GetMethod("LoadDictionary",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(loadDictMethod);

            var ours = (IReadOnlyList<string>)loadDictMethod!.Invoke(null, new object[] { dictPath })!;
            var reference = LoadDictRapidOcrReference(dictPath);

            _output.WriteLine($"  Our dict size: {ours.Count}");
            _output.WriteLine($"  Reference dict size: {reference.Count}");
            _output.WriteLine($"  Match: {ours.Count == reference.Count}");

            // КРИТИЧНІ інваріанти
            Assert.Equal("#", ours[0]);                    // CTC blank на index 0
            Assert.Equal(" ", ours[^1]);                   // space на останньому
            Assert.Equal(reference.Count, ours.Count);     // однаковий розмір
            Assert.Equal(18385, ours.Count);                // = output classes моделі

            // Покрокова перевірка перших 5 і останніх 5
            _output.WriteLine("\n  First 5:");
            for (var i = 0; i < 5; i++)
            {
                _output.WriteLine($"    [{i}]: ours='{ours[i]}' ref='{reference[i]}' match={ours[i] == reference[i]}");
                Assert.Equal(reference[i], ours[i]);
            }

            _output.WriteLine("\n  Last 5:");
            for (var i = ours.Count - 5; i < ours.Count; i++)
            {
                _output.WriteLine($"    [{i}]: ours='{ours[i]}' ref='{reference[i]}' match={ours[i] == reference[i]}");
                Assert.Equal(reference[i], ours[i]);
            }

            // Перевірка цифр (0-9 у файлі на індексах 16162-16171 → після зсуву 16163-16172)
            _output.WriteLine("\n  Digits (0-9):");
            for (var d = 0; d <= 9; d++)
            {
                var refIdx = 16163 + d; // file index 16162 + offset 1
                var expected = d.ToString();
                _output.WriteLine($"    [{refIdx}]: ours='{ours[refIdx]}' expected='{expected}' match={ours[refIdx] == expected}");
                Assert.Equal(expected, ours[refIdx]);
            }

            // Повне порівняння всіх 18385 елементів
            var allMatch = true;
            for (var i = 0; i < reference.Count; i++)
            {
                if (ours[i] != reference[i])
                {
                    _output.WriteLine($"  ❌ MISMATCH at [{i}]: ours='{ours[i]}' ref='{reference[i]}'");
                    allMatch = false;
                    break;
                }
            }
            Assert.True(allMatch, "Dictionary must match RapidOCR reference exactly");
            _output.WriteLine("\n  ✅ All 18385 entries match RapidOCR reference");
        }

        // ─────────────────────────────────────────────────────
        // Тест 2: Decode_MatchesRapidOCR — окремі кейси
        // ─────────────────────────────────────────────────────
        [Theory]
        [InlineData("3385")]     // Riccite Cluster 1
        [InlineData("6770")]     // Riccite Cluster 2
        [InlineData("10155")]    // Riccite Cluster 3
        [InlineData("13540")]    // Riccite Cluster 4
        [InlineData("16925")]    // Riccite Cluster 5
        [InlineData("20310")]    // Riccite Cluster 6
        [InlineData("23695")]    // Riccite Cluster 7
        [InlineData("27080")]    // Riccite Cluster 8
        [InlineData("30465")]    // Riccite Cluster 9
        [InlineData("33850")]    // Riccite Cluster 10
        [InlineData("AAAA")]
        [InlineData("HELLO")]
        [InlineData("1234")]
        public void Decode_MatchesRapidOCR(string text)
        {
            var modelsDir = FindModelsDir();
            if (modelsDir is null) { _output.WriteLine("❌ Models not found"); return; }

            var recPath = Path.Combine(modelsDir, "ch_PP-OCRv5_rec_mobile_infer.onnx");
            var dictPath = Path.Combine(modelsDir, "ppocrv5_dict.txt");

            using var session = new InferenceSession(recPath);
            var inputName = session.InputMetadata.Keys.First();
            var dict = LoadDictRapidOcrReference(dictPath);

            // Створити crop
            using var crop = CreateTextCrop(text);
            using var resized = new Mat();
            Cv2.Resize(crop, resized, new Size(crop.Cols, 48), 0, 0, InterpolationFlags.Linear);

            // Preprocessing: RGB + (x-127.5)/127.5
            var tensor = PreprocessRapidOcr(resized);
            var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
            using var results = session.Run(inputs);
            var output = results.First().AsTensor<float>();

            // Еталон: RapidOCR-style decode
            var rapidDecoded = DecodeRapidOcrStyle(output, dict);

            // Наш код: PaddleRecognizer.CtcGreedyDecode (через reflection)
            var recognizerType = typeof(PaddleRecognizer);
            var decodeMethod = recognizerType.GetMethod("CtcGreedyDecode",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(decodeMethod);

            var ourResult = (RecognizedText)decodeMethod!.Invoke(null,
                new object[] { output, dict })!;

            _output.WriteLine($"  Input: {text,-10} | RapidOCR: {rapidDecoded,-12} | Our: {ourResult.Text,-12} | Match={ourResult.Text == rapidDecoded}");
            Assert.Equal(rapidDecoded, ourResult.Text);
        }

        // ─────────────────────────────────────────────────────
        // Допоміжні методи (ідентичні OcrBatchAcceptance)
        // ─────────────────────────────────────────────────────
        private static Mat CreateTextCrop(string text, int height = 48, int width = 240)
        {
            var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(255, 255, 255));
            var fontScale = Math.Min(1.5, (double)(width - 20) / (text.Length * 20));
            if (fontScale < 0.6) fontScale = 0.6;
            var orgX = (width - (int)(text.Length * 20 * fontScale)) / 2;
            if (orgX < 5) orgX = 5;
            Cv2.PutText(mat, text, new Point(orgX, height - 10), HersheyFonts.HersheySimplex,
                fontScale, new Scalar(0, 0, 0), 2, LineTypes.AntiAlias);
            return mat;
        }

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
                    var p = srcBgr.At<Vec3b>(r, c);
                    tensor[0, 0, r, c] = (p[2] - mean) * norm;
                    tensor[0, 1, r, c] = (p[1] - mean) * norm;
                    tensor[0, 2, r, c] = (p[0] - mean) * norm;
                }
            return tensor;
        }

        private static string DecodeRapidOcrStyle(Tensor<float> output, List<string> dict)
        {
            var dims = output.Dimensions;
            var h = dims[1];
            var w = dims[2];
            var sb = new System.Text.StringBuilder();
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
                if (maxIndex > 0 && maxIndex < dict.Count && (!(i > 0 && maxIndex == lastIndex)))
                {
                    sb.Append(dict[maxIndex]);
                }
                lastIndex = maxIndex;
            }
            return sb.ToString();
        }
    }
}