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
    ILogger<OkxMonitorWorker> logger) : BackgroundService
{
    private const string Symbol = "BTC-USDT";
    private readonly Config.OkxCredentialsOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var environmentName = _options.DemoMode ? "DEMO" : "LIVE READ-ONLY";
        runtimeState.Set(RuntimeSnapshot.Starting(environmentName));

        if (!_options.IsConfigured)
        {
            runtimeState.Set(RuntimeSnapshot.Starting(environmentName) with { LastError = "OKX credentials are incomplete." });
            return;
        }

        using var client = new OKXRestClient(clientOptions =>
        {
            clientOptions.ApiCredentials = new OKXCredentials(_options.ApiKey!, _options.ApiSecret!, _options.Passphrase!);
            clientOptions.Environment = GetEnvironment();
        });

        while (!stoppingToken.IsCancellationRequested)
        {
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

    private OKXEnvironment GetEnvironment()
    {
        if (_options.Region.Equals("GLOBAL", StringComparison.OrdinalIgnoreCase))
            return _options.DemoMode ? OKXEnvironment.Demo : OKXEnvironment.Live;
        if (_options.Region.Equals("AU", StringComparison.OrdinalIgnoreCase) || _options.Region.Equals("US", StringComparison.OrdinalIgnoreCase))
            return OKXEnvironment.CreateCustom(
                _options.DemoMode ? OKXEnvironment.Demo.Name : OKXEnvironment.Live.Name,
                "https://us.okx.com",
                _options.DemoMode ? "wss://wsuspap.okx.com:8443" : "wss://wsus.okx.com:8443");
        throw new InvalidOperationException("OKX_REGION must be GLOBAL, AU, or US.");
    }
}
