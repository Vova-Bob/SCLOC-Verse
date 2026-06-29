using SCLOCVerse.Services.InputSystem.Diagnostics;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Бекенд гарячих клавіш на основі WinAPI RegisterHotKey.
    /// Фаза 1: повторює поведінку старого HangarHotkeyService без змін.
    /// </summary>
    public sealed class RegisterHotkeyBackend : IHotkeyBackend
    {
        private const int WmHotkey = 0x0312;
        private const int MinId = 0x0000;
        private const int MaxId = 0xBFFF;

        private readonly object _sync = new();
        private readonly Dictionary<int, HotkeyGesture> _idToGesture = new();
        private readonly Dictionary<HotkeyGesture, int> _gestureToId = new();
        private IHotkeyMessageSource? _messageSource;
        private bool _disposed;
        private int _nextId = MinId;

        /// <inheritdoc/>
        public bool IsInitialized => _messageSource != null;

        /// <inheritdoc/>
        public event EventHandler<HotkeyGesture>? GestureDetected;

        /// <inheritdoc/>
        public event EventHandler<HotkeyGesture>? KeyUp;

        /// <inheritdoc/>
        public void Initialize(IHotkeyMessageSource messageSource)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RegisterHotkeyBackend));

            if (_messageSource != null)
                return;

            _messageSource = messageSource ?? throw new ArgumentNullException(nameof(messageSource));
            _messageSource.AddHook(WndProc);

            InputDiagnostics.Write(
                "RegisterHotkeyBackend",
                $"Initialize HWND=0x{_messageSource.Handle:X}");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            lock (_sync)
            {
                if (_messageSource != null)
                {
                    foreach (var id in _idToGesture.Keys)
                    {
                        bool result = UnregisterHotKey(_messageSource.Handle, id);
                        InputDiagnostics.Write(
                            "RegisterHotkeyBackend",
                            $"Dispose UnregisterHotKey id={id} result={result}");
                    }

                    _messageSource.RemoveHook(WndProc);
                    _messageSource = null;
                }

                _idToGesture.Clear();
                _gestureToId.Clear();
            }

            GestureDetected = null;
        }

        /// <summary>
        /// Реєструє комбінацію в системі RegisterHotKey.
        /// Повертає true у разі успіху.
        /// </summary>
        internal bool TryRegister(HotkeyGesture gesture)
        {
            lock (_sync)
            {
                if (_messageSource == null || _gestureToId.ContainsKey(gesture))
                    return false;

                int id = AllocateId();
                if (id < 0)
                    return false;

                uint modifiers = ToNativeModifiers(gesture.Modifiers);
                uint vk = (uint)gesture.Key;

                InputDiagnostics.Write(
                    "RegisterHotkeyBackend",
                    $"TryRegister gesture={gesture} id={id} modifiers={modifiers} vk={vk}");

                if (RegisterHotKey(_messageSource.Handle, id, modifiers, vk))
                {
                    _idToGesture[id] = gesture;
                    _gestureToId[gesture] = id;

                    InputDiagnostics.Write(
                        "RegisterHotkeyBackend",
                        $"TryRegister SUCCESS gesture={gesture} id={id}");

                    return true;
                }

                InputDiagnostics.Write(
                    "RegisterHotkeyBackend",
                    $"TryRegister FAILED gesture={gesture} id={id} Win32Error={InputDiagnostics.GetWin32Error()}");

                return false;
            }
        }

        /// <summary>
        /// Скасовує реєстрацію комбінації в системі RegisterHotKey.
        /// </summary>
        internal void Unregister(HotkeyGesture gesture)
        {
            lock (_sync)
            {
                if (_messageSource == null || !_gestureToId.TryGetValue(gesture, out int id))
                    return;

                bool result = UnregisterHotKey(_messageSource.Handle, id);
                InputDiagnostics.Write(
                    "RegisterHotkeyBackend",
                    $"Unregister gesture={gesture} id={id} result={result} Win32Error={InputDiagnostics.GetWin32Error()}");

                _idToGesture.Remove(id);
                _gestureToId.Remove(gesture);
            }
        }

        private int AllocateId()
        {
            // Простий пошук вільного ідентифікатора.
            for (int i = 0; i <= MaxId - MinId; i++)
            {
                int candidate = MinId + ((_nextId + i) % (MaxId - MinId + 1));
                if (!_idToGesture.ContainsKey(candidate))
                {
                    _nextId = candidate + 1;
                    return candidate;
                }
            }

            return -1;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmHotkey)
                return IntPtr.Zero;

            int id = wParam.ToInt32();

            InputDiagnostics.Write(
                "RegisterHotkeyBackend",
                $"WndProc WM_HOTKEY id={id} wParam=0x{wParam:X} lParam=0x{lParam:X}");

            lock (_sync)
            {
                if (_idToGesture.TryGetValue(id, out var gesture))
                {
                    InputDiagnostics.Write(
                        "RegisterHotkeyBackend",
                        $"WM_HOTKEY MATCH gesture={gesture}");

                    GestureDetected?.Invoke(this, gesture);
                    handled = true;
                }
                else
                {
                    InputDiagnostics.Write(
                        "RegisterHotkeyBackend",
                        $"WM_HOTKEY UNKNOWN id={id}");
                }
            }

            return IntPtr.Zero;
        }

        private static uint ToNativeModifiers(HotkeyModifiers modifiers)
        {
            uint result = 0;
            if ((modifiers & HotkeyModifiers.Alt) != 0) result |= 0x0001;
            if ((modifiers & HotkeyModifiers.Control) != 0) result |= 0x0002;
            if ((modifiers & HotkeyModifiers.Shift) != 0) result |= 0x0004;
            if ((modifiers & HotkeyModifiers.Win) != 0) result |= 0x0008;
            return result;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
