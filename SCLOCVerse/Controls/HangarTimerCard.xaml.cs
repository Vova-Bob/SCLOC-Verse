using SCLOCVerse.Interfaces;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// Покращена картка інструменту Hangar Timer з LED-індикатором циклу,
    /// поточним часом та спливаючим вікном гарячих клавіш.
    /// </summary>
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

        /// <summary>
        /// Встановлює сервіс таймера, що живить картку даними.
        /// </summary>
        public void SetHangarTimerService(IHangarTimerService service)
        {
            _hangarTimerService = service;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
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
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            UpdateTimerText();
            UpdateLedIndicator();
        }

        private void UpdateTimerText()
        {
            var startMs = _hangarTimerService?.CycleStartMs;
            if (!startMs.HasValue)
            {
                TimerText.Text = "--:--:--";
                return;
            }

            var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs.Value;
            var time = TimeSpan.FromMilliseconds(Math.Max(0, elapsed));

            TimerText.Text = $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
        }

        private void UpdateLedIndicator()
        {
            var startMs = _hangarTimerService?.CycleStartMs;
            if (!startMs.HasValue)
            {
                SetLedState(0);
                return;
            }

            // Executive Hangar цикл триває 6 годин = 21 600 000 мс.
            const long cycleDurationMs = 6L * 60L * 60L * 1000L;
            var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs.Value;
            var progress = Math.Min(1.0, Math.Max(0.0, (double)elapsed / cycleDurationMs));

            int activeCount = progress switch
            {
                < 0.25 => 1,
                < 0.50 => 2,
                < 0.75 => 3,
                _ => 4
            };

            SetLedState(activeCount);
        }

        private void SetLedState(int activeCount)
        {
            var activeBrush = new SolidColorBrush(Color.FromRgb(109, 185, 248)); // #6DB9F8
            var inactiveBrush = new SolidColorBrush(Color.FromRgb(42, 74, 94));    // #2A4A5E

            Led1.Fill = activeCount >= 1 ? activeBrush : inactiveBrush;
            Led2.Fill = activeCount >= 2 ? activeBrush : inactiveBrush;
            Led3.Fill = activeCount >= 3 ? activeBrush : inactiveBrush;
            Led4.Fill = activeCount >= 4 ? activeBrush : inactiveBrush;
        }

        private void OpenOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            _hangarTimerService?.ToggleOverlayAsync();
        }

        private void HotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            // Клік також перемикає Popup, хоча основна логіка на hover.
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

        private void CardRoot_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Легка анімація при наведенні вже реалізована через триггер Border.
        }

        private void CardRoot_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Нічого додаткового; hover лише для стилю картки.
        }

        private void CardRoot_Loaded(object sender, RoutedEventArgs e)
        {
            // Зарезервовано для майбутніх ініціалізацій.
        }

        private void CardRoot_Unloaded(object sender, RoutedEventArgs e)
        {
            ClosePopup();
            OnUnloaded(sender, e);
        }

        private void OpenPopup()
        {
            HotkeyPopup.IsOpen = true;
        }

        private void ClosePopup()
        {
            HotkeyPopup.IsOpen = false;
        }

        private void DelayClosePopup()
        {
            // Даємо час перевести курсор з кнопки на Popup.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isHoveringButton && !_isHoveringPopup)
                    ClosePopup();
            }), DispatcherPriority.Input);
        }
    }
}
