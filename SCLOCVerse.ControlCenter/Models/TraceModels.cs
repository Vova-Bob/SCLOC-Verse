using System;

namespace SCLOCVerse.ControlCenter.Models;

public sealed class TraceEvent
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid CorrelationId { get; set; }
    public int Step { get; set; }
    public string? InstallId { get; set; }
    public Guid? UserId { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string AppVersion { get; set; } = "";
    public int TelemetryVersion { get; set; }
    public string Component { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Source { get; set; }
    public int? HttpStatus { get; set; }
    public string? Hresult { get; set; }
    public string? SupabaseCode { get; set; }
    public string? ExceptionType { get; set; }
    public string? ErrorMessage { get; set; }
    public int? DurationMs { get; set; }
    public string? DetailJson { get; set; }

    public string Signal => Hresult ?? SupabaseCode ?? HttpStatus?.ToString() ?? ExceptionType ?? "-";

    public string OutcomeIcon => Outcome switch
    {
        "Succeeded" or "Started" => "✔",
        "Failed" => "✖",
        "Cancelled" => "⊘",
        "Skipped" => "⊟",
        _ => "•"
    };

    public string OutcomeColor => Outcome switch
    {
        "Succeeded" or "Started" => "text-success",
        "Failed" => "text-danger",
        "Cancelled" or "Skipped" => "text-muted",
        _ => ""
    };
}

public sealed class TraceSummary
{
    public Guid CorrelationId { get; set; }
    public Guid SessionId { get; set; }
    public string? InstallId { get; set; }
    public string AppVersion { get; set; } = "";
    public int EventCount { get; set; }
    public DateTime FirstEvent { get; set; }
    public DateTime LastEvent { get; set; }
}

public sealed class TraceSearchFilter
{
    public Guid? CorrelationId { get; set; }
    public Guid? UserId { get; set; }
    public string? InstallId { get; set; }
    public string? AppVersion { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}