using MicroGrid.Bot.Config;
using MicroGrid.Bot.Services;

var envFile = EnvFileLoader.LoadFromRepositoryRoot(Directory.GetCurrentDirectory());
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.WebHost.UseUrls(builder.Configuration["MICROGRID_URL"] ?? "http://127.0.0.1:5080");

builder.Services.Configure<OkxCredentialsOptions>(options =>
{
    options.ApiKey = builder.Configuration["OKX_API_KEY"];
    options.ApiSecret = builder.Configuration["OKX_API_SECRET"];
    options.Passphrase = builder.Configuration["OKX_PASSPHRASE"];
    options.DemoMode = builder.Configuration.GetValue("OKX_DEMO_MODE", true);
    options.Region = builder.Configuration["OKX_REGION"] ?? "GLOBAL";
});
builder.Services.AddSingleton<LocalSettingsStore>();
builder.Services.AddSingleton<RuntimeState>();
builder.Services.AddHostedService<OkxMonitorWorker>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/status", (RuntimeState state) => Results.Ok(state.Get()));
app.MapGet("/api/settings", (LocalSettingsStore store) => Results.Ok(store.Get()));
app.MapPut("/api/settings", async (LocalBotSettings settings, LocalSettingsStore store, CancellationToken ct) =>
{
    try { return Results.Ok(await store.UpdateAsync(settings, ct)); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
});
app.MapGet("/health", (RuntimeState state) => Results.Ok(new
{
    status = "ok",
    okxConnected = state.Get().Connected
}));

if (envFile is not null)
    app.Logger.LogInformation("Loaded local configuration from {EnvFile}", envFile);
app.Logger.LogInformation("Local dashboard: {Url}", builder.Configuration["MICROGRID_URL"] ?? "http://127.0.0.1:5080");
await app.RunAsync();
