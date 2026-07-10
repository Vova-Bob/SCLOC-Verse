using System;
using System.Collections.Generic;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Серіалізатор HotkeyGesture у/з рядка для JSON-persistence.
    /// Формат збереження використовує імена enum (однозначний парсинг):
    ///   "Ctrl+Shift+F7", "F6", "Ctrl+OemPlus", "Alt+OemMinus".
    /// Не плутати з відображенням у UI (FormatGesture використовує символи +/−/0).
    /// </summary>
    public static class HotkeyGestureString
    {
        private static readonly Dictionary<HotkeyModifiers, string> ModifierOrder = new()
        {
            [HotkeyModifiers.Control] = "Ctrl",
            [HotkeyModifiers.Alt] = "Alt",
            [HotkeyModifiers.Shift] = "Shift",
            [HotkeyModifiers.Win] = "Win"
        };

        /// <summary>
        /// Серіалізує жест у рядок ("Ctrl+Shift+F7"). Без модифікаторів — лише клавіша ("F6").
        /// </summary>
        public static string Serialize(HotkeyGesture gesture)
        {
            var parts = new List<string>(5);

            foreach (var (mod, name) in ModifierOrder)
            {
                if ((gesture.Modifiers & mod) != 0)
                    parts.Add(name);
            }

            parts.Add(gesture.Key.ToString());
            return string.Join("+", parts);
        }

        /// <summary>
        /// Парсить рядок жесту. Киняє FormatException при невалідному форматі.
        /// </summary>
        public static HotkeyGesture Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException("Порожній жест.");

            var tokens = value.Split('+', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                throw new FormatException("Жест без токенів.");

            HotkeyModifiers modifiers = HotkeyModifiers.None;
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                modifiers |= ParseModifier(tokens[i]);
            }

            var key = ParseKey(tokens[^1]);
            return new HotkeyGesture(modifiers, key);
        }

        /// <summary>
        /// Безпечний парсинг. Повертає false при невалідному форматі.
        /// </summary>
        public static bool TryParse(string value, out HotkeyGesture gesture)
        {
            try
            {
                gesture = Parse(value);
                return true;
            }
            catch
            {
                gesture = default;
                return false;
            }
        }

        private static HotkeyModifiers ParseModifier(string token)
        {
            return token switch
            {
                "Ctrl" or "Control" => HotkeyModifiers.Control,
                "Alt" => HotkeyModifiers.Alt,
                "Shift" => HotkeyModifiers.Shift,
                "Win" => HotkeyModifiers.Win,
                _ => throw new FormatException($"Невідомий модифікатор: {token}")
            };
        }

        private static HotkeyKey ParseKey(string token)
        {
            if (Enum.TryParse<HotkeyKey>(token, ignoreCase: true, out var key))
                return key;

            throw new FormatException($"Невідома клавіша: {token}");
        }
    }
}