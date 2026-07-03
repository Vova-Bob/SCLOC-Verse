using SCLOCVerse.ControlCenter.Models;

namespace SCLOCVerse.ControlCenter.Data;

public interface IControlCenterRepository
{
    Task<OverviewData> GetOverviewDataAsync(CancellationToken ct = default);
    Task<List<ActiveIncident>> GetIncidentsAsync(string? statusFilter = null, CancellationToken ct = default);
    Task<IncidentDetail?> GetIncidentDetailAsync(string incidentId, CancellationToken ct = default);
    Task<List<ReleaseHealth>> GetReleaseHealthAsync(CancellationToken ct = default);
    Task TransitionIncidentAsync(long incidentId, string toStatus, string changedBy, string? note, CancellationToken ct = default);
    Task AddIncidentNoteAsync(long incidentId, string content, string createdBy, CancellationToken ct = default);
    Task AssignOwnerAsync(long incidentId, string owner, CancellationToken ct = default);
    Task<List<IncidentTimelineEntry>> GetIncidentTimelineAsync(long incidentId, CancellationToken ct = default);
    Task<List<IncidentNote>> GetIncidentNotesAsync(long incidentId, CancellationToken ct = default);
}