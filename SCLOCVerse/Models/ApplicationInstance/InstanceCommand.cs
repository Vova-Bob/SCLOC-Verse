namespace SCLOCVerse.Models.ApplicationInstance
{
    /// <summary>
    /// Усі команди, які може отримати перший процес від повторного запуску.
    /// Зворотна сумісність забезпечується через цей enum: нові команди
    /// додаються без ламання wire-формату IPC.
    /// </summary>
    public enum InstanceCommandKind
    {
        /// <summary>
        /// Показати головне вікно та активувати (стандартний повторний запуск).
        /// </summary>
        Show,

        /// <summary>
        /// Показати головне вікно та перейти на вкладку Assistant (L.I.A).
        /// Викликається при кліку на OS Toast з source=lia (Етап F).
        /// </summary>
        ShowLiaAssistant

        // Майбутні команди (НЕ реалізовуються зараз, але wire-формат готовий):
        // OpenSettings — відкрити канвас налаштувань
        // ApplyUpdate — застосувати завантажене оновлення локалізації
        // InstallLia  — запустити встановлення L.I.A
    }

    /// <summary>
    /// DTO команди IPC: Kind + опціональний Payload.
    /// Серіалізується в JSON (UTF-8) при передаванні через Named Pipe.
    /// </summary>
    public sealed class InstanceCommand
    {
        public InstanceCommandKind Kind { get; init; }

        /// <summary>
        /// Опціональний текстовий payload (шлях до файлу, версія, тощо).
        /// Null для команд без додаткових даних (наприклад, Show).
        /// </summary>
        public string? Payload { get; init; }
    }
}
