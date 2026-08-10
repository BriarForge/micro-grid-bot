using Microsoft.Extensions.Options;
using OKX.Net;
using OKX.Net.Clients;
using OKX.Net.Enums;
using MicroGrid.Domain.Config;
using MicroGrid.Domain.Grid;

namespace MicroGrid.Bot.Services;

public sealed class OkxDemoWorker(
    IOptions<Config.OkxCredentialsOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<OkxDemoWorker> logger) : BackgroundService
{
    private const string Symbol = "BTC-USDT";
    private readonly Config.OkxCredentialsOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunDemoAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal Ctrl+C / service shutdown.
        }
        catch (Exception exception)
        {
            logger.LogCritical("Demo runner stopped: {Error}", exception.Message);
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }
    }

    private async Task RunDemoAsync(CancellationToken stoppingToken)
    {
        ValidateConfiguration();
        var environment = GetDemoEnvironment();

        using var restClient = new OKXRestClient(clientOptions =>
        {
            clientOptions.ApiCredentials = new OKXCredentials(_options.ApiKey!, _options.ApiSecret!, _options.Passphrase!);
            clientOptions.Environment = environment;
        });
        using var socketClient = new OKXSocketClient(clientOptions =>
        {
            clientOptions.ApiCredentials = new OKXCredentials(_options.ApiKey!, _options.ApiSecret!, _options.Passphrase!);
            clientOptions.Environment = environment;
        });

        logger.LogInformation(
            "Starting OKX demo connectivity check for {Symbol} in region {Region}; no orders will be placed",
            Symbol, _options.Region);

        var account = await restClient.UnifiedApi.Account.GetAccountConfigurationAsync(stoppingToken);
        if (!account.Success)
            throw new InvalidOperationException($"OKX demo authentication failed: {account.Error?.ToString() ?? account.ToString()}");

        var feeResult = await restClient.UnifiedApi.Account.GetFeeRatesAsync(
            InstrumentType.Spot,
            symbol: Symbol,
            ct: stoppingToken);
        if (!feeResult.Success || feeResult.Data is null)
            throw new InvalidOperationException($"OKX fee refresh failed: {feeResult.Error?.ToString() ?? feeResult.ToString()}");

        if (feeResult.Data.Maker is not decimal makerRaw || feeResult.Data.Taker is not decimal takerRaw)
            throw new InvalidOperationException("OKX fee refresh returned no maker or taker rate for BTC-USDT.");

        var makerRate = Math.Abs(makerRaw);
        var takerRate = Math.Abs(takerRaw);
        var spacing = FeeAwareSpacing.Resolve(new GridSettings(), makerRate, takerRate);
        logger.LogInformation(
            "Live OKX fees tier={Tier}: maker={MakerPercent}% taker={TakerPercent}%. Effective grid spacing={SpacingPercent}%{Adjustment}",
            feeResult.Data.Level,
            makerRate * 100m,
            takerRate * 100m,
            spacing.EffectiveSpacing * 100m,
            spacing.WasAdjusted ? " (raised from configured minimum)" : string.Empty);

        var ticker = await restClient.UnifiedApi.ExchangeData.GetTickerAsync(Symbol, stoppingToken);
        if (!ticker.Success || ticker.Data is null)
            throw new InvalidOperationException($"OKX demo ticker request failed: {ticker.Error?.Message ?? "unknown error"}");

        logger.LogInformation(
            "OKX demo authenticated. {Symbol} last={LastPrice} USDT bid={BidPrice} ask={AskPrice}",
            Symbol, ticker.Data.LastPrice, ticker.Data.BestBidPrice, ticker.Data.BestAskPrice);

        if (_options.RunOnce)
        {
            logger.LogInformation("Demo check completed successfully; exiting because MICROGRID_RUN_ONCE=true");
            lifetime.StopApplication();
            return;
        }

        var subscription = await socketClient.UnifiedApi.ExchangeData.SubscribeToTickerUpdatesAsync(
            Symbol,
            update => logger.LogInformation(
                "OKX demo tick {Symbol} last={LastPrice} USDT bid={BidPrice} ask={AskPrice}",
                Symbol, update.Data.LastPrice, update.Data.BestBidPrice, update.Data.BestAskPrice),
            stoppingToken);

        if (!subscription.Success)
            throw new InvalidOperationException($"OKX demo WebSocket subscription failed: {subscription.Error?.Message ?? "unknown error"}");

        logger.LogInformation("OKX demo WebSocket connected. Press Ctrl+C to stop");
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private void ValidateConfiguration()
    {
        if (!_options.DemoMode)
            throw new InvalidOperationException("Live trading is disabled by this runner. Set OKX_DEMO_MODE=true and use demo API credentials.");
        if (!_options.IsConfigured)
            throw new InvalidOperationException("OKX demo credentials are incomplete. Set OKX_API_KEY, OKX_API_SECRET, and OKX_PASSPHRASE.");
    }

    private OKXEnvironment GetDemoEnvironment()
    {
        if (_options.Region.Equals("AU", StringComparison.OrdinalIgnoreCase) ||
            _options.Region.Equals("US", StringComparison.OrdinalIgnoreCase))
        {
            // Keep the built-in demo environment name so OKX.Net adds x-simulated-trading: 1,
            // while selecting the regional REST and paper WebSocket hosts required for AU/US keys.
            return OKXEnvironment.CreateCustom(
                OKXEnvironment.Demo.Name,
                "https://us.okx.com",
                "wss://wsuspap.okx.com:8443");
        }

        if (_options.Region.Equals("GLOBAL", StringComparison.OrdinalIgnoreCase))
            return OKXEnvironment.Demo;

        throw new InvalidOperationException("OKX_REGION must be AU, US, or GLOBAL for the demo runner.");
    }
}
