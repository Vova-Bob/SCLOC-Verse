using SCLOCVerse.Models.Observability;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Єдиний дозволений канал спостережуваності SCLOC-Verse
    /// (Конституція, Стаття 7 — Single Sanctioned Sink; Стаття 12 — Single Ingestion Contract).
    /// </summary>
    /// <remarks>
    /// Контракт методу <see cref="Track"/>:
    ///  - синхронний, O(1), ніколи не кидає (Стаття 1 — Absolute Isolation);
    ///  - не блокує UI (Стаття 2) — лише кладе в неблокуючу чергу;
    ///  - жоден викликач не обробляє результат.
    /// </remarks>
    public interface ITelemetryService
    {
        /// <summary>
        /// Зафіксувати структуровану подію спостережуваності.
        /// </summary>
        /// <param name="component">Категорія: Application/Auth/Installation/Localization/Updater/LIA/Network/UnhandledException.</param>
        /// <param name="operation">Операція всередині компонента (Start, SignIn, Sync, Install, ...).</param>
        /// <param name="outcome">Started/Succeeded/Failed/Cancelled/Skipped.</param>
        /// <param name="context">Опціональний diagnostic-контекст (обов'язковий для Failed).</param>
        void Track(string component, string operation, string outcome, TelemetryContext? context = null);
    }
}
