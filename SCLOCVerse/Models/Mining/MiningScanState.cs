namespace SCLOCVerse.Models.Mining
{
    /// <summary>
    /// Стани сканування mining-сигнатур для Overlay Scanner.
    ///
    /// <para><b>State Machine:</b></para>
    /// <para>
    /// <code>
    /// Idle ────────► Scanning ────────► Detected
    ///   ▲               │                  │
    ///   │               │                  ▼
    ///   │               ▼               Weak (OCR деградує)
    ///   │          (HUD не              │
    ///   │           знайдено)            ▼
    ///   │                            Lost (age > timeout)
    ///   │                               │
    ///   └───────────────────────────────┘
    ///   (Lost → Idle після короткого показу "Сигнал втрачено")
    /// </code>
    /// </para>
    ///
    /// <para><b>Переходи:</b></para>
    /// <list type="bullet">
    /// <item><b>Idle → Scanning:</b> Enable() або Discovery знайшов HUD.</item>
    /// <item><b>Scanning → Detected:</b> Успішний OCR, AllCandidates непорожній.</item>
    /// <item><b>Detected → Weak:</b> OCR промах, age &lt; ResultAgeTimeoutMs (grace period).</item>
    /// <item><b>Weak → Detected:</b> OCR відновився.</item>
    /// <item><b>Weak → Lost:</b> age ≥ ResultAgeTimeoutMs.</item>
    /// <item><b>Detected → Lost:</b> Resolver → Discovery (5 nulls).</item>
    /// <item><b>Lost → Idle:</b> Короткий показ "Сигнал втрачено", потім скидання.</item>
    /// <item><b>Lost → Scanning:</b> Discovery знайшов новий HUD (швидке відновлення).</item>
    /// <item><b>Any → Idle:</b> Disable().</item>
    /// </list>
    ///
    /// <para><b>Overlay відображення:</b></para>
    /// <list type="bullet">
    /// <item><b>Idle:</b> Overlay прихований або "Сканування...".</item>
    /// <item><b>Scanning:</b> "Сканування...".</item>
    /// <item><b>Detected:</b> Сигнатура (single/multi candidate).</item>
    /// <item><b>Weak:</b> Остання сигнатура (утримується, без мигання).</item>
    /// <item><b>Lost:</b> "Сигнал втрачено" (#FF6B6B).</item>
    /// </list>
    /// </summary>
    public enum MiningScanState
    {
        /// <summary>
        /// Немає сканування. Overlay прихований або показує "Сканування...".
        /// Початковий стан після Enable() і після Lost → Idle transition.
        /// </summary>
        Idle = 0,

        /// <summary>
        /// Є процес сканування (Discovery timer active), але OCR ще не впевнений.
        /// Overlay показує "Сканування...".
        /// </summary>
        Scanning = 1,

        /// <summary>
        /// Є стабільна сигнатура. AllCandidates непорожній.
        /// Overlay показує сигнатуру (single/multi candidate).
        /// </summary>
        Detected = 2,

        /// <summary>
        /// Раніше була сигнатура (Detected), тепер OCR промахнувся.
        /// Результат утримується протягом ResultAgeTimeoutMs (grace period).
        /// Overlay продовжує показувати останню сигнатуру (без мигання).
        /// </summary>
        Weak = 3,

        /// <summary>
        /// Раніше була сигнатура, OCR її більше не бачить, ResultAge timeout минув.
        /// Overlay показує "Сигнал втрачено" (#FF6B6B) короткий час, потім → Idle.
        /// </summary>
        Lost = 4
    }
}