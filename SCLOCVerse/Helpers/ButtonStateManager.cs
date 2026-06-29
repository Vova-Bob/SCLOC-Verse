using SCLOCVerse.Interfaces;
using System.Windows.Controls;

namespace SCLOCVerse.Helpers
{
    public class ButtonStateManager : IButtonStateManager
    {
        private readonly Button btnLocalization;
        private readonly Button btnAssistant;
        private readonly Button btnSettings;
        private readonly Button btnScTools;
        private readonly Button btnSelectFolder;

        public ButtonStateManager(
            Button loc, Button asst, Button set, Button scTools,
            Button selectFolder)
        {
            btnLocalization = loc;
            btnAssistant = asst;
            btnSettings = set;
            btnScTools = scTools;
            btnSelectFolder = selectFolder;
        }

        public void SetActive(string active)
        {

            btnLocalization.Tag = null;
            btnAssistant.Tag = null;
            btnSettings.Tag = null;
            btnScTools.Tag = null;

            switch (active)
            {
                case "localization": btnLocalization.Tag = "Active"; break;
                case "assistant": btnAssistant.Tag = "Active"; break;
                case "settings": btnSettings.Tag = "Active"; break;
                case "sctools": btnScTools.Tag = "Active"; break;
            }
        }

        public void SetButtonEnabled(Button button, bool enabled)
        {
            if (button == null) return;
            button.IsEnabled = enabled;
            button.Opacity = enabled ? 1.0 : 0.5;
        }

        public void UpdateFolderButtons(bool isPathSet)
        {
            SetButtonEnabled(btnSelectFolder, !isPathSet);
        }
    }
}
