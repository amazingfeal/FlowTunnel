using FlowTunnel.Application;
using FlowTunnel.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowTunnel.Application.Tests;

public sealed class ConnectionManagerTests
{
    private static readonly ProxyProfile Profile = new(Guid.NewGuid(), "Server", ProxyProtocol.Socks, "127.0.0.1", 1080, null, null, null, null, null);

    [Fact]
    public async Task Connect_TransitionsOnlyAfterValidatedRunningEngine()
    {
        var engine = new FakeEngine();
        using var fixture = new Fixture(engine, new ValidationResult(true, null));
        await fixture.Manager.ConnectAsync(Profile, CancellationToken.None);
        Assert.Equal(ConnectionState.Connected, fixture.Manager.Snapshot.State);
        Assert.True(engine.Started);
    }

    [Fact]
    public async Task InvalidConfiguration_TransitionsToError_AndNeverStarts()
    {
        var engine = new FakeEngine();
        using var fixture = new Fixture(engine, new ValidationResult(false, "bad config"));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Manager.ConnectAsync(Profile, CancellationToken.None));
        Assert.Equal(ConnectionState.Error, fixture.Manager.Snapshot.State);
        Assert.False(engine.Started);
    }

    [Fact]
    public async Task UnexpectedEngineExit_IsVisible()
    {
        var engine = new FakeEngine();
        using var fixture = new Fixture(engine, new ValidationResult(true, null));
        await fixture.Manager.ConnectAsync(Profile, CancellationToken.None);
        engine.Crash(23);
        Assert.Equal(ConnectionState.Error, fixture.Manager.Snapshot.State);
        Assert.Equal("FT-ENGINE-EXIT", fixture.Manager.Snapshot.ErrorCode);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "FlowTunnelTests", Guid.NewGuid().ToString("N"));
        public Fixture(FakeEngine engine, ValidationResult validation)
        {
            Directory.CreateDirectory(_directory);
            Manager = new ConnectionManager(engine, new FakeGenerator(), new FakeValidator(validation), new EmptyRules(), new MemorySettings(), Path.Combine(_directory, "active.json"), NullLogger<ConnectionManager>.Instance);
        }
        public ConnectionManager Manager { get; }
        public void Dispose() { Manager.Dispose(); Directory.Delete(_directory, true); }
    }

    private sealed class FakeEngine : IProxyEngine
    {
        public bool Started { get; private set; }
        public event EventHandler<int>? UnexpectedExit;
        public Task StartAsync(string configurationPath, CancellationToken cancellationToken) { Started = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { Started = false; return Task.CompletedTask; }
        public Task<bool> IsRunningAsync(CancellationToken cancellationToken) => Task.FromResult(Started);
        public void Crash(int code) { Started = false; UnexpectedExit?.Invoke(this, code); }
    }
    private sealed class FakeGenerator : IProxyConfigurationGenerator
    {
        public string Generate(ProxyProfile profile, RoutingMode mode, IReadOnlyCollection<ApplicationRoutingRule> rules, FailSafePolicy failSafePolicy) => "{}";
    }
    private sealed class FakeValidator(ValidationResult result) : IProxyConfigurationValidator
    {
        public Task<ValidationResult> ValidateAsync(string configurationPath, CancellationToken cancellationToken) => Task.FromResult(result);
    }
    private sealed class EmptyRules : IRoutingRuleRepository
    {
        public Task<IReadOnlyList<ApplicationRoutingRule>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ApplicationRoutingRule>>([]);
        public Task SaveAllAsync(IReadOnlyCollection<ApplicationRoutingRule> items, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class MemorySettings : ISettingsRepository
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new AppSettings());
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
