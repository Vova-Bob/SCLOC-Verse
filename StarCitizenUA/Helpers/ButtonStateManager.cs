using StarCitizenUA.Interfaces;
using System.Windows.Controls;

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

            btnLocalization.Tag = null;
            btnAssistant.Tag = null;
            btnSettings.Tag = null;

            switch (active)
            {
                case "localization": btnLocalization.Tag = "Active"; break;
                case "assistant": btnAssistant.Tag = "Active"; break;
                case "settings": btnSettings.Tag = "Active"; break;
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
