using OpenCvSharp;
using SCLOCVerse.Services.OcrPlatform.Vision;
using Xunit;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Тести для NccTemplateMatcher: basic matching + edge cases.
    /// </summary>
    public class NccTemplateMatcherTests
    {
        [Fact]
        public void Find_NullScene_Throws()
        {
            var matcher = new NccTemplateMatcher();
            using var template = new Mat(10, 10, MatType.CV_8UC1, new Scalar(128));
            Assert.Throws<ArgumentNullException>(() => matcher.Find(null!, template));
        }

        [Fact]
        public void Find_EmptyScene_ReturnsEmpty()
        {
            var matcher = new NccTemplateMatcher();
            using var scene = new Mat();
            using var template = new Mat(10, 10, MatType.CV_8UC1, new Scalar(128));
            var matches = matcher.Find(scene, template);
            Assert.Empty(matches);
        }

        [Fact]
        public void Find_TemplateLargerThanScene_ReturnsEmpty()
        {
            var matcher = new NccTemplateMatcher();
            using var scene = new Mat(5, 5, MatType.CV_8UC1, new Scalar(128));
            using var template = new Mat(10, 10, MatType.CV_8UC1, new Scalar(128));
            var matches = matcher.Find(scene, template);
            Assert.Empty(matches);
        }

        [Fact]
        public void Find_ExactMatch_HighConfidence()
        {
            var matcher = new NccTemplateMatcher();
            // Сцена: білий квадрат на чорному фоні.
            using var scene = new Mat(50, 50, MatType.CV_8UC1, new Scalar(0));
            Cv2.Rectangle(scene, new Rect(10, 10, 20, 20), new Scalar(255), -1);

            // Template: білий квадрат 20×20.
            using var template = new Mat(20, 20, MatType.CV_8UC1, new Scalar(255));

            var matches = matcher.Find(scene, template, threshold: 0.5);

            Assert.NotEmpty(matches);
            Assert.True(matches[0].Confidence > 0.8);
            Assert.Equal(20, matches[0].TemplateSize.Width);
            Assert.Equal(20, matches[0].TemplateSize.Height);
        }

        [Fact]
        public void Find_NoMatch_DifferentPattern_ReturnsEmpty()
        {
            var matcher = new NccTemplateMatcher();
            // Сцена: чорний фон з діагональною лінією.
            using var scene = new Mat(50, 50, MatType.CV_8UC1, new Scalar(0));
            Cv2.Line(scene, new Point(0, 0), new Point(50, 50), new Scalar(255), 2);
            // Template: горизонтальна лінія на чорному фоні (не співпадає з діагоналлю).
            using var template = new Mat(30, 10, MatType.CV_8UC1, new Scalar(0));
            Cv2.Line(template, new Point(0, 5), new Point(30, 5), new Scalar(255), 2);

            // Дуже високий threshold — діагональ vs горизонтальна лінія.
            var matches = matcher.Find(scene, template, threshold: 0.95);
            Assert.Empty(matches);
        }

        [Fact]
        public void Find_DefaultThreshold_085()
        {
            var matcher = new NccTemplateMatcher();
            using var scene = new Mat(50, 50, MatType.CV_8UC1, new Scalar(0));
            Cv2.Rectangle(scene, new Rect(10, 10, 20, 20), new Scalar(255), -1);
            using var template = new Mat(20, 20, MatType.CV_8UC1, new Scalar(255));

            // Без явного threshold → default 0.85.
            var matches = matcher.Find(scene, template);
            Assert.NotEmpty(matches);
        }
    }
}