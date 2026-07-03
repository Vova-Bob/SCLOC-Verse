using System;
using System.Threading;

namespace SCLOCVerse.Services.Observability
{
    /// <summary>
    /// Ambient-контекст трасування одного запуску (Конституція, Стаття 11 — Trace Mandatory).
    /// Створюється раз на запуск; усі події сесії несуть однакові SessionId/CorrelationId.
    /// </summary>
    /// <remarks>
    /// Для launch-trace correlation_id == session_id. Дискретні дії пізніше можуть
    /// форкнути дочірній correlation_id, не гублячи session_id.
    /// </remarks>
    public sealed class TraceContext
    {
        public Guid SessionId { get; }
        public Guid CorrelationId { get; }

        private int _step;

        public TraceContext()
        {
            SessionId = Guid.NewGuid();
            CorrelationId = SessionId;
        }

        /// <summary>Монотонний номер кроку в трасі (потокобезпечно).</summary>
        public int NextStep() => Interlocked.Increment(ref _step);
    }
}
