namespace MicroGrid.Bot.Config;

public sealed class OkxCredentialsOptions
{
    public const string SectionName = "Okx";

    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? Passphrase { get; set; }
    public bool DemoMode { get; set; } = true;
    public bool RunOnce { get; set; }
    public string Region { get; set; } = "GLOBAL";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret) &&
        !string.IsNullOrWhiteSpace(Passphrase);
}
