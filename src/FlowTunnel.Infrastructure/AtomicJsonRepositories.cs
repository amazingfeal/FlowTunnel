using System.Security.Cryptography;
using System.Text.Json;
using FlowTunnel.Core;
using Microsoft.Extensions.Logging;

namespace FlowTunnel.Infrastructure;

public abstract class AtomicJsonRepository<T>(string path, ILogger logger) : IRepository<T>
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    protected string PathName { get; } = path;
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(PathName)) return [];
            await using var stream = File.OpenRead(PathName);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "FT-STORAGE-001: Invalid JSON in {Path}", PathName);
            throw new InvalidDataException("Saved settings are corrupted.", ex);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAllAsync(IReadOnlyCollection<T> items, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
            var temporary = PathName + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, items, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, PathName, true);
        }
        finally { _gate.Release(); }
    }
}

public sealed class RoutingRuleRepository(string path, ILogger<RoutingRuleRepository> logger)
    : AtomicJsonRepository<ApplicationRoutingRule>(path, logger), IRoutingRuleRepository;

public sealed class NetworkRoutingRuleRepository(string path, ILogger<NetworkRoutingRuleRepository> logger)
    : AtomicJsonRepository<NetworkRoutingRule>(path, logger), INetworkRoutingRuleRepository;

public sealed class SubscriptionRepository(string path, ILogger<SubscriptionRepository> logger)
    : AtomicJsonRepository<ProxySubscription>(path, logger), ISubscriptionRepository;

public sealed class ProfileRepository : IProfileRepository
{
    private readonly RawProfileRepository _inner;
    public ProfileRepository(string path, ILogger<ProfileRepository> logger) => _inner = new(path, logger);

    public async Task<IReadOnlyList<ProxyProfile>> GetAllAsync(CancellationToken cancellationToken) =>
        (await _inner.GetAllAsync(cancellationToken).ConfigureAwait(false)).Select(Unprotect).ToArray();

    public Task SaveAllAsync(IReadOnlyCollection<ProxyProfile> items, CancellationToken cancellationToken) =>
        _inner.SaveAllAsync(items.Select(Protect).ToArray(), cancellationToken);

    private static ProxyProfile Protect(ProxyProfile profile) => profile with
    {
        UserId = (profile.Protocol is ProxyProtocol.Vless or ProxyProtocol.Vmess) && profile.UserId is not null ? "dpapi:" + SecretProtector.Protect(profile.UserId) : profile.UserId,
        Password = profile.Password is not null ? "dpapi:" + SecretProtector.Protect(profile.Password) : null
    };
    private static ProxyProfile Unprotect(ProxyProfile profile) => profile with
    {
        UserId = profile.UserId?.StartsWith("dpapi:", StringComparison.Ordinal) == true ? SecretProtector.Unprotect(profile.UserId[6..]) : profile.UserId,
        Password = profile.Password?.StartsWith("dpapi:", StringComparison.Ordinal) == true ? SecretProtector.Unprotect(profile.Password[6..]) : profile.Password
    };
    private sealed class RawProfileRepository(string path, ILogger logger) : AtomicJsonRepository<ProxyProfile>(path, logger);
}

public sealed class SettingsRepository(string path, ILogger<SettingsRepository> logger) : ISettingsRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return new();
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: cancellationToken).ConfigureAwait(false) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger.LogError(ex, "FT-STORAGE-002: Could not read settings");
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally { _gate.Release(); }
    }
}

public static class SecretProtector
{
    private static readonly byte[] Entropy = "FlowTunnel.Profile.v1"u8.ToArray();

    public static string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI profile protection requires Windows.");
        return Convert.ToBase64String(ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser));
    }

    public static string Unprotect(string protectedValue)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI profile protection requires Windows.");
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
