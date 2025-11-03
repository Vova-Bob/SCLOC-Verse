using System.Windows.Controls;

namespace StarCitizenUA.Controls
{
    /// <summary>
    /// Канва для взаємодії з голосовим асистентом.
    /// </summary>
    public partial class AssistantCanvas : Canvas
    {
        public AssistantCanvas()
        {
            InitializeComponent();
        }

        public Button ReturnHomeButton => BtnReturnHome;

        public Button InstallButton => BtnLiaInstall;

        public Button OpenSettingsButton => BtnLiaSettings;
    }
}
