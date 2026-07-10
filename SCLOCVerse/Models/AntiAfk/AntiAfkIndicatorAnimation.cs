namespace SCLOCVerse.Models.AntiAfk
{
    /// <summary>
    /// Тип анімації пульсації індикатора Anti-AFK.
    /// </summary>
    public enum AntiAfkIndicatorAnimation
    {
        /// <summary>Різкий ритмічний пульс (0.6с цикл).</summary>
        Pulse,

        /// <summary>Повільне дихання (2.5с цикл, плавний fade).</summary>
        Breathing
    }
}
