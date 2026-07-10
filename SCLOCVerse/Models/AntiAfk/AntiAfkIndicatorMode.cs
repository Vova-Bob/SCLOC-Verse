namespace SCLOCVerse.Models.AntiAfk
{
    /// <summary>
    /// Режим видимості індикатора Anti-AFK.
    /// </summary>
    public enum AntiAfkIndicatorMode
    {
        /// <summary>Індикатор видимий безперервно, поки Anti-AFK увімкнено.</summary>
        Running,

        /// <summary>Індикатор спалахує лише в момент виконання SimulatedInput (SendInput).</summary>
        IdleOnly
    }
}
