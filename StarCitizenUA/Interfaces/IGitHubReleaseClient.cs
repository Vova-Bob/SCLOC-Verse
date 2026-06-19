using StarCitizenUA.Models.ApplicationUpdate;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Interfaces
{
    public interface IGitHubReleaseClient
    {
        Task<GitHubRelease?> GetLatestReleaseAsync(string owner, string repo, string userAgent, CancellationToken cancellationToken = default);
        Task<List<GitHubRelease>> GetReleasesAsync(string owner, string repo, string userAgent, CancellationToken cancellationToken = default);
    }
}
