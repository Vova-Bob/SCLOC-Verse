using System.Net.Http.Json;

namespace SCLOCVerse.ControlCenter.Notifications;

public interface INotificationProvider
{
    string Name { get; }
    Task<bool> SendAsync(NotificationPayload payload, CancellationToken ct = default);
}

public sealed class NotificationPayload
{
    public long IncidentId { get; set; }
    public string IncidentCode { get; set; } = "";
    public string Component { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Signal { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Release { get; set; } = "";
    public int AffectedInstalls { get; set; }
    public int AffectedUsers { get; set; }
    public double FailurePct { get; set; }
    public string NotificationType { get; set; } = "";
}