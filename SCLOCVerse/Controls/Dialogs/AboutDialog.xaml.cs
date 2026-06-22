using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using SCLOCVerse.Helpers;

namespace SCLOCVerse.Controls.Dialogs
{
    public partial class AboutDialog : Window
    {
        public AboutDialog(string versionText)
        {
            InitializeComponent();
            VersionText.Text = $"Version {versionText}";
        }

        public static void Show(Window owner, string versionText)
        {
            var dialog = new AboutDialog(versionText)
            {
                Owner = owner
            };
            dialog.ShowDialog();
        }

        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);

            var helper = new WindowHelper();
            helper.ApplyWindowRoundCorners(this);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}
