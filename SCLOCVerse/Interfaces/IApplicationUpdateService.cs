using SCLOCVerse.Models.ApplicationUpdate;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    public interface IApplicationUpdateService
    {
        Task<UpdateCheckResult> CheckForUpdatesAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Повертає список релізів для історії оновлень (через IUpdateCacheService, без forceRefresh).
        /// Замість прямого GitHubReleaseClient.GetReleasesAsync — використовує кеш 30 хв.
        /// </summary>
        Task<List<GitHubRelease>> GetReleasesForHistoryAsync(CancellationToken cancellationToken = default);
    }
}
