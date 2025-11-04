using System.Windows.Controls;

namespace StarCitizenUA.Controls
{
    public partial class LocalizationCanvas : Canvas
    {
        public LocalizationCanvas()
        {
            InitializeComponent();
        }

        public EnvironmentSelector EnvironmentSelector => EnvSelector;
        public Button InstallButton => BtnInstall;
        public Button DeleteButton => BtnLocalisationDelete;
        public Button ReturnHomeButton => BtnReturnHome;
        public TextBox ReadmeTextBox => TxtLocalizationReadme;
    }
}
