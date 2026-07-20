using System.Windows;
using System.Collections.Generic;

namespace SCLOCVerse.Models.Mining
{
    /// <summary>
    /// Поточний стан локалізації Mining HUD.
    ///
    /// Архітектурний принцип:
    /// "Screen resolution is never the source of truth. The HUD layout is."
    ///
    /// Містить:
    /// - Mode (Discovery / Tracking).
    /// - HudBounds — bounds знайденого HUD (у пікселях екрана, обчислені з locator'а).
    /// - HudConfidence — якість локалізації (НЕ OCR confidence).
    /// - Regions — per-region стан (bounds + quality + ocrConfidence + lastText).
    /// - Thresholds для confidence-based transitions.
    /// </summary>
    public sealed class MiningHudLayout
    {
        /// <summary>Поточний режим (Discovery — пошук HUD, Tracking — OCR всередині HUD).</summary>
        public MiningRoiMode Mode { get; set; } = MiningRoiMode.Discovery;

        /// <summary>Bounds знайденого HUD (null = HUD не локалізовано).</summary>
        public Rect? HudBounds { get; set; }

        /// <summary>Confidence локалізації HUD [0..1] (від locator стратегії).</summary>
        public double HudConfidence { get; set; }

        /// <summary>UTC timestamp останньої локалізації HUD.</summary>
        public DateTime? HudLocatedAtUtc { get; set; }

        /// <summary>Per-region стан (key = region name, value = state).</summary>
        public Dictionary<string, MiningHudRegionState> Regions { get; } = new();

        // ── Thresholds для confidence-based transitions ──

        /// <summary>Hard threshold: OCR confidence нижче → негайно Discovery (раптова втрата).</summary>
        public const double HardConfidenceThreshold = 0.3;

        /// <summary>Soft threshold: OCR confidence нижче цього для N cycles → Discovery (поступова деградація).</summary>
        public const double SoftConfidenceThreshold = 0.6;

        /// <summary>Кількість consecutive cycles з low confidence перед Discovery (soft threshold).</summary>
        public const int SoftConfidenceGraceCycles = 3;

        /// <summary>Кількість consecutive null results перед Discovery (повна втрата сигналу).</summary>
        public const int MaxConsecutiveNulls = 5;

        // ── Per-region tracking ──

        /// <summary>Лічильник consecutive low-confidence cycles (для soft threshold).</summary>
        public int LowConfidenceStreak { get; set; }

        /// <summary>Лічильник consecutive null results (для MaxConsecutiveNulls).</summary>
        public int ConsecutiveNulls { get; set; }

        /// <summary>Скинути весь стан до Discovery (HUD втрачено).</summary>
        public void ResetToDiscovery()
        {
            Mode = MiningRoiMode.Discovery;
            HudBounds = null;
            HudConfidence = 0;
            HudLocatedAtUtc = null;
            LowConfidenceStreak = 0;
            ConsecutiveNulls = 0;
            Regions.Clear();
        }
    }
}