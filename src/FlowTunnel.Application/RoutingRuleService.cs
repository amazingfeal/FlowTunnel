using FlowTunnel.Core;

namespace FlowTunnel.Application;

public sealed class RoutingRuleService(IRoutingRuleRepository repository, IConnectionManager connectionManager)
{
    public async Task SetRouteAsync(ProcessIdentity identity, string displayName, ApplicationRoute route, CancellationToken cancellationToken)
    {
        var rules = (await repository.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        rules.RemoveAll(r => r.Identity.StableKey == identity.StableKey);
        if (route != ApplicationRoute.Default)
        {
            var priority = rules.Count == 0 ? 0 : rules.Max(r => r.Priority) + 10;
            rules.Add(new(Guid.NewGuid(), identity, route, priority, true, displayName, DateTimeOffset.UtcNow));
        }
        await repository.SaveAllAsync(rules, cancellationToken).ConfigureAwait(false);
        await connectionManager.ReapplyRoutingAsync(cancellationToken).ConfigureAwait(false);
    }

    public ApplicationRoute Resolve(ProcessIdentity identity, RoutingMode mode, IReadOnlyCollection<ApplicationRoutingRule> rules)
    {
        var match = rules.Where(r => r.Enabled).OrderBy(r => r.Priority)
            .FirstOrDefault(r => string.Equals(r.Identity.ExecutablePath, identity.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(r.Identity.ExecutablePath) && string.Equals(r.Identity.ExecutableName, identity.ExecutableName, StringComparison.OrdinalIgnoreCase)));
        return match?.Route ?? (mode == RoutingMode.OnlySelectedUseVpn ? ApplicationRoute.Direct : ApplicationRoute.Vpn);
    }
}
