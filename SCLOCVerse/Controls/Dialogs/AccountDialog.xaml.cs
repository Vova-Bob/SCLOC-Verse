using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Auth;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SCLOCVerse.Controls.Dialogs
{
    public partial class AccountDialog : Window
    {
    private readonly IAuthService _authService;
    private CancellationTokenSource _cts = new();

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

                    var displayName = profile?.DisplayName ?? "—";
                    UsernameText.Text = displayName;
                    InitialsText.Text = GetInitials(displayName);
                    ApplyAvatar(profile?.AvatarUrl);
                    JoinedText.Text = FormatJoined(profile?.JoinedAt);
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
            // Перестворюємо CancellationTokenSource перед кожним входом,
            // щоб скасування попередньої не блокувало наступні спроби.
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            SignedOutPanel.Visibility = Visibility.Collapsed;
            SigningInPanel.Visibility = Visibility.Visible;

            var result = await _authService.SignInAsync(_cts.Token).ConfigureAwait(true);

            switch (result)
            {
                case AuthResult.Success success:
                    UpdateVisibility(AuthState.SignedIn, success.Profile);
                    Close();
                    return;

                case AuthResult.Failure failure:
                    ShowError(failure.Message);
                    break;

                case AuthResult.Cancelled:
                    break;
            }

            UpdateVisibility(_authService.State, _authService.Profile);
        }

        private async void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            await _authService.SignOutAsync(_cts.Token).ConfigureAwait(true);
            // Закриваємо діалог після виходу — MainWindow через StatusChanged
            // автоматично поверне користувача в Auth Gate.
            Dispatcher.Invoke(Close);
        }

        private void CancelSignInButton_Click(object sender, RoutedEventArgs e)
        {
            _cts.Cancel();
            UpdateVisibility(_authService.State, _authService.Profile);
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == System.Windows.Input.Key.Escape)
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

        private static System.Windows.Media.ImageSource? LoadAvatar(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            try
            {
                return new System.Windows.Media.Imaging.BitmapImage(new Uri(url));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void ApplyAvatar(string? url)
        {
            var source = LoadAvatar(url);
            if (source != null)
            {
                AvatarImage.Source = source;
                AvatarImage.Visibility = Visibility.Visible;
            }
            else
            {
                // Аватар недоступний — показуємо ініціали, заховані під зображенням.
                AvatarImage.Source = null;
                AvatarImage.Visibility = Visibility.Collapsed;
            }
        }

        private void AvatarImage_ImageFailed(object sender, System.Windows.ExceptionRoutedEventArgs e)
        {
            AvatarImage.Source = null;
            AvatarImage.Visibility = Visibility.Collapsed;
        }

        private static string GetInitials(string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return "?";

            var parts = displayName.Split(new[] { ' ', '_', '|', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return displayName[..1].ToUpperInvariant();

            var initials = parts[0][..1];
            if (parts.Length > 1)
                initials += parts[^1][..1];

            return initials.ToUpperInvariant();
        }

        private static string FormatJoined(DateTime? joinedAt)
        {
            if (joinedAt == null)
                return string.Empty;

            try
            {
                var culture = new System.Globalization.CultureInfo("uk-UA");
                var formatted = joinedAt.Value.ToString("MMMM yyyy", culture);
                if (formatted.Length > 0)
                    formatted = char.ToUpper(formatted[0], culture) + formatted[1..];

                return $"У системі з {formatted}";
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
