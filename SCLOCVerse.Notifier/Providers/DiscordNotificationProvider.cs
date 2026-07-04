using System.Net.Http.Json;
using System.Text.Json;
using SCLOCVerse.Notifications;

namespace SCLOCVerse.Notifier.Providers;

/// <summary>
/// Discord Webhook провайдер (Стаття 27 — реалізація INotificationProvider).
/// Назва каналу — константа NotificationChannel.Discord (без магії).
/// Webhook URL надходить через конфіг/env (ніколи не зберігається в БД).
/// </summary>
public sealed class DiscordNotificationProvider : INotificationProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _webhookUrl;

    public string Name => NotificationChannel.Discord;

    public DiscordNotificationProvider(HttpClient httpClient, string webhookUrl)
    {
        _httpClient = httpClient;
        _webhookUrl = webhookUrl ?? throw new ArgumentNullException(nameof(webhookUrl));
    }

    public async Task<NotificationResult> SendAsync(NotificationPayload payload, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
            return NotificationResult.Fail("Webhook URL is not configured");

        try
        {
            var (embedBody, messageId) = BuildEmbed(payload);
            using var response = await _httpClient.PostAsJsonAsync(_webhookUrl, embedBody, ct).ConfigureAwait(false);

            // Discord webhook повертає 204 No Content (без message id) або 200 з тілом.
            // message_id витягується лише у випадку з тілом відповіді.
            string? resolvedMessageId = messageId;
            if (response.IsSuccessStatusCode && resolvedMessageId is null)
            {
                // Опціонально спробувати дістати id з відповіді, якщо є
                try
                {
                    var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        using var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("id", out var idProp))
                            resolvedMessageId = idProp.GetString();
                    }
                }
                catch { /* не критично */ }
            }

            return response.IsSuccessStatusCode
                ? NotificationResult.Ok((int)response.StatusCode, resolvedMessageId)
                : NotificationResult.Fail($"Discord returned {(int)response.StatusCode}", (int)response.StatusCode);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return NotificationResult.Fail($"Discord timeout: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return NotificationResult.Fail($"Discord HTTP error: {ex.Message}");
        }
    }

    private static (object body, string? messageId) BuildEmbed(NotificationPayload p)
    {
        var severityEmoji = p.Severity == "Critical" ? "🔴" : "🟡";
        var color = p.Severity == "Critical" ? 0xFF0000 : 0xFFA500;

        var body = new
        {
            embeds = new[]
            {
                new
                {
                    title = $"{severityEmoji} {p.NotificationType}: {p.IncidentCode}",
                    color,
                    fields = new[]
                    {
                        new { name = "Component", value = p.Component, inline = true },
                        new { name = "Operation", value = p.Operation, inline = true },
                        new { name = "Signal", value = $"`{p.Signal}`", inline = true },
                        new { name = "Severity", value = p.Severity, inline = true },
                        new { name = "Release", value = p.Release, inline = true },
                        new { name = "Failure Rate", value = $"{p.FailurePct:0.0}%", inline = true },
                        new { name = "Affected Installs", value = p.AffectedInstalls.ToString(), inline = true },
                        new { name = "Affected Users", value = p.AffectedUsers.ToString(), inline = true }
                    },
                    footer = new { text = $"SCLOC Observability Platform · payload v{p.Version}" },
                    timestamp = DateTime.UtcNow.ToString("o")
                }
            }
        };
        return (body, null);
    }
}
