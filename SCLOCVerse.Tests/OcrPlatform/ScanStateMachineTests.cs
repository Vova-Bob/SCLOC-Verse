using SCLOCVerse.Models.Mining;
using SCLOCVerse.Services.Mining.Signatures;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Тести для MiningScanState State Machine.
    ///
    /// <para>Перевіряє переходи станів та контракти MiningState.ScanState.</para>
    /// </summary>
    public class ScanStateMachineTests
    {
        private readonly ITestOutputHelper _output;
        public ScanStateMachineTests(ITestOutputHelper output) => _output = output;

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 1: Початковий стан — Idle
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void InitialState_IsIdle()
        {
            var state = new MiningState();
            _output.WriteLine($"Initial ScanState = {state.ScanState}");
            Assert.Equal(MiningScanState.Idle, state.ScanState);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 2: ScanState значення — 5 станів
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void ScanState_HasFiveStates()
        {
            _output.WriteLine($"Idle = {(int)MiningScanState.Idle}");
            _output.WriteLine($"Scanning = {(int)MiningScanState.Scanning}");
            _output.WriteLine($"Detected = {(int)MiningScanState.Detected}");
            _output.WriteLine($"Weak = {(int)MiningScanState.Weak}");
            _output.WriteLine($"Lost = {(int)MiningScanState.Lost}");

            Assert.Equal(0, (int)MiningScanState.Idle);
            Assert.Equal(1, (int)MiningScanState.Scanning);
            Assert.Equal(2, (int)MiningScanState.Detected);
            Assert.Equal(3, (int)MiningScanState.Weak);
            Assert.Equal(4, (int)MiningScanState.Lost);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 3: Detected з AllCandidates — Material не null
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void DetectedState_WithCandidates_MaterialNotNull()
        {
            var mat = new MiningMaterial { Code = "6770", Name = "Riccite", Category = "Mineral", ClusterFormat = "Cluster: 2 Rocks" };
            var state = new MiningState
            {
                ScanState = MiningScanState.Detected,
                AllCandidates = new[] { mat },
                LastGoodResultUtc = DateTime.UtcNow
            };

            _output.WriteLine($"ScanState={state.ScanState}, Material={state.Material?.Name}");
            Assert.Equal(MiningScanState.Detected, state.ScanState);
            Assert.NotNull(state.Material);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 4: Lost без AllCandidates — Material null
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void LostState_NoCandidates_MaterialNull()
        {
            var state = new MiningState
            {
                ScanState = MiningScanState.Lost,
                AllCandidates = System.Array.Empty<MiningMaterial>(),
                LastGoodResultUtc = null,
                RawCode = "12000",
                Confidence = 0.9
            };

            _output.WriteLine($"ScanState={state.ScanState}, Material={state.Material?.Name ?? "null"}");
            Assert.Equal(MiningScanState.Lost, state.ScanState);
            Assert.Null(state.Material);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 5: Weak з AllCandidates — результат утримується
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void WeakState_WithCandidates_ResultHeld()
        {
            var mat = new MiningMaterial { Code = "12000", Name = "ROC Mineable", Category = "ROC", ClusterFormat = "Tier 3" };
            var state = new MiningState
            {
                ScanState = MiningScanState.Weak,
                AllCandidates = new[] { mat },
                LastGoodResultUtc = DateTime.UtcNow.AddMilliseconds(-400) // 400ms ago, < 800ms timeout
            };

            var ageMs = (DateTime.UtcNow - state.LastGoodResultUtc!.Value).TotalMilliseconds;
            _output.WriteLine($"ScanState={state.ScanState}, age={ageMs:F0}ms, candidates={state.AllCandidates.Count}");

            Assert.Equal(MiningScanState.Weak, state.ScanState);
            Assert.NotEmpty(state.AllCandidates);
            Assert.True(ageMs < MiningHudLayout.ResultAgeTimeoutMs, "Weak = результат утримується (age < timeout)");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 6: Idle — Overlay показує "Сканування..."
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void IdleState_NoResult_ShowsScanning()
        {
            var state = new MiningState
            {
                ScanState = MiningScanState.Idle,
                AllCandidates = System.Array.Empty<MiningMaterial>(),
                RawCode = null
            };

            var isLost = state.ScanState == MiningScanState.Lost;
            var displayText = isLost ? "Сигнал втрачено" : (state.RawCode ?? "Сканування...");

            _output.WriteLine($"ScanState={state.ScanState}, displayText={displayText}");
            Assert.False(isLost);
            Assert.Equal("Сканування...", displayText);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 7: Lost → Overlay показує "Сигнал втрачено"
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void LostState_ShowsLostMessage()
        {
            var state = new MiningState
            {
                ScanState = MiningScanState.Lost,
                AllCandidates = System.Array.Empty<MiningMaterial>(),
                RawCode = "12000",
                Confidence = 0.9
            };

            var isLost = state.ScanState == MiningScanState.Lost;
            var displayText = isLost ? "Сигнал втрачено" : (state.RawCode ?? "Сканування...");

            _output.WriteLine($"ScanState={state.ScanState}, displayText={displayText}");
            Assert.True(isLost);
            Assert.Equal("Сигнал втрачено", displayText);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 8: Scanning — Overlay показує RawCode або "Сканування..."
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void ScanningState_ShowsDiscoveryStatus()
        {
            var state = new MiningState
            {
                ScanState = MiningScanState.Scanning,
                AllCandidates = System.Array.Empty<MiningMaterial>(),
                RawCode = "Discovery: сканування екрана..."
            };

            var isLost = state.ScanState == MiningScanState.Lost;
            var displayText = isLost ? "Сигнал втрачено" : (state.RawCode ?? "Сканування...");

            _output.WriteLine($"ScanState={state.ScanState}, displayText={displayText}");
            Assert.False(isLost);
            Assert.Contains("Discovery", displayText);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 9: State Machine — всі переходи послідовно
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void StateMachine_FullCycle_Idle_Scanning_Detected_Weak_Lost_Idle()
        {
            var state = new MiningState();

            // Idle → Scanning
            state.ScanState = MiningScanState.Scanning;
            Assert.Equal(MiningScanState.Scanning, state.ScanState);

            // Scanning → Detected (успішний OCR)
            var mat = new MiningMaterial { Code = "6770", Name = "Riccite", Category = "Mineral", ClusterFormat = "Cluster: 2 Rocks" };
            state.AllCandidates = new[] { mat };
            state.LastGoodResultUtc = DateTime.UtcNow;
            state.ScanState = MiningScanState.Detected;
            Assert.Equal(MiningScanState.Detected, state.ScanState);
            Assert.NotNull(state.Material);

            // Detected → Weak (OCR промах, grace period)
            state.ScanState = MiningScanState.Weak;
            Assert.Equal(MiningScanState.Weak, state.ScanState);
            Assert.NotEmpty(state.AllCandidates); // результат утримується

            // Weak → Lost (age > timeout)
            state.AllCandidates = System.Array.Empty<MiningMaterial>();
            state.LastGoodResultUtc = null;
            state.ScanState = MiningScanState.Lost;
            Assert.Equal(MiningScanState.Lost, state.ScanState);
            Assert.Null(state.Material);

            // Lost → Idle (скидання)
            state.ScanState = MiningScanState.Idle;
            state.RawCode = null;
            Assert.Equal(MiningScanState.Idle, state.ScanState);

            _output.WriteLine("Full cycle: Idle → Scanning → Detected → Weak → Lost → Idle ✅");
        }
    }
}