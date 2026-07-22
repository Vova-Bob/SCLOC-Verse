using SCLOCVerse.Controls;
using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Mining;
using System.Windows;
using System.Windows.Threading;

namespace SCLOCVerse.Services.Mining.Overlay
{
    /// <summary>
    /// Реалізація <see cref="IMiningOverlayService"/> — керує життєвим циклом
    /// <see cref="SignatureScannerOverlayWindow"/>.
    ///
    /// Особливості:
    /// - Foreground gate: overlay показується лише коли Star Citizen активний.
    /// - Position persistence: позиція/розмір/прозорість зберігаються у Settings.
    /// - НЕ слідкує за HUD — стоїть там, де користувач поставив.
    /// </summary>
    public sealed class MiningOverlayService : IMiningOverlayService, IDisposable
    {
        private SignatureScannerOverlayWindow? _window;
        private readonly IPreferencesService _preferences;
        private DispatcherTimer? _foregroundTimer;
        private bool _disposed;
        private bool _shouldBeVisible;

        public MiningOverlayService(IPreferencesService preferences)
        {
            _preferences = preferences;
        }

        /// <inheritdoc />
        public bool IsVisible => _window?.IsVisible ?? false;

        /// <inheritdoc />
        public void Show()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;

            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(Show));
                return;
            }

            _shouldBeVisible = true;

            try
            {
                if (_window is null)
                {
                    _window = new SignatureScannerOverlayWindow();
                    RestoreWindowSettings();
                    _window.PositionChanged += OnPositionChanged;
                }

                // Foreground gate — перевіряємо перед показом.
                if (StarCitizenForeground.IsStarCitizenForeground())
                {
                    if (!_window.IsVisible) _window.Show();
                }

                StartForegroundTimer();
            }
            catch
            {
                _window = new SignatureScannerOverlayWindow();
                RestoreWindowSettings();
                _window.PositionChanged += OnPositionChanged;
                if (StarCitizenForeground.IsStarCitizenForeground()) _window.Show();
                StartForegroundTimer();
            }
        }

        /// <inheritdoc />
        public void Hide()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;

            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(Hide));
                return;
            }

            _shouldBeVisible = false;
            StopForegroundTimer();

            if (_window is not null && _window.IsVisible)
            {
                _window.Hide();
            }
        }

        /// <inheritdoc />
        public void UpdateState(MiningState state)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;

            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action<MiningState>(UpdateState), state);
                return;
            }

            _window?.UpdateState(state);
        }

        /// <inheritdoc />
        public void SetPosition(double hudBoundsX, double hudBoundsBottom)
        {
            // НЕ використовується — overlay не слідкує за HUD.
        }

        /// <summary>
        /// Увімкнути тимчасовий режим перетягування overlay.
        /// </summary>
        public void BeginDrag()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(BeginDrag));
                return;
            }
            _window?.BeginTemporaryDragMode();
        }

        /// <summary>
        /// Збільшити прозорість overlay.
        /// </summary>
        public void IncreaseOpacity()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(IncreaseOpacity));
                return;
            }
            _window?.IncreaseOpacity();
        }

        /// <summary>
        /// Зменшити прозорість overlay.
        /// </summary>
        public void DecreaseOpacity()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(DecreaseOpacity));
                return;
            }
            _window?.DecreaseOpacity();
        }

        /// <summary>
        /// Foreground timer — перевіряє кожні 500мс чи Star Citizen активний.
        /// Якщо SC втратив фокус — overlay ховається. Якщо отримав — показується.
        /// </summary>
        private void StartForegroundTimer()
        {
            if (_foregroundTimer is not null) return;

            _foregroundTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _foregroundTimer.Tick += OnForegroundTick;
            _foregroundTimer.Start();
        }

        private void StopForegroundTimer()
        {
            if (_foregroundTimer is null) return;
            _foregroundTimer.Stop();
            _foregroundTimer = null;
        }

        private void OnForegroundTick(object? sender, EventArgs e)
        {
            if (_window is null || _disposed) return;

            var isSc = StarCitizenForeground.IsStarCitizenForeground();

            if (_shouldBeVisible && isSc && !_window.IsVisible)
            {
                _window.Show();
            }
            else if (_shouldBeVisible && !isSc && _window.IsVisible)
            {
                _window.Hide();
            }
        }

        private void OnPositionChanged(object? sender, Rect pos)
        {
            // Зберегти лише позицію та прозорість.
            // Width/Height НЕ зберігаємо — SizeToContent="Height" + ResizeMode="NoResize"
            // означають що розмір визначається XAML, а не користувачем.
            // Збереження Width/Height призводило до перезапису XAML-ширини при рестарті.
            _preferences.SetMiningOverlayX(pos.X);
            _preferences.SetMiningOverlayY(pos.Y);
            if (_window is not null)
            {
                _preferences.SetMiningOverlayOpacity(_window.SavedOpacity);
            }
        }

        private void RestoreWindowSettings()
        {
            if (_window is null) return;

            var x = _preferences.GetMiningOverlayX();
            var y = _preferences.GetMiningOverlayY();
            var opacity = _preferences.GetMiningOverlayOpacity();

            // Лише позиція та прозорість. Width визначається XAML (SizeToContent="Height").
            // Height не відновлюємо — SizeToContent підганяє під контент автоматично.
            // Раніше відновлення w/h перезаписувало XAML-Width і ламало Layout.
            if (x > 0 || y > 0)
            {
                _window.Left = x;
                _window.Top = y;
            }

            _window.SavedOpacity = opacity > 0 ? opacity : 0.9;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopForegroundTimer();

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null)
            {
                if (!dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(new Action(Dispose));
                    return;
                }
            }

            try
            {
                if (_window is not null)
                {
                    _window.AllowClose();
                    _window.Close();
                    _window = null;
                }
            }
            catch { }
        }
    }
}