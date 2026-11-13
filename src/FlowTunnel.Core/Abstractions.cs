namespace FlowTunnel.Core;

public interface IProcessDiscoveryService
{
    Task<IReadOnlyList<RunningApplication>> GetRunningApplicationsAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<ProcessChange> WatchProcessChangesAsync(CancellationToken cancellationToken);
}

public interface IRepository<T>
{
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken);
    Task SaveAllAsync(IReadOnlyCollection<T> items, CancellationToken cancellationToken);
}

public interface IRoutingRuleRepository : IRepository<ApplicationRoutingRule> { }
public interface IProfileRepository : IRepository<ProxyProfile> { }
public interface ISubscriptionRepository : IRepository<ProxySubscription> { }
public interface ISettingsRepository
{
    Task<AppSettings> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}

public interface IProxyEngine
{
    event EventHandler<int>? UnexpectedExit;
    Task StartAsync(string configurationPath, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task<bool> IsRunningAsync(CancellationToken cancellationToken);
}

public interface IProxyConfigurationGenerator
{
    string Generate(ProxyProfile profile, RoutingMode mode, IReadOnlyCollection<ApplicationRoutingRule> rules, FailSafePolicy failSafePolicy);
}

public sealed record ValidationResult(bool IsValid, string? Error);

public interface IProxyConfigurationValidator
{
    Task<ValidationResult> ValidateAsync(string configurationPath, CancellationToken cancellationToken);
}

public interface IConnectionManager
{
    ConnectionSnapshot Snapshot { get; }
    event EventHandler<ConnectionSnapshot>? StateChanged;
    Task ConnectAsync(ProxyProfile profile, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task ReapplyRoutingAsync(CancellationToken cancellationToken);
}

public interface IProfileImporter
{
    ProxyProfile ImportUri(string value);
}

public interface ISubscriptionService
{
    Task<ProxySubscription> AddOrUpdateAsync(ProxySubscription subscription, CancellationToken cancellationToken);
    Task RemoveAsync(Guid subscriptionId, CancellationToken cancellationToken);
}

public interface IProfileLatencyService
{
    Task<ProfileLatency> MeasureAsync(ProxyProfile profile, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface INetworkDiagnostics
{
    IAsyncEnumerable<EngineDiagnostic> StreamAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync(CancellationToken cancellationToken);
}

public interface IPrivilegedTunnelClient
{
    Task StartTunnelAsync(string approvedConfigurationPath, CancellationToken cancellationToken);
    Task StopTunnelAsync(CancellationToken cancellationToken);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}
