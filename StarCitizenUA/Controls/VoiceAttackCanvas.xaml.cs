using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace StarCitizenUA.Controls
{
    public partial class VoiceAttackCanvas : Canvas
    {
        public VoiceAttackCanvas()
        {
            InitializeComponent();
        }
        public Button ReturnButton => BtnReturnVA;
        public TextBox ReadmeTextBox => TxtVoiceAttackReadme;
    }
}
