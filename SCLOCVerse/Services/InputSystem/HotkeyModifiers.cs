using System;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Модифікатори глобальної гарячої клавіші.
    /// </summary>
    [Flags]
    public enum HotkeyModifiers : uint
    {
        /// <summary>
        /// Без модифікаторів.
        /// </summary>
        None = 0,

        /// <summary>
        /// Alt.
        /// </summary>
        Alt = 0x0001,

        /// <summary>
        /// Control.
        /// </summary>
        Control = 0x0002,

        /// <summary>
        /// Shift.
        /// </summary>
        Shift = 0x0004,

        /// <summary>
        /// Windows ключ.
        /// </summary>
        Win = 0x0008
    }
}
