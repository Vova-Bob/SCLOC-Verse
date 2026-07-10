namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Результат зміни жесту гарячої клавіші.
    /// Детальний статус без винятків — UI не аналізує винятки (погоджено Phase 0.5).
    /// </summary>
    public enum RebindResult
    {
        /// <summary>
        /// Жест змінено + персистовано + re-registered у бекенді.
        /// </summary>
        Success,

        /// <summary>
        /// Жест зайнятий іншою активною дією. ConflictingId встановлено.
        /// UI має показати діалог підтвердження; при згоді — повторити з policy=Replace.
        /// </summary>
        Conflict,

        /// <summary>
        /// Capture-валідація: лише модифікатори / неповна комбінація / невалідний жест.
        /// </summary>
        InvalidGesture,

        /// <summary>
        /// Win32 RegisterHotKey FAIL — комбінація зайнята іншим застосунком системно.
        /// </summary>
        RegistrationFailed,

        /// <summary>
        /// Новий жест == поточний EffectiveGesture — змін не потрібно.
        /// </summary>
        Unchanged
    }
}