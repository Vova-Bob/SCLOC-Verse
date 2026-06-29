namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Повідомлення Windows, які обробляє підсистема вводу.
    /// </summary>
    public static class WindowMessage
    {
        /// <summary>
        /// WM_HOTKEY.
        /// </summary>
        public const int WmHotkey = 0x0312;

        /// <summary>
        /// WM_INPUT.
        /// </summary>
        public const int WmInput = 0x00FF;
    }
}
