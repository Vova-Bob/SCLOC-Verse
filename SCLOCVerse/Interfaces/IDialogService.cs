using System.Threading.Tasks;
using System.Windows;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Сервіс для показу уніфікованих модальних діалогів.
    /// Безпечний для виклику з будь-якого потоку завдяки внутрішній диспетчеризації.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>
        /// Показує інформаційний діалог.
        /// </summary>
        Task ShowInfoAsync(string message, string? title = null, Window? owner = null);

        /// <summary>
        /// Показує діалог помилки.
        /// </summary>
        Task ShowErrorAsync(string message, string? title = null, Window? owner = null);

        /// <summary>
        /// Показує діалог попередження.
        /// </summary>
        Task ShowWarningAsync(string message, string? title = null, Window? owner = null);

        /// <summary>
        /// Показує діалог підтвердження з кнопками Так/Ні.
        /// </summary>
        Task<bool> ShowConfirmationAsync(string message, string? title = null, Window? owner = null);

        /// <summary>
        /// Показує компактний діалог оновлення з кнопками Встановити/Пізніше.
        /// </summary>
        Task<bool> ShowUpdateDialogAsync(string availableVersion, Window? owner = null);

        /// <summary>
        /// Показує діалог підтвердження з заданими кнопками.
        /// </summary>
        Task<MessageBoxResult> ShowMessageAsync(string message, string? title = null, MessageBoxButton buttons = MessageBoxButton.OK, Window? owner = null);
    }
}
