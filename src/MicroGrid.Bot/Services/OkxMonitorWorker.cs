using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MicroGrid.Domain.Grid;
using OKX.Net;
using OKX.Net.Clients;
using OKX.Net.Enums;

namespace MicroGrid.Bot.Services;

public sealed class OkxMonitorWorker(
    IOptions<Config.OkxCredentialsOptions> options,
    LocalSettingsStore settingsStore,
    RuntimeState runtimeState,
    OkxCredentialStore credentialStore,
    ILogger<OkxMonitorWorker> logger) : BackgroundService
{
    private const string Symbol = "BTC-USDT";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long lastGeneration = -1;
        OKXRestClient? client = null;
        bool activeDemoMode = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            var currentGeneration = credentialStore.Generation;
            if (client is null || currentGeneration != lastGeneration)
            {
                client?.Dispose();
                client = null;

                var resolved = await ResolveAsync(options.Value, credentialStore, stoppingToken);
                if (resolved is null)
                {
                    runtimeState.Set(RuntimeSnapshot.Starting("UNCONFIGURED")
                        with { LastError = "No OKX credentials. Add them in the local dashboard." });
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                activeDemoMode = resolved.DemoMode;
                var local = resolved;
                client = new OKXRestClient(clientOptions =>
                {
                    clientOptions.ApiCredentials = new OKXCredentials(local.ApiKey, local.ApiSecret, local.Passphrase);
                    clientOptions.Environment = GetEnvironment(local.DemoMode, local.Region);
                });
                lastGeneration = currentGeneration;
            }

            var environmentName = activeDemoMode ? "DEMO" : "LIVE READ-ONLY";
            runtimeState.Set(RuntimeSnapshot.Starting(environmentName));

            try
            {
                await RefreshAsync(client, environmentName, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning("OKX monitor refresh failed: {Error}", exception.Message);
                runtimeState.Set(runtimeState.Get() with { Connected = false, LastError = exception.Message, UpdatedAt = DateTimeOffset.UtcNow });
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }

        client?.Dispose();
    }

    private static async Task<Config.OkxResolvedCredentials?> ResolveAsync(
        Config.OkxCredentialsOptions configured,
        OkxCredentialStore store,
        CancellationToken cancellationToken)
    {
        var fromStore = await store.TryLoadAsync(cancellationToken);
        if (fromStore is not null) return fromStore;
        if (configured.IsConfigured)
            return new Config.OkxResolvedCredentials(
                configured.ApiKey!, configured.ApiSecret!, configured.Passphrase!,
                configured.DemoMode, configured.Region);
        return null;
    }

    private async Task RefreshAsync(OKXRestClient client, string environmentName, CancellationToken cancellationToken)
    {
        var tickerTask = client.UnifiedApi.ExchangeData.GetTickerAsync(Symbol, cancellationToken);
        var balanceTask = client.UnifiedApi.Account.GetAccountBalanceAsync(null, cancellationToken);
        var feeTask = client.UnifiedApi.Account.GetFeeRatesAsync(InstrumentType.Spot, symbol: Symbol, ct: cancellationToken);
        await Task.WhenAll(tickerTask, balanceTask, feeTask);

        var ticker = await tickerTask;
        var balance = await balanceTask;
        var fees = await feeTask;
        if (!ticker.Success || ticker.Data is null) throw new InvalidOperationException($"Ticker: {ticker.Error}");
        if (!balance.Success || balance.Data is null) throw new InvalidOperationException($"Balance: {balance.Error}");
        if (!fees.Success || fees.Data is null) throw new InvalidOperationException($"Fees: {fees.Error}");
        if (fees.Data.Maker is not decimal makerRaw || fees.Data.Taker is not decimal takerRaw)
            throw new InvalidOperationException("OKX returned no maker/taker fee rate.");

        var maker = Math.Abs(makerRaw);
        var taker = Math.Abs(takerRaw);
        var spacing = FeeAwareSpacing.Resolve(settingsStore.Get().ToGridSettings(), maker, taker);
        var assets = balance.Data.Details
            .Where(asset => asset.Equity != 0 || asset.AvailableBalance != 0)
            .Select(asset => new AssetBalance(asset.Asset, asset.AvailableBalance ?? 0, asset.UsdEquity ?? 0))
            .OrderByDescending(asset => asset.EquityUsd)
            .ToArray();

        runtimeState.Set(new RuntimeSnapshot(
            true, environmentName, Symbol,
            ticker.Data.LastPrice, ticker.Data.BestBidPrice, ticker.Data.BestAskPrice,
            balance.Data.TotalEquity, maker, taker, spacing.EffectiveSpacing,
            fees.Data.Level, assets, DateTimeOffset.UtcNow, null));
    }

    private static OKXEnvironment GetEnvironment(bool demoMode, string region)
    {
        if (string.Equals(region, "GLOBAL", StringComparison.OrdinalIgnoreCase))
            return demoMode ? OKXEnvironment.Demo : OKXEnvironment.Live;
        if (string.Equals(region, "AU", StringComparison.OrdinalIgnoreCase) || string.Equals(region, "US", StringComparison.OrdinalIgnoreCase))
            return OKXEnvironment.CreateCustom(
                demoMode ? OKXEnvironment.Demo.Name : OKXEnvironment.Live.Name,
                "https://us.okx.com",
                demoMode ? "wss://wsuspap.okx.com:8443" : "wss://wsus.okx.com:8443");
        throw new InvalidOperationException("OKX_REGION must be GLOBAL, AU, or US.");
    }
}