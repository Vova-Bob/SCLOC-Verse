namespace SCLOCVerse.Services.OcrPlatform.Validation
{
    /// <summary>
    /// Rolling buffer останніх N результатів OCR для majority vote.
    /// Стабілізує результати: якщо 3 з 5 кадрів дають однакове значення — приймаємо його.
    /// Одиничні промахи OCR (5↔6 flip, anti-aliasing glitch) відкидаються.
    ///
    /// Thread-safe. Capacity за замовчуванням = 5 (SC-Toolbox reference).
    /// </summary>
    public sealed class ConsensusBuffer
    {
        private readonly int _capacity;
        private readonly object _lock = new();
        private readonly Queue<string> _buffer = new();

        /// <param name="capacity">Скільки останніх результатів тримати (default 5).</param>
        public ConsensusBuffer(int capacity = 5)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        /// <summary>
        /// Додати новий результат у buffer. Якщо buffer заповнений — витісняє найстаріший.
        /// </summary>
        public void Add(string text)
        {
            lock (_lock)
            {
                if (_buffer.Count >= _capacity)
                {
                    _buffer.Dequeue();
                }
                _buffer.Enqueue(text ?? string.Empty);
            }
        }

        /// <summary>
        /// Отримати consensus: текст, що зустрічається найчастіше серед buffer contents.
        /// Повертає null, якщо buffer порожній або немає чіткої більшості.
        ///
        /// Strict majority: count ≥ ceil((count+1)/2).
        /// Напр. для buffer з 5: треба ≥ 3 однакових. Для buffer з 3: ≥ 2.
        /// </summary>
        public string? GetConsensus()
        {
            lock (_lock)
            {
                if (_buffer.Count == 0) return null;

                var snapshot = _buffer.ToArray();
                var groups = snapshot
                    .GroupBy(s => s)
                    .Select(g => (Text: g.Key, Count: g.Count()))
                    .OrderByDescending(x => x.Count)
                    .ThenByDescending(x => x.Text.Length) // Більш довгі результати preferred (не порожні)
                    .ToList();

                var best = groups[0];
                // Strict majority за CAPACITY (напр. 5 кадрів потребують 3 однакових),
                // навіть якщо buffer не заповнений (1 з 1 = INSUFFICIENT для capacity=5).
                var required = (_capacity + 1) / 2;
                if (best.Count < required) return null;

                return best.Text;
            }
        }

        /// <summary>
        /// Очистити buffer (використовується при reset/region change).
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _buffer.Clear();
            }
        }

        /// <summary>Кількість результатів у buffer.</summary>
        public int Count
        {
            get
            {
                lock (_lock) return _buffer.Count;
            }
        }
    }
}
