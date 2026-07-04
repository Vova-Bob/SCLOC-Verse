using SCLOCVerse.ControlCenter.Components;
using SCLOCVerse.ControlCenter.Data;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Control Center data access (cc_readonly via Npgsql, user-secrets/env var).
// Notification Engine ВИНЕСЕНО в окремий Worker Service SCLOCVerse.Notifier (Стаття 24).
// UI лише читає control_center.notifications VIEW для відображення статусу черги.
// Connection string надходить через user-secrets/env (Стаття 29 — Secret Independence).
// Control Center використовує виділену роль cc_readonly (НЕ cc_notifier).
var connStr = builder.Configuration.GetConnectionString("ControlCenter")
    ?? Environment.GetEnvironmentVariable("SCLOC_CC_READONLY_DB");
if (!string.IsNullOrWhiteSpace(connStr))
{
    builder.Services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(connStr));
    builder.Services.AddScoped<IControlCenterRepository, ControlCenterRepository>();
    builder.Services.AddScoped<ITraceRepository, TraceRepository>();
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
