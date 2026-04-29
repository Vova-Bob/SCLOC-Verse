using StarCitizenUA.Interfaces;
using System.Diagnostics;
using System.IO;

namespace StarCitizenUA.Services
{
    public class SettingsService : ISettingsService
    {
        public void ClearGameFolder()
        {
            Settings.Default.StarCitizenUA = string.Empty;
            Settings.Default.Save();
        }

        public string? GetGameFolder()
        {
            var path = Settings.Default.StarCitizenUA;
            return ValidatePath(path);
        }

        public bool TrySetGameFolder(string? path)
        {
            if (!TryPersist(path, value => Settings.Default.StarCitizenUA = value))
                return false;

            Settings.Default.Save();
            return true;
        }

        private static string? ValidatePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            return Directory.Exists(path) ? path : null;
        }

        private static bool TryPersist(string? path, Action<string> setter)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalized = Path.GetFullPath(path);
            if (!Directory.Exists(normalized))
            {
                Debug.WriteLine($"[SettingsService] Ігноруємо невалідний шлях: {normalized}");
                return false;
            }

            setter(normalized);
            return true;
        }
    }
}
