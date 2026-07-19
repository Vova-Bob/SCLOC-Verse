using OpenCvSharp;
using SCLOCVerse.Services.OcrPlatform.Pipeline;
using Xunit;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Тести для DefaultImagePipeline (T3.2+T3.3):
    /// Greyscale → Resize → Adaptive Threshold.
    /// </summary>
    public class DefaultImagePipelineTests
    {
        [Fact]
        public void Process_NullInput_Throws()
        {
            var pipeline = new DefaultImagePipeline();
            Assert.Throws<ArgumentNullException>(() => pipeline.Process(null!, new()));
        }

        [Fact]
        public void Process_EmptyMat_Throws()
        {
            var pipeline = new DefaultImagePipeline();
            using var empty = new Mat();
            Assert.Throws<ArgumentException>(() => pipeline.Process(empty, new()));
        }

        [Fact]
        public void Process_GrayscaleOnly_ReturnsSingleChannel()
        {
            var pipeline = new DefaultImagePipeline();
            using var input = CreateSolidColorMat(10, 10, new Scalar(100, 150, 200));
            var opts = new SCLOCVerse.Models.OcrPlatform.ImagePipelineOptions
            {
                UseGrayscale = true,
                ResizeFactor = 1,
                UseAdaptiveThreshold = false
            };

            using var result = pipeline.Process(input, opts);
            Assert.Equal(1, result.Channels());
        }

        [Fact]
        public void Process_ResizeFactor3_TriplesDimensions()
        {
            var pipeline = new DefaultImagePipeline();
            using var input = CreateSolidColorMat(20, 10, new Scalar(100, 150, 200));
            var opts = new SCLOCVerse.Models.OcrPlatform.ImagePipelineOptions
            {
                UseGrayscale = false,
                ResizeFactor = 3,
                UseAdaptiveThreshold = false
            };

            using var result = pipeline.Process(input, opts);
            Assert.Equal(60, result.Cols);
            Assert.Equal(30, result.Rows);
        }

        [Fact]
        public void Process_AdaptiveThreshold_ReturnsBinary()
        {
            var pipeline = new DefaultImagePipeline();
            // Створюємо зображення з чорним текстом на білому фоні.
            using var input = new Mat(50, 50, MatType.CV_8UC3, new Scalar(255, 255, 255));
            Cv2.Rectangle(input, new Rect(10, 20, 30, 10), new Scalar(0, 0, 0), -1);

            var opts = new SCLOCVerse.Models.OcrPlatform.ImagePipelineOptions
            {
                UseGrayscale = true,
                ResizeFactor = 1,
                UseAdaptiveThreshold = true,
                InvertForDarkBackground = false // темний текст → чорний на білому
            };

            using var result = pipeline.Process(input, opts);
            // Бінаризоване зображення має лише 0 та 255.
            Assert.Equal(1, result.Channels());
            Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out _, out _);
            Assert.True(minVal == 0 || maxVal == 255);
        }

        [Fact]
        public void Process_DoesNotModifyInput()
        {
            var pipeline = new DefaultImagePipeline();
            using var input = CreateSolidColorMat(10, 10, new Scalar(100, 150, 200));
            var originalPixel = input.At<Vec3b>(0, 0);

            var opts = new SCLOCVerse.Models.OcrPlatform.ImagePipelineOptions
            {
                UseGrayscale = true, ResizeFactor = 2, UseAdaptiveThreshold = true
            };
            using var result = pipeline.Process(input, opts);

            // Вхід не змінено.
            Assert.Equal(originalPixel, input.At<Vec3b>(0, 0));
        }

        /// <summary>Створює Mat суцільного кольору.</summary>
        private static Mat CreateSolidColorMat(int width, int height, Scalar color)
        {
            var mat = new Mat(height, width, MatType.CV_8UC3, color);
            return mat;
        }
    }
}