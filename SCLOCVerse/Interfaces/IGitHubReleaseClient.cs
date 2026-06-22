using SCLOCVerse.Models.ApplicationUpdate;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    public interface IGitHubReleaseClient
    {
        Task<GitHubRelease?> GetLatestReleaseAsync(
            string owner,
            string repo,
            CancellationToken cancellationToken = default);

        Task<List<GitHubRelease>> GetReleasesAsync(
            string owner,
            string repo,
            CancellationToken cancellationToken = default);

        Task<string> DownloadTextAsync(
            string url,
            CancellationToken cancellationToken = default);
    }
}
