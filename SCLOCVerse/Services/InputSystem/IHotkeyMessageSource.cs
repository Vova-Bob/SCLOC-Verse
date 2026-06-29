using System;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Джерело віконних повідомлень для бекенду гарячих клавіш.
    /// Ізолює бекенд від конкретної UI-технології (WPF, WinUI, Avalonia).
    /// </summary>
    public interface IHotkeyMessageSource
    {
        /// <summary>
        /// Хендл вікна, яке отримує повідомлення.
        /// </summary>
        IntPtr Handle { get; }

        /// <summary>
        /// Додає хук віконної процедури.
        /// </summary>
        void AddHook(WindowMessageHook hook);

        /// <summary>
        /// Видаляє хук віконної процедури.
        /// </summary>
        void RemoveHook(WindowMessageHook hook);
    }
}
