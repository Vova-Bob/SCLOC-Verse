using System.Windows.Controls;

namespace SCLOCVerse.Controls.SettingsHub
{
    /// <summary>
    /// Панель категорії «Загальне» Settings Hub.
    /// Містить мігровані з SettingsCanvas контролі (ті самі x:Name),
    /// щоб MainWindow.xaml.cs продовжував працювати без змін (Zero Regression).
    /// </summary>
    public partial class GeneralSettingsPane : UserControl
    {
        public GeneralSettingsPane()
        {
            InitializeComponent();
        }

        // Відкриті контролі для фасаду SettingsCanvas → MainWindow.
        public Button SelectFolderButton => BtnSelectFolder;
        public Button AutoSearchButton => BtnAutoSearch;
        public Button ResetCacheButton => BtnResetCash;
        public TextBox SelectedPathTextBox => TxtSelectedPath;
        public TextBox ReadmeTextBox => TxtReadme;
        public ComboBox UpdateChannelSelector => UpdateChannelComboBox;
        public Button UpdateHistoryButtonControl => UpdateHistoryButton;
        public CheckBox RunAtStartupCheckBoxControl => RunAtStartupCheckBox;
        public CheckBox MinimizeToTrayCheckBoxControl => MinimizeToTrayCheckBox;
        public CheckBox AutoUpdateLocalizationCheckBoxControl => AutoUpdateLocalizationCheckBox;
        public CheckBox AdvancedDiagnosticsCheckBoxControl => AdvancedDiagnosticsCheckBox;
    }
}
