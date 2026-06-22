using StarCitizenUA.Helpers;
using System.Windows;
using System.Windows.Input;

namespace StarCitizenUA.Controls.Dialogs
{
    public partial class UpdateDialog : Window
    {
        private UpdateDialog(string availableVersion)
        {
            InitializeComponent();

            MessageText.Text = $"Версія {availableVersion} готова до встановлення.";
        }

        public bool InstallRequested { get; private set; }

        public static bool Show(Window? owner, string availableVersion)
        {
            var dialog = new UpdateDialog(availableVersion);
            if (owner != null)
            {
                dialog.Owner = owner;
            }

            dialog.ShowDialog();
            return dialog.InstallRequested;
        }

        protected override void OnSourceInitialized(EventArgs e)
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
            InstallRequested = false;
            Close();
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            InstallRequested = true;
            DialogResult = true;
            Close();
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            InstallRequested = false;
            DialogResult = false;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Escape)
            {
                InstallRequested = false;
                Close();
            }
        }
    }
}
