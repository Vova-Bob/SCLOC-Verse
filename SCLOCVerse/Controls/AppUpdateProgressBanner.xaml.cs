using SCLOCVerse.Models.ApplicationUpdate;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SCLOCVerse.Controls
{
    public partial class AppUpdateProgressBanner : UserControl
    {
        private long? _lastTotalBytes;

        public AppUpdateProgressBanner()
        {
            InitializeComponent();
            HideImmediately();
        }

        public void Show(string stage)
        {
            StageTextBlock.Text = stage;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            PercentageTextBlock.Text = string.Empty;
            SizeTextBlock.Text = string.Empty;
            SpeedTextBlock.Text = string.Empty;
            EtaTextBlock.Text = string.Empty;

            if (Visibility == Visibility.Collapsed)
            {
                Visibility = Visibility.Visible;
                var storyboard = (Storyboard)FindResource("ShowStoryboard");
                storyboard.Begin(this);
            }
        }

        public void Report(UpdateDownloadProgress progress)
        {
            _lastTotalBytes = progress.TotalBytes;

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

        public void SetStage(string stage)
        {
            StageTextBlock.Text = stage;
        }

        public void Hide()
        {
            var storyboard = (Storyboard)FindResource("HideStoryboard");
            storyboard.Begin(this);
        }

        private void HideStoryboard_Completed(object sender, EventArgs e)
        {
            Visibility = Visibility.Collapsed;
        }

        private void HideImmediately()
        {
            Visibility = Visibility.Collapsed;
            Opacity = 0;
            RenderTransform = new System.Windows.Media.TranslateTransform(0, -20);
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
