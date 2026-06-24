using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Auth;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SCLOCVerse.Controls.Dialogs
{
    public partial class AccountDialog : Window
    {
        private readonly IAuthService _authService;
        private readonly CancellationTokenSource _cts = new();

        public AccountDialog(IAuthService authService)
        {
            InitializeComponent();
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _authService.StatusChanged += OnAuthStatusChanged;
            UpdateVisibility(_authService.State, _authService.Profile);
        }

        public static void Show(Window? owner, IAuthService authService)
        {
            var dialog = new AccountDialog(authService)
            {
                Owner = owner
            };

            dialog.ShowDialog();
        }

        private void OnAuthStatusChanged(object? sender, EventArgs e)
        {
            if (Dispatcher.CheckAccess())
            {
                UpdateVisibility(_authService.State, _authService.Profile);
            }
            else
            {
                Dispatcher.Invoke(() => UpdateVisibility(_authService.State, _authService.Profile));
            }
        }

        private void UpdateVisibility(AuthState state, DiscordUserProfile? profile)
        {
            SignedOutPanel.Visibility = Visibility.Collapsed;
            SignedInPanel.Visibility = Visibility.Collapsed;
            SigningInPanel.Visibility = Visibility.Collapsed;

            switch (state)
            {
                case AuthState.SignedIn:
                    SignedInPanel.Visibility = Visibility.Visible;
                    UsernameText.Text = profile?.DisplayName ?? "—";
                    StatusText.Text = "Ви увійшли через Discord";
                    AvatarImage.Source = LoadAvatar(profile?.AvatarUrl);
                    break;

                case AuthState.SigningIn:
                    SigningInPanel.Visibility = Visibility.Visible;
                    break;

                case AuthState.SignedOut:
                case AuthState.Error:
                default:
                    SignedOutPanel.Visibility = Visibility.Visible;
                    break;
            }
        }

        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            SignedOutPanel.Visibility = Visibility.Collapsed;
            SigningInPanel.Visibility = Visibility.Visible;

            var result = await _authService.SignInAsync(_cts.Token).ConfigureAwait(true);

            if (result is AuthResult.Failure failure)
            {
                ShowError(failure.Message);
            }

            UpdateVisibility(_authService.State, _authService.Profile);

            if (_authService.State == AuthState.SignedIn)
                Close();
        }

        private async void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            await _authService.SignOutAsync(_cts.Token).ConfigureAwait(true);
            UpdateVisibility(_authService.State, _authService.Profile);
        }

        private void CancelSignInButton_Click(object sender, RoutedEventArgs e)
        {
            _cts.Cancel();
            UpdateVisibility(_authService.State, _authService.Profile);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Escape)
                Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _authService.StatusChanged -= OnAuthStatusChanged;
            _cts.Dispose();
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(message, "Помилка входу", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private static ImageSource? LoadAvatar(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            try
            {
                return new BitmapImage(new Uri(url));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
