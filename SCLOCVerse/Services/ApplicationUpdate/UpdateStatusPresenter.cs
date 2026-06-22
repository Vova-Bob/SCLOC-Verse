using SCLOCVerse.Helpers;
using SCLOCVerse.Models.ApplicationUpdate;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SCLOCVerse.Services.ApplicationUpdate
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
            _availableVersionTextBlock.Text = "вЂ”";
            _statusTextBlock.Text = "РџРµСЂРµРІС–СЂСЏС”РјРѕ РѕРЅРѕРІР»РµРЅРЅСЏ...";
            _statusTextBlock.Foreground = Brushes.LightSlateGray;
        }

        public async Task ShowUpToDateAsync(UpdateCheckResult result)
        {
            EnsurePanelVisible();
            _currentVersionTextBlock.Text = result.CurrentVersion?.ToString() ?? "вЂ”";
            _availableVersionTextBlock.Text = result.LatestVersion?.ToString() ?? "вЂ”";
            _statusTextBlock.Text = "РђРєС‚СѓР°Р»СЊРЅР° РІРµСЂСЃС–СЏ";
            _statusTextBlock.Foreground = Brushes.LimeGreen;

            await Task.Delay(UpdateConstants.UpdatePanelAutoHideDelay).ConfigureAwait(true);
            BeginAutoHide();
        }

        public void ShowUpdateAvailable(UpdateCheckResult result)
        {
            EnsurePanelVisible();
            _currentVersionTextBlock.Text = result.CurrentVersion?.ToString() ?? "вЂ”";
            _availableVersionTextBlock.Text = result.LatestVersion?.ToString() ?? "вЂ”";
            _statusTextBlock.Text = $"Р”РѕСЃС‚СѓРїРЅР° РІРµСЂСЃС–СЏ {result.LatestVersion}";
            _statusTextBlock.Foreground = Brushes.Orange;
        }

        public async Task ShowUpdateCancelledAsync(UpdateCheckResult result)
        {
            EnsurePanelVisible();
            _currentVersionTextBlock.Text = result.CurrentVersion?.ToString() ?? "вЂ”";
            _availableVersionTextBlock.Text = result.LatestVersion?.ToString() ?? "вЂ”";
            _statusTextBlock.Text = "РћРЅРѕРІР»РµРЅРЅСЏ СЃРєР°СЃРѕРІР°РЅРѕ";
            _statusTextBlock.Foreground = Brushes.Gray;

            await Task.Delay(UpdateConstants.UpdatePanelAutoHideDelay).ConfigureAwait(true);
            BeginAutoHide();
        }

        public void ShowCheckFailed(UpdateCheckResult result)
        {
            EnsurePanelVisible();
            _statusTextBlock.Text = "РџРѕРјРёР»РєР° РїРµСЂРµРІС–СЂРєРё";
            _statusTextBlock.Foreground = Brushes.Red;
        }

        public void ShowChannelNotFound(UpdateCheckResult result)
        {
            EnsurePanelVisible();
            _statusTextBlock.Text = "РљР°РЅР°Р» РѕРЅРѕРІР»РµРЅСЊ РЅРµ Р·РЅР°Р№РґРµРЅРѕ";
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
