using SCLOCVerse.Models.ApplicationUpdate;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    public interface IUpdateDownloader
    {
        Task<string> DownloadAsync(
            string downloadUrl,
            string targetDirectory,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}

