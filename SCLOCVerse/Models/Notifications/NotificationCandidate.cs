using SCLOCVerse.Models.Notifications;

namespace SCLOCVerse.Models.Notifications
{
    /// <summary>
    /// Джерело сповіщення — тип події оновлення.
    /// Замість string-значення ("app"/"application" тощо) — типізований enum.
    /// </summary>
    public enum NotificationSource
    {
        Application,
        Localization,
        Lia
    }

    /// <summary>
    /// Політика доставки сповіщення.
    /// Auto — MainWindow сам вирішує за видимістю вікна (InApp або OS).
    /// InApp/OS — явне призначення каналу (майбутнє: критична помилка → InApp завжди).
    /// Silent — пригнічення (майбутнє: Focus Assist, нічний режим).
    /// </summary>
    public enum NotificationPolicy
    {
        Auto,
        InApp,
        OS,
        Silent
    }

    /// <summary>
    /// Кандидат на сповіщення, побудований NotificationRouter з UpdateCycleResult.
    /// MainWindow виконує фінальну маршрутизацію (InApp vs OS) за NotificationPolicy
    /// та власною видимістю.
    /// </summary>
    public sealed record NotificationCandidate(
        NotificationSource Source,
        string Environment,
        string Version,
        string Message,
        ToastSeverity Severity,
        NotificationPolicy Policy);
}