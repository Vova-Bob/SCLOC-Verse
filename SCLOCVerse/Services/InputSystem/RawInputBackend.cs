using SCLOCVerse.Services.InputSystem.Diagnostics;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Основний бекенд гарячих клавіш SCLOC-Verse на основі Raw Input.
    /// Отримує WM_INPUT незалежно від фокусу вікна через RIDEV_INPUTSINK.
    /// </summary>
    public sealed class RawInputBackend : IHotkeyBackend
    {
        private const int WmInput = 0x00FF;
        private const ushort UsagePageKeyboard = 0x01;
        private const ushort UsageKeyboard = 0x06;

        private const int VirtualKeyShift = 0x10;
        private const int VirtualKeyControl = 0x11;
        private const int VirtualKeyAlt = 0x12;
        private const int VirtualKeyLeftShift = 0xA0;
        private const int VirtualKeyRightShift = 0xA1;
        private const int VirtualKeyLeftControl = 0xA2;
        private const int VirtualKeyRightControl = 0xA3;
        private const int VirtualKeyLeftAlt = 0xA4;
        private const int VirtualKeyRightAlt = 0xA5;
        private const int VirtualKeyLeftWin = 0x5B;
        private const int VirtualKeyRightWin = 0x5C;

        private readonly Lock _lock = new();
        private readonly HashSet<HotkeyKey> _pressedKeys = [];

        private IHotkeyMessageSource? _messageSource;
        private bool _disposed;
        private bool _diagnosticsEnabled;

        /// <inheritdoc/>
        public bool IsInitialized => _messageSource != null;

        /// <inheritdoc/>
        public event EventHandler<HotkeyGesture>? GestureDetected;

        /// <inheritdoc/>
        public event EventHandler<HotkeyGesture>? KeyUp;

        /// <summary>
        /// Створює бекенд Raw Input.
        /// </summary>
        /// <param name="enableDiagnostics">Чи журналювати діагностичні повідомлення.</param>
        public RawInputBackend(bool enableDiagnostics = false)
        {
            _diagnosticsEnabled = enableDiagnostics;
        }

        /// <inheritdoc/>
        public void Initialize(IHotkeyMessageSource messageSource)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_messageSource != null)
                return;

            _messageSource = messageSource ?? throw new ArgumentNullException(nameof(messageSource));
            _messageSource.AddHook(WndProc);

            RegisterRawInputDevice();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            lock (_lock)
            {
                if (_messageSource != null)
                {
                    _messageSource.RemoveHook(WndProc);
                    UnregisterRawInputDevice();
                    _messageSource = null;
                }

                _pressedKeys.Clear();
            }

            GestureDetected = null;
            KeyUp = null;
        }

        private void RegisterRawInputDevice()
        {
            if (_messageSource == null)
                return;

            var device = new RawInputDevice
            {
                UsagePage = UsagePageKeyboard,
                Usage = UsageKeyboard,
                Flags = RawInputDeviceFlags.InputSink,
                Target = _messageSource.Handle
            };

            uint size = (uint)Marshal.SizeOf<RawInputDevice>();
            bool result = RegisterRawInputDevices([device], 1, size);
            int error = Marshal.GetLastWin32Error();

            LogDiagnostics(
                "RegisterRawInputDevices",
                $"size={size} HWND=0x{_messageSource.Handle:X} result={result} Win32Error={error}");
        }

        private void UnregisterRawInputDevice()
        {
            if (_messageSource == null)
                return;

            var device = new RawInputDevice
            {
                UsagePage = UsagePageKeyboard,
                Usage = UsageKeyboard,
                Flags = RawInputDeviceFlags.Remove,
                Target = IntPtr.Zero
            };

            uint size = (uint)Marshal.SizeOf<RawInputDevice>();
            bool result = RegisterRawInputDevices([device], 1, size);
            int error = Marshal.GetLastWin32Error();

            LogDiagnostics(
                "UnregisterRawInputDevices",
                $"result={result} Win32Error={error}");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmInput)
                return IntPtr.Zero;

            var gesture = ProcessRawInput(lParam);
            if (!gesture.HasValue)
                return IntPtr.Zero;

            LogDiagnostics(
                "WM_INPUT",
                $"gesture={gesture.Value} wParam=0x{wParam:X} lParam=0x{lParam:X}");

            lock (_lock)
            {
                if (_disposed || _messageSource == null)
                    return IntPtr.Zero;

                GestureDetected?.Invoke(this, gesture.Value);
                handled = true;
            }

            return IntPtr.Zero;
        }

        private HotkeyGesture? ProcessRawInput(IntPtr hRawInput)
        {
            var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
            uint size = 0;

            int firstResult = GetRawInputData(hRawInput, RawInputCommand.Input, IntPtr.Zero, ref size, headerSize);
            if (firstResult != 0 && firstResult != -1)
                return null;

            if (size == 0)
                return null;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                int readResult = GetRawInputData(hRawInput, RawInputCommand.Input, buffer, ref size, headerSize);
                if (readResult != size)
                    return null;

                var raw = Marshal.PtrToStructure<RawInput>(buffer);
                var keyboard = raw.Keyboard;
                var key = (HotkeyKey)keyboard.VKey;

                // Ігноруємо клавіші, які не входять до набору гарячих клавіш SCLOC-Verse.
                // Це запобігає обробці та журналюванню звичайного введення (паролі, повідомлення тощо).
                if (!IsKnownHotkeyKey(key))
                {
                    LogDiagnostics("IgnoredKey", $"VKey={keyboard.VKey}");
                    return null;
                }

                // Flags == 0 або 2 — keydown; 1 або 3 — keyup.
                bool isKeyDown = (keyboard.Flags & 1) == 0;
                bool isKeyUp = (keyboard.Flags & 1) != 0;

                lock (_lock)
                {
                    if (_disposed)
                        return null;

                    if (isKeyUp)
                    {
                        _pressedKeys.Remove(key);

                        var upModifiers = GetCurrentModifiers();
                        var upGesture = new HotkeyGesture(upModifiers, key);
                        KeyUp?.Invoke(this, upGesture);

                        return null;
                    }

                    // Auto-repeat: клавіша вже натиснута і приходить ще одна подія keydown.
                    if (_pressedKeys.Contains(key))
                        return null;

                    _pressedKeys.Add(key);
                }

                var modifiers = GetCurrentModifiers();
                var gesture = new HotkeyGesture(modifiers, key);

                LogDiagnostics("GestureDetected", $"VKey={keyboard.VKey} gesture={gesture}");
                return gesture;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool IsKnownHotkeyKey(HotkeyKey key)
        {
            return Enum.IsDefined(typeof(HotkeyKey), key);
        }

        private static HotkeyModifiers GetCurrentModifiers()
        {
            HotkeyModifiers modifiers = HotkeyModifiers.None;

            if ((GetAsyncKeyState(VirtualKeyLeftShift) & 0x8000) != 0
                || (GetAsyncKeyState(VirtualKeyRightShift) & 0x8000) != 0
                || (GetAsyncKeyState(VirtualKeyShift) & 0x8000) != 0)
                modifiers |= HotkeyModifiers.Shift;

            if ((GetAsyncKeyState(VirtualKeyLeftControl) & 0x8000) != 0
                || (GetAsyncKeyState(VirtualKeyRightControl) & 0x8000) != 0
                || (GetAsyncKeyState(VirtualKeyControl) & 0x8000) != 0)
                modifiers |= HotkeyModifiers.Control;

            if ((GetAsyncKeyState(VirtualKeyLeftAlt) & 0x8000) != 0
                || (GetAsyncKeyState(VirtualKeyRightAlt) & 0x8000) != 0
                || (GetAsyncKeyState(VirtualKeyAlt) & 0x8000) != 0)
                modifiers |= HotkeyModifiers.Alt;

            if ((GetAsyncKeyState(VirtualKeyLeftWin) & 0x8000) != 0
                || (GetAsyncKeyState(VirtualKeyRightWin) & 0x8000) != 0)
                modifiers |= HotkeyModifiers.Win;

            return modifiers;
        }

        private void LogDiagnostics(string source, string message)
        {
            if (_diagnosticsEnabled)
                InputDiagnostics.Write($"RawInputBackend.{source}", message);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RawInputDevice[] rawInputDevices, uint numDevices, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetRawInputData(IntPtr hRawInput, RawInputCommand command, IntPtr data, ref uint size, uint sizeHeader);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            public ushort UsagePage;
            public ushort Usage;
            public RawInputDeviceFlags Flags;
            public IntPtr Target;
        }

        [Flags]
        private enum RawInputDeviceFlags : uint
        {
            Remove = 0x00000001,
            InputSink = 0x00000100
        }

        private enum RawInputCommand : uint
        {
            Input = 0x10000003,
            Header = 0x10000005
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputHeader
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInput
        {
            public RawInputHeader Header;
            public RawInputKeyboard Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputKeyboard
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }
    }
}
