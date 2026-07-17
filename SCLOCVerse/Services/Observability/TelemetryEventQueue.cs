using SCLOCVerse.Models.Observability;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace SCLOCVerse.Services.Observability
{
    /// <summary>
    /// In-memory черга подій спостережуваності (Slice 1).
    /// </summary>
    /// <remarks>
    /// Відповідно до розрізу Phase 1, JSONL-persistency для перечікування офлайн
    /// віднесено до Slice 2 («доставка при втраті Інтернету»). Тут — лише швидка,
    /// потокобезпечна, неблокуюча черга (Конституція, Стаття 2).
    ///
    /// P0.3 — Poison eviction: кожна подія має лічильник retry. Після MaxRetries
    /// невдалих спроб подія скидається (drop), щоб один poison event не блокував
    /// чергу безкінечно (усуває нескінченний requeue loop).
    /// </remarks>
    public sealed class TelemetryEventQueue
    {
        // Верхня межа в памʼяті. Переповнення -> вибуває найстаріша подія
        // (захист від неконтрольованого росту в памʼяті).
        private const int MaxInMemory = 5000;

        // P0.3 — Максимальна кількість retry для однієї події.
        // Після перевищення — permanent drop (poison eviction).
        private const int MaxRetries = 3;

        private readonly ConcurrentQueue<QueuedEvent> _queue = new();
        private long _droppedCount;
        private long _poisonEvictedCount;

        public int Count => _queue.Count;

        /// <summary>O(1), потокобезпечно, не кидає. Викликається з гарячого шляху UI.</summary>
        public void Enqueue(TelemetryEvent evt)
        {
            // Якщо черга переповнена — викидаємо найстаріше (краще втратити старе, ніж заблокувати UI).
            while (_queue.Count >= MaxInMemory && _queue.TryDequeue(out _))
            {
                _droppedCount++;
            }

            _queue.Enqueue(new QueuedEvent(evt, 0));

            if (_droppedCount > 0 && (_droppedCount % 100) == 0)
            {
                Debug.WriteLine($"[Telemetry] Черга переповнена, втрачено (старі): {_droppedCount}");
            }
        }

        /// <summary>Атомарно вийняти до maxCount подій з черги.</summary>
        public List<QueuedEvent> Drain(int maxCount)
        {
            var batch = new List<QueuedEvent>(maxCount);
            for (var i = 0; i < maxCount && _queue.TryDequeue(out var qe); i++)
            {
                batch.Add(qe);
            }
            return batch;
        }

        /// <summary>
        /// Повернути події у чергу з інкрементованим лічильником retry.
        /// Події, що вичерпали MaxRetries — скидаються (poison eviction).
        /// </summary>
        /// <returns>Кількість подій, eviction-нутих через перевищення MaxRetries.</returns>
        public int Requeue(IEnumerable<QueuedEvent> items)
        {
            var evicted = 0;
            foreach (var item in items)
            {
                var newRetry = item.RetryCount + 1;
                if (newRetry > MaxRetries)
                {
                    evicted++;
                    _poisonEvictedCount++;
                    continue;
                }

                _queue.Enqueue(new QueuedEvent(item.Event, newRetry));
            }

            if (evicted > 0)
            {
                Debug.WriteLine($"[Telemetry] Poison eviction: {evicted} подій скинуто після {MaxRetries} невдалих спроб (всього evicted: {_poisonEvictedCount})");
            }

            return evicted;
        }

        /// <summary>
        /// Внутрішній wrapper: подія + лічильник retry.
        /// RetryCount не мапиться у БД (внутрішній стан черги).
        /// </summary>
        public sealed class QueuedEvent
        {
            public TelemetryEvent Event { get; }
            public int RetryCount { get; }

            public QueuedEvent(TelemetryEvent evt, int retryCount)
            {
                Event = evt;
                RetryCount = retryCount;
            }
        }
    }
}
