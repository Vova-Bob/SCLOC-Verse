using SCLOCVerse.Models;

namespace SCLOCVerse.Interfaces
{
    public interface ILocalizationInstaller
    {
        Task<LocalizationInstallResult> InstallAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default);
        Task<LocalizationDeleteResult> DeleteAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Перевіряє наявність оновлення локалізації без встановлення.
        /// Порівнює remote release.TagName з metadata.InstalledTag.
        /// Без побічних ефектів: не записує global.ini, не створює user.cfg, не оновлює metadata.
        /// Повертає результат з HasUpdate=true, якщо remote відрізняється від встановленого.
        /// </summary>
        Task<LocalizationInstallResult> CheckAsync(string environmentFolder, string environmentName, CancellationToken cancellationToken = default);
    }
}
