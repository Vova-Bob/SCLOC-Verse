using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.HangarTimer;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SCLOCVerse.Controls
{
    public partial class HangarTimerCard : UserControl
    {
        private IHangarTimerService? _hangarTimerService;
        private DispatcherTimer? _timer;
        private bool _isHoveringButton;
        private bool _isHoveringPopup;

        public HangarTimerCard()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public void SetHangarTimerService(IHangarTimerService service)
        {
            if (_hangarTimerService != null)
                _hangarTimerService.CycleStartChanged -= OnServiceCycleStartChanged;

            _hangarTimerService = service;
            _hangarTimerService.CycleStartChanged += OnServiceCycleStartChanged;
            OnTimerTick(null, EventArgs.Empty);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += OnTimerTick;
            _timer.Start();
            OnTimerTick(null, EventArgs.Empty);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
                _timer = null;
            }

            if (_hangarTimerService != null)
                _hangarTimerService.CycleStartChanged -= OnServiceCycleStartChanged;
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            UpdateFromCycleInfo();
        }

        private void OnServiceCycleStartChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => OnTimerTick(null, EventArgs.Empty)), DispatcherPriority.Render);
        }

        private void UpdateFromCycleInfo()
        {
            var info = _hangarTimerService?.GetCycleInfo();
            if (info == null)
            {
                TimerText.Text = "--:--:--";
                SetLedState(0);
                return;
            }

            TimerText.Text = info.TimerText;
            SetLedState(info.LedStates);
        }

        private void SetLedState(HangarLightState[] states)
        {
            SetLedFill(Led1, states.Length > 0 ? states[0] : HangarLightState.Black);
            SetLedFill(Led2, states.Length > 1 ? states[1] : HangarLightState.Black);
            SetLedFill(Led3, states.Length > 2 ? states[2] : HangarLightState.Black);
            SetLedFill(Led4, states.Length > 3 ? states[3] : HangarLightState.Black);
            SetLedFill(Led5, states.Length > 4 ? states[4] : HangarLightState.Black);
        }

        private static void SetLedFill(Ellipse led, HangarLightState state)
        {
            switch (state)
            {
                case HangarLightState.Green:
                    led.Fill = new SolidColorBrush(Color.FromRgb(80, 200, 80));
                    led.Stroke = new SolidColorBrush(Color.FromArgb(150, 80, 200, 80));
                    led.Effect = new DropShadowEffect
                    {
                        Color = Color.FromArgb(150, 80, 200, 80),
                        BlurRadius = 8,
                        ShadowDepth = 0,
                        Opacity = 0.7
                    };
                    break;

                case HangarLightState.Red:
                    led.Fill = new SolidColorBrush(Color.FromRgb(220, 60, 60));
                    led.Stroke = new SolidColorBrush(Color.FromArgb(150, 255, 80, 80));
                    led.Effect = new DropShadowEffect
                    {
                        Color = Color.FromArgb(150, 255, 80, 80),
                        BlurRadius = 8,
                        ShadowDepth = 0,
                        Opacity = 0.7
                    };
                    break;

                default:
                    led.Fill = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                    led.Stroke = new SolidColorBrush(Color.FromRgb(58, 90, 112));
                    led.Effect = null;
                    break;
            }
        }

        private void SetLedState(int activeCount)
        {
            var activeBrush = new SolidColorBrush(Color.FromRgb(80, 200, 80));
            var inactiveBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            var glow = new SolidColorBrush(Color.FromArgb(150, 80, 200, 80));

            SetLedFill(Led1, activeCount >= 1, activeBrush, inactiveBrush, glow);
            SetLedFill(Led2, activeCount >= 2, activeBrush, inactiveBrush, glow);
            SetLedFill(Led3, activeCount >= 3, activeBrush, inactiveBrush, glow);
            SetLedFill(Led4, activeCount >= 4, activeBrush, inactiveBrush, glow);
            SetLedFill(Led5, activeCount >= 5, activeBrush, inactiveBrush, glow);
        }

        private static void SetLedFill(Ellipse led, bool active, Brush activeBrush, Brush inactiveBrush, Brush glowBrush)
        {
            led.Fill = active ? activeBrush : inactiveBrush;
            led.Stroke = active ? glowBrush : new SolidColorBrush(Color.FromRgb(58, 90, 112));

            if (active)
            {
                led.Effect = new DropShadowEffect
                {
                    Color = ((SolidColorBrush)glowBrush).Color,
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.7
                };
            }
            else
            {
                led.Effect = null;
            }
        }

        private void OpenOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            _hangarTimerService?.ToggleOverlayAsync();
        }

        private void HotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (HotkeyPopup.IsOpen)
                ClosePopup();
            else
                OpenPopup();
        }

        private void HotkeyButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _isHoveringButton = true;
            OpenPopup();
        }

        private void HotkeyButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _isHoveringButton = false;
            DelayClosePopup();
        }

        private void HotkeyPopup_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _isHoveringPopup = true;
        }

        private void HotkeyPopup_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _isHoveringPopup = false;
            DelayClosePopup();
        }

        private void CardRoot_Loaded(object sender, RoutedEventArgs e) { }
        private void CardRoot_Unloaded(object sender, RoutedEventArgs e)
        {
            ClosePopup();
            OnUnloaded(sender, e);
        }

        private void OpenPopup() => HotkeyPopup.IsOpen = true;
        private void ClosePopup() => HotkeyPopup.IsOpen = false;

        private void DelayClosePopup()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isHoveringButton && !_isHoveringPopup)
                    ClosePopup();
            }), DispatcherPriority.Input);
        }
    }
}