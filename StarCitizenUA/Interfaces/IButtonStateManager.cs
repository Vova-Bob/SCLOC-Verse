using System.Windows.Controls;

namespace StarCitizenUA.Interfaces
{
    public interface IButtonStateManager
    {
        void SetActive(string active);
        void SetButtonEnabled(Button button, bool enabled);
    }
}
