using SCLOCVerse.Controls;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.AntiAfk;
using SCLOCVerse.Services.InputSystem;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace SCLOCVerse.Services.AntiAfk
{
    /// <summary>
    /// Координатор модуля Anti-AFK.
    /// Виявляє бездіяльність користувача через GetLastInputInfo (без hooks)
    /// та імітує рух миші (SendInput ±1px) для запобігання AFK-статусу.
    /// Керує індикатором-оверлеєм (пульсуюча точка) через AntiAfkIndicatorWindow.
    /// Повністю незалежний від Hangar Overlay.
    /// </summary>
    public class AntiAfkService : IAntiAfkService
    {
        private const int MovementDelta = 1;
        private const int PollIntervalMs = 1000;
        private const int ThresholdMinMs = 1000;
        private const int ThresholdMaxMs = 60000;
        private const double IdleFlashSeconds = 1.5;

        private readonly IHotkeyService _hotkeyService;
        private readonly IPreferencesService _preferences;
        private readonly System.Threading.Timer _timer;
        private readonly Random _random = new();

        private bool _isRunning;
        private bool _moveRight = true;
        private int _afkThreshold;
        private bool _disposed;

        private AntiAfkIndicatorWindow? _indicator;
        private DispatcherTimer? _idleFlashTimer;

        /// <inheritdoc/>
        public bool IsRunning => Volatile.Read(ref _isRunning);

        /// <inheritdoc/>
        public event EventHandler<bool>? StateChanged;

        public AntiAfkService(IHotkeyService hotkeyService, IPreferencesService preferences)
        {
            _hotkeyService = hotkeyService;
            _preferences = preferences;

            _timer = new System.Threading.Timer(TimerCallback, null, Timeout.Infinite, Timeout.Infinite);
            SetRandomAfkThreshold();
            RegisterHotkey();

            // Авто-старт якщо раніше було увімкнено.
            if (_preferences.GetAntiAfkEnabled())
                StartInternal();
        }

        /// <inheritdoc/>
        public void Toggle()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (IsRunning)
                StopInternal();
            else
                StartInternal();
        }

        /// <inheritdoc/>
        public void ApplyIndicatorSettings()
        {
            // Live Preview: перемалювати видимий індикатор з новими налаштуваннями.
            // У IdleOnly режимі індикатор не постійний — застосується при наступному спалахові.
            if (!IsRunning)
                return;

            var mode = _preferences.GetAntiAfkIndicatorMode();
            if (mode == AntiAfkIndicatorMode.IdleOnly)
                return;

            ShowIndicator();
        }

        // ===== Внутрішня логіка =====

        private void StartInternal()
        {
            Volatile.Write(ref _isRunning, true);
            _timer.Change(0, PollIntervalMs);
            _preferences.SetAntiAfkEnabled(true);

            // Індикатор: показати лише в Running режимі (IdleOnly спалахує при дії).
            var mode = _preferences.GetAntiAfkIndicatorMode();
            if (mode == AntiAfkIndicatorMode.Running)
                ShowIndicator();

            StateChanged?.Invoke(this, true);
        }

        private void StopInternal()
        {
            Volatile.Write(ref _isRunning, false);
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _preferences.SetAntiAfkEnabled(false);

            HideIndicator();

            StateChanged?.Invoke(this, false);
        }

        private void TimerCallback(object? state)
        {
            if (_disposed || !IsRunning)
                return;

            // Перевірка бездіяльності через GetLastInputInfo (без hooks).
            int idleMs = GetIdleMilliseconds();
            if (idleMs < _afkThreshold)
                return;

            // Користувач бездіяльний — імітуємо рух миші.
            SimulateMouseMove();
            SetRandomAfkThreshold();

            // IdleOnly: спалах індикатора в момент дії.
            var mode = _preferences.GetAntiAfkIndicatorMode();
            if (mode == AntiAfkIndicatorMode.IdleOnly)
                FlashIndicator();
        }

        private void SetRandomAfkThreshold()
        {
            _afkThreshold = _random.Next(ThresholdMinMs, ThresholdMaxMs);
        }

        private void RegisterHotkey()
        {
            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.AntiAfkToggle,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.None, HotkeyKey.End),
                Description = "Увімкнути / Вимкнути Anti-AFK",
                Handler = ct =>
                {
                    Toggle();
                    return ValueTask.CompletedTask;
                }
            });
        }

        // ===== Індикатор =====

        private void ShowIndicator()
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed)
                    return;

                EnsureIndicatorCreated();
                if (_indicator is null)
                    return;

                ApplyIndicatorAppearance(_indicator);
                _indicator.Show();
            }));
        }

        private void HideIndicator()
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _idleFlashTimer?.Stop();
                _indicator?.StopAnimation();
                _indicator?.Hide();
            }));
        }

        /// <summary>
        /// Спалах індикатора в IdleOnly режимі: показати + анімація + auto-hide через 1.5с.
        /// </summary>
        private void FlashIndicator()
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed)
                    return;

                var color = _preferences.GetAntiAfkIndicatorColor();
                if (color == "Hidden")
                    return;

                EnsureIndicatorCreated();
                if (_indicator is null)
                    return;

                ApplyIndicatorAppearance(_indicator);
                _indicator.Show();

                _idleFlashTimer?.Stop();
                _idleFlashTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(IdleFlashSeconds)
                };
                _idleFlashTimer.Tick += (_, _) =>
                {
                    _idleFlashTimer?.Stop();
                    _indicator?.StopAnimation();
                    _indicator?.Hide();
                };
                _idleFlashTimer.Start();
            }));
        }

        /// <summary>
        /// Застосовує поточні налаштування індикатора (колір, розмір, позиція, анімація).
        /// </summary>
        private void ApplyIndicatorAppearance(AntiAfkIndicatorWindow indicator)
        {
            var color = _preferences.GetAntiAfkIndicatorColor();
            var size = _preferences.GetAntiAfkIndicatorSize();
            var pos = _preferences.GetAntiAfkIndicatorPosition();
            var anim = _preferences.GetAntiAfkIndicatorAnimation();

            indicator.UpdateColor(color);
            indicator.UpdateSize(size, pos);
            indicator.StartAnimation(anim);
        }

        private void EnsureIndicatorCreated()
        {
            if (_indicator != null)
                return;

            _indicator = new AntiAfkIndicatorWindow();
        }

        // ===== PInvoke: GetLastInputInfo =====

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private static int GetIdleMilliseconds()
        {
            var info = new LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>()
            };

            if (!GetLastInputInfo(ref info))
                return 0;

            return Environment.TickCount - (int)info.dwTime;
        }

        // ===== PInvoke: SendInput =====

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public MouseEventFlags dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [Flags]
        private enum MouseEventFlags : uint
        {
            MOUSEEVENTF_MOVE = 0x0001
        }

        private const uint InputMouse = 0;

        private void SimulateMouseMove()
        {
            int delta;
            // _moveRight доступний лише з TimerCallback (однопотоково для System.Threading.Timer
            // при PollIntervalMs=1000 — callback не перекривається).
            delta = _moveRight ? MovementDelta : -MovementDelta;
            _moveRight = !_moveRight;

            var input = new INPUT
            {
                type = InputMouse,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = delta,
                        dy = delta,
                        dwFlags = MouseEventFlags.MOUSEEVENTF_MOVE
                    }
                }
            };

            try
            {
                SendInput(1, [input], Marshal.SizeOf<INPUT>());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AntiAfkService] SendInput error: {ex.Message}");
            }
        }

        // ===== Dispose =====

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Volatile.Write(ref _isRunning, false);

            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _timer.Dispose();

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    _idleFlashTimer?.Stop();
                    _indicator?.StopAnimation();
                    _indicator?.Close();
                    _indicator = null;
                }));
            }
        }
    }
}
