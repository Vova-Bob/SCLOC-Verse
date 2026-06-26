using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Auth;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// Canvas, що відображає Auth Gate / Loading Screen для обов'язкової Discord-авторизації.
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
            UpdateUi(_authService.State, animate: false);
        }

        private void OnAuthStateChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => UpdateUi(_authService.State, animate: true));
        }

        /// <summary>
        /// Оновлює UI відповідно до поточного AuthState.
        /// </summary>
        private void UpdateUi(AuthState state, bool animate)
        {
            switch (state)
            {
                case AuthState.Checking:
                    ShowLoading("Перевіряємо сесію...", animate);
                    break;

                case AuthState.SignedOut:
                    ShowSignIn(animate);
                    break;

                case AuthState.SigningIn:
                    ShowLoading("Відкриваємо Discord...", animate);
                    break;

                case AuthState.Error:
                    ShowError(animate);
                    break;

                case AuthState.SignedIn:
                    // AuthGateCanvas не вирішує, чи показувати Main UI.
                    // MainWindow підписується на StatusChanged і перемикає режими.
                    break;
            }
        }

        private void ShowSignIn(bool animate)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            SignInButton.Visibility = Visibility.Visible;
            SignInButton.IsEnabled = true;
            TitleText.Text = "SCLOC Account";
            DescriptionText.Text = "Увійдіть через Discord, щоб створити SCLOC Account.\nЦе дасть вам доступ до синхронізації, підтримки та майбутніх функцій.";

            if (animate)
                AnimateOpacity(SignInButton, 0, 1, 250);
            else
                SignInButton.Opacity = 1;
        }

        private void ShowLoading(string message, bool animate)
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

            if (animate)
                AnimateOpacity(LoadingPanel, 0, 1, 250);
            else
                LoadingPanel.Opacity = 1;
        }

        private void ShowError(bool animate)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            SignInButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Visible;
            ErrorText.Visibility = Visibility.Visible;

            TitleText.Text = "Помилка авторизації";
            DescriptionText.Text = "Не вдалося увійти через Discord. Спробуйте ще раз.";
            ErrorText.Text = "Сталася помилка під час входу. Перевірте з'єднання з Інтернетом і спробуйте знову.";

            if (animate)
            {
                AnimateOpacity(RetryButton, 0, 1, 250);
                AnimateOpacity(ErrorText, 0, 1, 250);
            }
            else
            {
                RetryButton.Opacity = 1;
                ErrorText.Opacity = 1;
            }
        }

        private static void AnimateOpacity(UIElement element, double from, double to, int milliseconds)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(milliseconds)
            };
            element.BeginAnimation(OpacityProperty, animation);
        }

        /// <summary>
        /// Плавно приховує Auth Gate перед переходом у Main UI.
        /// </summary>
        public async Task FadeOutAsync(int milliseconds = 300)
        {
            var tcs = new TaskCompletionSource();
            var animation = new DoubleAnimation
            {
                From = Opacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(milliseconds)
            };
            animation.Completed += (s, e) => tcs.SetResult();
            BeginAnimation(OpacityProperty, animation);
            await tcs.Task.ConfigureAwait(false);
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
