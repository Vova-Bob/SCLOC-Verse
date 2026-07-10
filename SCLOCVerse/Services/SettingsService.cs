using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.AntiAfk;
using System.Diagnostics;
using System.IO;

namespace SCLOCVerse.Services
{
    public class SettingsService : ISettingsService, IUpdateChannelService, IPreferencesService
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

        // ===== IPreferencesService =====

        public bool GetMinimizeToTray()
        {
            return Settings.Default.MinimizeToTray;
        }

        public void SetMinimizeToTray(bool value)
        {
            Settings.Default.MinimizeToTray = value;
            Settings.Default.Save();
        }

        public bool GetAutoUpdateLocalization()
        {
            return Settings.Default.AutoUpdateLocalization;
        }

        public void SetAutoUpdateLocalization(bool value)
        {
            Settings.Default.AutoUpdateLocalization = value;
            Settings.Default.Save();
        }

        public bool GetAdvancedDiagnostics()
        {
            return Settings.Default.AdvancedDiagnostics;
        }

        public void SetAdvancedDiagnostics(bool value)
        {
            Settings.Default.AdvancedDiagnostics = value;
            Settings.Default.Save();
        }

        public string GetLastLocalizationToast()
        {
            return Settings.Default.LastLocalizationToast ?? string.Empty;
        }

        public void SetLastLocalizationToast(string version)
        {
            Settings.Default.LastLocalizationToast = version ?? string.Empty;
            Settings.Default.Save();
        }

        public string GetLastLiaToast()
        {
            return Settings.Default.LastLiaToast ?? string.Empty;
        }

        public void SetLastLiaToast(string version)
        {
            Settings.Default.LastLiaToast = version ?? string.Empty;
            Settings.Default.Save();
        }

        public string GetLastAppToast()
        {
            return Settings.Default.LastAppToast ?? string.Empty;
        }

        public void SetLastAppToast(string version)
        {
            Settings.Default.LastAppToast = version ?? string.Empty;
            Settings.Default.Save();
        }

        public string GetLastToastTimestampUtc()
        {
            return Settings.Default.LastToastTimestampUtc ?? string.Empty;
        }

        public void SetLastToastTimestampUtc(string timestamp)
        {
            Settings.Default.LastToastTimestampUtc = timestamp ?? string.Empty;
            Settings.Default.Save();
        }

        // ===== Anti-AFK =====

        public bool GetAntiAfkEnabled()
        {
            return Settings.Default.AntiAfkEnabled;
        }

        public void SetAntiAfkEnabled(bool value)
        {
            Settings.Default.AntiAfkEnabled = value;
            Settings.Default.Save();
        }

        public string GetAntiAfkIndicatorColor()
        {
            return Settings.Default.AntiAfkIndicatorColor ?? "#4CAF50";
        }

        public void SetAntiAfkIndicatorColor(string value)
        {
            Settings.Default.AntiAfkIndicatorColor = value ?? "Hidden";
            Settings.Default.Save();
        }

        public AntiAfkIndicatorPosition GetAntiAfkIndicatorPosition()
        {
            return Enum.TryParse<AntiAfkIndicatorPosition>(
                Settings.Default.AntiAfkIndicatorPosition, out var pos)
                ? pos
                : AntiAfkIndicatorPosition.TopRight;
        }

        public void SetAntiAfkIndicatorPosition(AntiAfkIndicatorPosition value)
        {
            Settings.Default.AntiAfkIndicatorPosition = value.ToString();
            Settings.Default.Save();
        }

        public double GetAntiAfkIndicatorSize()
        {
            var value = Settings.Default.AntiAfkIndicatorSize;
            return value < 8 ? 12 : value;
        }

        public void SetAntiAfkIndicatorSize(double value)
        {
            Settings.Default.AntiAfkIndicatorSize = value;
            Settings.Default.Save();
        }

        public AntiAfkIndicatorAnimation GetAntiAfkIndicatorAnimation()
        {
            return Enum.TryParse<AntiAfkIndicatorAnimation>(
                Settings.Default.AntiAfkIndicatorAnimation, out var anim)
                ? anim
                : AntiAfkIndicatorAnimation.Pulse;
        }

        public void SetAntiAfkIndicatorAnimation(AntiAfkIndicatorAnimation value)
        {
            Settings.Default.AntiAfkIndicatorAnimation = value.ToString();
            Settings.Default.Save();
        }

        public AntiAfkIndicatorMode GetAntiAfkIndicatorMode()
        {
            return Enum.TryParse<AntiAfkIndicatorMode>(
                Settings.Default.AntiAfkIndicatorMode, out var mode)
                ? mode
                : AntiAfkIndicatorMode.Running;
        }

        public void SetAntiAfkIndicatorMode(AntiAfkIndicatorMode value)
        {
            Settings.Default.AntiAfkIndicatorMode = value.ToString();
            Settings.Default.Save();
        }
    }
}
