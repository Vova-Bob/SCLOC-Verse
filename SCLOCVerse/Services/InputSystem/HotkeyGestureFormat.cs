using System.Collections.Generic;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Спільне форматування жесту гарячої клавіші → рядок для UI.
    /// Єдине джерело форматування: використовується «Гарячі клавіші» (HotkeysSettingsPane),
    /// popup-підказкою Hangar Timer (HangarTimerCard) та підказкою overlay-вікна
    /// (HangarOverlayWindow) — без дублювання логіки.
    ///
    /// Дані про самі комбінації (актуальний жест) завжди беруться з IHotkeyService.GetDefinitions();
    /// цей клас лише форматує HotkeyGesture у people-readable рядок.
    /// </summary>
    public static class HotkeyGestureFormat
    {
        /// <summary>
        /// Форматує жест у рядок виду «Ctrl+Shift+F7» (модифікатори + клавіша через «+»).
        /// </summary>
        public static string Format(HotkeyGesture gesture)
        {
            var parts = new List<string>(5);
            if ((gesture.Modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((gesture.Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((gesture.Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((gesture.Modifiers & HotkeyModifiers.Win) != 0) parts.Add("Win");
            parts.Add(FormatKey(gesture.Key));
            return string.Join("+", parts);
        }

        /// <summary>
        /// Коротке дружнє ім'я одиночної клавіші (для badge/підказки).
        /// </summary>
        public static string FormatKey(HotkeyKey key) => key switch
        {
            HotkeyKey.Escape => "Esc",
            HotkeyKey.Space => "Space",
            HotkeyKey.Tab => "Tab",
            HotkeyKey.Enter => "Enter",
            HotkeyKey.Insert => "Ins",
            HotkeyKey.Delete => "Del",
            HotkeyKey.PageUp => "PgUp",
            HotkeyKey.PageDown => "PgDn",
            HotkeyKey.OemMinus => "−",
            HotkeyKey.OemPlus => "+",
            HotkeyKey.D0 => "0",
            _ => key.ToString()
        };
    }
}
