using SCLOCVerse.Controls;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.HangarTimer;
using System.Windows;
using System.Windows.Threading;

namespace SCLOCVerse.Services.HangarTimer
{
    /// <summary>
    /// Сервіс керування візуальним overlay Hangar Timer.
    /// Містить масштаб, прозорість та координацію з вікном.
    /// Логіка циклу винесена у HangarCycleCalculator.
    /// </summary>
    public class HangarOverlayService : IHangarOverlayService
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
        private readonly DispatcherTimer _timer;
        private readonly HangarTimerState _state;

        private HangarOverlayWindow? _window;
        private long _cycleStartMs;

        public HangarOverlayService(IHangarSettingsService settingsService)
        {
            _settingsService = settingsService;
            _state = new HangarTimerState();

            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _timer.Tick += OnTimerTick;
        }

        public bool IsOpen => _window != null;

        public void Show(long cycleStartMs)
        {
            if (_window != null)
            {
                _window.Activate();
                return;
            }

            _cycleStartMs = cycleStartMs;

            _window = new HangarOverlayWindow(_state, _settingsService, OnWindowClosed);
            _window.Show();
            _timer.Start();
            UpdateModel();
        }

        public void Hide()
        {
            _window?.Hide();
        }

        public void Toggle(long cycleStartMs)
        {
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
            _window?.Close();
        }

        public Window? GetWindow() => _window;

        public void UpdateCycleStart(long cycleStartMs)
        {
            _cycleStartMs = cycleStartMs;
            UpdateModel();
        }

        public void SetStartNow()
        {
            var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _cycleStartMs = ms;
            UpdateModel();
        }

        public void PromptManualStart(long manualStartMs)
        {
            _cycleStartMs = manualStartMs;
            UpdateModel();
        }

        public void ToggleClickThrough()
        {
            _window?.ToggleClickThrough();
        }

        public void BeginTemporaryDrag()
        {
            _window?.BeginTemporaryDragMode();
        }

        public void ScaleDown()
        {
            SetScale(Math.Max(ScaleMin, Math.Round(_state.Scale - ScaleStep, 2)));
        }

        public void ScaleUp()
        {
            SetScale(Math.Min(ScaleMax, Math.Round(_state.Scale + ScaleStep, 2)));
        }

        public void ScaleReset()
        {
            SetScale(1.0);
        }

        public void OpacityDown()
        {
            _state.Opacity = Clamp(_state.Opacity - OpacityStep, OpacityMin, OpacityMax);
        }

        public void OpacityUp()
        {
            _state.Opacity = Clamp(_state.Opacity + OpacityStep, OpacityMin, OpacityMax);
        }

        public void OpacityReset()
        {
            _state.Opacity = 0.92;
        }

        private void OnWindowClosed()
        {
            _timer.Stop();
            _window = null;
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
