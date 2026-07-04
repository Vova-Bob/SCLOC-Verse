using System;

namespace SCLOCVerse.ControlCenter.Models;

public sealed class IncidentDetail
{
    public string IncidentId { get; set; } = "";
    public long Id { get; set; }
    public string FingerprintKey { get; set; } = "";
    public string Release { get; set; } = "";
    public string Component { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Signal { get; set; } = "";
    public Guid? RootEventId { get; set; }
    public string Status { get; set; } = "Active";
    public string HighestSeverity { get; set; } = "Warning";
    public decimal? PeakFailurePct { get; set; }
    public int AffectedUsers { get; set; }
    public int AffectedInstalls { get; set; }
    public long EventCount { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime LastEventAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? Owner { get; set; }
    public TelemetryEventSummary? RootEvent { get; set; }
    public List<TraceStep> Trace { get; set; } = new();
    public List<TelemetryEventSummary> RelatedEvents { get; set; } = new();
    public List<IncidentTimelineEntry> Timeline { get; set; } = new();
    public List<IncidentNote> Notes { get; set; } = new();
    public KnownSolution? KnownSolution { get; set; }
}

/// <summary>
/// Підказка "Known Solution" з Knowledge Engine для інциденту (Slice 1).
/// </summary>
public sealed class KnownSolution
{
    public long KnowledgeId { get; set; }
    public string Title { get; set; } = "";
    public string? Symptoms { get; set; }
    public string KnownCause { get; set; } = "";
    public string? Workaround { get; set; }
    public string? PermanentFix { get; set; }
    public string? FixedVersion { get; set; }
    public string Confidence { get; set; } = "";
    public List<KnownSolutionReference> References { get; set; } = new();
}

public sealed class KnownSolutionReference
{
    public string Type { get; set; } = "";
    public string? Url { get; set; }
    public string? Label { get; set; }
}

public sealed record TelemetryEventSummary(
    Guid Id, Guid CorrelationId, int Step,
    string Component, string Operation, string Outcome,
    string? Signal, DateTime OccurredAt,
    string? ErrorMessage, int? DurationMs,
    string? Source, int? HttpStatus, string? Hresult, string? SupabaseCode);

public sealed record TraceStep(
    int Step, string Component, string Operation, string Outcome,
    string? Signal, DateTime OccurredAt,
    string? ErrorMessage, int? DurationMs);