using SCLOCVerse.Controls;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Mining;
using System.Windows;

namespace SCLOCVerse.Services.Mining.Overlay
{
    /// <summary>
    /// Реалізація <see cref="IMiningOverlayService"/> — керує життєвим циклом
    /// <see cref="MiningOverlayWindow"/> та оновлює вміст з MiningState.
    /// UI-потік: усі операції з вікном виконуються через Application.Current.Dispatcher.
    ///
    /// H1: Реалізує IDisposable для коректного закриття вікна при shutdown.
    /// </summary>
    public sealed class MiningOverlayService : IMiningOverlayService, IDisposable
    {
        private MiningOverlayWindow? _window;
        private bool _disposed;

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

            try
            {
                // Якщо вікно закрите або null — створюємо нове (AutoKey pattern).
                if (_window is null)
                {
                    _window = new MiningOverlayWindow();
                }

                if (!_window.IsVisible)
                {
                    _window.Show();
                }
            }
            catch
            {
                // Вікно могло бути закрите зовні — пересоздаємо.
                _window = new MiningOverlayWindow();
                _window.Show();
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

        /// <summary>
        /// H1: Коректне закриття overlay-вікна при shutdown.
        /// Викликається з AppCompositionRoot.Dispose().
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

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
            catch
            {
                // Вікно може бути вже закрито — ігноруємо.
            }
            _disposed = true;
        }
    }
}