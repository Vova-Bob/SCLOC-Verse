using StarCitizenUA.Models;
using System.Windows.Controls;

namespace StarCitizenUA.Interfaces
{
    public interface IButtonHelper
    {
        void SetButtonState(Button button, bool active, string activeText = "Скинути", string inactiveText = "Автопошук");
        string GetInstallButtonText(EnvironmentOption? env, string? localFolder);
        string GetLiaInstallButtonText(string? updateMessage);
    }
}
