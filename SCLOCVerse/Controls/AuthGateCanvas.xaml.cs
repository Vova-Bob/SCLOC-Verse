using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Auth;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// Canvas, що відображає Auth Gate для обов'язкової Discord-авторизації.
    /// Єдине джерело істини — IAuthStatusProvider.State та StatusChanged.
    /// </summary>
    public partial class AuthGateCanvas : Canvas
    {
        private readonly IAuthService _authService;
        private CancellationTokenSource? _signInCts;

        public AuthGateCanvas(IAuthService authService)
        {
            InitializeComponent();
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _authService.StatusChanged += OnAuthStateChanged;
            UpdateUi(_authService.State);
        }

        private void OnAuthStateChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => UpdateUi(_authService.State));
        }

        /// <summary>
        /// Оновлює UI відповідно до поточного AuthState.
        /// </summary>
        private void UpdateUi(AuthState state)
        {
            switch (state)
            {
                case AuthState.Checking:
                    ShowLoading("Перевіряємо сесію...");
                    break;

                case AuthState.SignedOut:
                    ShowSignIn();
                    break;

                case AuthState.SigningIn:
                    ShowLoading("Відкриваємо Discord...");
                    break;

                case AuthState.Error:
                    ShowError();
                    break;

                case AuthState.SignedIn:
                    // AuthGateCanvas не вирішує, чи показувати Main UI.
                    // MainWindow підписується на StatusChanged і перемикає режими.
                    HideAll();
                    break;
            }
        }

        private void ShowSignIn()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            SignInButton.Visibility = Visibility.Visible;
            SignInButton.IsEnabled = true;
            TitleText.Text = "SCLOC Account";
            DescriptionText.Text = "Увійдіть через Discord, щоб створити SCLOC Account.\nЦе дасть вам доступ до синхронізації, підтримки та майбутніх функцій.";
        }

        private void ShowLoading(string message)
        {
            SignInButton.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;
            LoadingText.Text = message;
            LoadingPanel.Visibility = Visibility.Visible;

            // Cancel показуємо тільки під час активного входу, а не під час Checking.
            CancelButton.Visibility = _authService.State == AuthState.SigningIn
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ShowError()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            SignInButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Visible;
            ErrorText.Visibility = Visibility.Visible;

            TitleText.Text = "Помилка авторизації";
            DescriptionText.Text = "Не вдалося увійти через Discord. Спробуйте ще раз.";
            ErrorText.Text = "Сталася помилка під час входу. Перевірте з'єднання з Інтернетом і спробуйте знову.";
        }

        private void HideAll()
        {
            SignInButton.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;
        }

        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            _signInCts?.Dispose();
            _signInCts = new CancellationTokenSource();

            try
            {
                await _authService.SignInAsync(_signInCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Користувач скасував — стан вже SignedOut через CancellationToken.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthGateCanvas] SignIn failed: {ex}");
            }
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            _signInCts?.Dispose();
            _signInCts = new CancellationTokenSource();

            try
            {
                await _authService.SignInAsync(_signInCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Скасовано.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthGateCanvas] Retry failed: {ex}");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _signInCts?.Cancel();
        }
    }
}
