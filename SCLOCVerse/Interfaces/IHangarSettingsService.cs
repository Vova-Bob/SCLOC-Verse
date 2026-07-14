using SCLOCVerse.Models.HangarTimer;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Налаштування модуля Hangar Timer.
    /// </summary>
    public interface IHangarSettingsService
    {
        double GetOverlayX();
        double GetOverlayY();
        void SetOverlayPosition(double x, double y);

        double GetOverlayScale();
        void SetOverlayScale(double scale);

        double GetOverlayOpacity();
        void SetOverlayOpacity(double opacity);

        HangarOverlayMode GetOverlayMode();
        void SetOverlayMode(HangarOverlayMode mode);

        bool HasCycleStartOverride();
        long GetCycleStartOverride();
        void SetCycleStartOverride(long startMs);
        void ClearCycleStartOverride();
    }
}
