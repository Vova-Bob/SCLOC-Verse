using System.Windows.Input;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Мапінг WPF Key → HotkeyGesture для capture-режиму Settings Hub.
    /// PreviewKeyDown вікна дає System.Windows.Input.Key; HotkeyService очікує HotkeyKey.
    /// </summary>
    public static class HotkeyCaptureMapper
    {
        /// <summary>
        /// Будує HotkeyGesture з WPF KeyEventArgs, або null якщо Key — модифікатор (неповна комбінація).
        /// null = «тільки модифікатори» → capture продовжується (InvalidGesture при спробі зберегти).
        /// </summary>
        public static HotkeyGesture? TryMap(Key key, ModifierKeys modifiers)
        {
            var hotkeyKey = MapKey(key);
            if (hotkeyKey is null)
                return null; // модифікатор або клавіша поза відомим набором

            var hotkeyMods = MapModifiers(modifiers);
            return new HotkeyGesture(hotkeyMods, hotkeyKey.Value);
        }

        /// <summary>
        /// Чи є WPF Key модифікатором (Ctrl/Shift/Alt/Win) — не клавіша.
        /// </summary>
        public static bool IsModifierKey(Key key)
        {
            return key is Key.LeftCtrl or Key.RightCtrl
                or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt
                or Key.LWin or Key.RWin;
        }

        private static HotkeyKey? MapKey(Key key)
        {
            return key switch
            {
                // F1–F24
                Key.F1 => HotkeyKey.F1, Key.F2 => HotkeyKey.F2, Key.F3 => HotkeyKey.F3,
                Key.F4 => HotkeyKey.F4, Key.F5 => HotkeyKey.F5, Key.F6 => HotkeyKey.F6,
                Key.F7 => HotkeyKey.F7, Key.F8 => HotkeyKey.F8, Key.F9 => HotkeyKey.F9,
                Key.F10 => HotkeyKey.F10, Key.F11 => HotkeyKey.F11, Key.F12 => HotkeyKey.F12,
                Key.F13 => HotkeyKey.F13, Key.F14 => HotkeyKey.F14, Key.F15 => HotkeyKey.F15,
                Key.F16 => HotkeyKey.F16, Key.F17 => HotkeyKey.F17, Key.F18 => HotkeyKey.F18,
                Key.F19 => HotkeyKey.F19, Key.F20 => HotkeyKey.F20, Key.F21 => HotkeyKey.F21,
                Key.F22 => HotkeyKey.F22, Key.F23 => HotkeyKey.F23, Key.F24 => HotkeyKey.F24,

                // Цифри верхнього ряду
                Key.D0 => HotkeyKey.D0, Key.D1 => HotkeyKey.D1, Key.D2 => HotkeyKey.D2,
                Key.D3 => HotkeyKey.D3, Key.D4 => HotkeyKey.D4, Key.D5 => HotkeyKey.D5,
                Key.D6 => HotkeyKey.D6, Key.D7 => HotkeyKey.D7, Key.D8 => HotkeyKey.D8,
                Key.D9 => HotkeyKey.D9,

                // NumPad
                Key.NumPad0 => HotkeyKey.NumPad0, Key.NumPad1 => HotkeyKey.NumPad1,
                Key.NumPad2 => HotkeyKey.NumPad2, Key.NumPad3 => HotkeyKey.NumPad3,
                Key.NumPad4 => HotkeyKey.NumPad4, Key.NumPad5 => HotkeyKey.NumPad5,
                Key.NumPad6 => HotkeyKey.NumPad6, Key.NumPad7 => HotkeyKey.NumPad7,
                Key.NumPad8 => HotkeyKey.NumPad8, Key.NumPad9 => HotkeyKey.NumPad9,

                // Літери A–Z
                Key.A => HotkeyKey.A, Key.B => HotkeyKey.B, Key.C => HotkeyKey.C,
                Key.D => HotkeyKey.D, Key.E => HotkeyKey.E, Key.F => HotkeyKey.F,
                Key.G => HotkeyKey.G, Key.H => HotkeyKey.H, Key.I => HotkeyKey.I,
                Key.J => HotkeyKey.J, Key.K => HotkeyKey.K, Key.L => HotkeyKey.L,
                Key.M => HotkeyKey.M, Key.N => HotkeyKey.N, Key.O => HotkeyKey.O,
                Key.P => HotkeyKey.P, Key.Q => HotkeyKey.Q, Key.R => HotkeyKey.R,
                Key.S => HotkeyKey.S, Key.T => HotkeyKey.T, Key.U => HotkeyKey.U,
                Key.V => HotkeyKey.V, Key.W => HotkeyKey.W, Key.X => HotkeyKey.X,
                Key.Y => HotkeyKey.Y, Key.Z => HotkeyKey.Z,

                // Навігація / редагування
                Key.Escape => HotkeyKey.Escape,
                Key.Tab => HotkeyKey.Tab,
                Key.Enter => HotkeyKey.Enter,
                Key.Space => HotkeyKey.Space,
                Key.Insert => HotkeyKey.Insert,
                Key.Delete => HotkeyKey.Delete,
                Key.Home => HotkeyKey.Home,
                Key.End => HotkeyKey.End,
                Key.PageUp => HotkeyKey.PageUp,
                Key.PageDown => HotkeyKey.PageDown,
                Key.Left => HotkeyKey.Left,
                Key.Up => HotkeyKey.Up,
                Key.Right => HotkeyKey.Right,
                Key.Down => HotkeyKey.Down,

                // OEM
                Key.OemMinus => HotkeyKey.OemMinus,
                Key.OemPlus => HotkeyKey.OemPlus,
                Key.OemComma => HotkeyKey.OemComma,
                Key.OemPeriod => HotkeyKey.OemPeriod,
                Key.Oem1 => HotkeyKey.Oem1,
                Key.Oem2 => HotkeyKey.Oem2,
                Key.Oem3 => HotkeyKey.Oem3,
                Key.Oem4 => HotkeyKey.Oem4,
                Key.Oem5 => HotkeyKey.Oem5,
                Key.Oem6 => HotkeyKey.Oem6,
                Key.Oem7 => HotkeyKey.Oem7,

                _ => null
            };
        }

        private static HotkeyModifiers MapModifiers(ModifierKeys modifiers)
        {
            HotkeyModifiers result = HotkeyModifiers.None;
            if ((modifiers & ModifierKeys.Control) != 0) result |= HotkeyModifiers.Control;
            if ((modifiers & ModifierKeys.Alt) != 0) result |= HotkeyModifiers.Alt;
            if ((modifiers & ModifierKeys.Shift) != 0) result |= HotkeyModifiers.Shift;
            if ((modifiers & ModifierKeys.Windows) != 0) result |= HotkeyModifiers.Win;
            return result;
        }
    }
}