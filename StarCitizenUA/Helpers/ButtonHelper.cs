using StarCitizenUA.Interfaces;
using StarCitizenUA.Models;
using StarCitizenUA.Services.LocalizationServices;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;

namespace StarCitizenUA.Helpers
{
    public class ButtonHelper : IButtonHelper
    {
        public void SetButtonState(Button button, bool active, string activeText = "Скинути", string inactiveText = "Автопошук")
        {
            if (button == null) return;

            var btnText = (TextBlock)button.Template.FindName("BtnText", button);
            var bgPath = (System.Windows.Shapes.Path)button.Template.FindName("BgPath", button);

            if (btnText == null || bgPath == null) return;

            if (active)
            {
                btnText.Text = activeText;
                bgPath.Fill = new SolidColorBrush(Colors.IndianRed);
            }
            else
            {
                btnText.Text = inactiveText;
                bgPath.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30546E")!);
            }
        }

        public string GetInstallButtonText(EnvironmentOption? env, string? localFolder)
        {
            if (env == null) return "Встановити";

            string? folder = !string.IsNullOrWhiteSpace(env.FolderPath)
                ? env.FolderPath
                : (!string.IsNullOrWhiteSpace(localFolder) ? Path.Combine(localFolder, env.Name) : null);

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return "Встановити";

            return LocalizationInstaller.IsLocalizationInstalled(folder, env.Name)
                ? "Оновити"
                : "Встановити";
        }
        
        public string GetLiaInstallButtonText(string? updateMessage)
        {
            if (string.IsNullOrWhiteSpace(updateMessage))
                return "Встановити";

            string msg = updateMessage.ToLowerInvariant().Trim();

            if (msg.Contains("не знайдено") || msg.Contains("бракує") || msg.Contains("відсутн"))
                return "Завантажити";

            if (msg.Contains("актуальна версія"))
                return "Актуально";

            if (msg.Contains("доступне оновлення"))
                return "Оновити";

            return "Встановити";
        }
    }
}