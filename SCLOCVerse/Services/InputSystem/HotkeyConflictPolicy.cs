namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Політика розв'язання конфліктів при реєстрації гарячих клавіш.
    /// </summary>
    public enum HotkeyConflictPolicy
    {
        /// <summary>
        /// Відхилити нову реєстрацію, якщо комбінація вже зайнята.
        /// </summary>
        Reject,

        /// <summary>
        /// Замінити існуючу реєстрацію новою.
        /// </summary>
        Replace,

        /// <summary>
        /// Дозволити обидві реєстрації, вибираючи за пріоритетом.
        /// </summary>
        AllowPriority
    }
}
