using SCLOCVerse.Interfaces;
using SCLOCVerse.Models;
using SCLOCVerse.Services.LocalizationServices;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SCLOCVerse.Helpers
{
    public class ButtonHelper : IButtonHelper
    {
        private const string AutoTextDefault = "Автопошук";
        private const string ResetTextDefault = "Скинути";

        private static readonly SolidColorBrush BrushAuto;
        private static readonly SolidColorBrush BrushReset;

        static ButtonHelper()
        {
            var auto = Color.FromRgb(0x30, 0x54, 0x6E);
            var reset = Color.FromRgb(0x7E, 0x2D, 0x2D);

            BrushAuto = new SolidColorBrush(auto);
            BrushReset = new SolidColorBrush(reset);

            BrushAuto.Freeze();
            BrushReset.Freeze();
        }

        public void ToggleAutoSearch(Button button)
        {
            if (button is null) return;
            bool nextActive = !IsResetState(button.Content as string);
            SetButtonState(button, nextActive, ResetTextDefault, AutoTextDefault);
        }

        public void SetButtonState(Button button, bool active, string activeText = ResetTextDefault, string inactiveText = AutoTextDefault)
        {
            if (button is null) return;
            if (!button.Dispatcher.CheckAccess())
                button.Dispatcher.Invoke(() => ApplyState(button, active, activeText, inactiveText));
            else
                ApplyState(button, active, activeText, inactiveText);
        }

        public string GetInstallButtonText(EnvironmentOption? env, string? localFolder)
        {
            if (env is null) return "Встановити";

            string? folder =
                !string.IsNullOrWhiteSpace(env.FolderPath) ? env.FolderPath :
                (!string.IsNullOrWhiteSpace(localFolder) ? Path.Combine(localFolder!, env.Name) : null);

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return "Встановити";

            return LocalizationInstaller.IsLocalizationInstalled(folder!, env.Name) ? "Оновити" : "Встановити";
        }

        public string GetLiaInstallButtonText(string? updateMessage)
        {
            if (string.IsNullOrWhiteSpace(updateMessage))
                return "Встановити";

            string msg = updateMessage.ToLowerInvariant();

            if (msg.Contains("не встановлено") || msg.Contains("не знайдено") || msg.Contains("бракує") || msg.Contains("відсутн"))
                return "Встановити";

            if (msg.Contains("актуальна версія"))
                return "Актуально";

            if (msg.Contains("доступне оновлення"))
                return "Оновити";

            return "Встановити";
        }

        private static bool IsResetState(string? contentText) =>
            string.Equals(contentText, ResetTextDefault, StringComparison.Ordinal);

        private static void ApplyState(Button button, bool active, string activeText, string inactiveText)
        {
            button.Content = active ? activeText : inactiveText;
            button.Background = active ? BrushReset : BrushAuto;
        }
    }
}
