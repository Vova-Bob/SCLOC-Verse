using SCLOCVerse.Models.ApplicationUpdate;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    public interface IApplicationUpdateService
    {
        Task<UpdateCheckResult> CheckForUpdatesAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default);
    }
}
