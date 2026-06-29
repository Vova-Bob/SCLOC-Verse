namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Ідентифікатори глобальних гарячих клавіш Hangar Timer.
    /// </summary>
    public static class HotkeyIds
    {
        /// <summary>
        /// Показати/приховати overlay.
        /// </summary>
        public static HotkeyId HangarToggleOverlay { get; } = new("HangarTimer.ToggleOverlay");

        /// <summary>
        /// Перемкнути режим кліків крізь overlay.
        /// </summary>
        public static HotkeyId HangarToggleClickThrough { get; } = new("HangarTimer.ToggleClickThrough");

        /// <summary>
        /// Тимчасово перетягувати overlay.
        /// </summary>
        public static HotkeyId HangarBeginTemporaryDrag { get; } = new("HangarTimer.BeginTemporaryDrag");

        /// <summary>
        /// Почати цикл зараз.
        /// </summary>
        public static HotkeyId HangarSetStartNow { get; } = new("HangarTimer.SetStartNow");

        /// <summary>
        /// Ввести час старту вручну.
        /// </summary>
        public static HotkeyId HangarPromptManualStart { get; } = new("HangarTimer.PromptManualStart");

        /// <summary>
        /// Синхронізувати час із віддаленим джерелом.
        /// </summary>
        public static HotkeyId HangarForceSync { get; } = new("HangarTimer.ForceSync");

        /// <summary>
        /// Стерти оверрайд і синхронізувати.
        /// </summary>
        public static HotkeyId HangarClearOverrideAndSync { get; } = new("HangarTimer.ClearOverrideAndSync");

        /// <summary>
        /// Зменшити масштаб overlay.
        /// </summary>
        public static HotkeyId HangarScaleDown { get; } = new("HangarTimer.ScaleDown");

        /// <summary>
        /// Збільшити масштаб overlay.
        /// </summary>
        public static HotkeyId HangarScaleUp { get; } = new("HangarTimer.ScaleUp");

        /// <summary>
        /// Скинути масштаб overlay.
        /// </summary>
        public static HotkeyId HangarScaleReset { get; } = new("HangarTimer.ScaleReset");

        /// <summary>
        /// Зменшити прозорість overlay.
        /// </summary>
        public static HotkeyId HangarOpacityDown { get; } = new("HangarTimer.OpacityDown");

        /// <summary>
        /// Збільшити прозорість overlay.
        /// </summary>
        public static HotkeyId HangarOpacityUp { get; } = new("HangarTimer.OpacityUp");

        /// <summary>
        /// Скинути прозорість overlay.
        /// </summary>
        public static HotkeyId HangarOpacityReset { get; } = new("HangarTimer.OpacityReset");
    }
}
