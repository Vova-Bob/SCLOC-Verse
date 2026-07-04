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

    /// <summary>
    /// Підказка Similar Solution (Priority 2) — component+signal match, не exact fingerprint.
    /// </summary>
    public KnownSolution? SimilarSolution { get; set; }

    /// <summary>
    /// Якщо для fingerprint існує Knowledge Entry, але вона ще не Verified (Draft/Reviewed),
    /// тут зберігається її статус — для UI-підказки "Knowledge Draft exists".
    /// </summary>
    public string? DraftKnowledgeStatus { get; set; }
    public long? DraftKnowledgeId { get; set; }
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
    public string Status { get; set; } = "";
    public string? AffectedVersions { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Priority { get; set; }
    public List<KnownSolutionReference> References { get; set; } = new();
}

public sealed class KnownSolutionReference
{
    public long ReferenceId { get; set; }
    public string Type { get; set; } = "";
    public string? Url { get; set; }
    public string? Label { get; set; }
}

/// <summary>
/// Вхідні дані для створення Draft Knowledge Entry з інциденту (Slice 2).
/// </summary>
public sealed class KnowledgeDraftInput
{
    public string Title { get; set; } = "";
    public string KnownCause { get; set; } = "";
    public string? Workaround { get; set; }
}

/// <summary>
/// Результат створення Knowledge: ID та ознака, чи створено новий запис.
/// </summary>
public sealed class KnowledgeCreateResult
{
    public long KnowledgeId { get; set; }
    public bool CreatedNew { get; set; }
}

/// <summary>
/// Вхідні дані для редагування Knowledge Entry (Slice 3).
/// </summary>
public sealed class KnowledgeEditInput
{
    public int ExpectedVersion { get; set; }
    public string Title { get; set; } = "";
    public string? Symptoms { get; set; }
    public string KnownCause { get; set; } = "";
    public string? Workaround { get; set; }
    public string? PermanentFix { get; set; }
    public string? AffectedVersions { get; set; }
    public string? FixedVersion { get; set; }
    public string ChangeReason { get; set; } = "";
}

/// <summary>
/// Вхідні дані для додавання reference до Knowledge Entry (Slice 3).
/// </summary>
public sealed class KnowledgeReferenceInput
{
    public string Type { get; set; } = "GitHubIssue";
    public string Url { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>
/// Запис історії версій Knowledge Entry (Slice 3).
/// </summary>
public sealed class KnowledgeHistoryEntry
{
    public int Version { get; set; }
    public string? ChangedBy { get; set; }
    public string? DbUser { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? ChangeReason { get; set; }
    public string ChangeType { get; set; } = "Updated";
}

/// <summary>
/// Метрика покриття знань (Slice 5). З materialized view control_center.knowledge_coverage.
/// </summary>
public sealed class KnowledgeCoverage
{
    public int TotalFingerprints { get; set; }
    public int CoveredFingerprints { get; set; }
    public int UncoveredFingerprints { get; set; }
    public decimal CoveragePct { get; set; }
}

/// <summary>
/// Результат ручного пошуку Knowledge (Slice 5).
/// </summary>
public sealed class KnowledgeSearchResult
{
    public long KnowledgeId { get; set; }
    public string Title { get; set; } = "";
    public string Component { get; set; } = "";
    public string Signal { get; set; } = "";
    public string Status { get; set; } = "";
    public string Confidence { get; set; } = "";
    public string? FixedVersion { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long TotalCount { get; set; }
}

/// <summary>
/// Запис зі списку топ непокритих component+signal (Slice 5).
/// </summary>
public sealed class MissingKnowledgeEntry
{
    public string Component { get; set; } = "";
    public string Signal { get; set; } = "";
    public int FingerprintCount { get; set; }
    public long TotalEvents { get; set; }
    public DateTime LastSeen { get; set; }
    public string HighestSeverity { get; set; } = "";
}

/// <summary>
/// Кандидат на авто-верифікацію (Slice 5). Повертається verify_knowledge_auto().
/// </summary>
public sealed class KnowledgeAutoVerifyResult
{
    public long KnowledgeId { get; set; }
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
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