using System.Windows.Controls;

namespace SCLOCVerse.Controls
{
    public partial class SettingsCanvas : Canvas
    {
        public SettingsCanvas()
        {
            InitializeComponent();
        }

        public Button ReturnHomeButton => BtnReturnHome;

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
    }
}
