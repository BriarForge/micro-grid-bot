using Microsoft.AspNetCore.DataProtection;
using MicroGrid.Bot.Config;
using MicroGrid.Bot.Services;

var builder = WebApplication.CreateBuilder(args);

// Load repository-local configuration before refreshing the environment provider.
// WebApplication.CreateBuilder reads environment variables during construction, so values
// added to the process by EnvFileLoader would otherwise be invisible to IConfiguration.
var envFile = EnvFileLoader.LoadFromRepositoryRoot(Directory.GetCurrentDirectory());
builder.Configuration.AddEnvironmentVariables();

// -- DataProtection: per-host key ring under MICROGRID_STATE_DIR/dp-keys --
var stateDir = builder.Configuration["MICROGRID_STATE_DIR"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(stateDir);
var dpKeysDir = Path.Combine(stateDir, "dp-keys");
Directory.CreateDirectory(dpKeysDir);

builder.Services
    .AddDataProtection()
    .SetApplicationName("MicroGrid.Bot")
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeysDir));

builder.WebHost.UseUrls(builder.Configuration["MICROGRID_URL"] ?? "http://127.0.0.1:5080");

// Env-fallback options (not the primary source — store is). Required=false semantics.
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

// Credential store is a singleton so its in-memory cache + Generation counter survive across requests.
builder.Services.AddSingleton<OkxCredentialStore>(sp =>
{
    var protector = sp.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector(OkxCredentialStore.Purpose);
    var blob = Path.Combine(stateDir, "okx-credentials.dp");
    return new OkxCredentialStore(protector, blob);
});

builder.Services.AddHostedService<OkxMonitorWorker>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// -- Existing JSON endpoints (unchanged) --
app.MapGet("/api/status", (RuntimeState state) => Results.Ok(state.Get()));
app.MapGet("/api/settings", (LocalSettingsStore store) => Results.Ok(store.Get()));
app.MapPut("/api/settings", async (LocalBotSettings settings, LocalSettingsStore store, CancellationToken ct) =>
{
    try { return Results.Ok(await store.UpdateAsync(settings, ct)); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
});

// -- New credential endpoints: status-only on GET; secrets accepted on PUT/DELETE --
app.MapGet("/api/credentials", async (
    OkxCredentialStore store,
    Microsoft.Extensions.Options.IOptions<OkxCredentialsOptions> options,
    CancellationToken ct) =>
    Results.Ok(await ResolveCredentialStatusAsync(store, options.Value, ct)));

app.MapPut("/api/credentials", async (
    OkxCredentialInput input,
    OkxCredentialStore store,
    Microsoft.Extensions.Options.IOptions<OkxCredentialsOptions> options,
    ILoggerFactory log,
    CancellationToken ct) =>
{
    try
    {
        await store.SaveAsync(input, ct);
        log.CreateLogger("credentials").LogInformation("OKX credentials saved via local UI.");
        return Results.Ok(await ResolveCredentialStatusAsync(store, options.Value, ct));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapDelete("/api/credentials", async (
    OkxCredentialStore store,
    Microsoft.Extensions.Options.IOptions<OkxCredentialsOptions> options,
    ILoggerFactory log,
    CancellationToken ct) =>
{
    await store.ClearAsync(ct);
    log.CreateLogger("credentials").LogInformation("OKX credentials cleared via local UI.");
    return Results.Ok(await ResolveCredentialStatusAsync(store, options.Value, ct));
});

app.MapGet("/health", (RuntimeState state) => Results.Ok(new
{
    status = "ok",
    okxConnected = state.Get().Connected
}));

if (envFile is not null)
    app.Logger.LogInformation("Loaded local configuration from {EnvFile}", envFile);
app.Logger.LogInformation("Local dashboard: {Url}", builder.Configuration["MICROGRID_URL"] ?? "http://127.0.0.1:5080");
app.Logger.LogInformation("DP key ring: {KeysDir}", dpKeysDir);
await app.RunAsync();

static async Task<OkxCredentialStatus> ResolveCredentialStatusAsync(
    OkxCredentialStore store,
    OkxCredentialsOptions configured,
    CancellationToken cancellationToken)
{
    var stored = await store.GetStatusAsync(cancellationToken);
    if (stored.Configured || !configured.IsConfigured)
        return stored;

    var key = configured.ApiKey!;
    var hint = key.Length <= 4 ? "****" : "...." + key[^4..];
    return new OkxCredentialStatus(true, configured.DemoMode, configured.Region, hint, null);
}
