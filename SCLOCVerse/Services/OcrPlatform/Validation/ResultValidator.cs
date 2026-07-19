using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Services.OcrPlatform.Validation
{
    /// <summary>
    /// Повна реалізація <see cref="IResultValidator"/> — інтеграція всіх шарів стабілізації:
    /// <list type="number">
    /// <item>Field Lock (skip OCR якщо crop ≈ cached fingerprint).</item>
    /// <item>Confidence Filter (відкидає matches &lt; MinConfidence).</item>
    /// <item>Consensus Buffer (5-frame majority vote).</item>
    /// </list>
    ///
    /// Pattern: "show nothing before wrong number" (SC-Toolbox reference).
    /// Stateful per regionId — тримає окремий Buffer + Lock на регіон.
    /// </summary>
    public sealed class ResultValidator : IResultValidator
    {
        private readonly object _stateLock = new();
        private readonly Dictionary<string, ConsensusBuffer> _buffers = new();
        private readonly Dictionary<string, FieldLock> _locks = new();
        private readonly int _bufferCapacity;

        /// <param name="bufferCapacity">Розмір rolling buffer для consensus (default 5).</param>
        public ResultValidator(int bufferCapacity = 5)
        {
            _bufferCapacity = bufferCapacity;
        }

        /// <inheritdoc />
        public OcrResult Validate(string regionId, OcrResult result, long? cropFingerprint, OcrOptions options)
        {
            ArgumentNullException.ThrowIfNull(regionId);
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(options);

            var buffer = GetOrCreateBuffer(regionId);
            var fieldLock = GetOrCreateLock(regionId);

            // Крок 1: Field Lock — якщо fingerprint співпадає з cached, повертаємо locked value.
            // Це дозволяє skip навіть виклик OCR Engine в Coordinator (early exit).
            if (fieldLock.TryGetLocked(cropFingerprint, out var lockedValue, out var lockedConfidence)
                && !string.IsNullOrEmpty(lockedValue))
            {
                return new OcrResult
                {
                    Matches = new[]
                    {
                        new OcrMatch
                        {
                            Text = lockedValue!,
                            Confidence = lockedConfidence,
                            Bounds = result.BestMatch?.Bounds ?? default,
                            Filtered = false
                        }
                    }
                };
            }

            // Крок 2: Confidence Filter — відкидає low-confidence matches.
            var filteredMatches = result.Matches
                .Where(m => m.Confidence >= options.MinConfidence)
                .ToList();

            // Крок 3: Consensus Buffer — додаємо best match у buffer.
            var currentText = filteredMatches.Count > 0
                ? filteredMatches[0].Text
                : string.Empty;
            buffer.Add(currentText);

            // Крок 4: GetConsensus — majority vote. Якщо є стабільне значення — повертаємо.
            var consensus = buffer.GetConsensus();
            if (string.IsNullOrEmpty(consensus))
            {
                // Недостатньо даних для consensus — повертаємо порожній результат.
                return OcrResult.Empty;
            }

            // Знайшли consensus — оновлюємо field lock.
            var bestMatch = filteredMatches.FirstOrDefault(m => m.Text == consensus);
            var finalConfidence = bestMatch?.Confidence ?? 0.9; // default 0.9 для locked value
            fieldLock.Update(cropFingerprint, consensus, finalConfidence);

            return new OcrResult
            {
                Matches = new[]
                {
                    new OcrMatch
                    {
                        Text = consensus,
                        Confidence = finalConfidence,
                        Bounds = bestMatch?.Bounds ?? result.BestMatch?.Bounds ?? default,
                        Filtered = bestMatch?.Filtered ?? false
                    }
                }
            };
        }

        private ConsensusBuffer GetOrCreateBuffer(string regionId)
        {
            lock (_stateLock)
            {
                if (!_buffers.TryGetValue(regionId, out var buffer))
                {
                    buffer = new ConsensusBuffer(_bufferCapacity);
                    _buffers[regionId] = buffer;
                }
                return buffer;
            }
        }

        private FieldLock GetOrCreateLock(string regionId)
        {
            lock (_stateLock)
            {
                if (!_locks.TryGetValue(regionId, out var fieldLock))
                {
                    fieldLock = new FieldLock();
                    _locks[regionId] = fieldLock;
                }
                return fieldLock;
            }
        }
    }
}

