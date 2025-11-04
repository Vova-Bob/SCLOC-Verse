using StarCitizenUA.Models.LiaModels;

namespace StarCitizenUA.Interfaces
{
    public interface IUpdater
    {
        Task<Dictionary<string, string>> GetRemoteFileListAsync();
        Task<SyncResult> SyncFilesAsync(
            Dictionary<string, string> remoteFiles,
            string localPath,
            Action<string>? onProgress = null,
            IProgress<int>? progress = null);
        Task<bool> DownloadAndInstallVoskModelAsync(string baseFolder, Action<string>? onProgress = null);
    }
}
