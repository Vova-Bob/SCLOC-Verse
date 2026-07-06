using SCLOCVerse.Models.ApplicationUpdate;
using SCLOCVerse.Models.Notifications;
using System;
using System.Collections.Generic;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Маршрутизатор сповіщень: перетворює UpdateCycleResult → NotificationCandidate[],
    /// виконує dedup через IPreferencesService, піднімає event для MainWindow.
    ///
    /// Чистий сервіс — не знає про WPF/Dispatcher. UI-маршалізацію виконує підписник
    /// (MainWindow через Dispatcher.CheckAccess).
    /// </summary>
    public interface INotificationRouter
    {
        /// <summary>
        /// Обробити результат циклу оркестратора: відфільтрувати дублікати,
        /// підняти NotificationsReady з відфільтрованими кандидатами.
        /// </summary>
        void Route(UpdateCycleResult result);

        /// <summary>
        /// Піднімається після Route, якщо є хоч один кандидат (не Silent).
        /// Підписник (MainWindow) виконує InApp/OS маршрутизацію.
        /// </summary>
        event EventHandler<IReadOnlyList<NotificationCandidate>>? NotificationsReady;
    }
}