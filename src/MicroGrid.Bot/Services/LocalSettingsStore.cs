using System.Text.Json;
using MicroGrid.Domain.Config;

namespace MicroGrid.Bot.Services;

public sealed record LocalBotSettings(
    decimal ActivePct = 0.65m,
    decimal ReservePct = 0.35m,
    decimal MaxBtcExposurePct = 0.65m,
    decimal ResumeBtcExposurePct = 0.60m,
    int Levels = 25,
    decimal MinimumSpacing = 0.0012m,
    int BuyLevelsBelowMid = 12,
    int SellLevelsAboveMid = 13,
    bool TradingEnabled = false)
{
    public GridSettings ToGridSettings() => new(
        ActivePct,
        ReservePct,
        MaxBtcExposurePct,
        ResumeBtcExposurePct,
        Levels,
        MinimumSpacing,
        BuyLevelsBelowMid,
        SellLevelsAboveMid);

    public void Validate()
    {
        ToGridSettings().Validate();
        if (ActivePct + ReservePct != 1m)
            throw new ArgumentException("Active and reserve percentages must total 100%.");
        if (TradingEnabled)
            throw new ArgumentException("Order placement is not implemented; TradingEnabled must remain false.");
    }
}

public sealed class LocalSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private LocalBotSettings _settings;

    public LocalSettingsStore(IHostEnvironment environment, IConfiguration configuration)
    {
        var stateDirectory = configuration["MICROGRID_STATE_DIR"] ?? Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(stateDirectory);
        _path = Path.Combine(stateDirectory, "settings.json");
        _settings = Load();
    }

    public LocalBotSettings Get() => _settings;

    public async Task<LocalBotSettings> UpdateAsync(LocalBotSettings settings, CancellationToken cancellationToken)
    {
        settings.Validate();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var temporaryPath = _path + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions), cancellationToken);
            File.Move(temporaryPath, _path, true);
            _settings = settings;
            return settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    private LocalBotSettings Load()
    {
        if (!File.Exists(_path)) return new LocalBotSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<LocalBotSettings>(File.ReadAllText(_path), JsonOptions)
                ?? new LocalBotSettings();
            settings.Validate();
            return settings;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Invalid local settings file {_path}: {exception.Message}", exception);
        }
    }
}
