using StarCitizenUA.Models.ApplicationUpdate;
using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Interfaces
{
    public interface IApplicationUpdateService
    {
        Task<UpdateCheckResult> CheckForUpdatesAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default);
    }
}
