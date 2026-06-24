using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Auth;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SCLOCVerse.Helpers
{
    /// <summary>
    /// Оновлює UI кнопки акаунта відповідно до стану аутентифікації.
    /// </summary>
    public sealed class AuthStatusPresenter : IDisposable
    {
        private readonly Button _accountButton;
        private readonly IAuthStatusProvider _statusProvider;
        private bool _disposed;

        public AuthStatusPresenter(Button accountButton, IAuthStatusProvider statusProvider)
        {
            _accountButton = accountButton ?? throw new ArgumentNullException(nameof(accountButton));
            _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));

            _statusProvider.StatusChanged += OnStatusChanged;
            UpdateButton(_statusProvider.State, _statusProvider.Profile);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _statusProvider.StatusChanged -= OnStatusChanged;
        }

        private void OnStatusChanged(object? sender, EventArgs e)
        {
            if (_accountButton.Dispatcher.CheckAccess())
            {
                UpdateButton(_statusProvider.State, _statusProvider.Profile);
            }
            else
            {
                _accountButton.Dispatcher.Invoke(() => UpdateButton(_statusProvider.State, _statusProvider.Profile));
            }
        }

        private void UpdateButton(AuthState state, DiscordUserProfile? profile)
        {
            _accountButton.Content = null;
            _accountButton.ToolTip = null;

            if (state == AuthState.SignedIn && profile != null)
            {
                var avatarSource = LoadAvatar(profile.AvatarUrl);
                if (avatarSource != null)
                {
                    var image = new Image
                    {
                        Width = 28,
                        Height = 28,
                        Stretch = Stretch.UniformToFill,
                        Source = avatarSource
                    };

                    // Кругла маска через OpacityMask.
                    image.OpacityMask = new VisualBrush
                    {
                        Visual = new Ellipse
                        {
                            Width = 28,
                            Height = 28,
                            Fill = Brushes.Black
                        }
                    };

                    _accountButton.Content = image;
                }
                else
                {
                    // Fallback: ініціали користувача, якщо аватар недоступний.
                    _accountButton.Content = new TextBlock
                    {
                        Text = GetInitials(profile.DisplayName),
                        Foreground = Brushes.White,
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                }

                _accountButton.ToolTip = profile.DisplayName;
            }
            else
            {
                _accountButton.Content = new TextBlock
                {
                    Text = "Увійти",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
            }
        }

        private static string GetInitials(string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return "?";

            var parts = displayName.Split(new[] { ' ', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return displayName[..1].ToUpperInvariant();

            var initials = parts[0][..1];
            if (parts.Length > 1)
                initials += parts[^1][..1];

            return initials.ToUpperInvariant();
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
