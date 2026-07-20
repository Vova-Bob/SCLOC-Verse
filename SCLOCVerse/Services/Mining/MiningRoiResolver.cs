using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Mining;
using System.Diagnostics;
using System.Windows;

namespace SCLOCVerse.Services.Mining
{
    /// <summary>
    /// Реалізація <see cref="IMiningRoiResolver"/> — динамічний ROI resolver.
    ///
    /// Архітектурний принцип:
    /// "Screen resolution is never the source of truth. The HUD layout is."
    ///
    /// Двоступенева архітектура:
    /// Discovery — повний екран (для locator'а, БЕЗ OCR).
    /// Tracking — HUD bounds (для OCR всередині знайдених регіонів).
    ///
    /// Confidence-based transitions:
    /// - Hard threshold (conf < 0.3): негайно Discovery (раптова втрата).
    /// - Soft threshold (conf < 0.6 × 3 cycles): Discovery (поступова деградація).
    /// - Null × 5: Discovery (повна втрата сигналу).
    /// </summary>
    public sealed class MiningRoiResolver : IMiningRoiResolver
    {
        private readonly MiningHudTemplate _template;
        private readonly object _lock = new();
        private readonly MiningHudLayout _layout = new();

        /// <summary>
        /// Створити resolver з шаблоном HUD (за замовчуванням — DefaultMiningHudTemplates.Default).
        /// </summary>
        public MiningRoiResolver(MiningHudTemplate? template = null)
        {
            _template = template ?? DefaultMiningHudTemplates.Default;
        }

        /// <inheritdoc />
        public MiningHudLayout Layout
        {
            get
            {
                lock (_lock)
                {
                    // Повертаємо копію стани для thread safety.
                    // (MiningHudLayout — mutable, але Regions dict замінюється новим при reset.)
                    return _layout;
                }
            }
        }

        /// <inheritdoc />
        public Rect CurrentCaptureRect
        {
            get
            {
                lock (_lock)
                {
                    if (_layout.Mode == MiningRoiMode.Tracking && _layout.HudBounds is Rect bounds)
                    {
                        return bounds;
                    }

                    // Discovery mode — повний екран (будь-яка роздільна здатність).
                    return GetFullScreenRect();
                }
            }
        }

        /// <inheritdoc />
        public void OnHudLocated(MiningHudLocationResult location)
        {
            ArgumentNullException.ThrowIfNull(location);

            lock (_lock)
            {
                // Записати HUD bounds + confidence.
                _layout.HudBounds = location.HudBounds;
                _layout.HudConfidence = location.Confidence;
                _layout.HudLocatedAtUtc = DateTime.UtcNow;
                _layout.Mode = MiningRoiMode.Tracking;
                _layout.LowConfidenceStreak = 0;
                _layout.ConsecutiveNulls = 0;

                // Обчислити абсолютні bounds регіонів з RelativeBounds × HUD bounds.
                _layout.Regions.Clear();
                foreach (var regionTemplate in _template.Regions)
                {
                    var absBounds = _template.ComputeRegionBounds(regionTemplate, location.HudBounds);
                    _layout.Regions[regionTemplate.Name] = new MiningHudRegionState
                    {
                        Name = regionTemplate.Name,
                        Bounds = absBounds,
                        RegionQuality = location.Confidence, // початкова якість = HUD confidence
                        OcrConfidence = 0,
                        LastText = null,
                        LastSeenUtc = null,
                        ConsecutiveFailures = 0
                    };
                }

                Debug.WriteLine("[MiningRoiResolver] HUD located: {0} conf={1:F2} → Tracking ({2} regions)",
                    location.HudBounds, location.Confidence, _layout.Regions.Count);
            }
        }

        /// <inheritdoc />
        public void OnRegionResult(string regionName, string? ocrText, double ocrConfidence)
        {
            lock (_lock)
            {
                if (_layout.Mode != MiningRoiMode.Tracking) return;

                var isNull = string.IsNullOrEmpty(ocrText);
                var isLowConfidence = !isNull && ocrConfidence < MiningHudLayout.SoftConfidenceThreshold;
                var isHardFailure = !isNull && ocrConfidence < MiningHudLayout.HardConfidenceThreshold;

                // Оновити per-region state.
                if (_layout.Regions.TryGetValue(regionName, out var state))
                {
                    if (!isNull)
                    {
                        state.LastText = ocrText;
                        state.OcrConfidence = ocrConfidence;
                        state.LastSeenUtc = DateTime.UtcNow;
                        state.ConsecutiveFailures = 0;
                    }
                    else
                    {
                        state.ConsecutiveFailures++;
                    }
                }

                // Confidence-based transitions (використовуємо signature region як індикатор).
                if (regionName == MiningHudRegionNames.Signature)
                {
                    if (isHardFailure)
                    {
                        // Hard threshold — негайно Discovery.
                        Debug.WriteLine("[MiningRoiResolver] Hard confidence drop ({0:F2} < {1}) → Discovery",
                            ocrConfidence, MiningHudLayout.HardConfidenceThreshold);
                        ResetToDiscoveryInternal();
                        return;
                    }

                    if (isNull)
                    {
                        _layout.ConsecutiveNulls++;
                        if (_layout.ConsecutiveNulls >= MiningHudLayout.MaxConsecutiveNulls)
                        {
                            Debug.WriteLine("[MiningRoiResolver] {0} consecutive nulls → Discovery",
                                _layout.ConsecutiveNulls);
                            ResetToDiscoveryInternal();
                            return;
                        }
                    }
                    else
                    {
                        _layout.ConsecutiveNulls = 0;

                        if (isLowConfidence)
                        {
                            _layout.LowConfidenceStreak++;
                            if (_layout.LowConfidenceStreak >= MiningHudLayout.SoftConfidenceGraceCycles)
                            {
                                Debug.WriteLine("[MiningRoiResolver] Soft confidence degradation ({0} cycles < {1}) → Discovery",
                                    _layout.LowConfidenceStreak, MiningHudLayout.SoftConfidenceThreshold);
                                ResetToDiscoveryInternal();
                                return;
                            }
                        }
                        else
                        {
                            // Good confidence — reset streaks.
                            _layout.LowConfidenceStreak = 0;
                        }
                    }
                }
            }
        }

        /// <inheritdoc />
        public void ResetToDiscovery()
        {
            lock (_lock)
            {
                ResetToDiscoveryInternal();
            }
        }

        private void ResetToDiscoveryInternal()
        {
            _layout.ResetToDiscovery();
            Debug.WriteLine("[MiningRoiResolver] Reset to Discovery mode");
        }

        /// <summary>Повний екран (будь-яка роздільна здатність).</summary>
        private static Rect GetFullScreenRect()
        {
            var width = SystemParameters.PrimaryScreenWidth;
            var height = SystemParameters.PrimaryScreenHeight;
            return new Rect(0, 0, width, height);
        }
    }
}