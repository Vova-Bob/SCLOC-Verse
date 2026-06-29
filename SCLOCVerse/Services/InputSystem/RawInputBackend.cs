using SCLOCVerse.Services.InputSystem.Diagnostics;
using System;
using System.Runtime.InteropServices;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Бекенд гарячих клавіш на основі Raw Input.
    /// Фаза 1: заготовка. Повноцінна реалізація у Фазі 2.
    /// </summary>
    public sealed class RawInputBackend : IHotkeyBackend
    {
        private const int WmInput = 0x00FF;
        private const ushort UsagePageKeyboard = 0x01;
        private const ushort UsageKeyboard = 0x06;

        private readonly object _sync = new();
        private IHotkeyMessageSource? _messageSource;
        private bool _disposed;

        /// <inheritdoc/>
        public bool IsInitialized => _messageSource != null;

        /// <inheritdoc/>
        public event EventHandler<HotkeyGesture>? GestureDetected;

        /// <inheritdoc/>
        public void Initialize(IHotkeyMessageSource messageSource)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RawInputBackend));

            if (_messageSource != null)
                return;

            _messageSource = messageSource ?? throw new ArgumentNullException(nameof(messageSource));
            _messageSource.AddHook(WndProc);

            InputDiagnostics.Write(
                "RawInputBackend",
                $"Initialize HWND=0x{_messageSource.Handle:X}");

            RegisterRawInputDevice();
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
                    _messageSource.RemoveHook(WndProc);
                    UnregisterRawInputDevice();
                    _messageSource = null;
                }
            }

            GestureDetected = null;
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

            InputDiagnostics.Write(
                "RawInputBackend",
                $"RegisterRawInputDevices size={size} HWND=0x{_messageSource.Handle:X}");

            bool result = RegisterRawInputDevices(new[] { device }, 1, size);
            int error = InputDiagnostics.GetWin32Error();

            InputDiagnostics.Write(
                "RawInputBackend",
                $"RegisterRawInputDevices result={result} Win32Error={error}");
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
            bool result = RegisterRawInputDevices(new[] { device }, 1, size);
            int error = InputDiagnostics.GetWin32Error();

            InputDiagnostics.Write(
                "RawInputBackend",
                $"UnregisterRawInputDevices result={result} Win32Error={error}");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmInput)
                return IntPtr.Zero;

            InputDiagnostics.Write(
                "RawInputBackend",
                $"WndProc WM_INPUT wParam=0x{wParam:X} lParam=0x{lParam:X}");

            return IntPtr.Zero;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RawInputDevice[] rawInputDevices, uint numDevices, uint size);

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
            InputSink = 0x00000100,
            Remove = 0x00000001
        }
    }
}
