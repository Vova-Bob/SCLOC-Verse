using SCLOCVerse.Models.Notifications;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Сервіс OS-сповіщень Windows (Notification Center). Окремий від in-app
    /// IToastService — не змішуємо дві системи (ADR зауваження #4).
    ///
    /// Універсальний метод Show(ToastNotification) дозволяє додавати нові типи
    /// повідомлень без зміни контракту — лише інша DTO (ADR зауваження користувача).
    ///
    /// Notification Routing Policy (видиме вікно → InApp, приховане → OS Toast)
    /// реалізується на Етапах E/F через єдиний NotificationRouter.
    /// </summary>
    public interface IToastNotificationService
    {
        /// <summary>
        /// Показати OS-сповіщення в Notification Center.
        /// </summary>
        /// <param name="notification">Універсальна модель повідомлення.</param>
        void Show(ToastNotification notification);
    }
}
