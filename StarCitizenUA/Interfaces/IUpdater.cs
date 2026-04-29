namespace StarCitizenUA.Interfaces
{
    public interface IUpdater
    {
        Task<LiaInstallStatus> GetStatusAsync(CancellationToken cancellationToken = default);
        Task InstallLatestAsync(Action<string>? onProgress = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
        Task UninstallAsync(Action<string>? onProgress = null, CancellationToken cancellationToken = default);
    }

    public sealed record LiaInstallStatus(
        bool IsInstalled,
        bool IsUpdateAvailable,
        Version? InstalledVersion,
        Version? LatestVersion,
        string Message,
        System.Windows.Media.Brush Color);
}
