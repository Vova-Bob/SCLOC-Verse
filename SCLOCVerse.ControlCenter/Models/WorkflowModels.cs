using System;

namespace SCLOCVerse.ControlCenter.Models;

public sealed record IncidentTimelineEntry(
    long Id, long IncidentId, string FromStatus, string ToStatus,
    string ChangedBy, DateTime ChangedAt, string? Note);

public sealed record IncidentNote(
    long Id, long IncidentId, string Content, string CreatedBy, DateTime CreatedAt);