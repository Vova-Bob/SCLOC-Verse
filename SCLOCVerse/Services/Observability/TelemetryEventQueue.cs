using SCLOCVerse.Models.Observability;
using System.Collections.Concurrent;
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
    /// </remarks>
    public sealed class TelemetryEventQueue
    {
        // Верхня межа в памʼяті. Переповнення -> вибуває найстаріша подія
        // (захист від неконтрольованого росту в памʼяті).
        private const int MaxInMemory = 5000;

        private readonly ConcurrentQueue<TelemetryEvent> _queue = new();
        private long _droppedCount;

        public int Count => _queue.Count;

        /// <summary>O(1), потокобезпечно, не кидає. Викликається з гарячого шляху UI.</summary>
        public void Enqueue(TelemetryEvent evt)
        {
            // Якщо черга переповнена — викидаємо найстаріше (краще втратити старе, ніж заблокувати UI).
            while (_queue.Count >= MaxInMemory && _queue.TryDequeue(out _))
            {
                _droppedCount++;
            }

            _queue.Enqueue(evt);

            if (_droppedCount > 0 && (_droppedCount % 100) == 0)
            {
                Debug.WriteLine($"[Telemetry] Черга переповнена, втрачено (старі): {_droppedCount}");
            }
        }

        /// <summary>Атомарно вийняти до maxCount подій з черги.</summary>
        public System.Collections.Generic.List<TelemetryEvent> Drain(int maxCount)
        {
            var batch = new System.Collections.Generic.List<TelemetryEvent>(maxCount);
            for (var i = 0; i < maxCount && _queue.TryDequeue(out var evt); i++)
            {
                batch.Add(evt);
            }
            return batch;
        }

        /// <summary>Повернути події у чергу (наприклад, після невдалої відправки).</summary>
        public void Requeue(System.Collections.Generic.IEnumerable<TelemetryEvent> events)
        {
            foreach (var evt in events)
            {
                _queue.Enqueue(evt);
            }
        }
    }
}
