using System.Windows.Controls;

namespace StarCitizenUA.Controls
{
    public partial class LiaSettingsCanvas : Canvas
    {
        public LiaSettingsCanvas()
        {
            InitializeComponent();
        }

        public Button ReturnButton => BtnReturn;

        public Button SelectFolderButton => BtnSelectFolder;

        public Button AutoSearchButton => BtnAutoSearch;

        public TextBox SelectedPathTextBox => TxtSelectedPath;

        public TextBox ReadmeTextBox => TxtReadme;
    }
}
