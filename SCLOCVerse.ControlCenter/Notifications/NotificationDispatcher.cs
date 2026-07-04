using Npgsql;
using System.Diagnostics;

namespace SCLOCVerse.ControlCenter.Notifications;

public sealed class NotificationDispatcher : IHostedService, IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IServiceProvider _services;
    private Timer? _timer;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public NotificationDispatcher(NpgsqlDataSource dataSource, IServiceProvider services)
    {
        _dataSource = dataSource;
        _services = services;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _timer = new Timer(_ => _ = ProcessAsync(), null, Interval, Interval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private async Task ProcessAsync()
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

            // Читаємо pending notifications (Стаття 25 — idempotent через UNIQUE)
            await using var cmd = new NpgsqlCommand("""
                SELECT n.id, n.incident_id, n.notification_type, n.provider, n.payload,
                       'INC-' || to_char(i.opened_at,'YYYY') || '-' || lpad(i.id::text,5,'0'),
                       i.component, i.signal, i.highest_severity, i.release
                FROM public.notification_queue n
                JOIN public.telemetry_incidents i ON i.id = n.incident_id
                WHERE n.status = 'Pending'
                ORDER BY n.created_at
                LIMIT 10
                """, conn);

            var pending = new List<(long id, string type, string provider, NotificationPayload payload)>();
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                pending.Add((reader.GetInt64(0), reader.GetString(2), reader.GetString(3),
                    new NotificationPayload
                    {
                        IncidentId = reader.GetInt64(1),
                        NotificationType = reader.GetString(2),
                        IncidentCode = reader.GetString(5),
                        Component = reader.GetString(6),
                        Signal = reader.GetString(7),
                        Severity = reader.GetString(8),
                        Release = reader.GetString(9),
                    }));
            }

            if (pending.Count == 0) return;
            reader.Dispose();

            // Отримати payload-дані з jsonb
            foreach (var (id, type, provider, payload) in pending)
            {
                await using var pcmd = new NpgsqlCommand("SELECT payload FROM public.notification_queue WHERE id = $1", conn);
                pcmd.Parameters.Add(new NpgsqlParameter<long> { Value = id });
                await using var pr = await pcmd.ExecuteReaderAsync().ConfigureAwait(false);
                if (await pr.ReadAsync().ConfigureAwait(false) && !pr.IsDBNull(0))
                {
                    var json = pr.GetString(0);
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("affected_installs", out var ai))
                        payload.AffectedInstalls = ai.GetInt32();
                    if (doc.RootElement.TryGetProperty("affected_users", out var au))
                        payload.AffectedUsers = au.GetInt32();
                    if (doc.RootElement.TryGetProperty("failure_pct", out var fp))
                        payload.FailurePct = fp.GetDouble();
                    if (doc.RootElement.TryGetProperty("severity", out var sv))
                        payload.Severity = sv.GetString() ?? payload.Severity;
                }
                pr.Dispose();
            }

            // Відправити через відповідний provider
            foreach (var (id, type, providerName, payload) in pending)
            {
                var provider = ResolveProvider(providerName);
                if (provider == null)
                {
                    await MarkStatusAsync(conn, id, "Failed", "No provider registered").ConfigureAwait(false);
                    continue;
                }

                try
                {
                    var success = await provider.SendAsync(payload).ConfigureAwait(false);
                    await MarkStatusAsync(conn, id, success ? "Delivered" : "Failed",
                        success ? null : "Provider returned failure").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Стаття 24: ізольована помилка — не впливає на інцидент
                    Debug.WriteLine($"[Notifications] Provider {providerName} failed for queue item {id}: {ex.Message}");
                    await MarkStatusAsync(conn, id, "Failed", ex.Message).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Notifications] Dispatcher error: {ex.Message}");
        }
    }

    private INotificationProvider? ResolveProvider(string name)
    {
        return name switch
        {
            "Discord" => _services.GetKeyedService<INotificationProvider>("Discord"),
            _ => null
        };
    }

    private static async Task MarkStatusAsync(NpgsqlConnection conn, long id, string status, string? error)
    {
        await using var cmd = new NpgsqlCommand("""
            UPDATE public.notification_queue SET
                status = @status,
                last_attempt_at = now(),
                delivered_at = CASE WHEN @status = 'Delivered' THEN now() ELSE delivered_at END,
                error_message = @error,
                retry_count = retry_count + 1
            WHERE id = @id
            """, conn);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public void Dispose() => _timer?.Dispose();
}