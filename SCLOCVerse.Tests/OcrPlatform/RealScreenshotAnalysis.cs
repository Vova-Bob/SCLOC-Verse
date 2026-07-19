using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using SCLOCVerse.Services.OcrPlatform.Engines.Internal;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    public class RealScreenshotAnalysis
    {
        private readonly ITestOutputHelper _output;
        public RealScreenshotAnalysis(ITestOutputHelper output) => _output = output;

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
        public void Diagnose_Recognition_RawTensorInspection()
        {
            var modelsDir = FindModelsDir();
            if (modelsDir is null) { _output.WriteLine("❌ Models not found"); return; }

            var recPath = Path.Combine(modelsDir, "ch_PP-OCRv5_rec_mobile_infer.onnx");
            var dictPath = Path.Combine(modelsDir, "ppocrv5_dict.txt");

            // ── 1. Створити ПРОСТИЙ тестовий crop ──
            // Чорне "0" на білому фоні — найпростіший можливий вхід для OCR.
            using var crop = new Mat(48, 100, MatType.CV_8UC3, new Scalar(255, 255, 255));
            Cv2.PutText(crop, "0", new Point(20, 38), HersheyFonts.HersheySimplex, 1.2, new Scalar(0, 0, 0), 2);
            _output.WriteLine($"Test crop: {crop.Cols}×{crop.Rows}, text='0'");

            // ── 2. Resize (height already 48, so ratio=1) ──
            using var resized = OcrPreprocessing.ResizeForRecognition(crop, 48);
            _output.WriteLine($"Resized: {resized.Cols}×{resized.Rows}");

            // ── 3. Normalize ──
            var tensorData = OcrPreprocessing.NormalizeForRecognition(resized);

            // ── 4. Перевірити tensor значення ──
            _output.WriteLine("\n── Tensor Value Inspection ──");
            _output.WriteLine($"  Tensor length: {tensorData.Length} (expected: 3×{resized.Rows}×{resized.Cols} = {3 * resized.Rows * resized.Cols})");

            var channelSize = resized.Rows * resized.Cols;
            var minB = tensorData.Take(channelSize).Min();
            var maxB = tensorData.Take(channelSize).Max();
            var avgB = tensorData.Take(channelSize).Average();
            _output.WriteLine($"  Channel B (0): min={minB:F4} max={maxB:F4} avg={avgB:F4}");

            var minG = tensorData.Skip(channelSize).Take(channelSize).Min();
            var maxG = tensorData.Skip(channelSize).Take(channelSize).Max();
            _output.WriteLine($"  Channel G (1): min={minG:F4} max={maxG:F4}");

            var minR = tensorData.Skip(2 * channelSize).Take(channelSize).Min();
            var maxR = tensorData.Skip(2 * channelSize).Take(channelSize).Max();
            _output.WriteLine($"  Channel R (2): min={minR:F4} max={maxR:F4}");

            // Перевірити на NaN
            var hasNaN = tensorData.Any(v => float.IsNaN(v));
            var hasInf = tensorData.Any(v => float.IsInfinity(v));
            _output.WriteLine($"  Has NaN: {hasNaN}, Has Inf: {hasInf}");

            // ── 5. Створити ONNX tensor ──
            var tensor = new DenseTensor<float>(tensorData, new[] { 1, 3, resized.Rows, resized.Cols });

            // ── 6. Run inference ──
            var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED };
            using var session = new InferenceSession(recPath, options);
            var inputName = session.InputMetadata.Keys.First();
            _output.WriteLine($"\n── ONNX Inference ──");
            _output.WriteLine($"  Input name: '{inputName}'");
            _output.WriteLine($"  Input dims: [1, 3, {resized.Rows}, {resized.Cols}]");

            var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
            using var results = session.Run(inputs);
            var output = results.First().AsTensor<float>();

            var outDims = output.Dimensions.ToArray();
            _output.WriteLine($"  Output dims: [{string.Join(", ", outDims)}]");

            if (outDims.Length == 3)
            {
                var timeSteps = outDims[1];
                var numClasses = outDims[2];
                _output.WriteLine($"  Time steps: {timeSteps}, Classes: {numClasses}");

                // ── 7. Перевірити output значення ──
                _output.WriteLine("\n── Output Inspection (per timestep) ──");

                for (var t = 0; t < Math.Min(timeSteps, 10); t++)
                {
                    // Знайти argmax
                    var bestIdx = 0;
                    var bestVal = float.MinValue;
                    double sum = 0;

                    for (var c = 0; c < numClasses; c++)
                    {
                        var val = output[0, t, c];
                        if (val > bestVal) { bestVal = val; bestIdx = c; }
                        sum += val;
                    }

                    var avgLogit = sum / numClasses;

                    // Softmax для best
                    double maxExp = bestVal;
                    double expSum = 0;
                    for (var c = 0; c < numClasses; c++)
                    {
                        expSum += Math.Exp(output[0, t, c] - maxExp);
                    }
                    var softmaxBest = Math.Exp(0) / expSum; // = 1/expSum (since bestVal-maxExp=0)

                    _output.WriteLine($"  T{t}: argmax={bestIdx} logit={bestVal:F4} avgLogit={avgLogit:F4} softmax={softmaxBest:F6}");
                }

                // ── 8. Перевірити чи output має сенс ──
                _output.WriteLine("\n── Diagnosis ──");
                var allSame = true;
                var firstVal = output[0, 0, 0];
                for (var i = 1; i < Math.Min(100, (int)(outDims[0] * outDims[1] * outDims[2])); i++)
                {
                    if (Math.Abs(output.GetValue(i) - firstVal) > 0.001f)
                    {
                        allSame = false;
                        break;
                    }
                }

                if (allSame)
                {
                    _output.WriteLine("  ❌ ALL OUTPUT VALUES ARE THE SAME — model is broken or input is invalid");
                }
                else
                {
                    _output.WriteLine("  Output varies — model is processing input");

                    // Знайти топ-5 класи для першого timestep
                    var topClasses = new List<(int idx, float val)>();
                    for (var c = 0; c < numClasses; c++)
                    {
                        topClasses.Add((c, output[0, 0, c]));
                    }
                    var top5 = topClasses.OrderByDescending(x => x.val).Take(5).ToList();

                    _output.WriteLine("  Top-5 classes for T0:");
                    var dict = File.ReadAllLines(dictPath);
                    foreach (var (idx, val) in top5)
                    {
                        var ch = idx < dict.Length ? dict[idx] : "<OOV>";
                        _output.WriteLine($"    class={idx} logit={val:F4} char='{ch}'");
                    }
                }
            }

            _output.WriteLine("\n═══════════════════════════════════════════");
        }
    }
}