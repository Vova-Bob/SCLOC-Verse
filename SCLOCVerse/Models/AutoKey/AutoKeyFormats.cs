using SCLOCVerse.Services.InputSystem;

namespace SCLOCVerse.Models.AutoKey
{
    /// <summary>
    /// Дружнє відображення коду клавіші для Action Key модуля Auto Key.
    /// Переважно OEM-символи (пов'язані з фізичною розкладкою), плюс людино-читані
    /// імена для службових клавіш. Для невідомих — падає на ToString().
    /// </summary>
    public static class AutoKeyFormats
    {
        /// <summary>
        /// Повертає коротке дружнє ім'я клавіші для UI (Action Key keycap).
        /// </summary>
        public static string FormatKey(HotkeyKey key) => key switch
        {
            // OEM — символи для типової задачі «прийняття місій»
            HotkeyKey.Oem4 => "[",       // [{
            HotkeyKey.Oem6 => "]",       // ]}
            HotkeyKey.Oem5 => "\\",      // \|
            HotkeyKey.Oem1 => ";",       // ;:
            HotkeyKey.Oem2 => "/",       // /?
            HotkeyKey.Oem3 => "`",       // `~
            HotkeyKey.Oem7 => "'",       // '"
            HotkeyKey.OemMinus => "-",
            HotkeyKey.OemPlus => "=",
            HotkeyKey.OemComma => ",",
            HotkeyKey.OemPeriod => ".",

            // Службові
            HotkeyKey.Space => "Space",
            HotkeyKey.Enter => "Enter",
            HotkeyKey.Tab => "Tab",
            HotkeyKey.Escape => "Esc",
            HotkeyKey.Insert => "Ins",
            HotkeyKey.Delete => "Del",
            HotkeyKey.Home => "Home",
            HotkeyKey.End => "End",
            HotkeyKey.PageUp => "PgUp",
            HotkeyKey.PageDown => "PgDn",
            HotkeyKey.Left => "←",
            HotkeyKey.Up => "↑",
            HotkeyKey.Right => "→",
            HotkeyKey.Down => "↓",

            // Верхній ряд цифр (D0..D9) та NumPad
            HotkeyKey.D0 => "0", HotkeyKey.D1 => "1", HotkeyKey.D2 => "2",
            HotkeyKey.D3 => "3", HotkeyKey.D4 => "4", HotkeyKey.D5 => "5",
            HotkeyKey.D6 => "6", HotkeyKey.D7 => "7", HotkeyKey.D8 => "8",
            HotkeyKey.D9 => "9",
            HotkeyKey.NumPad0 => "Num 0", HotkeyKey.NumPad1 => "Num 1",
            HotkeyKey.NumPad2 => "Num 2", HotkeyKey.NumPad3 => "Num 3",
            HotkeyKey.NumPad4 => "Num 4", HotkeyKey.NumPad5 => "Num 5",
            HotkeyKey.NumPad6 => "Num 6", HotkeyKey.NumPad7 => "Num 7",
            HotkeyKey.NumPad8 => "Num 8", HotkeyKey.NumPad9 => "Num 9",

            _ => key.ToString()
        };
    }
}
