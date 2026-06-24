using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Відстежує життєвий цикл сесії додатка: створення при старті та закриття при виході.
    /// </summary>
    public interface ISessionTrackerService
    {
        /// <summary>
        /// Закриває незавершені сесії поточної інсталяції та створює нову.
        /// Викликається при старті додатка.
        /// </summary>
        Task StartSessionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Закриває поточну сесію. Викликається при завершенні додатка.
        /// </summary>
        Task EndSessionAsync(CancellationToken cancellationToken = default);
    }
}
