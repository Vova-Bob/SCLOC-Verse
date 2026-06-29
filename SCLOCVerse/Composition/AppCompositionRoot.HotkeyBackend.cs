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
                ?? "RawInput";

            var diagnosticsEnabled = IsHotkeyDiagnosticsEnabled();

            return backendName switch
            {
                "RegisterHotKey" => new RegisterHotkeyBackend(),
                "RawInput" => new RawInputBackend(diagnosticsEnabled),
                _ => new RawInputBackend(diagnosticsEnabled)
            };
        }

        /// <summary>
        /// Визначає, чи увімкнено діагностичне журналювання гарячих клавіш.
        /// </summary>
        private static bool IsHotkeyDiagnosticsEnabled()
        {
            var value = Environment.GetEnvironmentVariable("SCLOCVERSE_HOTKEY_DIAGNOSTICS");

            if (string.IsNullOrWhiteSpace(value))
                return false;

            return bool.TryParse(value, out var result) && result;
        }
    }
}
