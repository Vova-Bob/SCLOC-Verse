using SCLOCVerse.Controls;
using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.AutoKey;
using SCLOCVerse.Services.InputSystem;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace SCLOCVerse.Services.AutoKey
{
    /// <summary>
    /// Координатор модуля Auto Key.
    ///
    /// Stateless-цикл (кожен send-інтервал, 100–2000мс):
    ///   GetForegroundWindow → GetWindowThreadProcessId → PID
    ///   → OpenProcess(LIMITED) → QueryFullProcessImageName → ProcessName
    ///   → «StarCitizen.exe» ?  →  RUNNING + SendInput  :  PAUSED
    ///
    /// Жодного кешу PID, жодної логіки відновлення: сервіс завжди бачить поточну
    /// реальність системи. Переживає краш/перезапуск/Alt+Tab гри та запуск гри
    /// після SCLOC-Verse. Якщо OpenProcess не відкрився — тихо PAUSED (без логів,
    /// без винятків, без SendInput).
    ///
    /// Керує індикатором-оверлеєм (3 стани) через AutoKeyIndicatorWindow.
    /// Повністю незалежний від Anti-AFK та Hangar Timer.
    /// </summary>
    public sealed class AutoKeyService : IAutoKeyService
    {
        private const int DefaultIntervalMs = 1000;
        private const int MinIntervalMs = 100;
        private const int MaxIntervalMs = 2000;
        private const HotkeyKey DefaultActionKey = HotkeyKey.Oem4; // '['

        private readonly IHotkeyService _hotkeyService;
        private readonly IPreferencesService _preferences;
        private readonly System.Threading.Timer _timer;

        private bool _enabled;
        private bool _disposed;
        private int _intervalMs = DefaultIntervalMs;
        private HotkeyKey _actionKey = DefaultActionKey;
        private AutoKeyState _state = AutoKeyState.Off;

        private AutoKeyIndicatorWindow? _indicator;

        /// <inheritdoc/>
        public bool IsEnabled => Volatile.Read(ref _enabled);

        /// <inheritdoc/>
        public AutoKeyState State => _state;

        /// <inheritdoc/>
        public event EventHandler<AutoKeyState>? StateChanged;

        public AutoKeyService(IHotkeyService hotkeyService, IPreferencesService preferences)
        {
            _hotkeyService = hotkeyService;
            _preferences = preferences;

            _intervalMs = ClampInterval(_preferences.GetAutoKeyIntervalMs());
            _actionKey = _preferences.GetAutoKeyActionKey();

            _timer = new System.Threading.Timer(TimerCallback, null, Timeout.Infinite, Timeout.Infinite);

            RegisterHotkey();
        }

        // ===== Публічний API =====

        /// <inheritdoc/>
        public void Toggle()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (IsEnabled)
                StopInternal();
            else
                StartInternal();
        }

        /// <inheritdoc/>
        public void ApplySettings()
        {
            _intervalMs = ClampInterval(_preferences.GetAutoKeyIntervalMs());
            _actionKey = _preferences.GetAutoKeyActionKey();

            // Якщо увімкнено — перезапустити таймер із новим інтервалом.
            if (IsEnabled)
                _timer.Change(_intervalMs, _intervalMs);
        }

        // ===== Внутрішня логіка =====

        private void StartInternal()
        {
            Volatile.Write(ref _enabled, true);

            // Підняти актуальні налаштування (інтервал/клавіша) на старті.
            _intervalMs = ClampInterval(_preferences.GetAutoKeyIntervalMs());
            _actionKey = _preferences.GetAutoKeyActionKey();
            _preferences.SetAutoKeyEnabled(true);

            _timer.Change(_intervalMs, _intervalMs);

            ShowIndicator();
            SetState(AutoKeyState.Paused);
        }

        private void StopInternal()
        {
            Volatile.Write(ref _enabled, false);
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _preferences.SetAutoKeyEnabled(false);

            SetState(AutoKeyState.Off);
            HideIndicator();
        }

        /// <summary>
        /// Stateless-цикл: Foreground Gate (спільний з Anti-AFK) → стан → дія.
        /// </summary>
        private void TimerCallback(object? state)
        {
            if (_disposed || !IsEnabled)
                return;

            // Foreground Gate: лише активне вікно Star Citizen. Спільний helper з Anti-AFK (DRY).
            if (StarCitizenForeground.IsStarCitizenForeground())
            {
                SetState(AutoKeyState.Running);
                SendKeyPress(_actionKey);
            }
            else
            {
                SetState(AutoKeyState.Paused);
            }
        }

        private void SetState(AutoKeyState newState)
        {
            if (_state == newState)
                return;

            _state = newState;
            UpdateIndicator(newState);
            StateChanged?.Invoke(this, newState);
        }

        private void RegisterHotkey()
        {
            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.AutoKeyToggle,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.None, HotkeyKey.Home),
                Description = "Увімкнути / Вимкнути Auto Key",
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

                _indicator.UpdateState(_state);
                _indicator.Show();
            }));
        }

        private void HideIndicator()
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _indicator?.Hide();
            }));
        }

        private void UpdateIndicator(AutoKeyState s)
        {
            // Оновлювати лише видиме вікно; якщо ще не створене — ShowIndicator покаже з _state.
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed || _indicator is null)
                    return;

                _indicator.UpdateState(s);
            }));
        }

        private void EnsureIndicatorCreated()
        {
            if (_indicator != null)
                return;

            _indicator = new AutoKeyIndicatorWindow();
        }

        // ===== PInvoke: SendInput (клавіатура) =====

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        // Win32 INPUT union.
        // MOUSEINPUT визначає підсумковий розмір INPUT (40 байт на x64).
        // Keyboard-only union має 32 байти, що несумісно із SendInput.
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint InputKeyboard = 1;
        private const uint KeyeventfKeyup = 0x0002;
        private const uint MapvkVkToVsc = 0;

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        /// <summary>
        /// Надсилає одне натискання клавіші (key-down + key-up) у активне вікно.
        /// Викликається лише у стані RUNNING (активне вікно Star Citizen).
        /// </summary>
        private void SendKeyPress(HotkeyKey key)
        {
            var keyCode = (ushort)key;
            // Scan code через MapVirtualKey: CryEngine/ігри часто ігнорують wVk і
            // читають лише scan code. Встановлюємо обидва для максимальної сумісності
            // (вкл. OEM-клавіші типу [, що залежать від розкладки).
            var scan = (ushort)MapVirtualKey(keyCode, MapvkVkToVsc);

            var inputs = new INPUT[2];
            inputs[0] = new INPUT
            {
                type = InputKeyboard,
                u = new InputUnion { ki = new KEYBDINPUT { wVk = keyCode, wScan = scan } }
            };
            inputs[1] = new INPUT
            {
                type = InputKeyboard,
                u = new InputUnion { ki = new KEYBDINPUT { wVk = keyCode, wScan = scan, dwFlags = KeyeventfKeyup } }
            };

            try
            {
                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            }
            catch (Exception ex)
            {
                // Тільки діагностика (debug-вивід); у Paused-шляху SendInput не викликається.
                Debug.WriteLine($"[AutoKeyService] SendInput error: {ex.Message}");
            }
        }

        // ===== Допоміжні =====

        private static int ClampInterval(int ms) => Math.Clamp(ms, MinIntervalMs, MaxIntervalMs);

        // ===== Dispose =====

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Volatile.Write(ref _enabled, false);

            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _timer.Dispose();

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    _indicator?.Hide();
                    _indicator?.Close();
                    _indicator = null;
                }));
            }
        }
    }
}
