using System;

namespace SCLOCVerse.ControlCenter.Models;

public sealed record ActiveIncident(
    string IncidentId,
    string Component,
    string Operation,
    string Signal,
    string HighestSeverity,
    string Status,
    long EventCount,
    int AffectedInstalls,
    int AffectedUsers,
    DateTime OpenedAt,
    DateTime LastEventAt);

public sealed record ComponentHealth(
    string Component,
    string Health,
    int ActiveIncidents,
    DateTime? LastEventAt);

public sealed record ReleaseSummary(
    string AppVersion,
    long Succeeded,
    long Failed,
    double SuccessRate,
    int ActiveInstalls);

public sealed record PlatformStats(
    long Events24h,
    int ActiveUsers24h,
    int ActiveInstallations,
    int OpenIncidents);

public sealed class OverviewData
{
    public List<ActiveIncident> ActiveIncidents { get; set; } = new();
    public List<ComponentHealth> SystemHealth { get; set; } = new();
    public List<ReleaseSummary> LatestReleases { get; set; } = new();
    public PlatformStats? Statistics { get; set; }
}
