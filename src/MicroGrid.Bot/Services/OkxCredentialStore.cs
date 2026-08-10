using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using MicroGrid.Bot.Config;

namespace MicroGrid.Bot.Services;

/// <summary>
/// Encrypted-at-rest OKX credential store backed by ASP.NET Core DataProtection.
/// Purpose: <c>MicroGrid.OkxCredentials.v1</c>. Atomic writes (tmp + move). Plaintext
/// never leaves the process; only the status projection is exposed to callers.
/// </summary>
public sealed class OkxCredentialStore
{
    public const string Purpose = "MicroGrid.OkxCredentials.v1";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly IDataProtector _protector;
    private readonly string _blobPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _versionLock = new();
    private OkxResolvedCredentials? _cached;
    private long _version;
    private DateTimeOffset? _lastWriteAtUtc;

    public OkxCredentialStore(IDataProtector protector, string blobPath)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        if (string.IsNullOrWhiteSpace(blobPath)) throw new ArgumentException("blobPath required.", nameof(blobPath));
        _blobPath = blobPath;
    }

    /// <summary>Where the DP-protected blob lives on disk (for tests + ops runbooks).</summary>
    public string BlobPath => _blobPath;

    /// <summary>Monotonic counter incremented only on Save/Clear so the worker can detect changes.</summary>
    public long Generation
    {
        get { lock (_versionLock) return _version; }
    }

    /// <summary>Returns a safe projection. Never contains raw secrets.</summary>
    public async Task<OkxCredentialStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null) return Project(_cached, _lastWriteAtUtc);
            if (!File.Exists(_blobPath)) return new OkxCredentialStatus(false, null, null, null, null);
            try
            {
                var resolved = ReadFromDisk();
                if (resolved is null) return new OkxCredentialStatus(false, null, null, null, null);
                _cached = resolved;
                _lastWriteAtUtc = File.GetLastWriteTimeUtc(_blobPath);
                return Project(resolved, _lastWriteAtUtc);
            }
            catch
            {
                return new OkxCredentialStatus(false, null, null, null, null);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Try to resolve credentials from the protected store. Returns null when the blob is
    /// missing or unreadable. Caller is responsible for env fallback.
    /// </summary>
    public async Task<OkxResolvedCredentials?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null) return _cached;
            if (!File.Exists(_blobPath)) return null;
            try
            {
                _cached = ReadFromDisk();
                _lastWriteAtUtc = File.GetLastWriteTimeUtc(_blobPath);
                return _cached;
            }
            catch { return null; }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(OkxCredentialInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);

        var payload = new OkxCredentialPayload(
            input.ApiKey.Trim(),
            input.ApiSecret.Trim(),
            input.Passphrase.Trim(),
            input.DemoMode,
            string.IsNullOrWhiteSpace(input.Region) ? "GLOBAL" : input.Region.Trim().ToUpperInvariant());
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var protectedBytes = _protector.Protect(plaintext);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_blobPath)!);
            var tmp = _blobPath + ".tmp";
            await File.WriteAllBytesAsync(tmp, protectedBytes, cancellationToken);
            File.Move(tmp, _blobPath, overwrite: true);
            _cached = new OkxResolvedCredentials(payload.ApiKey, payload.ApiSecret, payload.Passphrase, payload.DemoMode, payload.Region);
            _lastWriteAtUtc = DateTimeOffset.UtcNow;
            BumpVersion();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _cached = null;
            _lastWriteAtUtc = null;
            BumpVersion();
            if (File.Exists(_blobPath)) File.Delete(_blobPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void BumpVersion() { lock (_versionLock) _version++; }

    private OkxResolvedCredentials? ReadFromDisk()
    {
        var protectedBytes = File.ReadAllBytes(_blobPath);
        var plaintext = _protector.Unprotect(protectedBytes);
        var payload = JsonSerializer.Deserialize<OkxCredentialPayload>(plaintext, JsonOptions);
        return payload is null
            ? null
            : new OkxResolvedCredentials(payload.ApiKey, payload.ApiSecret, payload.Passphrase, payload.DemoMode, payload.Region);
    }

    private static OkxCredentialStatus Project(OkxResolvedCredentials c, DateTimeOffset? updatedAt)
    {
        // Never return the full key. Short keys: opaque mask only.
        var hint = c.ApiKey.Length <= 4
            ? "****"
            : "...." + c.ApiKey.Substring(c.ApiKey.Length - 4);
        return new OkxCredentialStatus(true, c.DemoMode, c.Region, hint, updatedAt);
    }

    private static void Validate(OkxCredentialInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ApiKey))
            throw new ArgumentException("API key is required.", nameof(input));
        if (string.IsNullOrWhiteSpace(input.ApiSecret))
            throw new ArgumentException("API secret is required.", nameof(input));
        if (string.IsNullOrWhiteSpace(input.Passphrase))
            throw new ArgumentException("Passphrase is required.", nameof(input));
        var region = (input.Region ?? "").Trim().ToUpperInvariant();
        if (region != "GLOBAL" && region != "AU" && region != "US")
            throw new ArgumentException("Region must be GLOBAL, AU, or US.", nameof(input));
    }
}