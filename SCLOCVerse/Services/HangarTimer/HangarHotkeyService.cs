using SCLOCVerse.Interfaces;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace SCLOCVerse.Services.HangarTimer
{
    /// <summary>
    /// Глобальні гарячі клавіші Hangar Timer через WinAPI RegisterHotKey.
    /// Реєстрація відбувається на HWND головного вікна WPF.
    /// </summary>
    public sealed class HangarHotkeyService : IHangarHotkeyService, IDisposable
    {
        private const int WmHotkey = 0x0312;

        private const int IdToggleOverlay = 0xB001;
        private const int IdToggleClickThrough = 0xB002;
        private const int IdTemporaryDrag = 0xB003;
        private const int IdSetStartNow = 0xB004;
        private const int IdPromptManual = 0xB005;
        private const int IdForceSync = 0xB006;
        private const int IdClearAndSync = 0xB007;
        private const int IdScaleDown = 0xB010;
        private const int IdScaleUp = 0xB011;
        private const int IdScaleReset = 0xB012;
        private const int IdOpacityDown = 0xB020;
        private const int IdOpacityUp = 0xB021;
        private const int IdOpacityReset = 0xB022;

        private const uint ModNone = 0x0000;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;

        private IntPtr _windowHandle = IntPtr.Zero;
        private HwndSource? _hwndSource;
        private bool _registered;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event EventHandler<HangarHotkeyAction>? ActionRequested;

        public void Register(IntPtr windowHandle)
        {
            if (_registered)
                return;

            _windowHandle = windowHandle;
            _hwndSource = HwndSource.FromHwnd(windowHandle);
            _hwndSource?.AddHook(WndProc);

            RegisterCore();
            _registered = true;
        }

        public void Unregister()
        {
            if (!_registered)
                return;

            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, IdToggleOverlay);
                UnregisterHotKey(_windowHandle, IdToggleClickThrough);
                UnregisterHotKey(_windowHandle, IdTemporaryDrag);
                UnregisterHotKey(_windowHandle, IdSetStartNow);
                UnregisterHotKey(_windowHandle, IdPromptManual);
                UnregisterHotKey(_windowHandle, IdForceSync);
                UnregisterHotKey(_windowHandle, IdClearAndSync);
                UnregisterHotKey(_windowHandle, IdScaleDown);
                UnregisterHotKey(_windowHandle, IdScaleUp);
                UnregisterHotKey(_windowHandle, IdScaleReset);
                UnregisterHotKey(_windowHandle, IdOpacityDown);
                UnregisterHotKey(_windowHandle, IdOpacityUp);
                UnregisterHotKey(_windowHandle, IdOpacityReset);
            }

            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
            _registered = false;
        }

        public void Dispose()
        {
            Unregister();
        }

        private void RegisterCore()
        {
            if (_windowHandle == IntPtr.Zero)
                return;

            RegisterHotKey(_windowHandle, IdToggleOverlay, ModNone, (uint)KeyInterop.VirtualKeyFromKey(Key.F6));
            RegisterHotKey(_windowHandle, IdToggleClickThrough, ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.F8));
            RegisterHotKey(_windowHandle, IdTemporaryDrag, ModControl, (uint)KeyInterop.VirtualKeyFromKey(Key.F8));

            RegisterHotKey(_windowHandle, IdSetStartNow, ModControl | ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.F7));
            RegisterHotKey(_windowHandle, IdPromptManual, ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.F7));
            RegisterHotKey(_windowHandle, IdForceSync, ModNone, (uint)KeyInterop.VirtualKeyFromKey(Key.F9));
            RegisterHotKey(_windowHandle, IdClearAndSync, ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.F9));

            RegisterHotKey(_windowHandle, IdScaleDown, ModControl, (uint)KeyInterop.VirtualKeyFromKey(Key.OemMinus));
            RegisterHotKey(_windowHandle, IdScaleUp, ModControl, (uint)KeyInterop.VirtualKeyFromKey(Key.OemPlus));
            RegisterHotKey(_windowHandle, IdScaleReset, ModControl, (uint)KeyInterop.VirtualKeyFromKey(Key.D0));

            RegisterHotKey(_windowHandle, IdOpacityDown, ModControl | ModAlt, (uint)KeyInterop.VirtualKeyFromKey(Key.OemMinus));
            RegisterHotKey(_windowHandle, IdOpacityUp, ModControl | ModAlt, (uint)KeyInterop.VirtualKeyFromKey(Key.OemPlus));
            RegisterHotKey(_windowHandle, IdOpacityReset, ModControl | ModAlt, (uint)KeyInterop.VirtualKeyFromKey(Key.D0));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmHotkey)
                return IntPtr.Zero;

            int id = wParam.ToInt32();
            var action = id switch
            {
                IdToggleOverlay => HangarHotkeyAction.ToggleOverlay,
                IdToggleClickThrough => HangarHotkeyAction.ToggleClickThrough,
                IdTemporaryDrag => HangarHotkeyAction.BeginTemporaryDrag,
                IdSetStartNow => HangarHotkeyAction.SetStartNow,
                IdPromptManual => HangarHotkeyAction.PromptManualStart,
                IdForceSync => HangarHotkeyAction.ForceSync,
                IdClearAndSync => HangarHotkeyAction.ClearOverrideAndSync,
                IdScaleDown => HangarHotkeyAction.ScaleDown,
                IdScaleUp => HangarHotkeyAction.ScaleUp,
                IdScaleReset => HangarHotkeyAction.ScaleReset,
                IdOpacityDown => HangarHotkeyAction.OpacityDown,
                IdOpacityUp => HangarHotkeyAction.OpacityUp,
                IdOpacityReset => HangarHotkeyAction.OpacityReset,
                _ => (HangarHotkeyAction?)null
            };

            if (action.HasValue)
            {
                ActionRequested?.Invoke(this, action.Value);
                handled = true;
            }

            return IntPtr.Zero;
        }
    }
}
