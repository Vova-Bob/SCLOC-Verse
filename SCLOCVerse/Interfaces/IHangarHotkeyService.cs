namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Сервіс глобальних гарячих клавіш Hangar Timer.
    /// </summary>
    public interface IHangarHotkeyService
    {
        /// <summary>
        /// Зареєструвати глобальні гарячі клавіші для вікна з заданим хендлом.
        /// </summary>
        void Register(IntPtr windowHandle);

        /// <summary>
        /// Скасувати реєстрацію всіх гарячих клавіш.
        /// </summary>
        void Unregister();

        /// <summary>
        /// Звільнити ресурси сервісу.
        /// </summary>
        void Dispose();

        /// <summary>
        /// Подія, яка виникає при спрацюванні будь-якої зареєстрованої комбінації.
        /// </summary>
        event EventHandler<HangarHotkeyAction>? ActionRequested;
    }

    /// <summary>
    /// Дії, доступні через глобальні гарячі клавіші Hangar Timer.
    /// </summary>
    public enum HangarHotkeyAction
    {
        ToggleOverlay,
        ToggleClickThrough,
        BeginTemporaryDrag,
        SetStartNow,
        PromptManualStart,
        ForceSync,
        ClearOverrideAndSync,
        ScaleDown,
        ScaleUp,
        ScaleReset,
        OpacityDown,
        OpacityUp,
        OpacityReset
    }
}
