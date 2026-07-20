using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    public class SessionOptionsTest
    {
        private readonly ITestOutputHelper _output;
        public SessionOptionsTest(ITestOutputHelper output) => _output = output;

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
        public void Test_SessionOptions_Variants()
        {
            var modelsDir = FindModelsDir();
            if (modelsDir is null) { _output.WriteLine("❌ Models not found"); return; }

            var recPath = Path.Combine(modelsDir, "ch_PP-OCRv5_rec_mobile_infer.onnx");
            var dict = File.ReadAllLines(Path.Combine(modelsDir, "ppocrv5_dict.txt"));

            // Простий тестовий crop: "0" чорним на білому
            using var crop = new Mat(48, 100, MatType.CV_8UC3, new Scalar(255, 255, 255));
            Cv2.PutText(crop, "0", new Point(20, 38), HersheyFonts.HersheySimplex, 1.2, new Scalar(0, 0, 0), 2);

            // ImageNet normalization (як в RapidOCRLib)
            var mean = new[] { 0.485f * 255f, 0.456f * 255f, 0.406f * 255f };
            var norm = new[] { 1f / (0.229f * 255f), 1f / (0.224f * 255f), 1f / (0.225f * 255f) };
            var rows = crop.Rows;
            var cols = crop.Cols;
            var tensorData = new float[3 * rows * cols];
            for (var y = 0; y < rows; y++)
                for (var x = 0; x < cols; x++)
                {
                    var p = crop.At<Vec3b>(y, x);
                    for (var c = 0; c < 3; c++)
                        tensorData[c * rows * cols + y * cols + x] = (p[c] - mean[c]) * norm[c];
                }

            var inputName = "x";

            // ── Варіант 1: БЕЗ SessionOptions (як RapidOCRLib) ──
            _output.WriteLine("── Варіант 1: new InferenceSession(path) — БЕЗ SessionOptions ──");
            using (var session = new InferenceSession(recPath))
            {
                inputName = session.InputMetadata.Keys.First();
                _output.WriteLine($"  Input name: '{inputName}'");

                var tensor = new DenseTensor<float>(tensorData, new[] { 1, 3, rows, cols });
                var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
                using var results = session.Run(inputs);
                var output = results.First().AsTensor<float>();
                var dims = output.Dimensions.ToArray();

                _output.WriteLine($"  Output dims: [{string.Join(", ", dims)}]");

                // Перший timestep top-5
                var numClasses = dims[2];
                var top5 = Enumerable.Range(0, numClasses)
                    .Select(c => (idx: c, val: output[0, 0, c]))
                    .OrderByDescending(x => x.val).Take(5).ToList();

                foreach (var (idx, val) in top5)
                {
                    var ch = idx < dict.Length ? dict[idx] : "<OOV>";
                    _output.WriteLine($"  T0: [{idx}]='{ch}' logit={val:F6}");
                }

                // Max logit
                var maxLogit = top5[0].val;
                var sumExp = 0.0;
                for (var c = 0; c < numClasses; c++)
                    sumExp += Math.Exp(output[0, 0, c] - maxLogit);
                _output.WriteLine($"  Max logit: {maxLogit:F6}, softmax: {1.0/sumExp:F6}");
            }

            // ── Варіант 2: ORT_ENABLE_ALL ──
            _output.WriteLine("\n── Варіант 2: ORT_ENABLE_ALL ──");
            using (var session = new InferenceSession(recPath, new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            }))
            {
                var tensor = new DenseTensor<float>(tensorData, new[] { 1, 3, rows, cols });
                var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
                using var results = session.Run(inputs);
                var output = results.First().AsTensor<float>();
                var dims = output.Dimensions.ToArray();
                var numClasses = dims[2];

                var top5 = Enumerable.Range(0, numClasses)
                    .Select(c => (idx: c, val: output[0, 0, c]))
                    .OrderByDescending(x => x.val).Take(5).ToList();

                foreach (var (idx, val) in top5)
                {
                    var ch = idx < dict.Length ? dict[idx] : "<OOV>";
                    _output.WriteLine($"  T0: [{idx}]='{ch}' logit={val:F6}");
                }
                var maxLogit = top5[0].val;
                var sumExp = 0.0;
                for (var c = 0; c < numClasses; c++)
                    sumExp += Math.Exp(output[0, 0, c] - maxLogit);
                _output.WriteLine($"  Max logit: {maxLogit:F6}, softmax: {1.0/sumExp:F6}");
            }

            // ── Варіант 3: ORT_DISABLE_ALL ──
            _output.WriteLine("\n── Варіант 3: ORT_DISABLE_ALL (no optimization) ──");
            using (var session = new InferenceSession(recPath, new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL
            }))
            {
                var tensor = new DenseTensor<float>(tensorData, new[] { 1, 3, rows, cols });
                var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
                using var results = session.Run(inputs);
                var output = results.First().AsTensor<float>();
                var dims = output.Dimensions.ToArray();
                var numClasses = dims[2];

                var top5 = Enumerable.Range(0, numClasses)
                    .Select(c => (idx: c, val: output[0, 0, c]))
                    .OrderByDescending(x => x.val).Take(5).ToList();

                foreach (var (idx, val) in top5)
                {
                    var ch = idx < dict.Length ? dict[idx] : "<OOV>";
                    _output.WriteLine($"  T0: [{idx}]='{ch}' logit={val:F6}");
                }
                var maxLogit = top5[0].val;
                var sumExp = 0.0;
                for (var c = 0; c < numClasses; c++)
                    sumExp += Math.Exp(output[0, 0, c] - maxLogit);
                _output.WriteLine($"  Max logit: {maxLogit:F6}, softmax: {1.0/sumExp:F6}");
            }

            // ── Варіант 4: Перевірити SHA256 моделі ──
            _output.WriteLine("\n── Перевірка моделі ──");
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(recPath))
            {
                var hash = sha.ComputeHash(stream);
                _output.WriteLine($"  File: {Path.GetFileName(recPath)}");
                _output.WriteLine($"  Size: {new FileInfo(recPath).Length} bytes");
                _output.WriteLine($"  SHA256: {BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()}");
            }

            // ── Варіант 5: Перевірити model metadata ──
            _output.WriteLine("\n── Model Metadata ──");
            using (var session = new InferenceSession(recPath))
            {
                var meta = session.ModelMetadata;
                _output.WriteLine($"  Producer: {meta.ProducerName}");
                _output.WriteLine($"  Graph name: {meta.GraphName}");
                _output.WriteLine($"  Domain: {meta.Domain}");
                _output.WriteLine($"  Description: {meta.Description}");
                _output.WriteLine($"  Version: {meta.Version}");

                foreach (var kv in session.InputMetadata)
                {
                    _output.WriteLine($"  Input: '{kv.Key}' dims=[{string.Join(", ", kv.Value.Dimensions)}] type={kv.Value.ElementDataType}");
                }
                foreach (var kv in session.OutputMetadata)
                {
                    _output.WriteLine($"  Output: '{kv.Key}' dims=[{string.Join(", ", kv.Value.Dimensions)}] type={kv.Value.ElementDataType}");
                }
            }

            _output.WriteLine("\n═══════════════════════════════════════════");
        }
    }
}