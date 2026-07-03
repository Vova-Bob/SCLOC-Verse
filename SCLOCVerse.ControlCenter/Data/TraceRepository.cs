using Npgsql;
using SCLOCVerse.ControlCenter.Models;
using System.Text.Json;

namespace SCLOCVerse.ControlCenter.Data;

public sealed class TraceRepository : ITraceRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public TraceRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<List<TraceEvent>> GetTraceAsync(Guid correlationId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadTraceEventsAsync(conn, "WHERE correlation_id = $1 ORDER BY step", correlationId, ct);
    }

    public async Task<TraceEvent?> GetEventAsync(Guid eventId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var events = await ReadTraceEventsAsync(conn, "WHERE id = $1", eventId, ct);
        return events.FirstOrDefault();
    }

    public async Task<List<TraceSummary>> SearchTracesAsync(TraceSearchFilter filter, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        var conditions = new List<string>();
        var cmd = new NpgsqlCommand();
        int p = 0;

        if (filter.CorrelationId.HasValue)
        {
            conditions.Add($"correlation_id = ${++p}");
            cmd.Parameters.Add(new NpgsqlParameter<Guid> { Value = filter.CorrelationId.Value });
        }
        if (filter.UserId.HasValue)
        {
            conditions.Add($"user_id = ${++p}");
            cmd.Parameters.Add(new NpgsqlParameter<Guid> { Value = filter.UserId.Value });
        }
        if (!string.IsNullOrEmpty(filter.InstallId))
        {
            conditions.Add($"install_id = ${++p}");
            cmd.Parameters.Add(new NpgsqlParameter<string> { Value = filter.InstallId });
        }
        if (!string.IsNullOrEmpty(filter.AppVersion))
        {
            conditions.Add($"app_version = ${++p}");
            cmd.Parameters.Add(new NpgsqlParameter<string> { Value = filter.AppVersion });
        }
        if (filter.DateFrom.HasValue)
        {
            conditions.Add($"occurred_at >= ${++p}");
            cmd.Parameters.Add(new NpgsqlParameter<DateTime> { Value = filter.DateFrom.Value });
        }
        if (filter.DateTo.HasValue)
        {
            conditions.Add($"occurred_at <= ${++p}");
            cmd.Parameters.Add(new NpgsqlParameter<DateTime> { Value = filter.DateTo.Value });
        }

        var where = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : "WHERE occurred_at > now() - interval '24 hours'";

        cmd.CommandText = $"""
            SELECT correlation_id, session_id, install_id, app_version,
                   COUNT(*) AS event_count,
                   MIN(occurred_at) AS first_event,
                   MAX(occurred_at) AS last_event
            FROM control_center.telemetry_events
            {where}
            GROUP BY correlation_id, session_id, install_id, app_version
            ORDER BY last_event DESC
            LIMIT 50
            """;
        cmd.Connection = conn;

        var results = new List<TraceSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new TraceSummary
            {
                CorrelationId = reader.GetGuid(0),
                SessionId = reader.GetGuid(1),
                InstallId = reader.IsDBNull(2) ? null : reader.GetString(2),
                AppVersion = reader.GetString(3),
                EventCount = reader.GetInt32(4),
                FirstEvent = reader.GetDateTime(5),
                LastEvent = reader.GetDateTime(6),
            });
        }
        return results;
    }

    private static async Task<List<TraceEvent>> ReadTraceEventsAsync(
        NpgsqlConnection conn, string whereClause, Guid paramValue, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT id, session_id, correlation_id, step, install_id, user_id,
                   occurred_at, received_at, app_version, telemetry_version, os_version,
                   component, operation, outcome, severity, category,
                   source, http_status, hresult, supabase_code, exception_type,
                   error_message, duration_ms, detail
            FROM control_center.telemetry_events
            {whereClause}
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter<Guid> { Value = paramValue });

        var results = new List<TraceEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var detail = reader.IsDBNull(23) ? null : reader.GetString(23);
            results.Add(new TraceEvent
            {
                Id = reader.GetGuid(0),
                SessionId = reader.GetGuid(1),
                CorrelationId = reader.GetGuid(2),
                Step = reader.GetInt32(3),
                InstallId = reader.IsDBNull(4) ? null : reader.GetString(4),
                UserId = reader.IsDBNull(5) ? null : reader.GetGuid(5),
                OccurredAt = reader.GetDateTime(6),
                ReceivedAt = reader.GetDateTime(7),
                AppVersion = reader.GetString(8),
                TelemetryVersion = reader.GetInt32(9),
                Component = reader.GetString(11),
                Operation = reader.GetString(12),
                Outcome = reader.GetString(13),
                Severity = reader.GetString(14),
                Category = reader.GetString(15),
                Source = reader.IsDBNull(16) ? null : reader.GetString(16),
                HttpStatus = reader.IsDBNull(17) ? null : reader.GetInt32(17),
                Hresult = reader.IsDBNull(18) ? null : reader.GetString(18),
                SupabaseCode = reader.IsDBNull(19) ? null : reader.GetString(19),
                ExceptionType = reader.IsDBNull(20) ? null : reader.GetString(20),
                ErrorMessage = reader.IsDBNull(21) ? null : reader.GetString(21),
                DurationMs = reader.IsDBNull(22) ? null : reader.GetInt32(22),
                DetailJson = detail,
            });
        }
        return results;
    }
}