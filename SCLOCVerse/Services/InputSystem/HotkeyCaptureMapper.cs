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
                Key.F6 => HotkeyKey.F6,
                Key.F7 => HotkeyKey.F7,
                Key.F8 => HotkeyKey.F8,
                Key.F9 => HotkeyKey.F9,
                Key.OemMinus => HotkeyKey.OemMinus,
                Key.OemPlus => HotkeyKey.OemPlus,
                Key.D0 => HotkeyKey.D0,
                Key.NumPad0 => HotkeyKey.D0,
                Key.Escape => HotkeyKey.Escape,
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