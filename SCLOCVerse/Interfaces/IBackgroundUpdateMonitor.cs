using SCLOCVerse.Models.ApplicationUpdate;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    public interface IBackgroundUpdateMonitor
    {
        void Start();
        void Stop();
        Task CheckOnceAsync(CancellationToken cancellationToken = default);
        event EventHandler<UpdateCheckResult>? UpdateAvailable;
        event EventHandler<Exception>? CheckFailed;
    }
}
