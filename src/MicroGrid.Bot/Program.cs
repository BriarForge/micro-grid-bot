using Microsoft.Extensions.Hosting;
using MicroGrid.Bot.Config;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Services.Configure<OkxCredentialsOptions>(options =>
{
    options.ApiKey = builder.Configuration["OKX_API_KEY"];
    options.ApiSecret = builder.Configuration["OKX_API_SECRET"];
    options.Passphrase = builder.Configuration["OKX_PASSPHRASE"];
    options.DemoMode = builder.Configuration.GetValue("OKX_DEMO_MODE", true);
});
var host = builder.Build();
await host.RunAsync();
