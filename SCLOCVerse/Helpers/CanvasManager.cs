using SCLOCVerse.Interfaces;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SCLOCVerse.Helpers
{
    public class CanvasManager : ICanvasManager
    {
        private readonly MainWindow _window;
        private Canvas? _currentCanvas;

        public CanvasManager(MainWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _currentCanvas = _window.CanvasHome;
        }

        public Canvas? GetCurrentVisibleCanvas()
        {
            if (_window.CanvasHome.Visibility == Visibility.Visible) return _window.CanvasHome;
            if (_window.CanvasLocalization.Visibility == Visibility.Visible) return _window.CanvasLocalization;
            if (_window.CanvasAssistant.Visibility == Visibility.Visible) return _window.CanvasAssistant;
            if (_window.CanvasSettings.Visibility == Visibility.Visible) return _window.CanvasSettings;
            if (_window.CanvasScTools.Visibility == Visibility.Visible) return _window.CanvasScTools;
            return null;
        }
        public void SwitchCanvas(Canvas showCanvas, double durationSeconds = 0.3)
        {
            if (showCanvas == null) return;

            var hideCanvas = GetCurrentVisibleCanvas();
            _currentCanvas = showCanvas;

            if (hideCanvas != null)
            {
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(durationSeconds));
                fadeOut.Completed += (s, e) =>
                {
                    hideCanvas.Visibility = Visibility.Collapsed;

                    showCanvas.Opacity = 0;
                    showCanvas.Visibility = Visibility.Visible;
                    var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(durationSeconds));
                    showCanvas.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                };
                hideCanvas.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            else
            {
                showCanvas.Opacity = 0;
                showCanvas.Visibility = Visibility.Visible;
                var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(durationSeconds));
                showCanvas.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
        }

        public void ShowCanvas(string which)
        {
            _window.CanvasHome.Visibility = Visibility.Collapsed;
            _window.CanvasLocalization.Visibility = Visibility.Collapsed;
            _window.CanvasAssistant.Visibility = Visibility.Collapsed;
            _window.CanvasSettings.Visibility = Visibility.Collapsed;
            _window.CanvasScTools.Visibility = Visibility.Collapsed;

            switch (which.ToLower())
            {
                case "home":
                    _window.CanvasHome.Visibility = Visibility.Visible;
                    _currentCanvas = _window.CanvasHome;
                    break;
                case "localization":
                    _window.CanvasLocalization.Visibility = Visibility.Visible;
                    _currentCanvas = _window.CanvasLocalization;
                    break;
                case "assistant":
                    _window.CanvasAssistant.Visibility = Visibility.Visible;
                    _currentCanvas = _window.CanvasAssistant;
                    break;
                case "settings":
                    _window.CanvasSettings.Visibility = Visibility.Visible;
                    _currentCanvas = _window.CanvasSettings;
                    break;
                case "sctools":
                    _window.CanvasScTools.Visibility = Visibility.Visible;
                    _currentCanvas = _window.CanvasScTools;
                    break;
                default:
                    _window.CanvasHome.Visibility = Visibility.Visible;
                    _currentCanvas = _window.CanvasHome;
                    break;
            }
        }
    }
}
