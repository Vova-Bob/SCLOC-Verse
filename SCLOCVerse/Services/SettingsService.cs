using SCLOCVerse.Interfaces;
using System.Diagnostics;
using System.IO;

namespace SCLOCVerse.Services
{
    public class SettingsService : ISettingsService, IUpdateChannelService
    {
        public void ClearGameFolder()
        {
            Settings.Default.GameFolder = string.Empty;
            Settings.Default.Save();
        }

        public string? GetGameFolder()
        {
            var path = Settings.Default.GameFolder;
            return ValidatePath(path);
        }

        public bool TrySetGameFolder(string? path)
        {
            if (!TryPersist(path, value => Settings.Default.GameFolder = value))
                return false;

            Settings.Default.Save();
            return true;
        }

        public string GetUpdateChannel()
        {
            var channel = Settings.Default.UpdateChannel;
            return string.IsNullOrWhiteSpace(channel) ? "Stable" : channel;
        }

        public void SetUpdateChannel(string channel)
        {
            Settings.Default.UpdateChannel = string.IsNullOrWhiteSpace(channel) ? "Stable" : channel;
            Settings.Default.Save();
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
