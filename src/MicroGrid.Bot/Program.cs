using Microsoft.Extensions.Hosting;
using MicroGrid.Bot.Config;
using MicroGrid.Bot.Services;

var envFile = EnvFileLoader.LoadFromRepositoryRoot(Directory.GetCurrentDirectory());
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Services.Configure<OkxCredentialsOptions>(options =>
{
    options.ApiKey = builder.Configuration["OKX_API_KEY"];
    options.ApiSecret = builder.Configuration["OKX_API_SECRET"];
    options.Passphrase = builder.Configuration["OKX_PASSPHRASE"];
    options.DemoMode = builder.Configuration.GetValue("OKX_DEMO_MODE", true);
    options.RunOnce = builder.Configuration.GetValue("MICROGRID_RUN_ONCE", false);
    options.Region = builder.Configuration["OKX_REGION"] ?? "AU";
});
builder.Services.AddHostedService<OkxDemoWorker>();
var host = builder.Build();
if (envFile is not null)
{
    host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Startup")
        .LogInformation("Loaded local configuration from {EnvFile}", envFile);
}
await host.RunAsync();
