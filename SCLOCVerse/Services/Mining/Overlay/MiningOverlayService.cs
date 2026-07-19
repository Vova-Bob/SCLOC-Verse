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
    /// </summary>
    public sealed class MiningOverlayService : IMiningOverlayService
    {
        private MiningOverlayWindow? _window;

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

            if (_window is null || !_window.IsVisible)
            {
                _window ??= new MiningOverlayWindow();
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
    }
}