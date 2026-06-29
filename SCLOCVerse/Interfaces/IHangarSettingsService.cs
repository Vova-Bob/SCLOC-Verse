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

        bool HasCycleStartOverride();
        long GetCycleStartOverride();
        void SetCycleStartOverride(long startMs);
        void ClearCycleStartOverride();
    }
}
