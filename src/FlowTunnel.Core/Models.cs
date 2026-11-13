namespace FlowTunnel.Core;

public enum RoutingMode { OnlySelectedUseVpn, AllUseVpnExceptSelected }
public enum ApplicationRoute { Default, Vpn, Direct }
public enum ConnectionState { Disconnected, Connecting, Connected, Disconnecting, Reconnecting, Error }
public enum ProxyProtocol { Vless, Vmess, Trojan, Shadowsocks, Socks, Http }
public enum ProcessChangeKind { Started, Stopped }
public enum FailSafePolicy { Block, AllowDirect }

public sealed record ProcessIdentity(string ExecutableName, string? ExecutablePath)
{
    public string StableKey => !string.IsNullOrWhiteSpace(ExecutablePath)
        ? $"path:{Path.GetFullPath(ExecutablePath).ToUpperInvariant()}"
        : $"name:{ExecutableName.ToUpperInvariant()}";
}

public sealed record ProcessInstance(
    int ProcessId,
    string ExecutableName,
    string? ExecutablePath,
    int? ParentProcessId,
    DateTimeOffset? StartTime);

public sealed record RunningApplication(
    ProcessIdentity Identity,
    string DisplayName,
    string? IconPath,
    IReadOnlyList<ProcessInstance> Instances,
    ApplicationRoute Route = ApplicationRoute.Default)
{
    public int ProcessCount => Instances.Count;
}

public sealed record ProcessChange(ProcessChangeKind Kind, ProcessInstance Process);

public sealed record ApplicationRoutingRule(
    Guid Id,
    ProcessIdentity Identity,
    ApplicationRoute Route,
    int Priority,
    bool Enabled,
    string DisplayName,
    DateTimeOffset UpdatedAt)
{
    public void Validate()
    {
        if (Route == ApplicationRoute.Default) throw new ArgumentException("A persisted rule must be VPN or Direct.");
        if (string.IsNullOrWhiteSpace(Identity.ExecutableName)) throw new ArgumentException("Executable name is required.");
        if (Priority < 0) throw new ArgumentOutOfRangeException(nameof(Priority));
    }
}

public sealed record ProxyProfile(
    Guid Id,
    string Name,
    ProxyProtocol Protocol,
    string Server,
    ushort Port,
    string? UserId,
    string? Password,
    string? Flow,
    TlsOptions? Tls,
    TransportOptions? Transport,
    bool IsDefault = false,
    Guid? SubscriptionId = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("Profile name is required.");
        if (string.IsNullOrWhiteSpace(Server)) throw new ArgumentException("Server is required.");
        if (Port is 0) throw new ArgumentException("Port must be between 1 and 65535.");
        if ((Protocol is ProxyProtocol.Vless or ProxyProtocol.Vmess) && !Guid.TryParse(UserId, out _))
            throw new ArgumentException("A valid UUID is required.");
        if (Tls?.Reality is not null && (!Tls.Enabled || string.IsNullOrWhiteSpace(Tls.Reality.PublicKey)))
            throw new ArgumentException("REALITY requires TLS and a public key.");
    }
}

public sealed record TlsOptions(bool Enabled, string? ServerName, string? Alpn, RealityOptions? Reality, string? Fingerprint);
public sealed record RealityOptions(string PublicKey, string? ShortId);
public sealed record TransportOptions(string Type, string? Path, string? Host, string? ServiceName, string? Mode = null, string? Extra = null);

public sealed record ProxySubscription(Guid Id, string Name, string Url, DateTimeOffset? LastUpdatedAt, string? LastError = null);
public sealed record ProfileLatency(Guid ProfileId, TimeSpan? Duration, string? Error)
{
    public bool IsReachable => Duration is not null;
}

public sealed record AppSettings(
    RoutingMode RoutingMode = RoutingMode.OnlySelectedUseVpn,
    Guid? SelectedProfileId = null,
    string Theme = "System",
    bool StartWithWindows = false,
    bool MinimizeToTray = true,
    FailSafePolicy FailSafePolicy = FailSafePolicy.AllowDirect);

public sealed record ConnectionSnapshot(
    ConnectionState State,
    string? ProfileName,
    DateTimeOffset? ConnectedAt,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record EngineDiagnostic(DateTimeOffset Timestamp, string Level, string Message);

public sealed record ConnectionInfo(
    string? ApplicationName,
    string? Executable,
    string DestinationAddress,
    ushort DestinationPort,
    string Protocol,
    ApplicationRoute Decision,
    DateTimeOffset StartedAt,
    string Status);
