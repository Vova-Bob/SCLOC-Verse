using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SCLOCVerse.Notifications;
using SCLOCVerse.Notifier.Dispatcher;
using SCLOCVerse.Notifier.Providers;

// Worker Service: Notification Dispatcher (Статті 24-27).
// Повністю автономний від UI — працює, навіть якщо Control Center вимкнений.
var builder = Host.CreateApplicationBuilder(args);

// Connection string надходить через user-secrets/env (Стаття 29 — Secret Independence).
// Worker використовує виділену роль cc_notifier (НЕ cc_readonly) — власна змінна SCLOC_NOTIFIER_DB.
var connStr = builder.Configuration.GetConnectionString("Notifier")
    ?? Environment.GetEnvironmentVariable("SCLOC_NOTIFIER_DB");
if (string.IsNullOrWhiteSpace(connStr))
    throw new InvalidOperationException(
        "Connection string 'Notifier' is not configured. " +
        "Use user-secrets ('ConnectionStrings:Notifier') or SCLOC_NOTIFIER_DB env var.");

builder.Services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(connStr));

// Discord provider (Стаття 27 — INotificationProvider, конкретний клас зареєстрований як реалізація).
// Webhook URL лише через конфіг/env — НІКОЛИ не в БД (користувач п.6).
var discordWebhook = builder.Configuration["Notifications:Discord:WebhookUrl"]
    ?? Environment.GetEnvironmentVariable("SCLOC_DISCORD_WEBHOOK");
if (!string.IsNullOrWhiteSpace(discordWebhook))
{
    const string httpClientName = nameof(DiscordNotificationProvider);
    builder.Services.AddHttpClient(httpClientName);
    builder.Services.AddSingleton<INotificationProvider>(sp =>
    {
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(httpClientName);
        return new DiscordNotificationProvider(client, discordWebhook);
    });
}
else
{
    // Без webhook Worker клеймит чергу, але відмітить кожен запис як Failed ("No provider").
    // Це коректна поведінка для аудиту (Стаття 26): жоден запис не зникає.
    ConsoleExtensions.Warning("SCLOC_DISCORD_WEBHOOK / Notifications:Discord:WebhookUrl не налаштовано — Discord provider вимкнено.");
}

// Dispatcher реєструється завжди: обробляє чергу навіть без провайдерів (фіксуючи відмову в аудит).
builder.Services.AddHostedService<NotificationDispatcher>();

builder.Services.Configure<DispatcherOptions>(builder.Configuration.GetSection("Notifications:Dispatcher"));

var host = builder.Build();
await host.RunAsync();

// Допоміжне розширення для попереджень до логування налаштування DI.
internal static class ConsoleExtensions
{
    public static void Warning(string message) =>
        Console.WriteLine($"[Notifier] WARN: {message}");
}
