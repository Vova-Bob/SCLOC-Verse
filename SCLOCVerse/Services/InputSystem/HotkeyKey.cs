namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Віртуальні коди клавіш для глобальних гарячих клавіш.
    /// Відповідають Win32 Virtual-Key Codes.
    /// </summary>
    public enum HotkeyKey : uint
    {
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        Escape = 0x1B,
        D0 = 0x30,
        OemMinus = 0xBD,
        OemPlus = 0xBB
    }
}
