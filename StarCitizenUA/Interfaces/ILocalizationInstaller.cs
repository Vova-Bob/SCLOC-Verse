using StarCitizenUA.Models;

namespace StarCitizenUA.Interfaces
{
    public interface ILocalizationInstaller
    {
        Task<LocalizationInstallResult> InstallAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default);
        Task<LocalizationDeleteResult> DeleteAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default);

    }
}
