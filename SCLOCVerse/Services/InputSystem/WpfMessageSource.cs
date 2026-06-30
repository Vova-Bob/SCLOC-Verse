using System;
using System.Collections.Generic;
using System.Windows.Interop;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Реалізація джерела віконних повідомлень на основі WPF HwndSource.
    /// </summary>
    public sealed class WpfMessageSource : IHotkeyMessageSource, IDisposable
    {
        private readonly HwndSource _hwndSource;
        private readonly Dictionary<WindowMessageHook, HwndSourceHook> _hookAdapters = new();
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

            // Зберігаємо саме адаптер, що реєструється в HwndSource, щоб пізніше зняти його
            // через RemoveHook. Делегати, створені з різних локальних функцій, не рівні між
            // собою, тож HwndSource.RemoveHook не знайшов би хук за «такою ж» адаптер-функцією.
            if (_hookAdapters.ContainsKey(hook))
                throw new InvalidOperationException("Цей хук вже зареєстровано в джерелі повідомлень.");

            IntPtr Adapter(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
                => hook(hwnd, msg, wParam, lParam, ref handled);

            HwndSourceHook adapter = Adapter;
            _hookAdapters[hook] = adapter;
            _hwndSource.AddHook(adapter);
        }

        /// <inheritdoc/>
        public void RemoveHook(WindowMessageHook hook)
        {
            if (hook == null)
                throw new ArgumentNullException(nameof(hook));

            if (_hookAdapters.TryGetValue(hook, out var adapter))
            {
                _hwndSource.RemoveHook(adapter);
                _hookAdapters.Remove(hook);
            }
        }

        /// <summary>
        /// Звільняє ресурси джерела повідомлень.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Гарантовано знімаємо всі адаптери, навіть якщо хтось забув викликати RemoveHook.
            foreach (var adapter in _hookAdapters.Values)
                _hwndSource.RemoveHook(adapter);

            _hookAdapters.Clear();
            _hwndSource.Dispose();
        }
    }
}
