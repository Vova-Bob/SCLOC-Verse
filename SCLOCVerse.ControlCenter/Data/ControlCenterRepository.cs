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
