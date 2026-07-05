namespace SCLOCVerse.Models.Notifications
{
    /// <summary>
    /// Рівень важливості повідомлення. Впливає на подальшу маршрутизацію
    /// (Етап D/F): Information може пригнічуватись Focus Assist, Error — ні;
    /// Success підлягає dedup, Error — ні тощо. На Етапі C зберігається в DTO,
    /// але візуально поки не диференціюється.
    /// </summary>
    public enum ToastSeverity
    {
        /// <summary>Інформаційне повідомлення (за замовчуванням).</summary>
        Information,

        /// <summary>Успішна операція (оновлено, встановлено).</summary>
        Success,

        /// <summary>Попередження.</summary>
        Warning,

        /// <summary>Помилка.</summary>
        Error
    }

    /// <summary>
    /// Універсальна модель OS-сповіщення для Notification Center.
    /// Незалежна від типу події — нові сценарії (LIA, локалізація, auth тощо)
    /// передають різні значення полів, не вимагаючи зміни інтерфейсу сервісу.
    /// </summary>
    public sealed class ToastNotification
    {
        /// <summary>Заголовок (перший рядок тоста).</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>Текст повідомлення (другий рядок тоста).</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// Тег джерела для dedup/групування (Етап D). Наприклад:
        /// "localization", "lia", "app-update".
        /// </summary>
        public string? SourceTag { get; init; }

        /// <summary>Рівень важливості. За замовчуванням Information.</summary>
        public ToastSeverity Severity { get; init; } = ToastSeverity.Information;
    }
}
