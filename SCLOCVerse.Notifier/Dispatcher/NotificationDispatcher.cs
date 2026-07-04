using System.Data;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using SCLOCVerse.Notifications;

namespace SCLOCVerse.Notifier.Dispatcher;

/// <summary>
/// Параметри диспетчера. Джерело: appsettings.json Notifications:Dispatcher.
/// </summary>
public sealed class DispatcherOptions
{
    public int IntervalSeconds { get; set; } = 30;
    public int ZombieTimeoutMinutes { get; set; } = 10;
    public int BatchSize { get; set; } = 5;
}

/// <summary>
/// Append-only запис про отриманий з черги елемент (перед відправкою провайдеру).
/// </summary>
internal sealed record ClaimedItem(
    long QueueId,
    long IncidentId,
    string NotificationType,
    string ProviderName,
    int AttemptNo,
    int MaxRetries,
    NotificationPayload Payload);

/// <summary>
/// Диспетчер черги сповіщень.
///
/// Архітектурні гарантії:
///  - Стаття 24 (Notification Independence): UI не запускає цей код, Worker повністю автономний.
///  - Стаття 25 (Notification Idempotency): UNIQUE у черзі гарантує, що повторні промоти
///    промоуту не створюють дублів.
///  - Стаття 26 (Delivery Audit): кожна спроба залишає запис у notification_attempts.
///    Перехід статусів: Pending → Sending → (Delivered | RetryScheduled | Failed).
///  - Стаття 27 (Provider Independence): працює лише з INotificationProvider, не знає про Discord.
///
/// Живучість:
///  - Claim атомарний (FOR UPDATE SKIP LOCKED) → безпечно для 2+ інстансів Worker.
///  - Zombie Recovery: елемент у Sending довше за ZombieTimeoutMinutes → RetryScheduled.
///  - Retry: експоненційний backoff через next_attempt_at = now + 2^attemptNo секунд.
/// </summary>
public sealed class NotificationDispatcher : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IServiceProvider _services;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly DispatcherOptions _options;
    private readonly string _instanceId;

    public NotificationDispatcher(
        NpgsqlDataSource dataSource,
        IServiceProvider services,
        IOptions<DispatcherOptions> options,
        ILogger<NotificationDispatcher> logger)
    {
        _dataSource = dataSource;
        _services = services;
        _logger = logger;
        _options = options.Value;
        _instanceId = $"{Environment.MachineName}:{Environment.ProcessId}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationDispatcher started (instance={Instance}, interval={Sec}s, zombieTimeout={Min}m, batch={Batch})",
            _instanceId, _options.IntervalSeconds, _options.ZombieTimeoutMinutes, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessCycleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Стаття 24: ізольована помилка не валить сервіс
                _logger.LogError(ex, "Dispatcher cycle failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("NotificationDispatcher stopped (instance={Instance})", _instanceId);
    }

    private async Task ProcessCycleAsync(CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        // 1. Zombie Recovery (Стаття 26 — Sending не може "зависнути" назавжди)
        await RecoverZombiesAsync(conn, ct).ConfigureAwait(false);

        // 2. Claim batch атомарно (FOR UPDATE SKIP LOCKED — horizontal-safe)
        var items = await ClaimBatchAsync(conn, ct).ConfigureAwait(false);
        if (items.Count == 0) return;

        // 3. Відправити кожен через відповідного провайдера
        foreach (var item in items)
        {
            await DispatchOneAsync(conn, item, ct).ConfigureAwait(false);
        }
    }

    private async Task RecoverZombiesAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            UPDATE public.notification_queue
            SET status = 'RetryScheduled',
                claimed_at = NULL,
                claimed_by = NULL,
                last_error = COALESCE(NULLIF(last_error, '') || ' | ', '') || 'zombie recovery after ' || @timeout
            WHERE status = 'Sending'
              AND claimed_at < now() - (@timeout || ' minutes')::interval
            """, conn);
        cmd.Parameters.AddWithValue("timeout", _options.ZombieTimeoutMinutes.ToString());

        var recovered = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (recovered > 0)
            _logger.LogWarning("Zombie recovery: {Count} items moved Sending → RetryScheduled", recovered);
    }

    private async Task<List<ClaimedItem>> ClaimBatchAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var items = new List<ClaimedItem>();
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Крок 1: claim атомарно (FOR UPDATE SKIP LOCKED — horizontal-safe).
        // UPDATE RETURNING без JOIN — стабільний синтаксис для будь-якої версії PG.
        var claimedRows = new List<(long Id, long IncidentId, string Type, string Provider, string Payload, int AttemptNo, int MaxRetries)>();

        await using (var cmd = new NpgsqlCommand("""
            WITH picked AS (
                SELECT id FROM public.notification_queue
                WHERE status IN ('Pending','RetryScheduled')
                  AND (next_attempt_at IS NULL OR next_attempt_at <= now())
                  AND retry_count < max_retries
                ORDER BY created_at
                LIMIT @batch
                FOR UPDATE SKIP LOCKED
            )
            UPDATE public.notification_queue q
            SET status = 'Sending',
                claimed_at = now(),
                claimed_by = @instance,
                last_attempt_at = now(),
                retry_count = retry_count + 1
            FROM picked
            WHERE q.id = picked.id
            RETURNING q.id, q.incident_id, q.notification_type, q.provider, q.payload, q.retry_count, q.max_retries
            """, conn, (NpgsqlTransaction)tx))
        {
            cmd.Parameters.AddWithValue("batch", _options.BatchSize);
            cmd.Parameters.AddWithValue("instance", _instanceId);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                claimedRows.Add((
                    Id: reader.GetInt64(0),
                    IncidentId: reader.GetInt64(1),
                    Type: reader.GetString(2),
                    Provider: reader.GetString(3),
                    Payload: reader.GetString(4),
                    AttemptNo: reader.GetInt32(5),
                    MaxRetries: reader.GetInt32(6)));
            }
        }

        // Крок 2: підтягнути incident-дані (код, component, operation, signal, severity, release).
        // Одним пакетним SELECT, щоб не дergати БД по рядку.
        if (claimedRows.Count > 0)
        {
            var incidentIds = claimedRows.Select(c => c.IncidentId).Distinct().ToList();
            var incidentMap = new Dictionary<long, (string Code, string Component, string Operation, string Signal, string Severity, string Release)>();

            await using (var qcmd = new NpgsqlCommand("""
                SELECT id,
                       'INC-' || to_char(opened_at,'YYYY') || '-' || lpad(id::text,5,'0'),
                       component, operation, signal, COALESCE(highest_severity,'Warning'), release
                FROM public.telemetry_incidents
                WHERE id = ANY(@ids)
                """, conn, (NpgsqlTransaction)tx))
            {
                qcmd.Parameters.AddWithValue("ids", incidentIds.ToArray());
                await using var qreader = await qcmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await qreader.ReadAsync(ct).ConfigureAwait(false))
                {
                    incidentMap[qreader.GetInt64(0)] = (
                        Code: qreader.GetString(1),
                        Component: qreader.GetString(2),
                        Operation: qreader.GetString(3),
                        Signal: qreader.GetString(4),
                        Severity: qreader.GetString(5),
                        Release: qreader.GetString(6));
                }
            }

            foreach (var c in claimedRows)
            {
                if (!incidentMap.TryGetValue(c.IncidentId, out var inc)) continue;
                var payload = ParsePayload(c.Payload);
                payload.IncidentId = c.IncidentId;
                payload.IncidentCode = inc.Code;
                payload.Component = inc.Component;
                payload.Operation = inc.Operation;
                payload.Signal = inc.Signal;
                payload.Severity = inc.Severity;
                payload.Release = inc.Release;
                payload.NotificationType = c.Type;

                items.Add(new ClaimedItem(
                    QueueId: c.Id,
                    IncidentId: c.IncidentId,
                    NotificationType: c.Type,
                    ProviderName: c.Provider,
                    AttemptNo: c.AttemptNo,
                    MaxRetries: c.MaxRetries,
                    Payload: payload));
            }
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return items;
    }

    private static NotificationPayload ParsePayload(string json)
    {
        var p = new NotificationPayload();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("version", out var v) && v.TryGetInt32(out var ver)) p.Version = ver;
            if (root.TryGetProperty("affected_installs", out var ai) && ai.TryGetInt32(out var aiv)) p.AffectedInstalls = aiv;
            if (root.TryGetProperty("affected_users", out var au) && au.TryGetInt32(out var auv)) p.AffectedUsers = auv;
            if (root.TryGetProperty("failure_pct", out var fp) && fp.TryGetDouble(out var fpv)) p.FailurePct = fpv;
            if (root.TryGetProperty("severity", out var sv)) p.Severity = sv.GetString() ?? "";
        }
        catch { /* некритично — fallback на empty */ }
        return p;
    }

    private async Task DispatchOneAsync(NpgsqlConnection conn, ClaimedItem item, CancellationToken ct)
    {
        var provider = ResolveProvider(item.ProviderName);

        // Відкрити audit-запис 'Sending' (Стаття 26)
        long attemptId = await InsertAttemptAsync(conn, item, "Sending",
            httpStatus: null, providerMessageId: null, error: null,
            started: true, existingId: null, ct: ct).ConfigureAwait(false);

        if (provider is null)
        {
            var err = $"No provider registered for channel '{item.ProviderName}'";
            await InsertAttemptAsync(conn, item, "Failed",
                httpStatus: null, providerMessageId: null, error: err,
                started: false, existingId: attemptId, ct: ct).ConfigureAwait(false);
            await FinalizeQueueAsync(conn, item, success: false, err, isPermanent: true, ct).ConfigureAwait(false);
            _logger.LogError("No provider for {QueueId} ({Channel})", item.QueueId, item.ProviderName);
            return;
        }

        NotificationResult result;
        try
        {
            result = await provider.SendAsync(item.Payload, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Стаття 24: провайдер не повинен кидати, але страховка
            result = NotificationResult.Fail($"Provider crashed: {ex.Message}");
        }

        // Закрити audit-запис
        await InsertAttemptAsync(conn, item,
            status: result.Success ? "Delivered" : "Failed",
            httpStatus: result.HttpStatus, providerMessageId: result.ProviderMessageId,
            error: result.Error, started: false, existingId: attemptId, ct: ct).ConfigureAwait(false);

        // Визначити permanent failure (retry вичерпано)
        bool isPermanent = !result.Success && item.AttemptNo >= item.MaxRetries;

        await FinalizeQueueAsync(conn, item, result.Success,
            result.Success ? null : (result.Error ?? "Provider returned failure"),
            isPermanent, ct).ConfigureAwait(false);

        if (result.Success)
            _logger.LogInformation("Delivered {QueueId} via {Channel} (attempt {Attempt})",
                item.QueueId, item.ProviderName, item.AttemptNo);
        else
            _logger.LogWarning("Failed {QueueId} via {Channel} (attempt {Attempt}, permanent={Perm}): {Error}",
                item.QueueId, item.ProviderName, item.AttemptNo, isPermanent, result.Error);
    }

    private INotificationProvider? ResolveProvider(string name)
    {
        var providers = _services.GetServices<INotificationProvider>();
        return providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<long> InsertAttemptAsync(
        NpgsqlConnection conn, ClaimedItem item, string status,
        int? httpStatus, string? providerMessageId, string? error,
        bool started, long? existingId = null, CancellationToken ct = default)
    {
        if (started)
        {
            // Створити новий запис аудиту 'Sending'
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO public.notification_attempts (queue_id, attempt_no, provider, status, started_at)
                VALUES (@qid, @attempt, @provider, 'Sending', now())
                RETURNING id
                """, conn);
            cmd.Parameters.AddWithValue("qid", item.QueueId);
            cmd.Parameters.AddWithValue("attempt", item.AttemptNo);
            cmd.Parameters.AddWithValue("provider", item.ProviderName);
            return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }
        else
        {
            // Закрити існуючий запис
            await using var cmd = new NpgsqlCommand("""
                UPDATE public.notification_attempts
                SET status = @status,
                    http_status = @http,
                    provider_message_id = @msg,
                    error_message = @err,
                    finished_at = now()
                WHERE id = @id
                """, conn);
            cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.AddWithValue("http", (object?)httpStatus ?? DBNull.Value);
            cmd.Parameters.AddWithValue("msg", (object?)providerMessageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("err", (object?)error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("id", existingId!.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return existingId.Value;
        }
    }

    private static async Task FinalizeQueueAsync(
        NpgsqlConnection conn, ClaimedItem item, bool success, string? error,
        bool isPermanent, CancellationToken ct)
    {
        var newStatus = success ? "Delivered" : (isPermanent ? "Failed" : "RetryScheduled");

        // Експоненційний backoff: 2^attemptNo секунд
        var nextAttempt = success || isPermanent
            ? "NULL"
            : $"now() + (power(2, {item.AttemptNo}) || ' seconds')::interval";

        await using var cmd = new NpgsqlCommand($"""
            UPDATE public.notification_queue
            SET status = @status,
                delivered_at = CASE WHEN @success THEN now() ELSE delivered_at END,
                claimed_at = NULL,
                claimed_by = NULL,
                next_attempt_at = {nextAttempt},
                last_error = @err
            WHERE id = @id
            """, conn);
        cmd.Parameters.AddWithValue("status", newStatus);
        cmd.Parameters.AddWithValue("success", success);
        cmd.Parameters.AddWithValue("err", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", item.QueueId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
