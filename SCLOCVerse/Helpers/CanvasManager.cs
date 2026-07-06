using SCLOCVerse.Interfaces;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SCLOCVerse.Helpers
{
    public class CanvasManager : ICanvasManager
    {
        private readonly MainWindow _window;
        private readonly IButtonStateManager _buttonStateManager;
        private Canvas? _currentCanvas;
        private bool _isTransitioning;
        private Canvas? _pendingCanvas;
        private string? _pendingActiveKey;
        private string? _targetActiveKey;

        private readonly Canvas[] _allCanvases;

        public CanvasManager(MainWindow window, IButtonStateManager buttonStateManager)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _buttonStateManager = buttonStateManager ?? throw new ArgumentNullException(nameof(buttonStateManager));

            _allCanvases =
            [
                _window.CanvasHome,
                _window.CanvasLocalization,
                _window.CanvasAssistant,
                _window.CanvasSettings,
                _window.CanvasScTools
            ];

            _currentCanvas = _window.CanvasHome;
        }

        public Canvas? GetCurrentVisibleCanvas()
        {
            foreach (var canvas in _allCanvases)
            {
                if (canvas.Visibility == Visibility.Visible)
                    return canvas;
            }

            return null;
        }

        public void SwitchCanvas(Canvas showCanvas, double durationSeconds = 0.3)
            => SwitchCanvas(showCanvas, ResolveActiveKey(showCanvas), durationSeconds);

        public void SwitchCanvas(Canvas showCanvas, string activeStateKey, double durationSeconds = 0.3)
        {
            if (showCanvas is null)
                return;

            // Ігноруємо запит на той самий Canvas.
            if (showCanvas == _currentCanvas)
                return;

            // Якщо йде перехід — запам’ятовуємо лише останній запит.
            if (_isTransitioning)
            {
                _pendingCanvas = showCanvas;
                _pendingActiveKey = activeStateKey;
                return;
            }

            _isTransitioning = true;
            _pendingCanvas = null;
            _pendingActiveKey = null;
            _targetActiveKey = activeStateKey;

            var hideCanvas = _currentCanvas;

            // Скасовуємо активні анімації Opacity на всіх Canvas, щоб уникнути
            // накладання через незавершені попередні переходи.
            foreach (var canvas in _allCanvases)
            {
                canvas.BeginAnimation(UIElement.OpacityProperty, null);
            }

            // Забезпечуємо чистий початковий стан: видимим має бути тільки
            // поточний Canvas, цільовий поки що прихований.
            ApplyCanvasInvariant(hideCanvas);

            if (hideCanvas is not null)
            {
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(durationSeconds));
                fadeOut.Completed += (s, e) =>
                {
                    hideCanvas.Visibility = Visibility.Collapsed;

                    showCanvas.Opacity = 0;
                    showCanvas.Visibility = Visibility.Visible;

                    var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(durationSeconds));
                    fadeIn.Completed += (sender, args) => OnTransitionCompleted(showCanvas);
                    showCanvas.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                };

                hideCanvas.Opacity = 1;
                hideCanvas.Visibility = Visibility.Visible;
                hideCanvas.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            else
            {
                showCanvas.Opacity = 0;
                showCanvas.Visibility = Visibility.Visible;

                var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(durationSeconds));
                fadeIn.Completed += (sender, args) => OnTransitionCompleted(showCanvas);
                showCanvas.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
        }

        public void ShowCanvas(string which)
        {
            // Скасовуємо активні анімації, щоб ShowCanvas не накладався на перехід.
            foreach (var canvas in _allCanvases)
            {
                canvas.BeginAnimation(UIElement.OpacityProperty, null);
            }

            _isTransitioning = false;
            _pendingCanvas = null;
            _pendingActiveKey = null;
            _targetActiveKey = null;

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

            ApplyCanvasInvariant(_currentCanvas);

            var activeKey = ResolveActiveKey(_currentCanvas);
            if (!string.IsNullOrEmpty(activeKey))
            {
                _buttonStateManager.SetActive(activeKey);
            }
        }

        private void OnTransitionCompleted(Canvas targetCanvas)
        {
            _currentCanvas = targetCanvas;
            _isTransitioning = false;

            ApplyCanvasInvariant(_currentCanvas);

            var activeKey = _targetActiveKey ?? ResolveActiveKey(_currentCanvas);
            if (!string.IsNullOrEmpty(activeKey))
            {
                _buttonStateManager.SetActive(activeKey);
            }

            _targetActiveKey = null;

            // Якщо за час переходу надійшов новий запит — виконуємо його зараз.
            var pending = _pendingCanvas;
            var pendingKey = _pendingActiveKey;
            _pendingCanvas = null;
            _pendingActiveKey = null;
            if (pending is not null && pending != _currentCanvas)
            {
                SwitchCanvas(pending, pendingKey ?? ResolveActiveKey(pending));
            }
        }

        private string ResolveActiveKey(Canvas? canvas)
        {
            return canvas switch
            {
                _ when canvas == _window.CanvasHome => "home",
                _ when canvas == _window.CanvasLocalization => "localization",
                _ when canvas == _window.CanvasAssistant => "assistant",
                _ when canvas == _window.CanvasSettings => "settings",
                _ when canvas == _window.CanvasScTools => "sctools",
                _ => string.Empty
            };
        }

        private void ApplyCanvasInvariant(Canvas? activeCanvas)
        {
            foreach (var canvas in _allCanvases)
            {
                bool isActive = canvas == activeCanvas;
                canvas.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                canvas.Opacity = isActive ? 1.0 : 0.0;
            }
        }


    }
}
