using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using SCLOCVerse.Services.OcrPlatform.Engines.Internal;
using System.IO;
using System.Text;

namespace SCLOCVerse.Services.OcrPlatform.Engines.Internal
{
    /// <summary>
    /// Text Recognition model (CRNN) для PaddleOCR PP-OCRv6.
    /// Приймає crop text region (через getPerspectiveTransform → warp),
    /// повертає розпізнаний текст + confidence.
    ///
    /// Pipeline:
    /// 1. Crop + perspective transform → horizontal rectangle (height=48 для PP-OCRv6_rec)
    /// 2. Normalize (ImageNet mean/std → NCHW tensor, BGR→RGB)
    /// 3. InferenceSession.Run → output [W, num_classes] (де W = часові кроки)
    /// 4. Greedy CTC decode → argmax per timestep → collapse repeats
    /// 5. Look up dictionary
    /// </summary>
    internal sealed class PaddleRecognizer : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly IReadOnlyList<string> _dictionary;
        private readonly int _recImageHeight;

        // PP-OCRv6_rec використовує height=48.
        private const int DefaultRecImageHeight = 48;

        public PaddleRecognizer(string modelPath, string dictionaryPath, int recImageHeight = DefaultRecImageHeight)
        {
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                InterOpNumThreads = 1,
                IntraOpNumThreads = Math.Min(4, Environment.ProcessorCount)
            };
            _session = new InferenceSession(modelPath, options);
            _inputName = _session.InputMetadata.Keys.First();
            _dictionary = LoadDictionary(dictionaryPath);
            _recImageHeight = recImageHeight;
        }

        /// <summary>
        /// Розпізнати текст у врізаному зображенні текстового регіону.
        /// </summary>
        public RecognizedText Recognize(Mat srcBgr)
        {
            // Крок 1: Resize до стандартної висоти (збереження пропорцій).
            using var resized = OcrPreprocessing.ResizeForRecognition(srcBgr, _recImageHeight);

            // Крок 2: Normalize → NCHW tensor.
            var tensorData = OcrPreprocessing.NormalizeForRecognition(resized);
            var tensor = new DenseTensor<float>(tensorData, new[] { 1, 3, resized.Rows, resized.Cols });

            // Крок 3: Run inference.
            var inputs = new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
            using var results = _session.Run(inputs);
            var output = results.First().AsTensor<float>();

            // Крок 4-5: CTC decode + dictionary lookup.
            return CtcGreedyDecode(output, _dictionary);
        }

        /// <summary>
        /// Завантажити словник символів ТОЧНО як офіційний RapidOCR CrnnNet.InitKeys.
        ///
        /// Forensic 2026-07-20: Root Cause розпізнавання — наша реалізація
        /// НЕ відповідала офіційній специфікації PaddleOCR/RapidOCR щодо побудови
        /// vocabulary. Файл читався як є (18383 рядки), але модель очікує 18385
        /// класів: CTC blank "#" (index 0) + file (18383) + space " " (last).
        /// Без зсуву всі індекси класів були зміщені на 1 під час CTC decoding.
        ///
        /// Офіційна логіка (RapidOCR CrnnNet.cs:50-60):
        ///   keys.Add("#");        // index 0 = CTC blank
        ///   keys.Add(line);      // file lines
        ///   keys.Add(" ");       // last index = space
        /// Разом: 18385 = output classes моделі.
        /// </summary>
        private static IReadOnlyList<string> LoadDictionary(string path)
        {
            var keys = new List<string> { "#" };
            keys.AddRange(File.ReadAllLines(path));
            keys.Add(" ");
            return keys;
        }

        /// <summary>
        /// Greedy CTC decode — ТОЧНО як офіційний RapidOCR CrnnNet.ScoreToTextLine.
        ///
        /// Forensic 2026-07-20: наш попередній decode відрізнявся від RapidOCR:
        ///   - blank = numClasses-1 (останній) замість 0 ("#")
        ///   - special tokens через ch.StartsWith("#") замість maxIndex > 0
        ///   - lastIndex скидався у null на blank замість збереження
        /// Batch-тест 45 значень довів: після виправлення словника обидва алгоритми
        /// дають ідентичний результат (45/45). Вирівнюємо з офіційною логікою,
        /// щоб не мати "своєї версії PaddleOCR".
        ///
        /// RapidOCR CrnnNet.cs ScoreToTextLine:
        ///   for i in timeSteps:
        ///     maxIndex = argmax(output[i])
        ///     if (maxIndex > 0 && maxIndex < keys.Count
        ///         && !(i > 0 && maxIndex == lastIndex))
        ///         sb.Append(keys[maxIndex])
        ///     lastIndex = maxIndex
        /// </summary>
        private static RecognizedText CtcGreedyDecode(Tensor<float> output, IReadOnlyList<string> dictionary)
        {
            // Output shape: [1, W, C] де W = часові кроки, C = num_classes.
            var dims = output.Dimensions;
            if (dims.Length != 3)
            {
                return RecognizedText.Empty;
            }

            var timeSteps = dims[1];
            var numClasses = dims[2];

            var text = new StringBuilder();
            var confidences = new List<float>();
            var lastIndex = 0; // RapidOCR: lastIndex = 0 (blank)

            for (var i = 0; i < timeSteps; i++)
            {
                // Argmax per timestep.
                var maxIndex = 0;
                var maxValue = -1000f;
                for (var j = 0; j < numClasses; j++)
                {
                    var v = output[0, i, j];
                    if (v > maxValue) { maxValue = v; maxIndex = j; }
                }

                // RapidOCR: if (maxIndex > 0 && maxIndex < keys.Count
                //            && (!(i > 0 && maxIndex == lastIndex)))
                if (maxIndex > 0 && maxIndex < dictionary.Count
                    && (!(i > 0 && maxIndex == lastIndex)))
                {
                    text.Append(dictionary[maxIndex]);
                    confidences.Add(maxValue);
                }
                lastIndex = maxIndex;
            }

            var avgConfidence = confidences.Count > 0 ? confidences.Average() : 0;
            return new RecognizedText
            {
                Text = text.ToString(),
                Confidence = avgConfidence
            };
        }

        public void Dispose()
        {
            _session.Dispose();
        }
    }

    /// <summary>
    /// Розпізнаний текст + confidence.
    /// </summary>
    internal sealed class RecognizedText
    {
        public string Text { get; init; } = string.Empty;
        public double Confidence { get; init; }

        public static RecognizedText Empty { get; } = new() { Text = string.Empty, Confidence = 0 };
    }
}
