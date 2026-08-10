namespace MicroGrid.Bot.Config;

/// <summary>
/// Plaintext shape submitted by the local UI. Never returned by GET endpoints.
/// </summary>
public sealed class OkxCredentialInput
{
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string Passphrase { get; set; } = "";
    public bool DemoMode { get; set; } = true;
    public string Region { get; set; } = "GLOBAL";
}

/// <summary>Status-only projection safe to return to the browser.</summary>
public sealed record OkxCredentialStatus(
    bool Configured,
    bool? DemoMode,
    string? Region,
    string? ApiKeyHint,
    DateTimeOffset? UpdatedAt);

/// <summary>Resolved credentials for the engine. Held only in memory at the call site.</summary>
public sealed record OkxResolvedCredentials(
    string ApiKey,
    string ApiSecret,
    string Passphrase,
    bool DemoMode,
    string Region);

/// <summary>
/// JSON contract persisted in the DP-protected blob. Kept internal so the secret fields
/// cannot leak through accidental serialization of the input/status DTOs.
/// </summary>
internal sealed record OkxCredentialPayload(
    string ApiKey,
    string ApiSecret,
    string Passphrase,
    bool DemoMode,
    string Region);