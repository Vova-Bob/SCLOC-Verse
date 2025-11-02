using System.Windows.Controls;

namespace StarCitizenUA.Controls
{
    /// <summary>
    /// Канва з елементами встановлення локалізації.
    /// </summary>
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
    }
}
