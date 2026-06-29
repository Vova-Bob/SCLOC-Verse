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
        /// Environment variable має пріоритет над налаштуваннями застосунку.
        /// </summary>
        private static IHotkeyBackend CreateHotkeyBackend()
        {
            var backendName = Environment.GetEnvironmentVariable("SCLOCVERSE_HOTKEY_BACKEND")
                ?? Settings.Default.InputSystemBackend
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
        /// Environment variable має пріоритет над налаштуваннями застосунку.
        /// </summary>
        private static bool IsHotkeyDiagnosticsEnabled()
        {
            var envValue = Environment.GetEnvironmentVariable("SCLOCVERSE_HOTKEY_DIAGNOSTICS");
            if (!string.IsNullOrWhiteSpace(envValue))
                return bool.TryParse(envValue, out var result) && result;

            return Settings.Default.InputSystemDiagnostics;
        }
    }
}