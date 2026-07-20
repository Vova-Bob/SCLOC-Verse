using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.AntiAfk;
using SCLOCVerse.Services.InputSystem;
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

        // ===== Auto Key =====

        public bool GetAutoKeyEnabled()
        {
            return Settings.Default.AutoKeyEnabled;
        }

        public void SetAutoKeyEnabled(bool value)
        {
            Settings.Default.AutoKeyEnabled = value;
            Settings.Default.Save();
        }

        public HotkeyKey GetAutoKeyActionKey()
        {
            return Enum.TryParse<HotkeyKey>(Settings.Default.AutoKeyActionKey, out var key)
                ? key
                : HotkeyKey.Oem4;
        }

        public void SetAutoKeyActionKey(HotkeyKey value)
        {
            Settings.Default.AutoKeyActionKey = value.ToString();
            Settings.Default.Save();
        }

        public int GetAutoKeyIntervalMs()
        {
            var value = Settings.Default.AutoKeyIntervalMs;
            return value is >= 100 and <= 2000 ? value : 1000;
        }

        public void SetAutoKeyIntervalMs(int value)
        {
            Settings.Default.AutoKeyIntervalMs = value;
            Settings.Default.Save();
        }

        // --- Mining Module ---

        public bool GetMiningEnabled()
        {
            return Settings.Default.MiningEnabled;
        }

        public void SetMiningEnabled(bool value)
        {
            Settings.Default.MiningEnabled = value;
            Settings.Default.Save();
        }

        public int GetMiningCycleIntervalMs()
        {
            var value = Settings.Default.MiningCycleIntervalMs;
            return value is >= 50 and <= 1000 ? value : 200;
        }

        public void SetMiningCycleIntervalMs(int value)
        {
            Settings.Default.MiningCycleIntervalMs = value;
            Settings.Default.Save();
        }

        public bool GetMiningManualRoiEnabled()
        {
            return Settings.Default.MiningManualRoiEnabled;
        }

        public void SetMiningManualRoiEnabled(bool value)
        {
            Settings.Default.MiningManualRoiEnabled = value;
            Settings.Default.Save();
        }

        public System.Windows.Rect GetMiningRoi()
        {
            return new System.Windows.Rect(
                Settings.Default.MiningRoiX,
                Settings.Default.MiningRoiY,
                Settings.Default.MiningRoiWidth,
                Settings.Default.MiningRoiHeight);
        }

        public void SetMiningRoi(System.Windows.Rect roi)
        {
            Settings.Default.MiningRoiX = roi.X;
            Settings.Default.MiningRoiY = roi.Y;
            Settings.Default.MiningRoiWidth = roi.Width;
            Settings.Default.MiningRoiHeight = roi.Height;
            Settings.Default.Save();
        }

        public bool GetMiningShowRoiDebug()
        {
            return Settings.Default.MiningShowRoiDebug;
        }

        public void SetMiningShowRoiDebug(bool value)
        {
            Settings.Default.MiningShowRoiDebug = value;
            Settings.Default.Save();
        }

        // --- Mining Overlay Position ---

        public double GetMiningOverlayX() => Settings.Default.MiningOverlayX;
        public void SetMiningOverlayX(double value) { Settings.Default.MiningOverlayX = value; Settings.Default.Save(); }

        public double GetMiningOverlayY() => Settings.Default.MiningOverlayY;
        public void SetMiningOverlayY(double value) { Settings.Default.MiningOverlayY = value; Settings.Default.Save(); }

        public double GetMiningOverlayWidth() => Settings.Default.MiningOverlayWidth;
        public void SetMiningOverlayWidth(double value) { Settings.Default.MiningOverlayWidth = value; Settings.Default.Save(); }

        public double GetMiningOverlayHeight() => Settings.Default.MiningOverlayHeight;
        public void SetMiningOverlayHeight(double value) { Settings.Default.MiningOverlayHeight = value; Settings.Default.Save(); }

        public double GetMiningOverlayOpacity() => Settings.Default.MiningOverlayOpacity;
        public void SetMiningOverlayOpacity(double value) { Settings.Default.MiningOverlayOpacity = value; Settings.Default.Save(); }
    }
}
