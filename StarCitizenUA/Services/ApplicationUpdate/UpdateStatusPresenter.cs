using StarCitizenUA.Helpers;
using StarCitizenUA.Models.ApplicationUpdate;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StarCitizenUA.Services.ApplicationUpdate
{
    public sealed class UpdateStatusPresenter : IDisposable
    {
        private readonly TextBlock _currentVersionTextBlock;
        private readonly TextBlock _availableVersionTextBlock;
        private readonly TextBlock _statusTextBlock;
        private readonly FrameworkElement _panel;
        private readonly Storyboard _hideStoryboard;

        public UpdateStatusPresenter(
            TextBlock currentVersionTextBlock,
            TextBlock availableVersionTextBlock,
            TextBlock statusTextBlock,
            FrameworkElement panel,
            Storyboard hideStoryboard)
        {
            _currentVersionTextBlock = currentVersionTextBlock ?? throw new ArgumentNullException(nameof(currentVersionTextBlock));
            _availableVersionTextBlock = availableVersionTextBlock ?? throw new ArgumentNullException(nameof(availableVersionTextBlock));
            _statusTextBlock = statusTextBlock ?? throw new ArgumentNullException(nameof(statusTextBlock));
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));
            _hideStoryboard = hideStoryboard ?? throw new ArgumentNullException(nameof(hideStoryboard));
        }

        public void ShowChecking(Version currentVersion)
        {
            EnsurePanelVisible();
            _currentVersionTextBlock.Text = currentVersion.ToString();
            _availableVersionTextBlock.Text = "—";
            _statusTextBlock.Text = "Перевіряємо оновлення...";
            _statusTextBlock.Foreground = Brushes.LightSlateGray;
        }

        public async Task ShowUpToDateAsync(UpdateCheckResult result)
        {
            EnsurePanelVisible();
            _currentVersionTextBlock.Text = result.CurrentVersion?.ToString() ?? "—";
            _availableVersionTextBlock.Text = result.LatestVersion?.ToString() ?? "—";
            _statusTextBlock.Text = "Актуальна версія";
            _statusTextBlock.Foreground = Brushes.LimeGreen;

            await Task.Delay(UpdateConstants.UpdatePanelAutoHideDelay).ConfigureAwait(true);
            BeginAutoHide();
        }

        public void ShowUpdateAvailable(UpdateCheckResult result)
        {
            EnsurePanelVisible();
            _currentVersionTextBlock.Text = result.CurrentVersion?.ToString() ?? "—";
            _availableVersionTextBlock.Text = result.LatestVersion?.ToString() ?? "—";
            _statusTextBlock.Text = $"Доступна версія {result.LatestVersion}";
            _statusTextBlock.Foreground = Brushes.Orange;
        }

        public async Task ShowUpdateCancelledAsync(UpdateCheckResult result)
        {
            EnsurePanelVisible();
            _currentVersionTextBlock.Text = result.CurrentVersion?.ToString() ?? "—";
            _availableVersionTextBlock.Text = result.LatestVersion?.ToString() ?? "—";
            _statusTextBlock.Text = "Оновлення скасовано";
            _statusTextBlock.Foreground = Brushes.Gray;

            await Task.Delay(UpdateConstants.UpdatePanelAutoHideDelay).ConfigureAwait(true);
            BeginAutoHide();
        }

        public void ShowCheckFailed(UpdateCheckResult result)
        {
            EnsurePanelVisible();
            _statusTextBlock.Text = "Помилка перевірки";
            _statusTextBlock.Foreground = Brushes.Red;
        }

        public void ShowChannelNotFound(UpdateCheckResult result)
        {
            EnsurePanelVisible();
            _statusTextBlock.Text = "Канал оновлень не знайдено";
            _statusTextBlock.Foreground = Brushes.Gray;
        }

        private void EnsurePanelVisible()
        {
            _panel.Visibility = Visibility.Visible;
            _panel.Opacity = 1;
        }

        private void BeginAutoHide()
        {
            if (_panel.Dispatcher.CheckAccess())
            {
                _hideStoryboard.Begin(_panel);
            }
            else
            {
                _panel.Dispatcher.Invoke(() => _hideStoryboard.Begin(_panel));
            }
        }

        public void Dispose()
        {
            // Storyboard resources are managed by WPF; no explicit cleanup required.
        }
    }
}
