using System.Text.Json.Serialization;

namespace SCLOCVerse.Notifications;

/// <summary>
/// Вхідний payload для провайдера. Версіонований (Стаття 27 + користувач п.8).
/// Формат 来源: notification_queue.payload jsonb, поле version.
/// </summary>
public sealed class NotificationPayload
{
    /// <summary>Версія формату payload. Збільшується при несумісних змінах схеми.</summary>
    public int Version { get; set; } = 1;

    public long IncidentId { get; set; }

    /// <summary>Код інциденту у вигляді INC-YYYY-NNNNN (для відображення).</summary>
    public string IncidentCode { get; set; } = "";

    public string Component { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Signal { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Release { get; set; } = "";
    public int AffectedInstalls { get; set; }
    public int AffectedUsers { get; set; }
    public double FailurePct { get; set; }

    /// <summary>IncidentCreated | IncidentEscalated | IncidentClosed.</summary>
    public string NotificationType { get; set; } = "";
}
