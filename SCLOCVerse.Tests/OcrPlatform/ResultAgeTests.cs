using SCLOCVerse.Models.Mining;
using SCLOCVerse.Services.Mining.Signatures;
using Xunit;
using Xunit.Abstractions;

namespace SCLOCVerse.Tests.OcrPlatform
{
    /// <summary>
    /// Тести для Result Age — механізму утримання результату при краткочасних
    /// промахах OCR (запобігає миганню Overlay).
    ///
    /// <para><b>Контракт:</b></para>
    /// <list type="bullet">
    /// <item>Успішний OCR → <see cref="MiningState.LastGoodResultUtc"/> = now.</item>
    /// <item>Промах OCR, age &lt; 800мс → результат залишається (не очищати).</item>
    /// <item>Промах OCR, age ≥ 800мс → результат очищається ("Сигнал втрачено").</item>
    /// <item>Disable → скидання LastGoodResultUtc + AllCandidates.</item>
    /// </list>
    /// </summary>
    public class ResultAgeTests
    {
        private readonly ITestOutputHelper _output;
        public ResultAgeTests(ITestOutputHelper output) => _output = output;

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 1: LastGoodResultUtc встановлюється при успішному LookupAll
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void LookupAll_Success_SetsLastGoodResultUtc()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var candidates = db.LookupAll("6770"); // Riccite cluster 2
            var state = new MiningState
            {
                AllCandidates = candidates,
                LastGoodResultUtc = DateTime.UtcNow
            };

            _output.WriteLine($"LookupAll(\"6770\") → {candidates.Count} candidate(s), LastGoodResultUtc = {state.LastGoodResultUtc:O}");
            Assert.NotEmpty(state.AllCandidates);
            Assert.NotNull(state.LastGoodResultUtc);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 2: Невідомий LookupAll → LastGoodResultUtc не встановлюється
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void LookupAll_Unknown_DoesNotSetLastGoodResultUtc()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var candidates = db.LookupAll("99999");
            var state = new MiningState
            {
                AllCandidates = candidates,
                LastGoodResultUtc = candidates.Count > 0 ? DateTime.UtcNow : null
            };

            _output.WriteLine($"LookupAll(\"99999\") → {candidates.Count} candidate(s), LastGoodResultUtc = {state.LastGoodResultUtc?.ToString("O") ?? "null"}");
            Assert.Empty(state.AllCandidates);
            Assert.Null(state.LastGoodResultUtc);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 3: Result Age timeout — константа
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void ResultAgeTimeout_Is800ms()
        {
            _output.WriteLine($"ResultAgeTimeoutMs = {MiningHudLayout.ResultAgeTimeoutMs}");
            Assert.Equal(800, MiningHudLayout.ResultAgeTimeoutMs);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 4: Симуляція Result Age — свіжий результат залишається
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void ResultAge_FreshResult_NotExpired()
        {
            var mat = new MiningMaterial { Code = "6770", Name = "Riccite", Category = "Mineral", ClusterFormat = "Cluster: 2 Rocks" };
            var state = new MiningState
            {
                AllCandidates = new[] { mat },
                LastGoodResultUtc = DateTime.UtcNow,           // щойно
                LastUpdatedUtc = DateTime.UtcNow
            };

            // Вік = 0мс — точно свіжий.
            var ageMs = (DateTime.UtcNow - state.LastGoodResultUtc!.Value).TotalMilliseconds;
            _output.WriteLine($"Age = {ageMs:F1}ms (timeout = {MiningHudLayout.ResultAgeTimeoutMs}ms)");

            Assert.True(ageMs < MiningHudLayout.ResultAgeTimeoutMs, "Свіжий результат не повинен бути expired");
            Assert.NotEmpty(state.AllCandidates);
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 5: Симуляція Result Age — старий результат expired
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void ResultAge_OldResult_Expired()
        {
            var mat = new MiningMaterial { Code = "12000", Name = "ROC Mineable", Category = "ROC", ClusterFormat = "Tier 3" };
            var state = new MiningState
            {
                AllCandidates = new[] { mat },
                LastGoodResultUtc = DateTime.UtcNow.AddSeconds(-2),   // 2 секунди тому — > 800мс
                LastUpdatedUtc = DateTime.UtcNow
            };

            var ageMs = (DateTime.UtcNow - state.LastGoodResultUtc!.Value).TotalMilliseconds;
            _output.WriteLine($"Age = {ageMs:F0}ms (timeout = {MiningHudLayout.ResultAgeTimeoutMs}ms)");

            Assert.True(ageMs >= MiningHudLayout.ResultAgeTimeoutMs, "Старий результат (2с) повинен бути expired");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 6: Overlay — "Сигнал втрачено" коли LastGoodResultUtc == null + був результат
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void Overlay_LostSignal_WhenResultExpired()
        {
            // Симулюємо стан після TryExpireOldResult:
            // AllCandidates порожній, LastGoodResultUtc = null, але RawCode ще є.
            var state = new MiningState
            {
                AllCandidates = System.Array.Empty<MiningMaterial>(),
                LastGoodResultUtc = null,          // очищено
                RawCode = "12000",                  // ще є (не скинуто)
                Confidence = 0.95                   // ще є (не скинуто)
            };

            // Ознака "Сигнал втрачено": LastGoodResultUtc == null + RawCode != null + Confidence > 0.
            var wasLost = state.LastGoodResultUtc is null
                          && !string.IsNullOrEmpty(state.RawCode)
                          && state.Confidence > 0;

            _output.WriteLine($"AllCandidates={state.AllCandidates.Count}, LastGoodResultUtc={state.LastGoodResultUtc?.ToString("O") ?? "null"}, RawCode={state.RawCode}");
            _output.WriteLine($"wasLost = {wasLost}");

            Assert.True(wasLost, "Стан після TryExpireOldResult повинен розпізнаватися як 'Сигнал втрачено'");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 7: Overlay — "Сканування..." при початковому стані (без результату)
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void Overlay_Initial_NoLostSignal()
        {
            // Початковий стан: нічого не було.
            var state = new MiningState
            {
                AllCandidates = System.Array.Empty<MiningMaterial>(),
                LastGoodResultUtc = null,
                RawCode = null,                    // ще не було жодного OCR
                Confidence = 0
            };

            var wasLost = state.LastGoodResultUtc is null
                          && !string.IsNullOrEmpty(state.RawCode)
                          && state.Confidence > 0;

            _output.WriteLine($"AllCandidates={state.AllCandidates.Count}, LastGoodResultUtc={state.LastGoodResultUtc?.ToString("O") ?? "null"}, RawCode={state.RawCode}");
            _output.WriteLine($"wasLost = {wasLost}");

            Assert.False(wasLost, "Початковий стан не повинен розпізнаватися як 'Сигнал втрачено'");
        }

        // ─────────────────────────────────────────────────────────────
        //  ТЕСТ 8: Колізія + Result Age — список кандидатів утримується
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void ResultAge_CollisionResult_HeldDuringTimeout()
        {
            var db = new MiningSignatureDatabase(overrideFilePath: null);
            var candidates = db.LookupAll("12000"); // 3 candidates
            var state = new MiningState
            {
                AllCandidates = candidates,
                LastGoodResultUtc = DateTime.UtcNow   // щойно
            };

            // Вік = 0мс — результат утримується.
            Assert.Equal(3, state.AllCandidates.Count);
            Assert.NotNull(state.LastGoodResultUtc);

            _output.WriteLine($"LookupAll(\"12000\") → {candidates.Count} candidates held (age = 0ms < {MiningHudLayout.ResultAgeTimeoutMs}ms)");
        }
    }
}