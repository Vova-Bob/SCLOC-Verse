using SCLOCVerse.Models;
using System.Windows.Controls;

namespace SCLOCVerse.Interfaces
{
    public interface IButtonHelper
    {
        void SetButtonState(Button button, bool active, string activeText = "Скинути", string inactiveText = "Автопошук");
        string GetInstallButtonText(EnvironmentOption? env, string? localFolder);
        string GetLiaInstallButtonText(string? updateMessage);
    }
}
