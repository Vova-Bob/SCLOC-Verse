using StarCitizenUA.Interfaces;
using System.Collections.Concurrent;
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
        private readonly ConcurrentQueue<(string message, int duration)> _toastQueue = new();
        private bool _isShowing;

        public ToastService(Border toastBorder, TextBlock toastText)
        {
            _toastBorder = toastBorder;
            _toastText = toastText;

            if (_toastBorder.RenderTransform == null)
                _toastBorder.RenderTransform = new TranslateTransform();
        }

        public Task ShowToastAsync(string message, int durationMs = 5000)
        {
            var tcs = new TaskCompletionSource();
            _toastQueue.Enqueue((message, durationMs));
            if (!_isShowing)
            {
                _ = ShowNextToastAsync();
            }
            return tcs.Task;
        }

        private async Task ShowNextToastAsync()
        {
            _isShowing = true;

            while (_toastQueue.TryDequeue(out var item))
            {
                string message = item.message;
                int durationMs = item.duration;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _toastText.Text = message;
                    _toastBorder.Visibility = Visibility.Visible;

                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                    var moveIn = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(300));

                    _toastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                    (_toastBorder.RenderTransform as TranslateTransform)?.BeginAnimation(TranslateTransform.YProperty, moveIn);
                });

                await Task.Delay(durationMs);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500));
                    fadeOut.Completed += (s, e) =>
                    {
                        _toastBorder.Visibility = Visibility.Collapsed;
                    };
                    _toastBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                });

                await Task.Delay(500);
            }

            _isShowing = false;
        }
    }
}