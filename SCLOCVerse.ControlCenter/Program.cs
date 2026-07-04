using SCLOCVerse.ControlCenter.Components;
using SCLOCVerse.ControlCenter.Data;
using SCLOCVerse.ControlCenter.Notifications;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connStr = builder.Configuration.GetConnectionString("ControlCenter");
if (!string.IsNullOrWhiteSpace(connStr))
{
    builder.Services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(connStr));
    builder.Services.AddScoped<IControlCenterRepository, ControlCenterRepository>();
    builder.Services.AddScoped<ITraceRepository, TraceRepository>();

    // Notification Engine (Стаття 24/25)
    var discordWebhook = builder.Configuration["Notifications:Discord:WebhookUrl"]
        ?? Environment.GetEnvironmentVariable("SCLOC_DISCORD_WEBHOOK");
    if (!string.IsNullOrWhiteSpace(discordWebhook))
    {
        builder.Services.AddKeyedSingleton<INotificationProvider, DiscordNotificationProvider>("Discord",
            (sp, _) => new DiscordNotificationProvider(new HttpClient(), discordWebhook!));
        builder.Services.AddHostedService<NotificationDispatcher>();
    }
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
