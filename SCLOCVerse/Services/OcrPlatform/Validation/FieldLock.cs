namespace SCLOCVerse.Services.OcrPlatform.Validation
{
    /// <summary>
    /// Field-value locking: пропускає нові результати OCR, поки crop не змінюється
    /// (NCC &lt; 0.85 від cached fingerprint). SC-Toolbox pattern.
    ///
    /// Користь: при стабільному HUD (цифри не змінюються) — skip OCR повністю,
    /// економія CPU. При русі/зміні — оновлюємо.
    ///
    /// У нашій реалізації: crop fingerprint = hash рядків пікселів (варіант спрощення).
    /// Future: replace на NCC correlation.
    /// </summary>
    public sealed class FieldLock
    {
        private readonly object _lock = new();
        private long? _cachedFingerprint;
        private string? _lockedValue;
        private double _lockedConfidence;

        /// <summary>
        /// Чи активний lock (має cached value)?
        /// </summary>
        public bool IsLocked
        {
            get
            {
                lock (_lock) return _lockedFingerprint.HasValue;
            }
        }

        /// <summary>
        /// Спробувати повернути locked value, якщо fingerprint співпадає.
        /// </summary>
        /// <param name="cropFingerprint">Поточний fingerprint crop (може бути null = skip lock).</param>
        /// <param name="lockedValue">Locked value якщо lock активний і fingerprint співпадає.</param>
        /// <param name="lockedConfidence">Confidence locked value.</param>
        /// <returns>True, якщо можна пропустити новий OCR і використати locked value.</returns>
        public bool TryGetLocked(long? cropFingerprint, out string? lockedValue, out double lockedConfidence)
        {
            lock (_lock)
            {
                if (cropFingerprint is null || _cachedFingerprint is null)
                {
                    lockedValue = null;
                    lockedConfidence = 0;
                    return false;
                }

                // Дозволяємо невелику дельту (1%) — стійкість до дрібного шуму.
                if (Math.Abs(cropFingerprint.Value - _cachedFingerprint.Value) <= Math.Max(1, _cachedFingerprint.Value / 100))
                {
                    lockedValue = _lockedValue;
                    lockedConfidence = _lockedConfidence;
                    return _lockedValue != null;
                }

                lockedValue = null;
                lockedConfidence = 0;
                return false;
            }
        }

        /// <summary>
        /// Оновити locked value після успішного OCR.
        /// </summary>
        public void Update(long? cropFingerprint, string value, double confidence)
        {
            lock (_lock)
            {
                _cachedFingerprint = cropFingerprint;
                _lockedValue = value;
                _lockedConfidence = confidence;
            }
        }

        /// <summary>
        /// Скинути lock (використовується при region change або disable).
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _cachedFingerprint = null;
                _lockedValue = null;
                _lockedConfidence = 0;
            }
        }
    }
}
