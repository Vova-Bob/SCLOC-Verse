using SCLOCVerse.Models.ApplicationInstance;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Керує життєвим циклом єдиного екземпляра застосунку:
    /// виявлення повторного запуску (Mutex), IPC-активація першого
    /// екземпляра (Named Pipe), обробка команд від другого процесу.
    ///
    /// Архітектура: Mutex + Named Pipe. Перший процес стає власником Mutex
    /// і піднімає pipe-сервер; другий процес виявляє зайнятий Mutex, передає
    /// команду через pipe першому процесу й тихо завершується.
    /// </summary>
    public interface IApplicationInstanceService : IAsyncDisposable
    {
        /// <summary>
        /// true, якщо поточний процес — перший (власник Mutex).
        /// Другий процес отримує false і має викликати SignalExistingInstanceAsync.
        /// </summary>
        bool IsFirstInstance { get; }

        /// <summary>
        /// Перший процес: запускає Named Pipe сервер у фоновому потоці для
        /// прослуховування команд від повторних запусків. Викликається один раз
        /// після OnStartup. Ідемпотентний.
        /// </summary>
        Task StartServerAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Другий процес: підключається до pipe першого процесу, передає команду
        /// й відключається. Повертає true, якщо перший процес прийняв сигнал;
        /// false — при таймауті або помилці (другий процес має тихо завершитись).
        /// </summary>
        Task<bool> SignalExistingInstanceAsync(
            InstanceCommand command,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Перший процес: піднімається при отриманні команди від другого процесу.
        /// UI-обробник підписується і виконує Show/Activate або іншу дію за Kind.
        /// Подія маршалінгується в потік, що викликав StartServerAsync (UI-потік).
        /// </summary>
        event EventHandler<InstanceCommand>? CommandReceived;
    }
}
