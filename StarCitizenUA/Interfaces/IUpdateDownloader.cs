using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Interfaces
{
    public interface IUpdateDownloader
    {
        Task<string> DownloadAsync(
            string downloadUrl,
            string targetDirectory,
            CancellationToken cancellationToken = default);
    }
}
