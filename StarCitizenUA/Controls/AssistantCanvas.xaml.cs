using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

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
        public Button DeleteButton => BtnLiaDelete;
        public Button VoiceAttackButton => BtnLiaSettingsVA;
        public RichTextBox ReadmeTextBox => TxtLiaReadme;
        public TextBox PathVersionTextBox => TxtLiaVersionPath;
        public TextBox SetupInfoTextBox => TxtLiaSetupe;

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}