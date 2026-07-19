using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    public class NormalizationFinder
    {
        private readonly ITestOutputHelper _output;
        public NormalizationFinder(ITestOutputHelper output) => _output = output;

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

        [Fact]
        public void Find_Correct_Normalization()
        {
            var modelsDir = FindModelsDir();
            if (modelsDir is null) { _output.WriteLine("❌ Models not found"); return; }

            var recPath = Path.Combine(modelsDir, "ch_PP-OCRv5_rec_mobile_infer.onnx");
            var dictPath = Path.Combine(modelsDir, "ppocrv5_dict.txt");
            var dict = File.ReadAllLines(dictPath);

            // Створити тестове зображення: "0" чорним на білому, 48×100
            using var crop = new Mat(48, 100, MatType.CV_8UC3, new Scalar(255, 255, 255));
            Cv2.PutText(crop, "0", new Point(20, 38), HersheyFonts.HersheySimplex, 1.2, new Scalar(0, 0, 0), 2);

            var session = new InferenceSession(recPath, new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED
            });
            var inputName = session.InputMetadata.Keys.First();

            _output.WriteLine("═══════════════════════════════════════════════════");
            _output.WriteLine("NORMALIZATION FINDER: 4 варіанти на одному зображенні");
            _output.WriteLine($"Тест: '0' (чорний на білому), 100×48, модель PP-OCRv5 rec");
            _output.WriteLine($"Клас '0' в словнику: індекс {Array.IndexOf(dict, "0")}");
            _output.WriteLine("═══════════════════════════════════════════════════\n");

            // 4 варіанти нормалізації
            var variants = new (string Name, Func<Mat, float[]> Norm)[]
            {
                ("A: Raw [0,255] (NO normalization)", (m) => RawNormalize(m)),
                ("B: x/255 → [0,1]", (m) => ScaleOnly(m)),
                ("C: (x-127.5)/127.5 → [-1,1] (PP-OCRv5)", (m) => PaddleOCRNorm(m)),
                ("D: ImageNet (mean=0.485,std=0.229)", (m) => ImageNetNorm(m)),
            };

            foreach (var (name, norm) in variants)
            {
                var tensorData = norm(crop);
                var tensor = new DenseTensor<float>(tensorData, new[] { 1, 3, 48, 100 });

                var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
                using var results = session.Run(inputs);
                var output = results.First().AsTensor<float>();

                var dims = output.Dimensions.ToArray();
                var timeSteps = dims[1];
                var numClasses = dims[2];

                // Decode + show top-3 per timestep
                _output.WriteLine($"── {name} ──");

                var decodedText = new System.Text.StringBuilder();
                var maxConfidence = 0.0;
                int? prevIdx = null;
                var blankIdx = numClasses - 1;

                for (var t = 0; t < timeSteps; t++)
                {
                    var bestIdx = 0;
                    var bestVal = float.MinValue;
                    for (var c = 0; c < numClasses; c++)
                    {
                        var v = output[0, t, c];
                        if (v > bestVal) { bestVal = v; bestIdx = c; }
                    }

                    // Softmax confidence
                    double maxExp = bestVal;
                    double expSum = 0;
                    for (var c = 0; c < numClasses; c++)
                        expSum += Math.Exp(output[0, t, c] - maxExp);
                    var conf = 1.0 / expSum;

                    if (conf > maxConfidence) maxConfidence = conf;

                    // CTC decode
                    if (bestIdx != blankIdx && bestIdx != prevIdx && bestIdx < dict.Length)
                    {
                        var ch = dict[bestIdx];
                        if (!ch.StartsWith("#"))
                            decodedText.Append(ch);
                    }
                    prevIdx = bestIdx;

                    // Top-3 for first 3 timesteps
                    if (t < 3)
                    {
                        var top3 = Enumerable.Range(0, numClasses)
                            .Select(c => (idx: c, val: output[0, t, c]))
                            .OrderByDescending(x => x.val)
                            .Take(3)
                            .Select(x => $"  [{x.idx}]={GetChar(dict, x.idx)}({x.val:F4})");
                        _output.WriteLine($"  T{t}: {string.Join(" ", top3)} conf={conf:F6}");
                    }
                }

                _output.WriteLine($"  Decoded: '{decodedText}'");
                _output.WriteLine($"  Max confidence: {maxConfidence:F6}");
                _output.WriteLine("");
            }

            session.Dispose();
            _output.WriteLine("═══════════════════════════════════════════════════");
        }

        private static string GetChar(string[] dict, int idx)
        {
            if (idx < 0 || idx >= dict.Length) return "<BLANK>";
            var ch = dict[idx];
            return ch.Length <= 2 ? ch : ch[..2];
        }

        // A: Raw [0,255]
        private static float[] RawNormalize(Mat m)
        {
            var data = new float[3 * m.Rows * m.Cols];
            for (var y = 0; y < m.Rows; y++)
                for (var x = 0; x < m.Cols; x++)
                {
                    var p = m.At<Vec3b>(y, x);
                    data[0 * m.Rows * m.Cols + y * m.Cols + x] = p[0];
                    data[1 * m.Rows * m.Cols + y * m.Cols + x] = p[1];
                    data[2 * m.Rows * m.Cols + y * m.Cols + x] = p[2];
                }
            return data;
        }

        // B: x/255 → [0,1]
        private static float[] ScaleOnly(Mat m)
        {
            var data = new float[3 * m.Rows * m.Cols];
            for (var y = 0; y < m.Rows; y++)
                for (var x = 0; x < m.Cols; x++)
                {
                    var p = m.At<Vec3b>(y, x);
                    data[0 * m.Rows * m.Cols + y * m.Cols + x] = p[0] / 255f;
                    data[1 * m.Rows * m.Cols + y * m.Cols + x] = p[1] / 255f;
                    data[2 * m.Rows * m.Cols + y * m.Cols + x] = p[2] / 255f;
                }
            return data;
        }

        // C: (x-127.5)/127.5 → [-1,1]
        private static float[] PaddleOCRNorm(Mat m)
        {
            var data = new float[3 * m.Rows * m.Cols];
            for (var y = 0; y < m.Rows; y++)
                for (var x = 0; x < m.Cols; x++)
                {
                    var p = m.At<Vec3b>(y, x);
                    data[0 * m.Rows * m.Cols + y * m.Cols + x] = (p[0] - 127.5f) / 127.5f;
                    data[1 * m.Rows * m.Cols + y * m.Cols + x] = (p[1] - 127.5f) / 127.5f;
                    data[2 * m.Rows * m.Cols + y * m.Cols + x] = (p[2] - 127.5f) / 127.5f;
                }
            return data;
        }

        // D: ImageNet
        private static float[] ImageNetNorm(Mat m)
        {
            var mean = new[] { 0.485f * 255f, 0.456f * 255f, 0.406f * 255f };
            var norm = new[] { 1f / (0.229f * 255f), 1f / (0.224f * 255f), 1f / (0.225f * 255f) };
            var data = new float[3 * m.Rows * m.Cols];
            for (var y = 0; y < m.Rows; y++)
                for (var x = 0; x < m.Cols; x++)
                {
                    var p = m.At<Vec3b>(y, x);
                    for (var c = 0; c < 3; c++)
                        data[c * m.Rows * m.Cols + y * m.Cols + x] = (p[c] - mean[c]) * norm[c];
                }
            return data;
        }
    }
}