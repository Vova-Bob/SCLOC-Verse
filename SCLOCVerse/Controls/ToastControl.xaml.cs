using System.Windows.Controls;

namespace SCLOCVerse.Controls
{
    public partial class ToastControl : UserControl
    {       
        public Border ToastBorder => ToastMessageBorder;
        public TextBlock ToastText => ToastTextBlock;

        public ToastControl()
        {
            InitializeComponent();
        }
    }
}
