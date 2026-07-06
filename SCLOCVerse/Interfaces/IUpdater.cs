using SCLOCVerse.Models.LiaModels;

namespace SCLOCVerse.Interfaces
{
    public interface IUpdater
    {
        Task<LiaInstallStatus> GetStatusAsync(CancellationToken cancellationToken = default);

        Task InstallLatestAsync(
            Action<string>? onProgress = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        Task UninstallAsync(
            Action<string>? onProgress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Запускає локально встановлений пакунок Л.І.А через стандартний
        /// Windows AppX activation (shell:AppsFolder). Без мережі — лише
        /// читання локального реєстру AppX та активація пакунка.
        /// </summary>
        Task LaunchAsync(CancellationToken cancellationToken = default);
    }

    public sealed record LiaInstallStatus(
        bool IsInstalled,
        bool IsUpdateAvailable,
        Version? InstalledVersion,
        Version? LatestVersion,
        string Message,
        LiaStatusColor Color);
}