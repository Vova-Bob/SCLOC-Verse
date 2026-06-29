using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Опис глобальної гарячої клавіші, включаючи її поточний та типовий жест,
    /// обробник, пріоритет і метадані для UI.
    /// </summary>
    public sealed class HotkeyDefinition
    {
        /// <summary>
        /// Унікальний ідентифікатор гарячої клавіші.
        /// </summary>
        public required HotkeyId Id { get; init; }

        /// <summary>
        /// Типовий жест гарячої клавіші, встановлений розробником.
        /// </summary>
        public required HotkeyGesture DefaultGesture { get; init; }

        /// <summary>
        /// Обробник, який виконується при спрацюванні гарячої клавіші.
        /// </summary>
        public required Func<CancellationToken, ValueTask> Handler { get; init; }

        /// <summary>
        /// Поточний жест, який може бути перевизначений користувачем.
        /// Якщо null, використовується <see cref="DefaultGesture"/>.
        /// </summary>
        public HotkeyGesture? CurrentGesture { get; set; }

        /// <summary>
        /// Чи активна гаряча клавіша.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Пріоритет обробки при конфлікті комбінацій.
        /// Більше значення — вищий пріоритет.
        /// </summary>
        public int Priority { get; init; }

        /// <summary>
        /// Опис гарячої клавіші для відображення в UI.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Чи відображати гарячу клавішу в UI налаштувань.
        /// </summary>
        public bool VisibleInUi { get; init; } = true;

        /// <summary>
        /// Повертає активний жест гарячої клавіші.
        /// </summary>
        public HotkeyGesture EffectiveGesture => CurrentGesture ?? DefaultGesture;
    }
}
