using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Повний forensic OCR pipeline за 11 пунктами.
    /// Жодних припущень — лише докази.
    /// </summary>
    public class OcrForensicPipeline
    {
        private readonly ITestOutputHelper _output;
        public OcrForensicPipeline(ITestOutputHelper output) => _output = output;

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
        // Допоміжне: створити crop з текстом
        // ─────────────────────────────────────────────────────
        private static Mat CreateTextCrop(string text, int height = 48, int width = 200)
        {
            var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(255, 255, 255));
            Cv2.PutText(mat, text, new Point(10, height - 12), HersheyFonts.HersheySimplex,
                1.0, new Scalar(0, 0, 0), 2);
            return mat;
        }

        // ─────────────────────────────────────────────────────
        // Допоміжне: preprocessing ТОЧНО як RapidOCR
        // ─────────────────────────────────────────────────────
        // RapidOCR OcrUtils.SubstractMeanNormalize:
        //   Image<Rgb, byte> srcImg = src.ToImage<Rgb, byte>();
        //   data = value * normVals[ch] - meanVals[ch] * normVals[ch]
        //   = (value - mean) * norm  (алгебраїчно однаково)
        // Mean = 127.5, Norm = 1/127.5
        // Channel order: RGB (ToImage<Rgb, byte>)
        private static DenseTensor<float> PreprocessRapidOcrStyle(Mat srcBgr, bool useRgb)
        {
            var rows = srcBgr.Rows;
            var cols = srcBgr.Cols;
            var tensor = new DenseTensor<float>(new[] { 1, 3, rows, cols });

            // channelOrder: RGB = [2,1,0] (BGR→RGB), BGR = [0,1,2]
            var order = useRgb ? new[] { 2, 1, 0 } : new[] { 0, 1, 2 };
            var mean = 127.5f;
            var norm = 1f / 127.5f;

            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    var p = srcBgr.At<Vec3b>(r, c);
                    for (var ch = 0; ch < 3; ch++)
                    {
                        var srcCh = order[ch];
                        var val = (p[srcCh] - mean) * norm;
                        tensor[0, ch, r, c] = val;
                    }
                }
            return tensor;
        }

        // ─────────────────────────────────────────────────────
        // Допоміжне: завантажити dictionary ТОЧНО як RapidOCR
        // ─────────────────────────────────────────────────────
        // RapidOCR CrnnNet.InitKeys:
        //   keys.Add("#");        // index 0 = blank
        //   keys.Add(line);       // file lines
        //   keys.Add(" ");        // last = space
        private static List<string> LoadDictRapidOcrStyle(string path)
        {
            var keys = new List<string> { "#" };
            foreach (var line in File.ReadAllLines(path))
                keys.Add(line);
            keys.Add(" ");
            return keys;
        }

        // ─────────────────────────────────────────────────────
        // Допоміжне: завантажити dictionary БЕЗ зсуву (наш поточний код)
        // ─────────────────────────────────────────────────────
        private static List<string> LoadDictRaw(string path)
        {
            return File.ReadAllLines(path).ToList();
        }

        // ─────────────────────────────────────────────────────
        // Допоміжне: run inference + вивести Top-20
        // ─────────────────────────────────────────────────────
        private void RunAndPrint(InferenceSession session, string inputName,
            DenseTensor<float> tensor, List<string> dict, string label)
        {
            var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
            using var results = session.Run(inputs);
            var output = results.First().AsTensor<float>();
            var dims = output.Dimensions.ToArray();

            _output.WriteLine($"\n{'='*60}");
            _output.WriteLine($"LABEL: {label}");
            _output.WriteLine($"Output dims: [{string.Join(", ", dims)}]");
            _output.WriteLine($"Dict size: {dict.Count}");
            _output.WriteLine($"Output classes: {dims[2]}");
            _output.WriteLine($"Dict == classes: {dict.Count == dims[2]}");

            var timeSteps = dims[1];
            var numClasses = dims[2];

            // Top-20 для ПЕРШОГО timestep (не blank-кроку)
            // Спочатку знайдемо timestep з найвищим non-blank logit
            var bestT = 0;
            var bestNonBlankLogit = float.MinValue;
            for (var t = 0; t < timeSteps; t++)
            {
                // Знайти max logit на цьому timestep
                var maxIdx = 0;
                var maxVal = float.MinValue;
                for (var c = 0; c < numClasses; c++)
                {
                    var v = output[0, t, c];
                    if (v > maxVal) { maxVal = v; maxIdx = c; }
                }
                // Якщо це не blank (class 0 у RapidOCR style), перевірити
                if (maxIdx != 0 && maxVal > bestNonBlankLogit)
                {
                    bestNonBlankLogit = maxVal;
                    bestT = t;
                }
            }

            _output.WriteLine($"\n--- Top-20 at T={bestT} (best non-blank timestep) ---");
            _output.WriteLine($"{"Class",8} {"Logit",12} {"Prob",10} {"DictSymbol",12}");
            _output.WriteLine(new string('-', 8) + " " + new string('-', 12) + " " + new string('-', 10) + " " + new string('-', 12));

            var top20 = Enumerable.Range(0, numClasses)
                .Select(c => (idx: c, val: output[0, bestT, c]))
                .OrderByDescending(x => x.val).Take(20).ToList();

            // Обчислити softmax для цього timestep
            var maxLogit = top20[0].val;
            double sumExp = 0;
            for (var c = 0; c < numClasses; c++)
                sumExp += Math.Exp(output[0, bestT, c] - maxLogit);

            foreach (var (idx, val) in top20)
            {
                var prob = Math.Exp(val - maxLogit) / sumExp;
                var sym = idx < dict.Count ? dict[idx] : "<OOV>";
                // Безпечний вивід unicode
                var safeSym = sym switch
                {
                    " " => "<space>",
                    "#" => "<blank>",
                    _ => sym.Length > 1 ? $"[{sym.Length}ch]" : sym
                };
                _output.WriteLine($"{idx,8} {val,12:F6} {prob,10:F6} {safeSym,12}");
            }

            // Також вивести T=0 (перший timestep — зазвичай blank)
            _output.WriteLine($"\n--- Top-5 at T=0 (first timestep) ---");
            var top5t0 = Enumerable.Range(0, numClasses)
                .Select(c => (idx: c, val: output[0, 0, c]))
                .OrderByDescending(x => x.val).Take(5).ToList();
            var maxT0 = top5t0[0].val;
            double sumT0 = 0;
            for (var c = 0; c < numClasses; c++)
                sumT0 += Math.Exp(output[0, 0, c] - maxT0);
            foreach (var (idx, val) in top5t0)
            {
                var prob = Math.Exp(val - maxT0) / sumT0;
                var sym = idx < dict.Count ? dict[idx] : "<OOV>";
                var safeSym = sym switch
                {
                    " " => "<space>",
                    "#" => "<blank>",
                    _ => sym.Length > 1 ? $"[{sym.Length}ch]" : sym
                };
                _output.WriteLine($"{idx,8} {val,12:F6} {prob,10:F6} {safeSym,12}");
            }

            // Повний CTC decode (greedy)
            _output.WriteLine($"\n--- Full CTC greedy decode (RapidOCR style) ---");
            var sb = new StringBuilder();
            var lastIndex = -1;
            for (var i = 0; i < timeSteps; i++)
            {
                var maxIdx = 0;
                var maxVal = float.MinValue;
                for (var j = 0; j < numClasses; j++)
                {
                    var idx = i * numClasses + j;
                    // output flattened: [batch, time, class] → [0, i, j]
                    var v = output[0, i, j];
                    if (v > maxVal) { maxVal = v; maxIdx = j; }
                }
                // RapidOCR: if (maxIndex > 0 && maxIndex < keys.Count && (!(i > 0 && maxIndex == lastIndex)))
                if (maxIdx > 0 && maxIdx < dict.Count && (!(i > 0 && maxIdx == lastIndex)))
                {
                    sb.Append(dict[maxIdx]);
                }
                lastIndex = maxIdx;
            }
            _output.WriteLine($"Decoded text: '{sb}'");
            _output.WriteLine($"{'='*60}\n");
        }

        // ═════════════════════════════════════════════════════
        // ГОЛОВНИЙ ТЕСТ: повний forensic по 11 пунктах
        // ═════════════════════════════════════════════════════
        [Fact]
        public void Full_Forensic_11_Points()
        {
            var modelsDir = FindModelsDir();
            if (modelsDir is null) { _output.WriteLine("❌ Models not found"); return; }

            var recPath = Path.Combine(modelsDir, "ch_PP-OCRv5_rec_mobile_infer.onnx");
            var dictPath = Path.Combine(modelsDir, "ppocrv5_dict.txt");

            // ─────────────────────────────────────────────
            // ПУНКТ 1: Модель — name, input, output, shapes
            // ─────────────────────────────────────────────
            _output.WriteLine("╔══════════════════════════════════════════╗");
            _output.WriteLine("║  ПУНКТ 1: Model identity                 ║");
            _output.WriteLine("╚══════════════════════════════════════════╝");

            using var session = new InferenceSession(recPath);
            var inputName = session.InputMetadata.Keys.First();
            var outputName = session.OutputMetadata.Keys.First();
            var meta = session.ModelMetadata;

            _output.WriteLine($"  Model file: {Path.GetFileName(recPath)}");
            _output.WriteLine($"  Model size: {new FileInfo(recPath).Length} bytes");
            _output.WriteLine($"  Producer: '{meta.ProducerName}'");
            _output.WriteLine($"  Graph name: '{meta.GraphName}'");
            _output.WriteLine($"  Domain: '{meta.Domain}'");
            _output.WriteLine($"  Version: {meta.Version}");
            _output.WriteLine($"  Description: '{meta.Description}'");
            _output.WriteLine($"  Input name: '{inputName}'");
            _output.WriteLine($"  Input shape: [{string.Join(", ", session.InputMetadata[inputName].Dimensions)}]");
            _output.WriteLine($"  Input dtype: {session.InputMetadata[inputName].ElementDataType}");
            _output.WriteLine($"  Output name: '{outputName}'");
            _output.WriteLine($"  Output shape: [{string.Join(", ", session.OutputMetadata[outputName].Dimensions)}]");
            _output.WriteLine($"  Output dtype: {session.OutputMetadata[outputName].ElementDataType}");

            // ─────────────────────────────────────────────
            // ПУНКТ 2: Dictionary vs output classes
            // ─────────────────────────────────────────────
            _output.WriteLine("\n╔══════════════════════════════════════════╗");
            _output.WriteLine("║  ПУНКТ 2: Dictionary compatibility       ║");
            _output.WriteLine("╚══════════════════════════════════════════╝");

            var rawDict = LoadDictRaw(dictPath);
            var rapidDict = LoadDictRapidOcrStyle(dictPath);
            var outputClasses = session.OutputMetadata[outputName].Dimensions.Last();

            _output.WriteLine($"  Output classes: {outputClasses}");
            _output.WriteLine($"  Raw dict entries (file lines): {rawDict.Count}");
            _output.WriteLine($"  RapidOCR dict entries (# + file + space): {rapidDict.Count}");
            _output.WriteLine($"  Diff (classes - rawDict): {outputClasses - rawDict.Count}");
            _output.WriteLine($"  Diff (classes - rapidDict): {outputClasses - rapidDict.Count}");
            _output.WriteLine($"  → Raw dict (our code): {(rawDict.Count == outputClasses ? "MATCH" : "MISMATCH")}");
            _output.WriteLine($"  → RapidOCR dict (#+space): {(rapidDict.Count == outputClasses ? "MATCH" : "MISMATCH")}");

            _output.WriteLine($"\n  Raw dict [0]: '{rawDict[0]}'");
            _output.WriteLine($"  RapidOCR dict [0]: '{rapidDict[0]}' (should be '#')");
            _output.WriteLine($"  RapidOCR dict [1]: '{rapidDict[1]}' (should = raw[0])");
            _output.WriteLine($"  Raw dict [16162]: '{rawDict[16162]}' (should be '0')");
            _output.WriteLine($"  RapidOCR dict [16163]: '{rapidDict[16163]}' (should be '0' with offset)");
            _output.WriteLine($"  Raw dict[last={rawDict.Count-1}]: '{rawDict[rawDict.Count-1]}'");
            _output.WriteLine($"  RapidOCR dict[last={rapidDict.Count-1}]: '{rapidDict[rapidDict.Count-1]}' (should be ' ')");

            // ─────────────────────────────────────────────
            // ПУНКТ 3, 4, 5, 6, 7: Preprocessing перевірки
            // ─────────────────────────────────────────────
            _output.WriteLine("\n╔══════════════════════════════════════════╗");
            _output.WriteLine("║  ПУНКТ 3-7: Preprocessing forensic       ║");
            _output.WriteLine("╚══════════════════════════════════════════╝");

            // Створити тестовий crop "3385"
            using var crop3385 = CreateTextCrop("3385", 48, 200);

            // Зберегти original crop
            var outDir = Path.Combine(Path.GetTempPath(), "ocr_forensic");
            Directory.CreateDirectory(outDir);
            Cv2.ImWrite(Path.Combine(outDir, "01_original_3385.png"), crop3385);
            _output.WriteLine($"  Saved original: {Path.Combine(outDir, "01_original_3385.png")}");
            _output.WriteLine($"  Original size: {crop3385.Cols}×{crop3385.Rows}, channels: {crop3385.Channels()}");

            // Resize до height=48 (вже 48, але для тесту)
            using var resized = new Mat();
            Cv2.Resize(crop3385, resized, new Size(200, 48), 0, 0, InterpolationFlags.Linear);
            Cv2.ImWrite(Path.Combine(outDir, "02_resized_3385.png"), resized);
            _output.WriteLine($"  Resized: {resized.Cols}×{resized.Rows}");

            // ПУНКТ 5: Tensor layout — NCHW
            _output.WriteLine($"\n  ПУНКТ 5: Tensor layout");
            _output.WriteLine($"  DenseTensor dims: [1, 3, {resized.Rows}, {resized.Cols}] = NCHW ✓");

            // ПУНКТ 6: DataType
            _output.WriteLine($"\n  ПУНКТ 6: DataType");
            _output.WriteLine($"  Tensor type: float (float32) ✓");
            _output.WriteLine($"  Input metadata dtype: {session.InputMetadata[inputName].ElementDataType}");

            // ПУНКТ 3: Channel order — 2 варіанти
            // ПУНКТ 7: Input range — 127.5/127.5 (RapidOCR standard)
            _output.WriteLine($"\n  ПУНКТ 3: Channel order (RGB vs BGR)");
            _output.WriteLine($"\n  ПУНКТ 7: Input range = (x - 127.5) / 127.5 → [-1, 1]");

            // Зберегти tensor статистику для обох варіантів
            var tensorRgb = PreprocessRapidOcrStyle(resized, useRgb: true);
            var tensorBgr = PreprocessRapidOcrStyle(resized, useRgb: false);

            // Перевірити чи тензори дійсно різні (для кольорового зображення)
            float minRgb = float.MaxValue, maxRgb = float.MinValue;
            float minBgr = float.MaxValue, maxBgr = float.MinValue;
            var tensorSize = 3 * resized.Rows * resized.Cols;
            for (var i = 0; i < tensorSize; i++)
            {
                var vr = tensorRgb[0, 0, 0, 0]; // just for stats
                if (i < 100)
                {
                    // sample
                }
            }

            // Простий min/max через LINQ
            var rgbFlat = tensorRgb.ToArray();
            var bgrFlat = tensorBgr.ToArray();
            _output.WriteLine($"  RGB tensor: min={rgbFlat.Min():F4} max={rgbFlat.Max():F4}");
            _output.WriteLine($"  BGR tensor: min={bgrFlat.Min():F4} max={bgrFlat.Max():F4}");

            // Чи тензори різні? (для чорно-білого тексту RGB==BGR)
            var sameCount = 0;
            for (var i = 0; i < tensorSize; i++)
                if (Math.Abs(rgbFlat[i] - bgrFlat[i]) < 0.001f) sameCount++;
            _output.WriteLine($"  RGB==BGR pixels: {sameCount}/{tensorSize} ({100.0*sameCount/tensorSize:F1}%)");
            _output.WriteLine($"  → Для чорно-білого тексту RGB==BGR (очікувано)");

            // ─────────────────────────────────────────────
            // ПУНКТ 8 + 9: Top-20 + реакція на різні входи
            // ─────────────────────────────────────────────
            _output.WriteLine("\n╔══════════════════════════════════════════╗");
            _output.WriteLine("║  ПУНКТ 8-9: Top-20 + input reactivity    ║");
            _output.WriteLine("╚══════════════════════════════════════════╝");

            // 4 різних входи + 2 channel orders + 2 dict styles
            var testInputs = new[]
            {
                ("3385", CreateTextCrop("3385", 48, 200)),
                ("8888", CreateTextCrop("8888", 48, 200)),
                ("1111", CreateTextCrop("1111", 48, 200)),
                ("AAAA", CreateTextCrop("AAAA", 48, 200)),
            };

            foreach (var (text, crop) in testInputs)
            {
                // Resize
                using var r = new Mat();
                Cv2.Resize(crop, r, new Size(200, 48), 0, 0, InterpolationFlags.Linear);

                // RapidOCR style: RGB + 127.5 normalization + dict with # and space
                var tensor = PreprocessRapidOcrStyle(r, useRgb: true);
                _output.WriteLine($"\n>>> Input: '{text}' | RGB | dict+#/space | norm=127.5");
                RunAndPrint(session, inputName, tensor, rapidDict, $"'{text}' RGB+127.5+dict#/space");

                crop.Dispose();
            }

            // ─────────────────────────────────────────────
            // ПУНКТ 10: Порівняння RapidOCR-style vs наш поточний код
            // ─────────────────────────────────────────────
            _output.WriteLine("\n╔══════════════════════════════════════════╗");
            _output.WriteLine("║  ПУНКТ 10: RapidOCR-style vs our code    ║");
            _output.WriteLine("╚══════════════════════════════════════════╝");

            using var cropTest = CreateTextCrop("3385", 48, 200);
            using var resizedTest = new Mat();
            Cv2.Resize(cropTest, resizedTest, new Size(200, 48), 0, 0, InterpolationFlags.Linear);

            // A: RapidOCR style (RGB + 127.5 + dict#/space)
            _output.WriteLine("\n--- A: RapidOCR-style (RGB + 127.5 + dict#/space) ---");
            var tensorA = PreprocessRapidOcrStyle(resizedTest, useRgb: true);
            RunAndPrint(session, inputName, tensorA, rapidDict, "A: RapidOCR-style");

            // B: Our current code (BGR + 127.5 + raw dict)
            _output.WriteLine("\n--- B: Our current code (BGR + 127.5 + raw dict) ---");
            var tensorB = PreprocessRapidOcrStyle(resizedTest, useRgb: false);
            RunAndPrint(session, inputName, tensorB, rawDict, "B: Our current code");

            // C: RGB + 127.5 + raw dict (only channel fix, no dict fix)
            _output.WriteLine("\n--- C: RGB + 127.5 + raw dict (channel fix only) ---");
            var tensorC = PreprocessRapidOcrStyle(resizedTest, useRgb: true);
            RunAndPrint(session, inputName, tensorC, rawDict, "C: RGB + raw dict");

            // D: BGR + 127.5 + dict#/space (only dict fix, no channel fix)
            _output.WriteLine("\n--- D: BGR + 127.5 + dict#/space (dict fix only) ---");
            var tensorD = PreprocessRapidOcrStyle(resizedTest, useRgb: false);
            RunAndPrint(session, inputName, tensorD, rapidDict, "D: BGR + dict#/space");

            // ─────────────────────────────────────────────
            // Підсумок
            // ─────────────────────────────────────────────
            _output.WriteLine("\n╔══════════════════════════════════════════╗");
            _output.WriteLine("║  ROOT CAUSE ANALYSIS SUMMARY             ║");
            _output.WriteLine("╚══════════════════════════════════════════╝");
            _output.WriteLine($"  Output classes: {outputClasses}");
            _output.WriteLine($"  Raw dict (our code): {rawDict.Count} → MISMATCH by {outputClasses - rawDict.Count}");
            _output.WriteLine($"  RapidOCR dict (#+space): {rapidDict.Count} → {(rapidDict.Count == outputClasses ? "MATCH" : "MISMATCH")}");
            _output.WriteLine("");
            _output.WriteLine("  Compare decoded text across A/B/C/D:");
            _output.WriteLine("  A (RGB + dict#/space) → should produce '3385'");
            _output.WriteLine("  B (BGR + raw dict)    → our current broken code");
            _output.WriteLine("  C (RGB + raw dict)    → channel fix only");
            _output.WriteLine("  D (BGR + dict#/space) → dict fix only");
            _output.WriteLine("");
            _output.WriteLine("  Whichever produces '3385' = the correct fix.");
            _output.WriteLine("  If NONE produces '3385' → problem is deeper (model, ONNX Runtime, etc).");

            session.Dispose();
        }
    }
}