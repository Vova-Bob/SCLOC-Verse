namespace SCLOCVerse.Notifications;

/// <summary>
/// Контракт провайдера каналу сповіщень (Стаття 27 — Provider Independence).
/// Notification Engine знає лише цей інтерфейс.
/// Discord/Email/Telegram — окремі реалізації, що реєструються у DI.
/// </summary>
public interface INotificationProvider
{
    /// <summary>Назва каналу, наприклад "Discord". Має збігатися з notification_queue.provider.</summary>
    string Name { get; }

    /// <summary>Відправити повідомлення. Ніколи не кидає — повертає NotificationResult.</summary>
    Task<NotificationResult> SendAsync(NotificationPayload payload, CancellationToken ct = default);
}
