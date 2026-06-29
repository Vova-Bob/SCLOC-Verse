using System;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Бекенд виявлення жестів глобальних гарячих клавіш.
    /// Конкретна реалізація може використовувати RegisterHotKey, Raw Input або інший механізм.
    /// </summary>
    public interface IHotkeyBackend : IDisposable
    {
        /// <summary>
        /// Чи ініціалізований бекенд.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Ініціалізує бекенд із заданим джерелом віконних повідомлень.
        /// </summary>
        void Initialize(IHotkeyMessageSource messageSource);

        /// <summary>
        /// Подія, яка виникає коли бекенд виявив жест гарячої клавіші.
        /// </summary>
        event EventHandler<HotkeyGesture>? GestureDetected;

        /// <summary>
        /// Подія, яка виникає коли клавіша, що утворює жест, була відпущена.
        /// Може не генеруватися бекендами, які не підтримують відстеження key-up.
        /// </summary>
        event EventHandler<HotkeyGesture>? KeyUp;
    }
}
