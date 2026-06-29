using SCLOCVerse.Services.InputSystem;
using System;

namespace SCLOCVerse.Composition
{
    /// <summary>
    /// Частина AppCompositionRoot, що відповідає за вибір бекенду гарячих клавіш.
    /// </summary>
    public partial class AppCompositionRoot
    {
        /// <summary>
        /// Створює бекенд гарячих клавіш відповідно до конфігурації.
        /// </summary>
        private static IHotkeyBackend CreateHotkeyBackend()
        {
            var backendName = Environment.GetEnvironmentVariable("SCLOCVERSE_HOTKEY_BACKEND")
                ?? "RegisterHotKey";

            return backendName switch
            {
                "RawInput" => new RawInputBackend(),
                _ => new RegisterHotkeyBackend()
            };
        }
    }
}
