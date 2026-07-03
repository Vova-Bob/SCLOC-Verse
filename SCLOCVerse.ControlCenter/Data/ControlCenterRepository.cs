using Npgsql;
using SCLOCVerse.ControlCenter.Models;
using System.Diagnostics;

namespace SCLOCVerse.ControlCenter.Data;

public sealed class ControlCenterRepository : IControlCenterRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private static readonly string[] KnownComponents = { "Application", "Auth", "Installation", "Updater", "LIA" };

    public ControlCenterRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<OverviewData> GetOverviewDataAsync(CancellationToken ct = default)
    {
        var data = new OverviewData();

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        data.ActiveIncidents = await GetActiveIncidentsAsync(conn, ct);
        data.SystemHealth = await GetComponentHealthAsync(conn, ct);
        data.LatestReleases = await GetLatestReleasesAsync(conn, ct);
        data.Statistics = await GetPlatformStatsAsync(conn, ct);

        return data;
    }

    public async Task<List<ActiveIncident>> GetIncidentsAsync(string? statusFilter = null, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        return await GetActiveIncidentsAsync(conn, ct, statusFilter);
    }

    private static async Task<List<ActiveIncident>> GetActiveIncidentsAsync(NpgsqlConnection conn, CancellationToken ct, string? statusFilter = null)
    {
        var where = statusFilter is { Length: > 0 }
            ? "WHERE status = $1"
            : "WHERE status != 'Closed'";

        await using var cmd = new NpgsqlCommand($$"""
            SELECT incident_id, component, operation, signal, highest_severity,
                   status, event_count, affected_installs, affected_users,
                   opened_at, last_event_at
            FROM control_center.incidents
            {{where}}
            ORDER BY opened_at DESC
            """, conn);

        if (statusFilter is { Length: > 0 })
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

    private static async Task<List<ComponentHealth>> GetComponentHealthAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        // Candidate severity per component (from incident_candidates_live)
        var candidateSeverity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new NpgsqlCommand("""
            SELECT component, MAX(severity) AS sev
            FROM control_center.incident_candidates_live
            GROUP BY component
            """, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var component = reader.GetString(0);
                var sev = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (sev != null)
                    candidateSeverity[component] = sev;
            }
        }

        // Active incidents count per component
        var incidentCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new NpgsqlCommand("""
            SELECT component, COUNT(*)
            FROM control_center.incidents
            WHERE status = 'Active'
            GROUP BY component
            """, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                incidentCounts[reader.GetString(0)] = reader.GetInt32(1);
        }

        // Last event per component (24h)
        var lastEvents = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new NpgsqlCommand("""
            SELECT component, MAX(received_at)
            FROM control_center.telemetry_events
            WHERE received_at > now() - interval '24 hours'
            GROUP BY component
            """, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!reader.IsDBNull(1))
                    lastEvents[reader.GetString(0)] = reader.GetDateTime(1);
            }
        }

        var results = new List<ComponentHealth>();
        foreach (var component in KnownComponents)
        {
            var health = candidateSeverity.GetValueOrDefault(component) switch
            {
                "Critical" => "RED",
                "Warning" => "YELLOW",
                _ => "GREEN"
            };
            results.Add(new ComponentHealth(
                component,
                health,
                incidentCounts.GetValueOrDefault(component),
                lastEvents.TryGetValue(component, out var dt) ? dt : null));
        }
        return results;
    }

    private static async Task<List<ReleaseSummary>> GetLatestReleasesAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT app_version,
                   COUNT(*) FILTER (WHERE outcome = 'Succeeded') AS succeeded,
                   COUNT(*) FILTER (WHERE outcome = 'Failed') AS failed,
                   COUNT(DISTINCT install_id) AS installs
            FROM control_center.telemetry_events
            WHERE received_at > now() - interval '7 days'
              AND app_version IS NOT NULL
            GROUP BY app_version
            ORDER BY app_version DESC
            LIMIT 5
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

    private static async Task<PlatformStats> GetPlatformStatsAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT
              COUNT(*) FILTER (WHERE received_at > now() - interval '24 hours') AS events_24h,
              COUNT(DISTINCT user_id) FILTER (WHERE received_at > now() - interval '24 hours') AS users_24h,
              COUNT(DISTINCT install_id) AS installs,
              (SELECT COUNT(*) FROM control_center.incidents WHERE status != 'Closed') AS open_inc
            FROM control_center.telemetry_events
            """, conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return new PlatformStats(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3));
        }
        return new PlatformStats(0, 0, 0, 0);
    }
}
