using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Слухає OAuth callback на локальному loopback адресі.
    /// </summary>
    public interface ILoopbackCallbackListener : IDisposable
    {
        /// <summary>
        /// Запускає слухач та повертає callback URL, який очікує авторизаційний code.
        /// </summary>
        Task<Uri> StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Очікує перший callback запит та повертає його URL.
        /// </summary>
        Task<Uri?> WaitForCallbackAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Зупиняє слухач та звільняє ресурси.
        /// </summary>
        Task StopAsync();
    }
}
