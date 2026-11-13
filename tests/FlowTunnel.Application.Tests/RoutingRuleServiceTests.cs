using FlowTunnel.Application;
using FlowTunnel.Core;
using Xunit;

namespace FlowTunnel.Application.Tests;

public sealed class RoutingRuleServiceTests
{
    [Theory]
    [InlineData(RoutingMode.OnlySelectedUseVpn, ApplicationRoute.Direct)]
    [InlineData(RoutingMode.AllUseVpnExceptSelected, ApplicationRoute.Vpn)]
    public void Resolve_ReturnsModeDefault_WhenNoRule(RoutingMode mode, ApplicationRoute expected)
    {
        var service = new RoutingRuleService(new MemoryRules(), new FakeConnection());
        Assert.Equal(expected, service.Resolve(new("app.exe", null), mode, []));
    }

    [Fact]
    public void Resolve_PathRuleWinsByPriority()
    {
        var rules = new[]
        {
            new ApplicationRoutingRule(Guid.NewGuid(), new("app.exe", @"C:\A\app.exe"), ApplicationRoute.Vpn, 0, true, "A", DateTimeOffset.UtcNow),
            new ApplicationRoutingRule(Guid.NewGuid(), new("app.exe", null), ApplicationRoute.Direct, 10, true, "A", DateTimeOffset.UtcNow)
        };
        var service = new RoutingRuleService(new MemoryRules(), new FakeConnection());
        Assert.Equal(ApplicationRoute.Vpn, service.Resolve(new("app.exe", @"C:\A\app.exe"), RoutingMode.OnlySelectedUseVpn, rules));
    }

    [Theory]
    [InlineData(ApplicationRoute.Vpn)]
    [InlineData(ApplicationRoute.Direct)]
    public async Task SetRouteAsync_PersistsExplicitSelection(ApplicationRoute route)
    {
        var repository = new MemoryRules();
        var service = new RoutingRuleService(repository, new FakeConnection());
        var identity = new ProcessIdentity("Discord.exe", @"C:\Apps\Discord.exe");

        await service.SetRouteAsync(identity, "Discord", route, CancellationToken.None);

        var saved = Assert.Single(repository.Items);
        Assert.Equal(route, saved.Route);
        Assert.Equal(identity.StableKey, saved.Identity.StableKey);
    }

    private sealed class MemoryRules : IRoutingRuleRepository
    {
        public List<ApplicationRoutingRule> Items { get; } = [];
        public Task<IReadOnlyList<ApplicationRoutingRule>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ApplicationRoutingRule>>(Items);
        public Task SaveAllAsync(IReadOnlyCollection<ApplicationRoutingRule> items, CancellationToken cancellationToken) { Items.Clear(); Items.AddRange(items); return Task.CompletedTask; }
    }
    private sealed class FakeConnection : IConnectionManager
    {
        public ConnectionSnapshot Snapshot { get; } = new(ConnectionState.Disconnected, null, null, null, null);
        public event EventHandler<ConnectionSnapshot>? StateChanged { add { } remove { } }
        public Task ConnectAsync(ProxyProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReapplyRoutingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
