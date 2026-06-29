using SCLOCVerse.Interfaces;

namespace SCLOCVerse.Services.HangarTimer
{
    /// <summary>
    /// Реалізація налаштувань модуля Hangar Timer через SCLOC-Verse Settings.
    /// </summary>
    public class HangarSettingsService : IHangarSettingsService
    {
        public double GetOverlayX()
        {
            var value = Settings.Default.HangarOverlayX;
            return double.IsNaN(value) ? 20.0 : value;
        }

        public double GetOverlayY()
        {
            var value = Settings.Default.HangarOverlayY;
            return double.IsNaN(value) ? 20.0 : value;
        }

        public void SetOverlayPosition(double x, double y)
        {
            Settings.Default.HangarOverlayX = x;
            Settings.Default.HangarOverlayY = y;
            Settings.Default.Save();
        }

        public double GetOverlayScale()
        {
            var value = Settings.Default.HangarOverlayScale;
            return value <= 0 ? 0.6 : value;
        }

        public void SetOverlayScale(double scale)
        {
            Settings.Default.HangarOverlayScale = scale;
            Settings.Default.Save();
        }

        public double GetOverlayOpacity()
        {
            var value = Settings.Default.HangarOverlayOpacity;
            return value <= 0 ? 0.92 : value;
        }

        public void SetOverlayOpacity(double opacity)
        {
            Settings.Default.HangarOverlayOpacity = opacity;
            Settings.Default.Save();
        }

        public bool HasCycleStartOverride()
        {
            return Settings.Default.HangarCycleStartOverride != 0;
        }

        public long GetCycleStartOverride()
        {
            return Settings.Default.HangarCycleStartOverride;
        }

        public void SetCycleStartOverride(long startMs)
        {
            Settings.Default.HangarCycleStartOverride = startMs;
            Settings.Default.Save();
        }

        public void ClearCycleStartOverride()
        {
            Settings.Default.HangarCycleStartOverride = 0;
            Settings.Default.Save();
        }
    }
}
