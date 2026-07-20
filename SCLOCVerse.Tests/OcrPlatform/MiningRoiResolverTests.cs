using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Mining;
using SCLOCVerse.Services.Mining;
using Xunit;
using Xunit.Abstractions;
using System.Windows;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Тести динамічного ROI Resolver для Mining Module.
    ///
    /// Архітектурний принцип:
    /// "Screen resolution is never the source of truth. The HUD layout is."
    ///
    /// Перевіряє: Discovery → Tracking → confidence-based fallback → Discovery.
    /// Жодних hardcoded пікселів, жодного масштабування від reference resolution.
    /// </summary>
    public class MiningRoiResolverTests
    {
        private readonly ITestOutputHelper _output;

        public MiningRoiResolverTests(ITestOutputHelper output) => _output = output;

        /// <summary>Створити resolver + fake HUD location result.</summary>
        private static MiningRoiResolver CreateResolver()
        {
            return new MiningRoiResolver();
        }

        private static MiningHudLocationResult CreateLocation(Rect bounds, double confidence = 0.9)
        {
            return new MiningHudLocationResult
            {
                HudBounds = bounds,
                Confidence = confidence,
                StrategyName = "TestLocator"
            };
        }

        [Fact]
        public void InitialMode_IsDiscovery()
        {
            var resolver = CreateResolver();
            Assert.Equal(MiningRoiMode.Discovery, resolver.Layout.Mode);
            Assert.Null(resolver.Layout.HudBounds);
            _output.WriteLine("✅ Initial mode = Discovery (no HUD located yet)");
        }

        [Fact]
        public void DiscoveryCaptureRect_IsFullScreen_NoHardcodedPixels()
        {
            var resolver = CreateResolver();
            var rect = resolver.CurrentCaptureRect;

            // Discovery = повний екран (X=0, Y=0).
            Assert.Equal(0, rect.X);
            Assert.Equal(0, rect.Y);
            Assert.Equal(SystemParameters.PrimaryScreenWidth, rect.Width);
            Assert.Equal(SystemParameters.PrimaryScreenHeight, rect.Height);

            _output.WriteLine($"✅ Discovery rect = full screen: {rect.Width}×{rect.Height}");
            _output.WriteLine("   (працює на будь-якій роздільній здатності)");
        }

        [Fact]
        public void OnHudLocated_TransitionsToTracking()
        {
            var resolver = CreateResolver();
            var hudBounds = new Rect(1000, 200, 400, 300);

            resolver.OnHudLocated(CreateLocation(hudBounds, 0.9));

            Assert.Equal(MiningRoiMode.Tracking, resolver.Layout.Mode);
            Assert.Equal(hudBounds, resolver.Layout.HudBounds);
            Assert.Equal(0.9, resolver.Layout.HudConfidence);
            Assert.NotNull(resolver.Layout.HudLocatedAtUtc);

            _output.WriteLine("✅ OnHudLocated → Tracking");
            _output.WriteLine($"   HudBounds = {hudBounds}, confidence = {resolver.Layout.HudConfidence}");
        }

        [Fact]
        public void OnHudLocated_ComputesRegionBoundsFromTemplate()
        {
            var resolver = CreateResolver();
            var hudBounds = new Rect(1000, 200, 400, 300);

            resolver.OnHudLocated(CreateLocation(hudBounds));

            // Регіони повинні бути обчислені з RelativeBounds × hudBounds.
            Assert.NotEmpty(resolver.Layout.Regions);
            Assert.True(resolver.Layout.Regions.ContainsKey(MiningHudRegionNames.Signature));

            var sigState = resolver.Layout.Regions[MiningHudRegionNames.Signature];
            _output.WriteLine($"✅ Regions computed from template");
            _output.WriteLine($"   Signature bounds: {sigState.Bounds}");
            _output.WriteLine($"   Region count: {resolver.Layout.Regions.Count}");

            // Signature bounds мають бути всередині HUD bounds.
            Assert.True(sigState.Bounds.X >= hudBounds.X);
            Assert.True(sigState.Bounds.Y >= hudBounds.Y);
            Assert.True(sigState.Bounds.Right <= hudBounds.Right);
            Assert.True(sigState.Bounds.Bottom <= hudBounds.Bottom);
        }

        [Fact]
        public void TrackingCaptureRect_IsHudBounds_NotFullScreen()
        {
            var resolver = CreateResolver();
            var hudBounds = new Rect(1000, 200, 400, 300);

            resolver.OnHudLocated(CreateLocation(hudBounds));
            var rect = resolver.CurrentCaptureRect;

            // Tracking = HUD bounds (не повний екран).
            Assert.Equal(hudBounds, rect);

            _output.WriteLine("✅ Tracking rect = HUD bounds (не повний екран)");
            _output.WriteLine($"   HUD: {hudBounds}");
            _output.WriteLine($"   Capture: {rect}");
        }

        [Fact]
        public void OnRegionResult_HighConfidence_KeepsTracking()
        {
            var resolver = CreateResolver();
            resolver.OnHudLocated(CreateLocation(new Rect(1000, 200, 400, 300)));

            // High confidence — лишаємось у Tracking.
            resolver.OnRegionResult(MiningHudRegionNames.Signature, "3,385", 0.95);

            Assert.Equal(MiningRoiMode.Tracking, resolver.Layout.Mode);
            Assert.Equal(0, resolver.Layout.LowConfidenceStreak);
            Assert.Equal(0, resolver.Layout.ConsecutiveNulls);

            _output.WriteLine("✅ High confidence (0.95) → still Tracking");
        }

        [Fact]
        public void OnRegionResult_HardThreshold_ImmediatelyDiscovery()
        {
            var resolver = CreateResolver();
            resolver.OnHudLocated(CreateLocation(new Rect(1000, 200, 400, 300)));

            // Hard threshold: confidence < 0.3 → негайно Discovery.
            resolver.OnRegionResult(MiningHudRegionNames.Signature, "garbage", 0.2);

            Assert.Equal(MiningRoiMode.Discovery, resolver.Layout.Mode);
            Assert.Null(resolver.Layout.HudBounds);

            _output.WriteLine($"✅ Hard threshold (< {MiningHudLayout.HardConfidenceThreshold}) → immediately Discovery");
        }

        [Fact]
        public void OnRegionResult_SoftThreshold_GraceCyclesThenDiscovery()
        {
            var resolver = CreateResolver();
            resolver.OnHudLocated(CreateLocation(new Rect(1000, 200, 400, 300)));

            // Soft threshold: confidence < 0.6 для GraceCycles cycles → Discovery.
            // GraceCycles-1 cycles — still Tracking (grace period).
            for (var i = 0; i < MiningHudLayout.SoftConfidenceGraceCycles - 1; i++)
            {
                resolver.OnRegionResult(MiningHudRegionNames.Signature, "low", 0.4);
                Assert.Equal(MiningRoiMode.Tracking, resolver.Layout.Mode);
            }

            // Наступна low-confidence (досягнення GraceCycles) → Discovery.
            resolver.OnRegionResult(MiningHudRegionNames.Signature, "low", 0.4);
            Assert.Equal(MiningRoiMode.Discovery, resolver.Layout.Mode);

            _output.WriteLine($"✅ Soft threshold (< {MiningHudLayout.SoftConfidenceThreshold} × {MiningHudLayout.SoftConfidenceGraceCycles}) → Discovery");
        }

        [Fact]
        public void OnRegionResult_NullResults_MaxConsecutiveNullsThenDiscovery()
        {
            var resolver = CreateResolver();
            resolver.OnHudLocated(CreateLocation(new Rect(1000, 200, 400, 300)));

            // Null results — ConsecutiveNulls increment.
            for (var i = 0; i < MiningHudLayout.MaxConsecutiveNulls; i++)
            {
                resolver.OnRegionResult(MiningHudRegionNames.Signature, null, 0);
                if (i < MiningHudLayout.MaxConsecutiveNulls - 1)
                    Assert.Equal(MiningRoiMode.Tracking, resolver.Layout.Mode);
            }

            // Після MaxConsecutiveNulls → Discovery.
            Assert.Equal(MiningRoiMode.Discovery, resolver.Layout.Mode);

            _output.WriteLine($"✅ {MiningHudLayout.MaxConsecutiveNulls} null results → Discovery");
        }

        [Fact]
        public void OnRegionResult_LowThenHighConfidence_ResetsStreak()
        {
            var resolver = CreateResolver();
            resolver.OnHudLocated(CreateLocation(new Rect(1000, 200, 400, 300)));

            // 2 cycles low confidence.
            resolver.OnRegionResult(MiningHudRegionNames.Signature, "low", 0.4);
            resolver.OnRegionResult(MiningHudRegionNames.Signature, "low", 0.4);
            Assert.Equal(2, resolver.Layout.LowConfidenceStreak);

            // High confidence → streak reset.
            resolver.OnRegionResult(MiningHudRegionNames.Signature, "3,385", 0.95);
            Assert.Equal(0, resolver.Layout.LowConfidenceStreak);
            Assert.Equal(MiningRoiMode.Tracking, resolver.Layout.Mode);

            _output.WriteLine("✅ Low streak reset by high confidence");
        }

        [Fact]
        public void ResetToDiscovery_ClearsAllState()
        {
            var resolver = CreateResolver();
            resolver.OnHudLocated(CreateLocation(new Rect(1000, 200, 400, 300)));
            Assert.Equal(MiningRoiMode.Tracking, resolver.Layout.Mode);

            resolver.ResetToDiscovery();

            Assert.Equal(MiningRoiMode.Discovery, resolver.Layout.Mode);
            Assert.Null(resolver.Layout.HudBounds);
            Assert.Null(resolver.Layout.HudLocatedAtUtc);
            Assert.Empty(resolver.Layout.Regions);

            _output.WriteLine("✅ ResetToDiscovery clears all state (bounds, regions, confidence)");
        }

        [Fact]
        public void NoHardcodedPixels_ResolverWorksOnAnyResolution()
        {
            var resolver = CreateResolver();

            // Discovery rect = поточний екран (будь-яка роздільна здатність).
            var discoveryRect = resolver.CurrentCaptureRect;
            Assert.Equal(SystemParameters.PrimaryScreenWidth, discoveryRect.Width);
            Assert.Equal(SystemParameters.PrimaryScreenHeight, discoveryRect.Height);

            // Після локалізації — Tracking rect = HUD bounds (не залежить від resolution).
            var hudBounds = new Rect(800, 400, 300, 200);
            resolver.OnHudLocated(CreateLocation(hudBounds));
            var trackingRect = resolver.CurrentCaptureRect;

            Assert.Equal(hudBounds, trackingRect);

            _output.WriteLine("✅ No hardcoded pixels — resolution-independent");
            _output.WriteLine($"   Discovery: {discoveryRect.Width}×{discoveryRect.Height} (= screen)");
            _output.WriteLine($"   Tracking:  {trackingRect} (= HUD bounds)");
        }
    }
}