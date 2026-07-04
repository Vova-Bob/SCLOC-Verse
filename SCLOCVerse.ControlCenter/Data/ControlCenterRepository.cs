using Npgsql;
using SCLOCVerse.ControlCenter.Models;

namespace SCLOCVerse.ControlCenter.Data;

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
        data.ActiveIncidents = await QueryIncidentsAsync(conn, "WHERE status != 'Closed'", null, ct);
        data.SystemHealth = await QueryComponentHealthAsync(conn, ct);
        data.LatestReleases = await QueryReleaseHealthAsync(conn, ct);
        data.Statistics = await QueryPlatformStatsAsync(conn, ct);
        return data;
    }

    public async Task<List<ActiveIncident>> GetIncidentsAsync(string? statusFilter = null, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var (where, param) = !string.IsNullOrEmpty(statusFilter)
            ? ("WHERE status = $1", (object?)statusFilter)
            : ("WHERE status != 'Closed'", null);
        return await QueryIncidentsAsync(conn, where, param, ct);
    }

    public async Task<IncidentDetail?> GetIncidentDetailAsync(string incidentId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var incident = await QueryIncidentByIdAsync(conn, incidentId, ct);
        if (incident == null) return null;
        if (incident.RootEventId.HasValue)
        {
            incident.RootEvent = await QueryEventSummaryAsync(conn, incident.RootEventId.Value, ct);
            if (incident.RootEvent != null)
                incident.Trace = await QueryTraceAsync(conn, incident.RootEvent.CorrelationId, ct);
        }
        incident.RelatedEvents = await QueryRelatedEventsAsync(conn, incident, ct);
        incident.Timeline = await ReadTimelineAsync(conn, incident.Id, ct);
        incident.Notes = await ReadNotesAsync(conn, incident.Id, ct);
        incident.KnownSolution = await GetKnownSolutionAsync(conn, incident.Id, ct);
        if (incident.KnownSolution != null)
        {
            incident.KnownSolution.Priority = 1;
        }
        else
        {
            incident.SimilarSolution = await MatchKnowledgePriority2Async(conn, incident.Id, ct);
            if (incident.SimilarSolution == null)
            {
                var draft = await QueryDraftKnowledgeAsync(conn, incident.FingerprintKey, ct);
                incident.DraftKnowledgeId = draft?.KnowledgeId;
                incident.DraftKnowledgeStatus = draft?.Status;
            }
        }
        return incident;
    }

    public async Task<KnownSolution?> GetKnownSolutionAsync(long incidentId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        return await GetKnownSolutionAsync(conn, incidentId, ct);
    }

    public async Task<KnowledgeCreateResult> CreateKnowledgeFromIncidentAsync(long incidentId, KnowledgeDraftInput input, string createdBy, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT knowledge_id, created_new FROM public.create_knowledge_from_incident($1,$2,$3,$4,$5)", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = incidentId });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = input.Title });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = input.KnownCause });
        cmd.Parameters.Add(new NpgsqlParameter<string?> { Value = input.Workaround });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = createdBy });
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException("Knowledge creation did not return a result.");

        return new KnowledgeCreateResult
        {
            KnowledgeId = reader.GetInt64(0),
            CreatedNew = reader.GetBoolean(1)
        };
    }

    public async Task<bool> CanCreateKnowledgeForIncidentAsync(long incidentId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT public.can_create_knowledge_for_incident($1)", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = incidentId });
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is true;
    }

    public async Task UpdateKnowledgeAsync(long knowledgeId, KnowledgeEditInput input, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT public.update_knowledge_entry($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = knowledgeId });
        cmd.Parameters.Add(new NpgsqlParameter<int> { Value = input.ExpectedVersion });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = input.Title });
        cmd.Parameters.Add(new NpgsqlParameter<string?> { Value = input.Symptoms });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = input.KnownCause });
        cmd.Parameters.Add(new NpgsqlParameter<string?> { Value = input.Workaround });
        cmd.Parameters.Add(new NpgsqlParameter<string?> { Value = input.PermanentFix });
        cmd.Parameters.Add(new NpgsqlParameter<string?> { Value = input.AffectedVersions });
        cmd.Parameters.Add(new NpgsqlParameter<string?> { Value = input.FixedVersion });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = changedBy });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = input.ChangeReason });
        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.Message.Contains("concurrently"))
        {
            throw new InvalidOperationException("Knowledge entry was modified by another user. Please reload and try again.", ex);
        }
    }

    public async Task TransitionKnowledgeAsync(long knowledgeId, string targetStatus, string reason, int expectedVersion, string changedBy, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT public.transition_knowledge($1,$2,$3,$4,$5)", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = knowledgeId });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = targetStatus });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = reason });
        cmd.Parameters.Add(new NpgsqlParameter<int> { Value = expectedVersion });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = changedBy });
        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.Message.Contains("concurrently"))
        {
            throw new InvalidOperationException("Knowledge entry was modified by another user. Please reload and try again.", ex);
        }
    }

    public async Task AddKnowledgeReferenceAsync(long knowledgeId, KnowledgeReferenceInput input, string addedBy, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT public.add_knowledge_reference($1,$2,$3,$4,$5)", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = knowledgeId });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = input.Type });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = input.Url });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = input.Label });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = addedBy });
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveKnowledgeReferenceAsync(long referenceId, string removedBy, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT public.remove_knowledge_reference($1,$2)", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = referenceId });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = removedBy });
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<KnowledgeHistoryEntry>> GetKnowledgeHistoryAsync(long knowledgeId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT version, changed_by, db_user, changed_at, change_reason, change_type FROM public.get_knowledge_history($1)", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = knowledgeId });
        var results = new List<KnowledgeHistoryEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new KnowledgeHistoryEntry
            {
                Version = reader.GetInt32(0),
                ChangedBy = reader.IsDBNull(1) ? null : reader.GetString(1),
                DbUser = reader.IsDBNull(2) ? null : reader.GetString(2),
                ChangedAt = reader.GetDateTime(3),
                ChangeReason = reader.IsDBNull(4) ? null : reader.GetString(4),
                ChangeType = reader.IsDBNull(5) ? "Updated" : reader.GetString(5)
            });
        }
        return results;
    }

    public async Task<int> GetKnowledgeCurrentVersionAsync(long knowledgeId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT public.get_knowledge_current_version($1)", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = knowledgeId });
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is int v ? v : 0;
    }

    public async Task<bool> CanEditKnowledgeAsync(long knowledgeId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT public.can_edit_knowledge($1)", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = knowledgeId });
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is true;
    }

    // Внутрішній overload — використовується в GetIncidentDetailAsync, щоб не відкривати друге підключення.
    private static async Task<KnownSolution?> MatchKnowledgePriority2Async(NpgsqlConnection conn, long incidentId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT d.id, d.title, d.symptoms, d.known_cause, d.workaround, d.permanent_fix,
                   d.fixed_version, d.confidence, d.status,
                   COALESCE(array_to_string(d.affected_versions, ','), '') AS affected_versions,
                   d.updated_at, d.references
            FROM public.match_knowledge_priority2($1) m
            JOIN control_center.knowledge_entry_detail d ON d.id = m.knowledge_id
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = incidentId });
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;

        var refsJson = reader.IsDBNull(11) ? null : reader.GetString(11);
        var refs = string.IsNullOrWhiteSpace(refsJson)
            ? new List<KnownSolutionReference>()
            : System.Text.Json.JsonSerializer.Deserialize<List<KnownSolutionReference>>(refsJson);

        return new KnownSolution
        {
            KnowledgeId = reader.GetInt64(0),
            Title = reader.GetString(1),
            Symptoms = reader.IsDBNull(2) ? null : reader.GetString(2),
            KnownCause = reader.GetString(3),
            Workaround = reader.IsDBNull(4) ? null : reader.GetString(4),
            PermanentFix = reader.IsDBNull(5) ? null : reader.GetString(5),
            FixedVersion = reader.IsDBNull(6) ? null : reader.GetString(6),
            Confidence = reader.GetString(7),
            Status = reader.GetString(8),
            AffectedVersions = reader.IsDBNull(9) ? null : reader.GetString(9),
            UpdatedAt = reader.GetDateTime(10),
            Priority = 2,
            References = refs ?? new List<KnownSolutionReference>()
        };
    }

    public async Task<KnownSolution?> MatchKnowledgePriority2Async(long incidentId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        return await MatchKnowledgePriority2Async(conn, incidentId, ct);
    }

    // Внутрішній overload — використовується в GetIncidentDetailAsync, щоб не відкривати друге підключення.
    private static async Task<KnownSolution?> GetKnownSolutionAsync(NpgsqlConnection conn, long incidentId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT d.id, d.title, d.symptoms, d.known_cause, d.workaround, d.permanent_fix,
                   d.fixed_version, d.confidence, d.status,
                   COALESCE(array_to_string(d.affected_versions, ','), '') AS affected_versions,
                   d.updated_at, d.references
            FROM public.match_knowledge_for_incident($1) m
            JOIN control_center.knowledge_entry_detail d ON d.id = m.knowledge_id
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = incidentId });
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;

        var refsJson = reader.IsDBNull(11) ? null : reader.GetString(11);
        var refs = string.IsNullOrWhiteSpace(refsJson)
            ? new List<KnownSolutionReference>()
            : System.Text.Json.JsonSerializer.Deserialize<List<KnownSolutionReference>>(refsJson);

        return new KnownSolution
        {
            KnowledgeId = reader.GetInt64(0),
            Title = reader.GetString(1),
            Symptoms = reader.IsDBNull(2) ? null : reader.GetString(2),
            KnownCause = reader.GetString(3),
            Workaround = reader.IsDBNull(4) ? null : reader.GetString(4),
            PermanentFix = reader.IsDBNull(5) ? null : reader.GetString(5),
            FixedVersion = reader.IsDBNull(6) ? null : reader.GetString(6),
            Confidence = reader.GetString(7),
            Status = reader.GetString(8),
            AffectedVersions = reader.IsDBNull(9) ? null : reader.GetString(9),
            UpdatedAt = reader.GetDateTime(10),
            Priority = 1,
            References = refs ?? new List<KnownSolutionReference>()
        };
    }

    public async Task<List<ReleaseHealth>> GetReleaseHealthAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("""
            SELECT app_version, succeeded, failed, success_rate, active_installs, users_24h,
                   new_incidents, critical_incidents, first_seen, last_seen,
                   top_fingerprint, top_component, top_signal, top_event_count, top_severity
            FROM control_center.release_health_detail
            """, conn);
        var results = new Dictionary<string, ReleaseHealth>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var ver = reader.GetString(0);
            if (!results.TryGetValue(ver, out var rh))
            {
                rh = new ReleaseHealth
                {
                    AppVersion = ver, Succeeded = reader.GetInt64(1), Failed = reader.GetInt64(2),
                    SuccessRate = (double)reader.GetDecimal(3), ActiveInstalls = reader.GetInt32(4),
                    ActiveUsers24h = reader.GetInt32(5), NewIncidents = reader.GetInt32(6),
                    CriticalIncidents = reader.GetInt32(7),
                    FirstSeen = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    LastSeen = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                };
                rh.Recommendation = rh.CriticalIncidents > 0 || rh.SuccessRate < 60 ? "Rollback Recommended"
                    : rh.NewIncidents > 0 || rh.SuccessRate < 95 ? "Watch" : "Healthy";
                rh.RecommendationIcon = rh.Recommendation == "Rollback Recommended" ? "🔴"
                    : rh.Recommendation == "Watch" ? "🟡" : "🟢";
                results[ver] = rh;
            }
            if (!reader.IsDBNull(10))
                rh.TopFingerprints.Add(new TopFingerprint(reader.GetString(10), reader.GetString(11),
                    reader.GetString(12), reader.GetInt32(13), reader.IsDBNull(14) ? "Warning" : reader.GetString(14)));
        }
        return results.Values.OrderByDescending(r => r.LastSeen ?? DateTime.MinValue).ToList();
    }

    public async Task TransitionIncidentAsync(long incidentId, string toStatus, string changedBy, string? note, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT transition_incident($1,$2,$3,$4)", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = incidentId });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = toStatus });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = changedBy });
        cmd.Parameters.Add(new NpgsqlParameter<string?> { Value = note });
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task AddIncidentNoteAsync(long incidentId, string content, string createdBy, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT add_incident_note($1,$2,$3)", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = incidentId });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = content });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = createdBy });
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task AssignOwnerAsync(long incidentId, string owner, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT assign_incident_owner($1,$2)", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = incidentId });
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = owner });
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<IncidentTimelineEntry>> GetIncidentTimelineAsync(long incidentId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadTimelineAsync(conn, incidentId, ct);
    }

    public async Task<List<IncidentNote>> GetIncidentNotesAsync(long incidentId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadNotesAsync(conn, incidentId, ct);
    }

    // ── Private query helpers ──

    private static async Task<List<ActiveIncident>> QueryIncidentsAsync(NpgsqlConnection conn, string where, object? param, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"SELECT incident_id, component, operation, signal, highest_severity, status, event_count, affected_installs, affected_users, opened_at, last_event_at FROM control_center.incidents {where} ORDER BY opened_at DESC", conn);
        if (param != null) cmd.Parameters.Add(new NpgsqlParameter<string> { Value = (string)param });
        var results = new List<ActiveIncident>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(new ActiveIncident(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetDateTime(9), reader.GetDateTime(10)));
        return results;
    }

    private static async Task<List<ComponentHealth>> QueryComponentHealthAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT component, health, active_incidents, last_event_at FROM control_center.component_health", conn);
        var results = new List<ComponentHealth>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(new ComponentHealth(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetDateTime(3)));
        return results;
    }

    private static async Task<List<ReleaseSummary>> QueryReleaseHealthAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT app_version, succeeded, failed, active_installs FROM control_center.release_health LIMIT 5", conn);
        var results = new List<ReleaseSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var s = reader.GetInt64(1); var f = reader.GetInt64(2); var t = s + f;
            results.Add(new ReleaseSummary(reader.GetString(0), s, f, t > 0 ? Math.Round(100.0 * s / t, 1) : 0, reader.GetInt32(3)));
        }
        return results;
    }

    private static async Task<PlatformStats> QueryPlatformStatsAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT events_24h, active_users_24h, active_installations, open_incidents FROM control_center.platform_stats", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
            return new PlatformStats(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
        return new PlatformStats(0, 0, 0, 0);
    }

    private static async Task<IncidentDetail?> QueryIncidentByIdAsync(NpgsqlConnection conn, string incidentId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT incident_id, id, fingerprint_key, release, component, operation, signal,
                   root_event_id, last_event_id, opened_at, last_event_at, closed_at,
                   status, highest_severity, peak_failure_pct, affected_users, affected_installs, event_count, owner
            FROM control_center.incidents WHERE incident_id = $1
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = incidentId });
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new IncidentDetail
        {
            IncidentId = reader.GetString(0), Id = reader.GetInt64(1), FingerprintKey = reader.GetString(2),
            Release = reader.GetString(3), Component = reader.GetString(4), Operation = reader.GetString(5),
            Signal = reader.GetString(6), RootEventId = reader.IsDBNull(7) ? null : reader.GetGuid(7),
            OpenedAt = reader.GetDateTime(9), LastEventAt = reader.GetDateTime(10),
            ClosedAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
            Status = reader.GetString(12), HighestSeverity = reader.GetString(13),
            PeakFailurePct = reader.IsDBNull(14) ? null : reader.GetDecimal(14),
            AffectedUsers = reader.GetInt32(15), AffectedInstalls = reader.GetInt32(16),
            EventCount = reader.GetInt64(17), Owner = reader.IsDBNull(18) ? null : reader.GetString(18),
        };
    }

    private static async Task<TelemetryEventSummary?> QueryEventSummaryAsync(NpgsqlConnection conn, Guid eventId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT id, correlation_id, step, component, operation, outcome,
                   COALESCE(hresult, supabase_code, http_status::text, exception_type, '-'),
                   occurred_at, error_message, duration_ms, source, http_status, hresult, supabase_code
            FROM control_center.telemetry_events WHERE id = $1
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter<Guid> { Value = eventId });
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new TelemetryEventSummary(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetDateTime(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetInt32(11), reader.IsDBNull(12) ? null : reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    private static async Task<List<TraceStep>> QueryTraceAsync(NpgsqlConnection conn, Guid correlationId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT step, component, operation, outcome,
                   COALESCE(hresult, supabase_code, http_status::text, exception_type, '-'),
                   occurred_at, error_message, duration_ms
            FROM control_center.telemetry_events WHERE correlation_id = $1 ORDER BY step
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter<Guid> { Value = correlationId });
        var results = new List<TraceStep>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(new TraceStep(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDateTime(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        return results;
    }

    private static async Task<List<TelemetryEventSummary>> QueryRelatedEventsAsync(NpgsqlConnection conn, IncidentDetail incident, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT id, correlation_id, step, component, operation, outcome,
                   COALESCE(hresult, supabase_code, http_status::text, exception_type, '-'),
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
            results.Add(new TelemetryEventSummary(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetDateTime(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetInt32(11), reader.IsDBNull(12) ? null : reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetString(13)));
        return results;
    }

    private static async Task<List<IncidentTimelineEntry>> ReadTimelineAsync(NpgsqlConnection conn, long incidentId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT id, incident_id, from_status, to_status, changed_by, changed_at, note FROM control_center.incident_timeline WHERE incident_id = $1 ORDER BY changed_at ASC", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = incidentId });
        var results = new List<IncidentTimelineEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(new IncidentTimelineEntry(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDateTime(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        return results;
    }

    private static async Task<List<IncidentNote>> ReadNotesAsync(NpgsqlConnection conn, long incidentId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT id, incident_id, content, created_by, created_at FROM control_center.incident_notes_view WHERE incident_id = $1 ORDER BY created_at ASC", conn);
        cmd.Parameters.Add(new NpgsqlParameter<long> { Value = incidentId });
        var results = new List<IncidentNote>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(new IncidentNote(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetDateTime(4)));
        return results;
    }

    private static async Task<(long KnowledgeId, string Status)?> QueryDraftKnowledgeAsync(NpgsqlConnection conn, string fingerprintKey, CancellationToken ct)
    {
        // Читаємо через control_center VIEW (доступно cc_readonly), а не напряму public.knowledge_entries,
        // бо остання захищена RLS deny-all для cc_readonly.
        await using var cmd = new NpgsqlCommand("""
            SELECT id, status FROM control_center.knowledge_entry_detail
            WHERE fingerprint_key = $1 AND status NOT IN ('Verified','Archived')
            ORDER BY updated_at DESC, id DESC LIMIT 1
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter<string> { Value = fingerprintKey });
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return (reader.GetInt64(0), reader.GetString(1));
    }
}