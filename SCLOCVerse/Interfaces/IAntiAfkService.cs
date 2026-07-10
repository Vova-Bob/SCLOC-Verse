using System;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Координатор модуля Anti-AFK.
    /// Виявляє бездіяльність користувача через GetLastInputInfo та імітує
    /// рух миші (SendInput ±1px) для запобігання AFK-статусу.
    /// Керує індикатором-оверлеєм (пульсуюча точка).
    /// </summary>
    public interface IAntiAfkService : IDisposable
    {
        /// <summary>Чи увімкнено Anti-AFK зараз.</summary>
        bool IsRunning { get; }

        /// <summary>Сповіщає при зміні стану (true — увімкнено, false — вимкнено).</summary>
        event EventHandler<bool>? StateChanged;

        /// <summary>
        /// Перемкнути Anti-AFK (увімкнути/вимкнути).
        /// Зберігає стан у IPreferencesService.
        /// </summary>
        void Toggle();

        /// <summary>
        /// Перечитати налаштування індикатора з IPreferencesService та застосувати
        /// до видимого індикатора (Live Preview). Викликається Settings Hub
        /// після зміни будь-якого налаштування індикатора.
        /// </summary>
        void ApplyIndicatorSettings();
    }
}
