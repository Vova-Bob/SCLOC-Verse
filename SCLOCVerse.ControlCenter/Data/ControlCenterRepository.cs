using Npgsql;
using SCLOCVerse.ControlCenter.Models;

namespace SCLOCVerse.ControlCenter.Data;

/// <summary>
/// Усі звернення — лише через control_center.* VIEWs (Стаття 20 — Dashboard Purity).
/// Жодного public.* доступу, жодного агрегатного SQL у C#.
/// </summary>
public sealed class ControlCenterRepository : IControlCenterRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public ControlCenterRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<OverviewData> GetOverviewDataAsync(CancellationToken ct = default)
    {
        var data = new OverviewData();
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        data.ActiveIncidents = await ReadActiveIncidentsAsync(conn, ct);
        data.SystemHealth = await ReadComponentHealthAsync(conn, ct);
        data.LatestReleases = await ReadReleaseHealthAsync(conn, ct);
        data.Statistics = await ReadPlatformStatsAsync(conn, ct);

        return data;
    }

    public async Task<List<ActiveIncident>> GetIncidentsAsync(string? statusFilter = null, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadActiveIncidentsAsync(conn, ct, statusFilter);
    }

    public async Task<IncidentDetail?> GetIncidentDetailAsync(string incidentId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        var incident = await ReadIncidentByIdAsync(conn, incidentId, ct);
        if (incident == null)
            return null;

        if (incident.RootEventId.HasValue)
        {
            incident.RootEvent = await ReadEventSummaryAsync(conn, incident.RootEventId.Value, ct);
            if (incident.RootEvent != null)
                incident.Trace = await ReadTraceAsync(conn, incident.RootEvent.CorrelationId, ct);
        }

        incident.RelatedEvents = await ReadRelatedEventsAsync(conn, incident, ct);
        return incident;
    }

    public async Task<List<ReleaseHealth>> GetReleaseHealthAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("""
            SELECT app_version, succeeded, failed, success_rate,
                   active_installs, users_24h, new_incidents, critical_incidents,
                   first_seen, last_seen,
                   top_fingerprint, top_component, top_signal, top_event_count, top_severity
            FROM control_center.release_health_detail
            """, conn);

        var results = new Dictionary<string, ReleaseHealth>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var version = reader.GetString(0);
            if (!results.TryGetValue(version, out var rh))
            {
                rh = new ReleaseHealth
                {
                    AppVersion = version,
                    Succeeded = reader.GetInt64(1),
                    Failed = reader.GetInt64(2),
                    SuccessRate = (double)reader.GetDecimal(3),
                    ActiveInstalls = reader.GetInt32(4),
                    ActiveUsers24h = reader.GetInt32(5),
                    NewIncidents = reader.GetInt32(6),
                    CriticalIncidents = reader.GetInt32(7),
                    FirstSeen = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    LastSeen = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                };
                // Recommendation
                if (rh.CriticalIncidents > 0 || rh.SuccessRate < 60)
                {
                    rh.Recommendation = "Rollback Recommended";
                    rh.RecommendationIcon = "🔴";
                }
                else if (rh.NewIncidents > 0 || rh.SuccessRate < 95)
                {
                    rh.Recommendation = "Watch";
                    rh.RecommendationIcon = "🟡";
                }
                else
                {
                    rh.Recommendation = "Healthy";
                    rh.RecommendationIcon = "🟢";
                }
                results[version] = rh;
            }

            if (!reader.IsDBNull(10))
            {
                rh.TopFingerprints.Add(new TopFingerprint(
                    reader.GetString(10), reader.GetString(11),
                    reader.GetString(12), reader.GetInt32(13),
                    reader.IsDBNull(14) ? "Warning" : reader.GetString(14)));
            }
        }
        return results.Values.OrderByDescending(r => r.LastSeen ?? DateTime.MinValue).ToList();
    }

    private static async Task<IncidentDetail?> ReadIncidentByIdAsync(NpgsqlConnection conn, string incidentId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT incident_id, id, fingerprint_key, release, component, operation, signal,
                   root_event_id, last_event_id, opened_at, last_event_at, closed_at,
                   status, highest_severity, peak_failure_pct, affected_users, affected_installs, event_count
            FROM control_center.incidents
            WHERE incident_id = $1
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = incidentId });

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return new IncidentDetail
        {
            IncidentId = reader.GetString(0),
            Id = reader.GetInt64(1),
            FingerprintKey = reader.GetString(2),
            Release = reader.GetString(3),
            Component = reader.GetString(4),
            Operation = reader.GetString(5),
            Signal = reader.GetString(6),
            RootEventId = reader.IsDBNull(7) ? null : reader.GetGuid(7),
            OpenedAt = reader.GetDateTime(9),
            LastEventAt = reader.GetDateTime(10),
            ClosedAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
            Status = reader.GetString(12),
            HighestSeverity = reader.GetString(13),
            PeakFailurePct = reader.IsDBNull(14) ? null : reader.GetDecimal(14),
            AffectedUsers = reader.GetInt32(15),
            AffectedInstalls = reader.GetInt32(16),
            EventCount = reader.GetInt64(17),
        };
    }

    private static async Task<TelemetryEventSummary?> ReadEventSummaryAsync(NpgsqlConnection conn, Guid eventId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT id, correlation_id, step, component, operation, outcome,
                   COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') AS signal,
                   occurred_at, error_message, duration_ms, source, http_status, hresult, supabase_code
            FROM control_center.telemetry_events
            WHERE id = $1
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter<Guid> { Value = eventId });

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return new TelemetryEventSummary(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetString(6), reader.GetDateTime(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetInt32(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    private static async Task<List<TraceStep>> ReadTraceAsync(NpgsqlConnection conn, Guid correlationId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT step, component, operation, outcome,
                   COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') AS signal,
                   occurred_at, error_message, duration_ms
            FROM control_center.traces
            WHERE correlation_id = $1
            ORDER BY step
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter<Guid> { Value = correlationId });

        var results = new List<TraceStep>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new TraceStep(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        }
        return results;
    }

    private static async Task<List<TelemetryEventSummary>> ReadRelatedEventsAsync(NpgsqlConnection conn, IncidentDetail incident, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT id, correlation_id, step, component, operation, outcome,
                   COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') AS signal,
                   occurred_at, error_message, duration_ms, source, http_status, hresult, supabase_code
            FROM control_center.telemetry_events
            WHERE component = $1 AND operation = $2 AND app_version = $3
              AND COALESCE(hresult, supabase_code, http_status::text, exception_type, '-') = $4
              AND outcome = 'Failed'
            ORDER BY occurred_at DESC LIMIT 20
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = incident.Component });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = incident.Operation });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = incident.Release });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = incident.Signal });

        var results = new List<TelemetryEventSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new TelemetryEventSummary(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetString(6), reader.GetDateTime(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13)));
        }
        return results;
    }

    private static async Task<List<ActiveIncident>> ReadActiveIncidentsAsync(NpgsqlConnection conn, CancellationToken ct, string? statusFilter = null)
    {
        var where = !string.IsNullOrEmpty(statusFilter) ? "WHERE status = $1" : "WHERE status != 'Closed'";
        await using var cmd = new NpgsqlCommand($$"""
            SELECT incident_id, component, operation, signal, highest_severity,
                   status, event_count, affected_installs, affected_users,
                   opened_at, last_event_at
            FROM control_center.incidents {{where}} ORDER BY opened_at DESC
            """, conn);
        if (!string.IsNullOrEmpty(statusFilter))
            cmd.Parameters.Add(new NpgsqlParameter<string> { Value = statusFilter });

        var results = new List<ActiveIncident>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new ActiveIncident(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetInt64(6),
                reader.GetInt32(7), reader.GetInt32(8),
                reader.GetDateTime(9), reader.GetDateTime(10)));
        }
        return results;
    }

    private static async Task<List<ComponentHealth>> ReadComponentHealthAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT component, health, active_incidents, last_event_at
            FROM control_center.component_health
            """, conn);

        var results = new List<ComponentHealth>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new ComponentHealth(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3)));
        }
        return results;
    }

    private static async Task<List<ReleaseSummary>> ReadReleaseHealthAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT app_version, succeeded, failed, active_installs
            FROM control_center.release_health LIMIT 5
            """, conn);

        var results = new List<ReleaseSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var succeeded = reader.GetInt64(1);
            var failed = reader.GetInt64(2);
            var total = succeeded + failed;
            results.Add(new ReleaseSummary(
                reader.GetString(0),
                succeeded,
                failed,
                total > 0 ? Math.Round(100.0 * succeeded / total, 1) : 0,
                reader.GetInt32(3)));
        }
        return results;
    }

    private static async Task<PlatformStats> ReadPlatformStatsAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT events_24h, active_users_24h, active_installations, open_incidents
            FROM control_center.platform_stats
            """, conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return new PlatformStats(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
        }
        return new PlatformStats(0, 0, 0, 0);
    }
}
