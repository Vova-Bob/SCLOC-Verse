using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text;
using Xunit;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Тести для CTC Greedy Decode логіки (PaddleRecognizer.CtcGreedyDecode).
    /// Мокуємо ONNX output Tensor з known argmax → перевіряємо decoded text.
    /// </summary>
    public class CtcDecodeTests
    {
        /// <summary>
        /// Створює synthetic ONNX output tensor [1, timeSteps, numClasses]
        /// де кожен timestep має argmax = вказаний index.
        /// </summary>
        private static Tensor<float> CreateOutput(int timeSteps, int numClasses, int[] argmaxIndices)
        {
            var data = new float[timeSteps * numClasses];
            for (var t = 0; t < timeSteps; t++)
            {
                for (var c = 0; c < numClasses; c++)
                {
                    data[t * numClasses + c] = (c == argmaxIndices[t]) ? 10.0f : 1.0f;
                }
            }
            return new DenseTensor<float>(data, new[] { 1, timeSteps, numClasses });
        }

        [Fact]
        public void SimpleSequence_DecodesCorrectly()
        {
            // Словник: 0="A", 1="B", 2="C", 3=blank
            var dict = new List<string> { "A", "B", "C", "#blank" };
            var numClasses = 4;
            var timeSteps = 3;
            // Argmax: [0, 1, 2] → "ABC"
            var output = CreateOutput(timeSteps, numClasses, new[] { 0, 1, 2 });

            var result = Decode(output, dict);

            Assert.Equal("ABC", result.Text);
            Assert.True(result.Confidence > 0.9); // softmax для 10.0 vs 1.0 ~ 0.99
        }

        [Fact]
        public void RepeatedCharacters_Collapse()
        {
            // Словник: 0="A", 1=blank
            var dict = new List<string> { "A", "#blank" };
            var numClasses = 2;
            // Argmax: [0, 0, 0] → "A" (collapse repeats)
            var output = CreateOutput(3, numClasses, new[] { 0, 0, 0 });

            var result = Decode(output, dict);
            Assert.Equal("A", result.Text);
        }

        [Fact]
        public void BlankBetweenRepeats_KeepsBoth()
        {
            // Словник: 0="A", 1=blank
            var dict = new List<string> { "A", "#blank" };
            var numClasses = 2;
            // Argmax: [0, 1, 0] → "AA" (blank розділяє → не collapse)
            var output = CreateOutput(3, numClasses, new[] { 0, 1, 0 });

            var result = Decode(output, dict);
            Assert.Equal("AA", result.Text);
        }

        [Fact]
        public void AllBlanks_ReturnsEmpty()
        {
            var dict = new List<string> { "A", "B", "#blank" };
            var numClasses = 3;
            var output = CreateOutput(5, numClasses, new[] { 2, 2, 2, 2, 2 });

            var result = Decode(output, dict);
            Assert.Equal("", result.Text);
        }

        [Fact]
        public void SpecialTokens_Skipped()
        {
            // "#blank" та "#space" — пропускаються (не входять у текст)
            var dict = new List<string> { "A", "#space", "B", "#blank" };
            var numClasses = 4;
            var output = CreateOutput(3, numClasses, new[] { 0, 1, 2 });

            var result = Decode(output, dict);
            Assert.Equal("AB", result.Text); // #space пропущено
        }

        [Fact]
        public void WrongDimensions_ReturnsEmpty()
        {
            var dict = new List<string> { "A", "B" };
            // 2D tensor замість 3D — помилковий формат
            var output = new DenseTensor<float>(new float[] { 1, 2 }, new[] { 1, 2 });

            var result = Decode(output, dict);
            Assert.Equal("", result.Text);
        }

        [Fact]
        public void Confidence_NonZero_ForValidDecode()
        {
            var dict = new List<string> { "X", "#blank" };
            var numClasses = 2;
            var output = CreateOutput(1, numClasses, new[] { 0 });

            var result = Decode(output, dict);
            Assert.True(result.Confidence > 0 && result.Confidence <= 1.0);
        }

        /// <summary>
        /// Inline copy of CTC decode logic (з PaddleRecognizer.CtcGreedyDecode).
        /// Дозволяє тестувати алгоритм без завантаження ONNX model.
        /// </summary>
        private static (string Text, double Confidence) Decode(
            Tensor<float> output, IReadOnlyList<string> dictionary)
        {
            var dims = output.Dimensions;
            if (dims.Length != 3) return ("", 0);

            var timeSteps = dims[1];
            var numClasses = dims[2];

            var bestIndices = new int[timeSteps];
            for (var t = 0; t < timeSteps; t++)
            {
                var bestIdx = 0;
                var bestVal = float.MinValue;
                for (var c = 0; c < numClasses; c++)
                {
                    var val = output[0, t, c];
                    if (val > bestVal) { bestVal = val; bestIdx = c; }
                }
                bestIndices[t] = bestIdx;
            }

            var text = new StringBuilder();
            var confidences = new List<float>();
            var blankIdx = numClasses - 1;
            int? prevIdx = null;

            for (var t = 0; t < timeSteps; t++)
            {
                var idx = bestIndices[t];
                if (idx == blankIdx) { prevIdx = null; continue; }
                if (prevIdx == idx) continue;

                if (idx < dictionary.Count)
                {
                    var ch = dictionary[idx];
                    if (!ch.StartsWith("#"))
                    {
                        text.Append(ch);
                        // Softmax confidence
                        float max = float.MinValue;
                        for (var c = 0; c < numClasses; c++)
                        {
                            var v = output[0, t, c];
                            if (v > max) max = v;
                        }
                        double sum = 0;
                        for (var c = 0; c < numClasses; c++)
                        {
                            sum += Math.Exp(output[0, t, c] - max);
                        }
                        confidences.Add((float)(Math.Exp(output[0, t, idx] - max) / sum));
                    }
                }
                prevIdx = idx;
            }

            var avgConf = confidences.Count > 0 ? confidences.Average() : 0;
            return (text.ToString(), avgConf);
        }
    }
}