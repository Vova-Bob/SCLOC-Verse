using System;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Бекенд гарячих клавіш на основі Raw Input.
    /// Фаза 1: заготовка. Повноцінна реалізація у Фазі 2.
    /// </summary>
    public sealed class RawInputBackend : IHotkeyBackend
    {
        /// <inheritdoc/>
        public bool IsInitialized => false;

        /// <inheritdoc/>
        public event EventHandler<HotkeyGesture>? GestureDetected;

        /// <inheritdoc/>
        public void Initialize(IHotkeyMessageSource messageSource)
        {
            // Заготовка для Фази 2.
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Заготовка для Фази 2.
        }
    }
}
