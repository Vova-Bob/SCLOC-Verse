using System.Windows.Controls;

namespace StarCitizenUA.Controls
{
    public partial class AssistantCanvas : Canvas
    {
        public AssistantCanvas()
        {
            InitializeComponent();
        }

        public Button ReturnHomeButton => BtnReturnHome;
        public Button InstallButton => BtnLiaInstall;
        public Button OpenSettingsButton => BtnLiaSettings;
        public TextBox ReadmeTextBox => TxtLiaReadme;
        public TextBox PathVersionTextBox => TxtLiaVersionPath;
        public TextBox SetupInfoTextBox => TxtLiaSetupe;
    }
}