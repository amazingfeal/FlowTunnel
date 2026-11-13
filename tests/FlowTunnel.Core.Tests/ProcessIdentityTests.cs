using FlowTunnel.Core;
using Xunit;

namespace FlowTunnel.Core.Tests;

public sealed class ProcessIdentityTests
{
    [Fact]
    public void StableKey_PrefersPath_AndIsCaseInsensitive()
    {
        var first = new ProcessIdentity("Discord.exe", @"C:\Users\A\Discord.exe");
        var second = new ProcessIdentity("other.exe", @"c:\users\a\discord.exe");
        Assert.Equal(first.StableKey, second.StableKey);
    }

    [Fact]
    public void PersistedRule_CannotUseDefaultRoute()
    {
        var rule = new ApplicationRoutingRule(Guid.NewGuid(), new("a.exe", null), ApplicationRoute.Default, 0, true, "A", DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentException>(rule.Validate);
    }
}
