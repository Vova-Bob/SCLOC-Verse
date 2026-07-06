using SCLOCVerse.Models.ApplicationUpdate;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SCLOCVerse.Windows
{
    public partial class AppUpdateProgressWindow : Window
    {
        private bool _allowClose;
        private bool _isCancelled;

        public event EventHandler? CancelRequested;

        public AppUpdateProgressWindow()
        {
            InitializeComponent();
        }

        public void SetStage(string stage)
        {
            StageTextBlock.Text = stage;
        }

        public void Report(UpdateDownloadProgress progress)
        {
            var downloadedMb = BytesToMegabytes(progress.DownloadedBytes);
            var totalMb = progress.TotalBytes.HasValue
                ? BytesToMegabytes(progress.TotalBytes.Value)
                : (double?)null;

            var percentageText = progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0
                ? $"{Math.Min(100, (double)progress.DownloadedBytes / progress.TotalBytes.Value * 100):F0}%"
                : string.Empty;

            var sizeText = totalMb.HasValue
                ? $"{downloadedMb:F1} / {totalMb.Value:F1} МБ"
                : $"Завантажено {downloadedMb:F1} МБ";

            var speedText = progress.BytesPerSecond.HasValue
                ? $"{BytesToMegabytes((long)progress.BytesPerSecond.Value):F1} МБ/с"
                : string.Empty;

            var etaText = ComputeEta(progress);

            ProgressBar.IsIndeterminate = !progress.TotalBytes.HasValue;

            if (progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0)
            {
                ProgressBar.Value = Math.Min(100, (double)progress.DownloadedBytes / progress.TotalBytes.Value * 100);
            }

            PercentageTextBlock.Text = percentageText;
            SizeTextBlock.Text = sizeText;
            SpeedTextBlock.Text = speedText;
            EtaTextBlock.Text = etaText;
        }

        public void MarkCompleted(string stage)
        {
            SetStage(stage);
            CancelButton.IsEnabled = false;
            CancelButton.Content = "Закрити";
            _allowClose = true;
        }

        public void MarkFailed(string stage)
        {
            SetStage(stage);
            ProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4C, 0x4C));
            CancelButton.IsEnabled = true;
            CancelButton.Content = "Закрити";
            _allowClose = true;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_allowClose)
                return;

            e.Cancel = true;
            PromptCancel();
        }

        private void PromptCancel()
        {
            if (_isCancelled)
                return;

            var result = MessageBox.Show(
                "Завантаження буде перервано. Скасувати встановлення оновлення?",
                "Скасувати оновлення",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            _isCancelled = true;
            CancelRequested?.Invoke(this, EventArgs.Empty);

            CancelButton.IsEnabled = false;
            CancelButton.Content = "Скасування...";
            StageTextBlock.Text = "Скасування...";
        }

        private void WindowCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allowClose)
            {
                Close();
                return;
            }

            PromptCancel();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allowClose)
            {
                Close();
                return;
            }

            PromptCancel();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private static double BytesToMegabytes(long bytes)
            => bytes / (1024.0 * 1024.0);

        private static string ComputeEta(UpdateDownloadProgress progress)
        {
            if (!progress.TotalBytes.HasValue || !progress.BytesPerSecond.HasValue || progress.BytesPerSecond.Value <= 0)
                return string.Empty;

            var remainingBytes = progress.TotalBytes.Value - progress.DownloadedBytes;
            if (remainingBytes <= 0)
                return string.Empty;

            var remainingSeconds = remainingBytes / progress.BytesPerSecond.Value;
            if (remainingSeconds < 1)
                return "Залишилось менше секунди";

            if (remainingSeconds < 60)
                return $"Залишилось приблизно {Math.Ceiling(remainingSeconds)} секунд";

            var minutes = (int)Math.Ceiling(remainingSeconds / 60);
            return minutes == 1
                ? "Залишилось приблизно хвилина"
                : $"Залишилось приблизно {minutes} хвилин";
        }
    }
}
