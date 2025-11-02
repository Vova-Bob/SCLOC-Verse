using StarCitizenUA.Interfaces;
using System.Windows.Controls;
using System.Windows.Media;

namespace StarCitizenUA.Helpers
{
    public class ButtonStateManager : IButtonStateManager
    {
        private readonly Button btnLocalization;
        private readonly Button btnAssistant;
        private readonly Button btnSettings;
        private readonly Button btnSelectFolder;
        private readonly Button btnSelectLiaFolder;

        public ButtonStateManager(
            Button loc, Button asst, Button set,
            Button selectFolder, Button selectLiaFolder)
        {
            btnLocalization = loc;
            btnAssistant = asst;
            btnSettings = set;
            btnSelectFolder = selectFolder;
            btnSelectLiaFolder = selectLiaFolder;
        }

        public void SetActive(string active)
        {
            var inactive = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#143A52"));
            var activeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5190C3"));

            btnLocalization.Background = inactive;
            btnAssistant.Background = inactive;
            btnSettings.Background = inactive;

            switch (active)
            {
                case "localization": btnLocalization.Background = activeBrush; break;
                case "assistant": btnAssistant.Background = activeBrush; break;
                case "settings": btnSettings.Background = activeBrush; break;
            }
        }

        public void SetButtonEnabled(Button button, bool enabled)
        {
            if (button == null) return;
            button.IsEnabled = enabled;
            button.Opacity = enabled ? 1.0 : 0.5;
        }

        public void UpdateFolderButtons(bool isPathSet, bool isLiaPathSet)
        {
            SetButtonEnabled(btnSelectFolder, !isPathSet);
            SetButtonEnabled(btnSelectLiaFolder, !isLiaPathSet);
        }
    }
}