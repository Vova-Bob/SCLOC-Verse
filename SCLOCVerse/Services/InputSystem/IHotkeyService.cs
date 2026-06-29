using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Центральний сервіс глобальних гарячих клавіш SCLOC-Verse.
    /// Координує бекенд і реєстрації, ізолюючи споживачів від деталей вводу.
    /// </summary>
    public interface IHotkeyService : IDisposable
    {
        /// <summary>
        /// Чи ініціалізований бекенд.
        /// </summary>
        bool IsBackendInitialized { get; }

        /// <summary>
        /// Реєструє гарячу клавішу.
        /// </summary>
        /// <param name="definition">Опис гарячої клавіші.</param>
        /// <param name="policy">Політика розв'язання конфліктів.</param>
        void Register(HotkeyDefinition definition, HotkeyConflictPolicy policy = HotkeyConflictPolicy.Reject);

        /// <summary>
        /// Скасовує реєстрацію гарячої клавіші.
        /// </summary>
        void Unregister(HotkeyId id);

        /// <summary>
        /// Змінює активність гарячої клавіші.
        /// </summary>
        void SetEnabled(HotkeyId id, bool enabled);
        /// <summary>
        /// Ініціалізує бекенд із заданим джерелом віконних повідомлень.
        /// </summary>
        void InitializeBackend(IHotkeyMessageSource messageSource);
    }
}
