using System;

namespace SCLOCVerse.ControlCenter.Models;

public sealed class ReleaseHealth
{
    public string AppVersion { get; set; } = "";
    public long Succeeded { get; set; }
    public long Failed { get; set; }
    public double SuccessRate { get; set; }
    public int ActiveInstalls { get; set; }
    public int ActiveUsers24h { get; set; }
    public int NewIncidents { get; set; }
    public int CriticalIncidents { get; set; }
    public DateTime? FirstSeen { get; set; }
    public DateTime? LastSeen { get; set; }
    public string Recommendation { get; set; } = "Healthy";
    public string RecommendationIcon { get; set; } = "🟢";
    public List<TopFingerprint> TopFingerprints { get; set; } = new();
}

public sealed record TopFingerprint(
    string Fingerprint, string Component, string Signal,
    int EventCount, string Severity);