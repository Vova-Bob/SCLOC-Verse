using OpenCvSharp;
using SCLOCVerse.Services.OcrPlatform.Coordinator;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Тести для pre-filter <see cref="OcrCoordinator.HasContent"/>.
    ///
    /// <para>Перевіряє, що pre-filter коректно розрізняє:</para>
    /// <list type="bullet">
    /// <item>Порожню область (чорний фон) → false → OCR не запускається.</item>
    /// <item>Область з яскравими пікселями (цифри HUD) → true → OCR запускається.</item>
    /// <item>Область з мінімальною кількістю яскравих пікселів (&lt; 0.5%) → false.</item>
    /// </list>
    /// </summary>
    public class ContentPreFilterTests
    {
        private readonly ITestOutputHelper _output;
        public ContentPreFilterTests(ITestOutputHelper output) => _output = output;

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 1: Чорний Mat (порожня область) → false
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void HasContent_AllBlack_ReturnsFalse()
        {
            using var mat = new Mat(30, 100, MatType.CV_8UC3, Scalar.Black);
            var result = OcrCoordinator.HasContent(mat);

            _output.WriteLine($"All-black 30×100 → HasContent = {result}");
            Assert.False(result, "Чорна область не повинна запускати OCR");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 2: Темно-сірий Mat (низька яскравість) → false
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void HasContent_DarkGray_ReturnsFalse()
        {
            using var mat = new Mat(30, 100, MatType.CV_8UC3, new Scalar(50, 50, 50));
            var result = OcrCoordinator.HasContent(mat);

            _output.WriteLine($"Dark-gray 30×100 (val=50) → HasContent = {result}");
            Assert.False(result, "Темна область (яскравість 50 < threshold 120) не повинна запускати OCR");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 3: Білий Mat (повністю яскравий) → true
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void HasContent_AllWhite_ReturnsTrue()
        {
            using var mat = new Mat(30, 100, MatType.CV_8UC3, Scalar.White);
            var result = OcrCoordinator.HasContent(mat);

            _output.WriteLine($"All-white 30×100 → HasContent = {result}");
            Assert.True(result, "Біла область повинна запускати OCR");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 4: Чорний Mat з кількома яскравими пікселями (симуляція цифр)
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void HasContent_BlackWithBrightPixels_ReturnsTrue()
        {
            using var mat = new Mat(30, 100, MatType.CV_8UC3, Scalar.Black);

            // Симулюємо цифри: малюємо кілька яскравих прямокутників.
            // Цифри займають ~2-5% площі HUD регіону.
            Cv2.Rectangle(mat, new Rect(10, 5, 20, 20), Scalar.White, -1);  // 400 px з 3000 = 13%
            Cv2.Rectangle(mat, new Rect(40, 5, 20, 20), Scalar.White, -1);  // +400 px = 27%

            var result = OcrCoordinator.HasContent(mat);

            _output.WriteLine($"Black with 2 white rectangles (27% bright) → HasContent = {result}");
            Assert.True(result, "Область з яскравими цифрами повинна запускати OCR");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 5: Чорний Mat з мінімальною кількістю яскравих пікселів (< 0.5%)
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void HasContent_BelowMinFraction_ReturnsFalse()
        {
            using var mat = new Mat(30, 100, MatType.CV_8UC3, Scalar.Black);

            // 1 піксель з 3000 = 0.03% — значно менше за 0.5% threshold.
            mat.Set(0, 0, new Vec3b(255, 255, 255));

            var result = OcrCoordinator.HasContent(mat);

            _output.WriteLine($"Black with 1 white pixel (0.03% bright) → HasContent = {result}");
            Assert.False(result, "Область з < 0.5% яскравих пікселів не повинна запускати OCR");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 6: Thresholds — константи перевірки
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void Thresholds_AreExpectedValues()
        {
            _output.WriteLine($"ContentBrightnessThreshold = {OcrCoordinator.ContentBrightnessThreshold}");
            _output.WriteLine($"ContentMinFraction = {OcrCoordinator.ContentMinFraction}");

            Assert.Equal(120, OcrCoordinator.ContentBrightnessThreshold);
            Assert.Equal(0.005, OcrCoordinator.ContentMinFraction);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 7: Яскравість рівно на threshold — межовий кейс
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void HasContent_PixelAtThreshold_ReturnsFalse()
        {
            using var mat = new Mat(30, 100, MatType.CV_8UC3, new Scalar(120, 120, 120));
            var result = OcrCoordinator.HasContent(mat);

            // Яскравість 120 == threshold → > 120 = false → 0% bright → false.
            _output.WriteLine($"All-gray 30×100 (val=120, at threshold) → HasContent = {result}");
            Assert.False(result, "Пікселі на threshold (== 120, не > 120) не повинні запускати OCR");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 8: Яскравість вище threshold + достатня частка → true
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void HasContent_PixelAboveThreshold_ReturnsTrue()
        {
            using var mat = new Mat(30, 100, MatType.CV_8UC3, new Scalar(121, 121, 121));
            var result = OcrCoordinator.HasContent(mat);

            // Яскравість 121 > 120 → 100% bright → true.
            _output.WriteLine($"All-gray 30×100 (val=121, above threshold) → HasContent = {result}");
            Assert.True(result, "Пікселі вище threshold (> 120) з 100% часткою повинні запускати OCR");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 9: Бірюзовий колір (SC HUD) → true
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void HasContent_CyanColor_SC_HUD_Style_ReturnsTrue()
        {
            // SC HUD сигнатура — бірюзові/блакитні цифри (R~100, G~255, B~255).
            // Grayscale яскравість ≈ 0.299×100 + 0.587×255 + 0.114×255 ≈ 210.
            using var mat = new Mat(30, 100, MatType.CV_8UC3, new Scalar(100, 255, 255));
            var result = OcrCoordinator.HasContent(mat);

            _output.WriteLine($"Cyan 30×100 (SC HUD style, grayscale≈210) → HasContent = {result}");
            Assert.True(result, "Бірюзові цифри SC HUD (grayscale ≈ 210 > 120) повинні запускати OCR");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 10: Різні розміри Mat — pre-filter стійкий до розміру
        // ─────────────────────────────────────────────────────────────
        [Theory]
        [InlineData(10, 20)]    // дуже малий
        [InlineData(30, 100)]   // типовий HUD регіон
        [InlineData(100, 300)]  // великий
        [InlineData(1, 1)]      // мінімальний
        public void HasContent_VariousSizes_BlackReturnsFalse(int height, int width)
        {
            using var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);
            var result = OcrCoordinator.HasContent(mat);

            _output.WriteLine($"Black {height}×{width} → HasContent = {result}");
            Assert.False(result, $"Чорний Mat {height}×{width} не повинен запускати OCR");
        }
    }
}