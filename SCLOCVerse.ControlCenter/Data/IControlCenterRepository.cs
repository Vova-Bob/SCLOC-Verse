using SCLOCVerse.ControlCenter.Models;

namespace SCLOCVerse.ControlCenter.Data;

public interface IControlCenterRepository
{
    Task<OverviewData> GetOverviewDataAsync(CancellationToken ct = default);
    Task<List<ActiveIncident>> GetIncidentsAsync(string? statusFilter = null, CancellationToken ct = default);
}
