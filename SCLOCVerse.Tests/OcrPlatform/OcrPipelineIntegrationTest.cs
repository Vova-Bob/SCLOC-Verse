using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using SCLOCVerse.Models.OcrPlatform;
using SCLOCVerse.Services.OcrPlatform.Engines;
using SCLOCVerse.Services.OcrPlatform.Engines.Internal;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    public class OcrPipelineIntegrationTest
    {
        private readonly ITestOutputHelper _output;

        public OcrPipelineIntegrationTest(ITestOutputHelper output) => _output = output;

        private string? FindModelsDir()
        {
            var candidates = new[]
            {
                "Resources/OcrModels",
                "../../../../SCLOCVerse/Resources/OcrModels",
                "../../../../../../SCLOCVerse/bin/Debug/net9.0-windows10.0.18362.0/win-x64/Resources/OcrModels",
            };
            foreach (var c in candidates)
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(Path.Combine(full, "ch_PP-OCRv5_mobile_det.onnx")))
                    return full;
            }
            return null;
        }

        [Fact]
        public void Diagnose_StepByStep_OcrPipeline()
        {
            var modelsDir = FindModelsDir();
            if (modelsDir is null)
            {
                _output.WriteLine("❌ SKIP: Models not found");
                return;
            }

            _output.WriteLine("════════════════════════════════════════════════");
            _output.WriteLine("ДІАГНОСТИКА: Покрокова перевірка OCR pipeline");
            _output.WriteLine("════════════════════════════════════════════════");

            // ── Крок 0: Перевірити model metadata ──
            _output.WriteLine("\n── Крок 0: Model Metadata ──");
            using (var detSession = new InferenceSession(Path.Combine(modelsDir, "ch_PP-OCRv5_mobile_det.onnx")))
            {
                foreach (var kv in detSession.InputMetadata)
                {
                    _output.WriteLine($"  Det input: name='{kv.Key}' dims=[{string.Join(", ", kv.Value.Dimensions)}] type={kv.Value.ElementDataType}");
                }
                foreach (var kv in detSession.OutputMetadata)
                {
                    _output.WriteLine($"  Det output: name='{kv.Key}' dims=[{string.Join(", ", kv.Value.Dimensions)}] type={kv.Value.ElementDataType}");
                }
            }

            using (var recSession = new InferenceSession(Path.Combine(modelsDir, "ch_PP-OCRv5_rec_mobile_infer.onnx")))
            {
                foreach (var kv in recSession.InputMetadata)
                {
                    _output.WriteLine($"  Rec input: name='{kv.Key}' dims=[{string.Join(", ", kv.Value.Dimensions)}] type={kv.Value.ElementDataType}");
                }
                foreach (var kv in recSession.OutputMetadata)
                {
                    _output.WriteLine($"  Rec output: name='{kv.Key}' dims=[{string.Join(", ", kv.Value.Dimensions)}] type={kv.Value.ElementDataType}");
                }
            }

            // ── Крок 1: Створити тестові зображення ──
            _output.WriteLine("\n── Крок 1: Створення тестових зображень ──");

            // Варіант A: Чорний текст на білому фоні (найкраще для OCR)
            var imgA = new Mat(100, 300, MatType.CV_8UC3, new Scalar(255, 255, 255));
            Cv2.PutText(imgA, "3385", new Point(30, 70), HersheyFonts.HersheySimplex, 2.0, new Scalar(0, 0, 0), 3);
            Cv2.ImWrite(Path.Combine(Path.GetTempPath(), "scloc_test_A_black_on_white.png"), imgA);
            _output.WriteLine($"  A: Чорний на білому, 300×100, '3385'");

            // Варіант B: Білий текст на чорному (SC HUD style)
            var imgB = new Mat(100, 300, MatType.CV_8UC3, new Scalar(0, 0, 0));
            Cv2.PutText(imgB, "3385", new Point(30, 70), HersheyFonts.HersheySimplex, 2.0, new Scalar(255, 255, 255), 3);
            Cv2.ImWrite(Path.Combine(Path.GetTempPath(), "scloc_test_B_white_on_black.png"), imgB);
            _output.WriteLine($"  B: Білий на чорному, 300×100, '3385'");

            // Варіант C: Білий на темно-синьому (ближче до SC)
            var imgC = new Mat(100, 300, MatType.CV_8UC3, new Scalar(15, 20, 35));
            Cv2.PutText(imgC, "3385", new Point(30, 70), HersheyFonts.HersheySimplex, 2.0, new Scalar(255, 220, 100), 3);
            Cv2.ImWrite(Path.Combine(Path.GetTempPath(), "scloc_test_C_yellow_on_darkblue.png"), imgC);
            _output.WriteLine($"  C: Жовтий на темно-синьому, 300×100, '3385'");

            // ── Крок 2: Detector — окремо для кожного зображення ──
            _output.WriteLine("\n── Крок 2: PaddleDetector — окремо ──");

            using var detector = new PaddleDetector(Path.Combine(modelsDir, "ch_PP-OCRv5_mobile_det.onnx"));

            foreach (var (name, img) in new[] { ("A", imgA), ("B", imgB), ("C", imgC) })
            {
                var sw = Stopwatch.StartNew();
                var boxes = detector.Detect(img, maxSideLen: 1024, boxScoreThresh: 0.5f, boxThresh: 0.3f, unclipRatio: 1.6f);
                sw.Stop();

                _output.WriteLine($"  Image {name}: {boxes.Count} boxes detected ({sw.ElapsedMilliseconds}ms)");
                for (var i = 0; i < Math.Min(boxes.Count, 3); i++)
                {
                    var box = boxes[i];
                    _output.WriteLine($"    Box {i}: score={box.Score:F4} rect={box.BoundingRect}");
                }

                img.Dispose();
            }

            // ── Крок 3: Detector + Recognizer — end-to-end для найкращого зображення ──
            _output.WriteLine("\n── Крок 3: End-to-end для Image A (чорний на білому) ──");

            // Перестворити Image A (was disposed)
            var testImg = new Mat(100, 300, MatType.CV_8UC3, new Scalar(255, 255, 255));
            Cv2.PutText(testImg, "3385", new Point(30, 70), HersheyFonts.HersheySimplex, 2.0, new Scalar(0, 0, 0), 3);

            // Padding (як в PaddleOcrEngine)
            var padded = OcrPreprocessing.MakePadding(testImg, 50);
            _output.WriteLine($"  Padded: {padded.Cols}×{padded.Rows}");

            // Detect
            var paddedBoxes = detector.Detect(padded, maxSideLen: 1024, boxScoreThresh: 0.5f, boxThresh: 0.3f, unclipRatio: 1.6f);
            _output.WriteLine($"  Detection: {paddedBoxes.Count} boxes");

            if (paddedBoxes.Count > 0)
            {
                // Recognize each box
                using var recognizer = new PaddleRecognizer(
                    Path.Combine(modelsDir, "ch_PP-OCRv5_rec_mobile_infer.onnx"),
                    Path.Combine(modelsDir, "ppocrv5_dict.txt"));

                foreach (var box in paddedBoxes.Take(3))
                {
                    var br = box.BoundingRect;
                    var cropRect = new Rect(
                        Math.Max(0, br.X), Math.Max(0, br.Y),
                        Math.Min(br.Width, padded.Cols - br.X),
                        Math.Min(br.Height, padded.Rows - br.Y));

                    if (cropRect.Width <= 0 || cropRect.Height <= 0) continue;

                    using var crop = new Mat(padded, cropRect);
                    _output.WriteLine($"  Crop: {crop.Cols}×{crop.Rows} at ({cropRect.X},{cropRect.Y})");

                    // Resize for recognition
                    using var resized = OcrPreprocessing.ResizeForRecognition(crop, 48);
                    _output.WriteLine($"  Resized for rec: {resized.Cols}×{resized.Rows}");

                    // Recognize
                    var recResult = recognizer.Recognize(crop);
                    _output.WriteLine($"  Recognition: text='{recResult.Text}' confidence={recResult.Confidence:F4}");
                }
            }
            else
            {
                _output.WriteLine("  ❌ Detector не знайшов жодного текстового регіону.");
                _output.WriteLine("  Можливі причини:");
                _output.WriteLine("    1. Тестове зображення занадто просте для DB детектора");
                _output.WriteLine("    2. Параметри threshold/dilate потребують тюнінгу");
                _output.WriteLine("    3. Розмір зображення не підходить для моделі");
            }

            padded.Dispose();
            testImg.Dispose();

            // ── Крок 4: Спробувати з різними thresholds ──
            _output.WriteLine("\n── Крок 4: Тест з різними thresholds ──");

            var tImg = new Mat(100, 300, MatType.CV_8UC3, new Scalar(255, 255, 255));
            Cv2.PutText(tImg, "3385", new Point(30, 70), HersheyFonts.HersheySimplex, 2.0, new Scalar(0, 0, 0), 3);
            var tPadded = OcrPreprocessing.MakePadding(tImg, 50);

            foreach (var (thresh, scoreThresh) in new[] { (0.3f, 0.5f), (0.2f, 0.3f), (0.1f, 0.2f), (0.05f, 0.1f) })
            {
                var b = detector.Detect(tPadded, maxSideLen: 1024, boxScoreThresh: scoreThresh, boxThresh: thresh, unclipRatio: 1.6f);
                _output.WriteLine($"  boxThresh={thresh}, scoreThresh={scoreThresh} → {b.Count} boxes");
            }

            tPadded.Dispose();
            tImg.Dispose();

            _output.WriteLine("\n════════════════════════════════════════════════");
            _output.WriteLine("Діагностика завершена.");
            _output.WriteLine("════════════════════════════════════════════════");
        }
    }
}