using StarCitizenUA.Interfaces;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StarCitizenUA.Views;

namespace StarCitizenUA.Helpers
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
            if (_window.CanvasLiaSettings.Visibility == Visibility.Visible) return _window.CanvasLiaSettings;
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
            _window.CanvasLiaSettings.Visibility = Visibility.Collapsed;

            switch (which)
            {
                case "home":
                    _window.CanvasHome.Visibility = Visibility.Visible;
                    break;
                case "localization":
                    _window.CanvasLocalization.Visibility = Visibility.Visible;
                    break;
                case "assistant":
                    _window.CanvasAssistant.Visibility = Visibility.Visible;
                    break;
                case "settings":
                    _window.CanvasSettings.Visibility = Visibility.Visible;
                    break;
                case "liasettings":
                    _window.CanvasLiaSettings.Visibility = Visibility.Visible;
                    break;
            }

            SetActiveButton(which);
        }

        public void SetActiveButton(string active)
        {
            // Скидаємо колір усіх
            var inactive = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#143A52"));
            var activeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5190C3"));

            _window.BtnLocalization.Background = inactive;
            _window.BtnAssistant.Background = inactive;
            _window.BtnSettings.Background = inactive;

            switch (active)
            {
                case "localization":
                    _window.BtnLocalization.Background = activeBrush;
                    break;
                case "assistant":
                    _window.BtnAssistant.Background = activeBrush;
                    break;
                case "settings":
                    _window.BtnSettings.Background = activeBrush;
                    break;
            }
        }
    }
}