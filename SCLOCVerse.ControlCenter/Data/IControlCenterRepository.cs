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
    Task<KnownSolution?> GetKnownSolutionAsync(long incidentId, CancellationToken ct = default);
    Task<KnowledgeCreateResult> CreateKnowledgeFromIncidentAsync(long incidentId, KnowledgeDraftInput input, string createdBy, CancellationToken ct = default);
    Task<bool> CanCreateKnowledgeForIncidentAsync(long incidentId, CancellationToken ct = default);
    Task UpdateKnowledgeAsync(long knowledgeId, KnowledgeEditInput input, string changedBy, CancellationToken ct = default);
    Task TransitionKnowledgeAsync(long knowledgeId, string targetStatus, string reason, int expectedVersion, string changedBy, CancellationToken ct = default);
    Task AddKnowledgeReferenceAsync(long knowledgeId, KnowledgeReferenceInput input, string addedBy, CancellationToken ct = default);
    Task RemoveKnowledgeReferenceAsync(long referenceId, string removedBy, CancellationToken ct = default);
    Task<List<KnowledgeHistoryEntry>> GetKnowledgeHistoryAsync(long knowledgeId, CancellationToken ct = default);
    Task<int> GetKnowledgeCurrentVersionAsync(long knowledgeId, CancellationToken ct = default);
    Task<bool> CanEditKnowledgeAsync(long knowledgeId, CancellationToken ct = default);
    Task<KnownSolution?> MatchKnowledgePriority2Async(long incidentId, CancellationToken ct = default);
}
