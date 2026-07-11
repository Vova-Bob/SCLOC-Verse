using SCLOCVerse.Models.AutoKey;
using System;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Координатор модуля Auto Key.
    /// Автоматично натискає задану клавішу (Action Key) через заданий інтервал,
    /// ЛИШЕ коли активне вікно (foreground) належить процесу Star Citizen.
    ///
    /// Перевірка виконується stateless кожен цикл: ForegroundWindow → PID →
    /// ProcessName. Жодного кешу PID, жодної логіки відновлення — сервіс завжди
    /// бачить поточний стан системи (переживає краш/перезапуск/Alt+Tab гри).
    ///
    /// Повністю незалежний від Anti-AFK та Hangar Timer.
    /// </summary>
    public interface IAutoKeyService : IDisposable
    {
        /// <summary>Чи увімкнено Auto Key.</summary>
        bool IsEnabled { get; }

        /// <summary>Поточний стан (Off/Running/Paused).</summary>
        AutoKeyState State { get; }

        /// <summary>Сповіщає при зміні стану (може надходити не з UI-потоку).</summary>
        event EventHandler<AutoKeyState>? StateChanged;

        /// <summary>
        /// Перемкнути Auto Key (OFF → ON / ON → OFF). Зберігає стан у IPreferencesService.
        /// </summary>
        void Toggle();

        /// <summary>
        /// Перечитати налаштування (інтервал, Action Key) з IPreferencesService та
        /// застосувати (Live Preview). Викликається Settings Hub після зміни.
        /// </summary>
        void ApplySettings();
    }
}
