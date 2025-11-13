using StarCitizenUA.Interfaces;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StarCitizenUA.Services
{
    class ToastService : IToastService
    {
        private readonly Border _toastBorder;
        private readonly TextBlock _toastText;
        private CancellationTokenSource? _cts;

        public ToastService(Border toastBorder, TextBlock toastText)
        {
            _toastBorder = toastBorder;
            _toastText = toastText;

            if (_toastBorder.RenderTransform == null)
                _toastBorder.RenderTransform = new TranslateTransform();
        }

        public async Task ShowToastAsync(string message, int durationMs = 5000)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _toastText.Text = message;
            _toastBorder.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            var moveIn = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(300));

            _toastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            (_toastBorder.RenderTransform as TranslateTransform)?.BeginAnimation(TranslateTransform.YProperty, moveIn);

            try
            {
                await Task.Delay(durationMs, token).ConfigureAwait(true);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500));
            fadeOut.Completed += (s, e) =>
            {
                if (!token.IsCancellationRequested)
                    _toastBorder.Visibility = Visibility.Collapsed;
            };

            _toastBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
}