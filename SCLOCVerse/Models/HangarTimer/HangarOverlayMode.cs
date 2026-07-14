namespace SCLOCVerse.Models.HangarTimer
{
    /// <summary>
    /// Режим відображення overlay Hangar Timer.
    /// Classic — повний вигляд (статус, таймер, LED, підказки).
    /// Simplified — компактний бейдж (5 LED + таймер).
    /// </summary>
    public enum HangarOverlayMode
    {
        Classic = 0,
        Simplified = 1
    }
}