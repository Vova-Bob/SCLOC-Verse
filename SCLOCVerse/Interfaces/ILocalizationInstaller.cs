using SCLOCVerse.Models;

namespace SCLOCVerse.Interfaces
{
    public interface ILocalizationInstaller
    {
        Task<LocalizationInstallResult> InstallAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default);
        Task<LocalizationDeleteResult> DeleteAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default);

    }
}
