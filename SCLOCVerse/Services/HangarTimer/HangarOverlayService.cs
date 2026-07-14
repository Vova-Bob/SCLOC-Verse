using SCLOCVerse.Controls;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.HangarTimer;
using SCLOCVerse.Services.InputSystem;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace SCLOCVerse.Services.HangarTimer
{
    /// <summary>
    /// Сервіс керування візуальним overlay Hangar Timer.
    /// Містить масштаб, прозорість та координацію з вікном.
    /// Логіка циклу винесена у HangarCycleCalculator.
    /// </summary>
    public class HangarOverlayService : IHangarOverlayService, IDisposable
    {
        private const int BaseWidth = 820;
        private const int BaseHeight = 280;

        private const double OpacityMin = 0.50;
        private const double OpacityMax = 0.95;
        private const double OpacityStep = 0.05;

        private const double ScaleMin = 0.60;
        private const double ScaleMax = 1.00;
        private const double ScaleStep = 0.05;

        private readonly IHangarSettingsService _settingsService;
        private readonly IHotkeyService _hotkeyService;
        private readonly DispatcherTimer _timer;
        private readonly HangarTimerState _state;

        private Window? _window;
        private IHangarOverlayWindow? _overlayWindow;
        private long _cycleStartMs;
        private bool _disposed;

        public HangarOverlayService(IHangarSettingsService settingsService, IHotkeyService hotkeyService)
        {
            _settingsService = settingsService;
            _hotkeyService = hotkeyService;
            _state = new HangarTimerState();

            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _timer.Tick += OnTimerTick;
        }

        public bool IsOpen => _window != null;

        /// <summary>
        /// Runtime стан overlay (Single Source of Truth для масштабу/прозорості).
        /// Settings Hub підписується на PropertyChanged для двосторонньої синхронізації.
        /// </summary>
        public HangarTimerState State => _state;

        /// <summary>
        /// Сповіщає при переміщенні overlay (drag). Settings Hub оновлює поля X/Y.
        /// </summary>
        public event EventHandler<(double X, double Y)>? PositionChanged;

        public void Show(long cycleStartMs)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            if (_window != null)
            {
                _overlayWindow?.SetHotkeyHint(BuildHotkeyHint());
                _window.Activate();
                return;
            }

            _cycleStartMs = cycleStartMs;

            CreateWindow();
            _window!.LocationChanged += OnWindowLocationChanged;
            _overlayWindow?.SetHotkeyHint(BuildHotkeyHint());
            _window.Show();
            _timer.Start();
            UpdateModel();
        }

        /// <summary>
        /// Створює вікно overlay відповідно до поточного режиму (Classic / Simplified).
        /// </summary>
        private void CreateWindow()
        {
            var mode = _settingsService.GetOverlayMode();

            if (mode == HangarOverlayMode.Simplified)
            {
                var compact = new HangarCompactOverlayWindow(_state, _settingsService, OnWindowClosed);
                _window = compact;
                _overlayWindow = compact;
            }
            else
            {
                var classic = new HangarOverlayWindow(_state, _settingsService, OnWindowClosed);
                _window = classic;
                _overlayWindow = classic;
            }
        }

        /// <summary>
        /// Перемикає режим відображення overlay (класичний / спрощений).
        /// Якщо overlay відкритий — закриває старе вікно та створює нове (live-apply).
        /// Персистить не тут — це робить Settings Hub через IHangarSettingsService.
        /// </summary>
        public void ApplyOverlayMode(HangarOverlayMode mode)
        {
            if (_disposed)
                return;

            // Якщо вікно не відкрите — режим застосується при наступному Show().
            if (_window == null)
                return;

            // Зберегти поточну позицію перед закриттям (щоб нове вікно відкрилось там же).
            var x = _window.Left;
            var y = _window.Top;

            var wasVisible = _window.IsVisible;
            _window.LocationChanged -= OnWindowLocationChanged;
            _window.Close();
            _window = null;
            _overlayWindow = null;

            if (wasVisible)
            {
                CreateWindow();
                _window!.Left = x;
                _window.Top = y;
                _window.LocationChanged += OnWindowLocationChanged;
                _overlayWindow?.SetHotkeyHint(BuildHotkeyHint());
                _window.Show();
            }
        }

        /// <summary>
        /// Будує рядок підказки гарячих клавіш з IHotkeyService.GetDefinitions()
        /// (SSOT) — «жест: опис • жест: опис • … • Esc: закрити».
        /// Esc — локальне закриття вікна (не глобальний хоткей), тому статичний суфікс.
        /// </summary>
        private string BuildHotkeyHint()
        {
            var sb = new StringBuilder();

            foreach (var def in _hotkeyService.GetDefinitions())
            {
                if (!def.Id.Value.StartsWith("HangarTimer.", System.StringComparison.Ordinal))
                    continue;

                if (sb.Length > 0)
                    sb.Append(" • ");

                var gesture = def.IsUnassigned
                    ? "—"
                    : HotkeyGestureFormat.Format(def.EffectiveGesture);

                sb.Append(gesture).Append(": ").Append(def.Description ?? def.Id.Value);
            }

            // Esc — локальна клавіша закриття overlay (не частина HotkeyService).
            if (sb.Length > 0)
                sb.Append(" • ");
            sb.Append("Esc: закрити");

            return sb.ToString();
        }

        public void Hide()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            _window?.Hide();
        }

        public void Toggle(long cycleStartMs)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            if (_window == null)
            {
                Show(cycleStartMs);
                return;
            }

            if (_window.Visibility == Visibility.Visible && _window.IsVisible)
                _window.Hide();
            else
                _window.Show();
        }

        public void Close()
        {
            _timer.Stop();
            _window?.Close();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _timer.Stop();

            if (_window == null)
                return;

            var window = _window;
            _window = null;

            if (Application.Current?.Dispatcher != null)
            {
                try
                {
                    _ = Application.Current.Dispatcher.InvokeAsync(() => window.Close(), DispatcherPriority.Send);
                }
                catch
                {
                    // Ігноруємо помилки Dispatcher під час виходу.
                }
            }
            else
            {
                try { window.Close(); }
                catch { /* ігноруємо */ }
            }
        }

        public Window? GetWindow() => _window;

        public void UpdateCycleStart(long cycleStartMs)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            _cycleStartMs = cycleStartMs;
            UpdateModel();
        }

        public void SetStartNow()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _cycleStartMs = ms;
            UpdateModel();
        }

        public void PromptManualStart(long manualStartMs)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            _cycleStartMs = manualStartMs;
            UpdateModel();
        }

        public void ToggleClickThrough()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            _overlayWindow?.ToggleClickThrough();
        }

        public void BeginTemporaryDrag()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            _overlayWindow?.BeginTemporaryDragMode();
        }

        public void ScaleDown()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            SetScale(Math.Max(ScaleMin, Math.Round(_state.Scale - ScaleStep, 2)));
        }

        public void ScaleUp()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            SetScale(Math.Min(ScaleMax, Math.Round(_state.Scale + ScaleStep, 2)));
        }

        public void ScaleReset()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            SetScale(1.0);
        }

        public void OpacityDown()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            _state.Opacity = Clamp(_state.Opacity - OpacityStep, OpacityMin, OpacityMax);
        }

        public void OpacityUp()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            _state.Opacity = Clamp(_state.Opacity + OpacityStep, OpacityMin, OpacityMax);
        }

        public void OpacityReset()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HangarOverlayService));

            _state.Opacity = 0.92;
        }

        /// <summary>
        /// Застосувати масштаб у реальному часі (live-preview зі Settings Hub).
        /// Оновлює стан → вікно реагує через PropertyChanged. Не персистить
        /// (персистенцію робить OverlaySettingsPane через IHangarSettingsService).
        /// </summary>
        public void ApplyScale(double scale)
        {
            if (_disposed)
                return;

            _state.Scale = scale;
        }

        /// <summary>
        /// Застосувати прозорість у реальному часі (live-preview зі Settings Hub).
        /// Оновлює стан → вікно реагує через PropertyChanged.
        /// </summary>
        public void ApplyOpacity(double opacity)
        {
            if (_disposed)
                return;

            _state.Opacity = opacity;
        }

        private void OnWindowLocationChanged(object? sender, EventArgs e)
        {
            if (_window is null)
                return;

            PositionChanged?.Invoke(this, (_window.Left, _window.Top));
        }

        private void OnWindowClosed()
        {
            _timer.Stop();
            if (_window != null)
                _window.LocationChanged -= OnWindowLocationChanged;
            _window = null;
            _overlayWindow = null;
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            UpdateModel();
        }

        private void SetScale(double scale)
        {
            if (Math.Abs(scale - _state.Scale) < 0.001)
                return;

            _state.Scale = scale;
            _settingsService.SetOverlayScale(scale);
        }

        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private void UpdateModel()
        {
            var info = HangarCycleCalculator.Compute(_cycleStartMs);

            _state.Phase = info.Phase;
            _state.StatusMessage = info.StatusMessage;
            _state.StatusLine = info.StatusLine;
            _state.TimerText = info.TimerText;

            var lights = _state.Lights;
            for (int i = 0; i < lights.Length; i++)
            {
                lights[i].State = info.LedStates[i];
                lights[i].Label = string.Empty;
            }

            // Міні-таймер під найближчим активним LED (зберігаємо стару поведінку).
            int minTimerIndex = -1;
            int bestVal = int.MaxValue;
            int interval = info.Phase == HangarCyclePhase.Closed
                ? HangarCycleCalculator.RedPhaseSeconds / HangarCycleCalculator.LedCount
                : HangarCycleCalculator.GreenPhaseSeconds / HangarCycleCalculator.LedCount;

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int elapsed = (int)Math.Floor((nowMs - _cycleStartMs) / 1000.0);
            int cyclePos = Mod(elapsed, HangarCycleCalculator.TotalCycleSeconds);

            for (int i = 0; i < lights.Length; i++)
            {
                if (info.Phase == HangarCyclePhase.Closed)
                {
                    if (info.LedStates[i] != HangarLightState.Red) continue;
                    int target = (i + 1) * interval;
                    int left = target - cyclePos;
                    if (left > 0 && left < bestVal) { bestVal = left; minTimerIndex = i; }
                }
                else if (info.Phase == HangarCyclePhase.Open)
                {
                    if (info.LedStates[i] != HangarLightState.Green) continue;
                    int timeSinceGreen = cyclePos - HangarCycleCalculator.RedPhaseSeconds;
                    int target = (HangarCycleCalculator.LedCount - i) * interval;
                    int left = target - timeSinceGreen;
                    if (left > 0 && left < bestVal) { bestVal = left; minTimerIndex = i; }
                }
            }

            if (minTimerIndex >= 0)
            {
                lights[minTimerIndex].Label = FormatMMSS(bestVal);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (i != minTimerIndex)
                        lights[i].Label = string.Empty;
                }
            }
        }

        private static int Mod(int a, int m)
        {
            return (a % m + m) % m;
        }

        private static string FormatMMSS(int seconds)
        {
            int m = seconds / 60;
            int s = seconds % 60;
            return $"{m:00}:{s:00}";
        }
    }
}
