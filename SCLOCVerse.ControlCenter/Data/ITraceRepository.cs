using SCLOCVerse.ControlCenter.Models;

namespace SCLOCVerse.ControlCenter.Data;

public interface ITraceRepository
{
    Task<List<TraceEvent>> GetTraceAsync(Guid correlationId, CancellationToken ct = default);
    Task<TraceEvent?> GetEventAsync(Guid eventId, CancellationToken ct = default);
    Task<List<TraceSummary>> SearchTracesAsync(TraceSearchFilter filter, CancellationToken ct = default);
}