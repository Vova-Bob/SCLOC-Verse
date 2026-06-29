using System;
using System.Windows.Interop;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Реалізація джерела віконних повідомлень на основі WPF HwndSource.
    /// </summary>
    public sealed class WpfMessageSource : IHotkeyMessageSource, IDisposable
    {
        private readonly HwndSource _hwndSource;
        private bool _disposed;

        /// <summary>
        /// Створює джерело повідомлень для заданого WPF-вікна.
        /// </summary>
        public WpfMessageSource(System.Windows.Window window)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            var helper = new WindowInteropHelper(window);
            _hwndSource = HwndSource.FromHwnd(helper.EnsureHandle())
                ?? throw new InvalidOperationException("Не вдалося отримати HwndSource вікна.");
        }

        /// <inheritdoc/>
        public IntPtr Handle => _hwndSource.Handle;

        /// <inheritdoc/>
        public void AddHook(WindowMessageHook hook)
        {
            if (hook == null)
                throw new ArgumentNullException(nameof(hook));

            IntPtr Adapter(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
                => hook(hwnd, msg, wParam, lParam, ref handled);

            _hwndSource.AddHook(Adapter);
        }

        /// <inheritdoc/>
        public void RemoveHook(WindowMessageHook hook)
        {
            if (hook == null)
                throw new ArgumentNullException(nameof(hook));

            IntPtr Adapter(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
                => hook(hwnd, msg, wParam, lParam, ref handled);

            _hwndSource.RemoveHook(Adapter);
        }

        /// <summary>
        /// Звільняє ресурси джерела повідомлень.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _hwndSource.Dispose();
        }
    }
}
