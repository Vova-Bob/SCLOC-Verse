using SCLOCVerse.Models.ApplicationUpdate;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Єдиний оркестратор фонових перевірок оновлень: послідовно App → Localization → LIA.
    /// Один DispatcherTimer, один цикл, SemaphoreSlim TryEnter guard проти накладання циклів.
    ///
    /// Піднімає UpdateCycleCompleted з агрегованим результатом — NotificationRouter будує
    /// NotificationCandidate[] для MainWindow. Оркестратор не знає про UI/toast.
    /// </summary>
    public interface IBackgroundUpdateMonitor
    {
        /// <summary>
        /// Запускає періодичний таймер оновлень. При runImmediately=true перша перевірка
        /// виконується негайно (fire-and-forget), не чекаючи першого тику таймера (1 год).
        /// Це забезпечує Toast про нові Localization/LIA одразу після старту.
        /// </summary>
        void Start(bool runImmediately = false);
        void Stop();
        Task CheckOnceAsync(CancellationToken cancellationToken = default);

        /// <summary>Піднімається після завершення циклу (App→Localization→LIA послідовно).</summary>
        event EventHandler<UpdateCycleResult>? UpdateCycleCompleted;

        /// <summary>Помилка всього циклу (network down тощо) — зберігається для сумісності.</summary>
        event EventHandler<Exception>? CheckFailed;
    }
}