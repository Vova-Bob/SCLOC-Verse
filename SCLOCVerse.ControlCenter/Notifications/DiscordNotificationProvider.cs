using System.Net.Http.Json;
using System.Text.Json;

namespace SCLOCVerse.ControlCenter.Notifications;

public sealed class DiscordNotificationProvider : INotificationProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _webhookUrl;

    public string Name => "Discord";

    public DiscordNotificationProvider(HttpClient httpClient, string webhookUrl)
    {
        _httpClient = httpClient;
        _webhookUrl = webhookUrl;
    }

    public async Task<bool> SendAsync(NotificationPayload payload, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
            return false;

        var severityEmoji = payload.Severity == "Critical" ? "🔴" : "🟡";
        var embed = new
        {
            title = $"{severityEmoji} {payload.NotificationType}: {payload.IncidentCode}",
            color = payload.Severity == "Critical" ? 0xFF0000 : 0xFFA500,
            fields = new[]
            {
                new { name = "Component", value = payload.Component, inline = true },
                new { name = "Signal", value = $"`{payload.Signal}`", inline = true },
                new { name = "Severity", value = payload.Severity, inline = true },
                new { name = "Release", value = payload.Release, inline = true },
                new { name = "Affected Installs", value = payload.AffectedInstalls.ToString(), inline = true },
                new { name = "Failure Rate", value = $"{payload.FailurePct}%", inline = true },
            },
            footer = new { text = "SCLOC Observability Platform" },
            timestamp = DateTime.UtcNow.ToString("o")
        };

        var body = new { embeds = new[] { embed } };
        var response = await _httpClient.PostAsJsonAsync(_webhookUrl, body, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }
}