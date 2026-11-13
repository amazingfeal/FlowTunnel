using FlowTunnel.Core;
using Microsoft.Extensions.Logging;

namespace FlowTunnel.Application;

public sealed class ConnectionManager : IConnectionManager, IDisposable
{
    private readonly IProxyEngine _engine;
    private readonly IProxyConfigurationGenerator _generator;
    private readonly IProxyConfigurationValidator _validator;
    private readonly IRoutingRuleRepository _rules;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<ConnectionManager> _logger;
    private readonly string _configurationPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProxyProfile? _activeProfile;

    public ConnectionManager(IProxyEngine engine, IProxyConfigurationGenerator generator, IProxyConfigurationValidator validator,
        IRoutingRuleRepository rules, ISettingsRepository settings, string configurationPath, ILogger<ConnectionManager> logger)
    {
        _engine = engine; _generator = generator; _validator = validator; _rules = rules; _settings = settings;
        _configurationPath = configurationPath; _logger = logger;
        _engine.UnexpectedExit += OnUnexpectedExit;
    }

    public ConnectionSnapshot Snapshot { get; private set; } = new(ConnectionState.Disconnected, null, null, null, null);
    public event EventHandler<ConnectionSnapshot>? StateChanged;

    public async Task ConnectAsync(ProxyProfile profile, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Snapshot.State is ConnectionState.Connecting or ConnectionState.Disconnecting) throw new InvalidOperationException("A connection transition is already in progress.");
            SetState(new(ConnectionState.Connecting, profile.Name, null, null, null));
            try
            {
                var rules = await _rules.GetAllAsync(cancellationToken).ConfigureAwait(false);
                var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
                var configuration = _generator.Generate(profile, settings.RoutingMode, rules, settings.FailSafePolicy);
                await WriteAtomicallyAsync(_configurationPath, configuration, cancellationToken).ConfigureAwait(false);
                var validation = await _validator.ValidateAsync(_configurationPath, cancellationToken).ConfigureAwait(false);
                if (!validation.IsValid) throw new InvalidDataException(validation.Error);
                await _engine.StartAsync(_configurationPath, cancellationToken).ConfigureAwait(false);
                if (!await _engine.IsRunningAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("The proxy engine did not remain running.");
                _activeProfile = profile;
                SetState(new(ConnectionState.Connected, profile.Name, DateTimeOffset.UtcNow, null, null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FT-CONNECT-001: Connection failed for profile {Profile}", profile.Name);
                try { await _engine.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception stopError) { _logger.LogError(stopError, "FT-CONNECT-002: Cleanup failed"); }
                SetState(new(ConnectionState.Error, profile.Name, null, "FT-CONNECT-001", ex.Message));
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetState(Snapshot with { State = ConnectionState.Disconnecting, ErrorCode = null, ErrorMessage = null });
            try
            {
                await _engine.StopAsync(cancellationToken).ConfigureAwait(false);
                _activeProfile = null;
                SetState(new(ConnectionState.Disconnected, null, null, null, null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FT-DISCONNECT-001: Failed to stop tunnel cleanly");
                SetState(Snapshot with { State = ConnectionState.Error, ErrorCode = "FT-DISCONNECT-001", ErrorMessage = ex.Message });
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task ReapplyRoutingAsync(CancellationToken cancellationToken)
    {
        var profile = _activeProfile;
        if (profile is null || Snapshot.State != ConnectionState.Connected) return;
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await ConnectAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    private void OnUnexpectedExit(object? sender, int exitCode)
    {
        _logger.LogError("FT-ENGINE-EXIT: Xray exited unexpectedly with code {ExitCode}", exitCode);
        SetState(Snapshot with { State = ConnectionState.Error, ErrorCode = "FT-ENGINE-EXIT", ErrorMessage = $"The network engine stopped unexpectedly (exit code {exitCode}). Windows may restore direct routing; close protected applications if direct fallback is unsafe." });
    }

    private void SetState(ConnectionSnapshot snapshot) { Snapshot = snapshot; StateChanged?.Invoke(this, snapshot); }

    private static async Task WriteAtomicallyAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

    public void Dispose() { _engine.UnexpectedExit -= OnUnexpectedExit; _gate.Dispose(); }
}
