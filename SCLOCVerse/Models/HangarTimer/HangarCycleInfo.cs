namespace SCLOCVerse.Models.HangarTimer
{
    /// <summary>
    /// Результат обчислення поточного стану циклу Executive Hangar.
    /// </summary>
    public sealed class HangarCycleInfo
    {
        public HangarCyclePhase Phase { get; set; }
        public string TimerText { get; set; } = string.Empty;
        public string StatusMessage { get; set; } = string.Empty;
        public string StatusLine { get; set; } = string.Empty;
        public HangarLightState[] LedStates { get; set; } = Array.Empty<HangarLightState>();
        public int ActiveLedCount { get; set; }
    }
}
