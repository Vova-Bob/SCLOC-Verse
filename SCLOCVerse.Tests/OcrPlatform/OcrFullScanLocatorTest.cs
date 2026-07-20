using OpenCvSharp;
using SCLOCVerse.Models.Mining;
using SCLOCVerse.Models.OcrPlatform;
using SCLOCVerse.Services.Mining.Locators;
using SCLOCVerse.Services.Mining.Signatures;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Тест OcrFullScanLocator на реальному SCAN.png.
    /// Перевіряє, чи locator знаходить HUD signature з DiscoveryScan options.
    /// </summary>
    public class OcrFullScanLocatorTest
    {
        private readonly ITestOutputHelper _output;

        public OcrFullScanLocatorTest(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Locate_OnScanPng_FindsSignature()
        {
            var screenshotPath = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "SCLOCVerse", "Images", "SCAN.png");
            screenshotPath = Path.GetFullPath(screenshotPath);
            _output.WriteLine($"Screenshot: {screenshotPath}");
            Assert.True(File.Exists(screenshotPath), $"SCAN.png not found: {screenshotPath}");

            var modelsDir = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "SCLOCVerse", "Resources", "OcrModels");
            modelsDir = Path.GetFullPath(modelsDir);
            _output.WriteLine($"Models: {modelsDir}");
            Assert.True(Directory.Exists(modelsDir), $"Models dir not found: {modelsDir}");

            using var engine = new SCLOCVerse.Services.OcrPlatform.Engines.PaddleOcrEngine(
                detModelPath: Path.Combine(modelsDir, "ch_PP-OCRv5_mobile_det.onnx"),
                recModelPath: Path.Combine(modelsDir, "ch_PP-OCRv5_rec_mobile_infer.onnx"),
                dictPath: Path.Combine(modelsDir, "ppocrv5_dict.txt"));

            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var locator = new OcrFullScanLocator(engine, db);

            using var screenshot = Cv2.ImRead(screenshotPath, ImreadModes.Color);
            _output.WriteLine($"Screenshot size: {screenshot.Cols}×{screenshot.Rows}");

            // Act
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = locator.Locate(screenshot);
            sw.Stop();

            _output.WriteLine($"Locate time: {sw.ElapsedMilliseconds} ms");
            _output.WriteLine($"Result: {(result is null ? "NULL" : $"HUD bounds={result.HudBounds}, conf={result.Confidence:F2}, details={result.Details}")}");

            Assert.NotNull(result);
            Assert.True(result!.Confidence > 0.5, $"Confidence too low: {result.Confidence}");
            Assert.True(result.HudBounds.Width > 0 && result.HudBounds.Height > 0, "HUD bounds must be non-empty");
        }
    }
}