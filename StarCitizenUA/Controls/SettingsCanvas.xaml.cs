using System.Windows.Controls;

namespace StarCitizenUA.Controls
{
    public partial class SettingsCanvas : Canvas
    {
        public SettingsCanvas()
        {
            InitializeComponent();
        }

        public Button ReturnButton => BtnReturn;

        public Button SelectFolderButton => BtnSelectFolder;

        public Button AutoSearchButton => BtnAutoSearch;

        public Button ResetCacheButton => BtnResetCash;

        public TextBox SelectedPathTextBox => TxtSelectedPath;

        public TextBox ReadmeTextBox => TxtReadme;

        public ComboBox UpdateChannelSelector => UpdateChannelComboBox;
    }
}
