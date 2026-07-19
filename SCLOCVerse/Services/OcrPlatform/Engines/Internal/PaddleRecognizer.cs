using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using SCLOCVerse.Services.OcrPlatform.Engines.Internal;
using System.IO;
using System.Text;

namespace SCLOCVerse.Services.OcrPlatform.Engines.Internal
{
    /// <summary>
    /// Text Recognition model (CRNN) для PaddleOCR PP-OCRv5/v6.
    /// Приймає crop text region (через getPerspectiveTransform → warp),
    /// повертає розпізнаний текст + confidence.
    ///
    /// Pipeline:
    /// 1. Crop + perspective transform → horizontal rectangle (height=48 для PP-OCRv5_mobile_rec)
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

        // PP-OCRv5_mobile_rec використовує height=48.
        private const int DefaultRecImageHeight = 48;

        public PaddleRecognizer(string modelPath, string dictionaryPath, int recImageHeight = DefaultRecImageHeight)
        {
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED
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
        /// Завантажити словник символів з файлу (один символ на рядок).
        /// </summary>
        private static IReadOnlyList<string> LoadDictionary(string path)
        {
            var lines = File.ReadAllLines(path);
            // PaddleOCR додає "blank" (CTC blank) + "space" автомитично.
            // Словник має format: line 0 → blank (ігнорується), далі — реальні символи.
            // Для PP-OCRv5 dictionary: 6623 lines (включаючи #blank та #space).
            return lines.ToList();
        }

        /// <summary>
        /// Greedy CTC decode: argmax per timestep + collapse repeated + remove blanks.
        /// </summary>
        private static RecognizedText CtcGreedyDecode(Tensor<float> output, IReadOnlyList<string> dictionary)
        {
            // Output shape: [1, W, C] де W = часові кроки, C = num_classes.
            // PP-OCRv5 output: dimensions [batch=1, time_steps, num_classes].
            var dims = output.Dimensions;
            if (dims.Length != 3)
            {
                return RecognizedText.Empty;
            }

            var timeSteps = dims[1];
            var numClasses = dims[2];

            // Argmax per timestep.
            var bestIndices = new int[timeSteps];
            for (var t = 0; t < timeSteps; t++)
            {
                var bestIdx = 0;
                var bestVal = float.MinValue;
                for (var c = 0; c < numClasses; c++)
                {
                    var val = output[0, t, c];
                    if (val > bestVal)
                    {
                        bestVal = val;
                        bestIdx = c;
                    }
                }
                bestIndices[t] = bestIdx;
            }

            // Collapse repeats + skip blanks (blank = last index, numClasses-1).
            var text = new StringBuilder();
            var confidences = new List<float>();
            var blankIdx = numClasses - 1;
            int? prevIdx = null;

            for (var t = 0; t < timeSteps; t++)
            {
                var idx = bestIndices[t];
                if (idx == blankIdx)
                {
                    prevIdx = null;
                    continue;
                }
                if (prevIdx == idx) continue; // Collapse repeat.

                // Look up dictionary.
                if (idx < dictionary.Count)
                {
                    var ch = dictionary[idx];
                    // Спеціальні токени: "#blank", "#space" — пропускаємо.
                    if (!ch.StartsWith("#"))
                    {
                        text.Append(ch);
                        confidences.Add(SoftmaxConfidence(output, t, idx, numClasses));
                    }
                }
                prevIdx = idx;
            }

            var avgConfidence = confidences.Count > 0 ? confidences.Average() : 0;
            return new RecognizedText
            {
                Text = text.ToString(),
                Confidence = avgConfidence
            };
        }

        /// <summary>
        /// Softmax confidence для конкретного класу в конкретному timestep.
        /// Використовується як пер-character confidence.
        /// </summary>
        private static float SoftmaxConfidence(Tensor<float> output, int t, int classIdx, int numClasses)
        {
            // Знайти max для стабільності.
            var max = float.MinValue;
            for (var c = 0; c < numClasses; c++)
            {
                var v = output[0, t, c];
                if (v > max) max = v;
            }

            // exp sum.
            double sum = 0;
            for (var c = 0; c < numClasses; c++)
            {
                sum += Math.Exp(output[0, t, c] - max);
            }

            // softmax[classIdx].
            return (float)(Math.Exp(output[0, t, classIdx] - max) / sum);
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
