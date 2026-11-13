using System.Text.Json;
using FlowTunnel.Core;
using FlowTunnel.Networking;
using Xunit;

namespace FlowTunnel.Networking.Tests;

public sealed class ConfigurationGeneratorTests
{
    private static readonly ProxyProfile Profile = new(Guid.NewGuid(), "Reality", ProxyProtocol.Vless, "example.com", 443,
        "01890c8f-2c46-7a35-b721-cf3a5f9ebd89", null, "xtls-rprx-vision",
        new(true, "www.microsoft.com", null, new("public-key", "0123abcd"), "chrome"), null);

    [Fact]
    public void OnlySelectedMode_UsesDirectFinal_AndPathRule()
    {
        var rule = new ApplicationRoutingRule(Guid.NewGuid(), new("Discord.exe", @"C:\Discord\Discord.exe"), ApplicationRoute.Vpn, 10, true, "Discord", DateTimeOffset.UtcNow);
        using var json = JsonDocument.Parse(new SingBoxConfigurationGenerator().Generate(Profile, RoutingMode.OnlySelectedUseVpn, [rule], FailSafePolicy.Block));
        Assert.Equal("direct", json.RootElement.GetProperty("route").GetProperty("final").GetString());
        var rules = json.RootElement.GetProperty("route").GetProperty("rules");
        Assert.Contains(rules.EnumerateArray(), item => item.TryGetProperty("process_path", out var path) && path[0].GetString() == @"C:\Discord\Discord.exe" && item.GetProperty("outbound").GetString() == "proxy");
    }

    [Fact]
    public void ExceptSelectedMode_UsesProxyFinal_AndDirectException()
    {
        var rule = new ApplicationRoutingRule(Guid.NewGuid(), new("chrome.exe", null), ApplicationRoute.Direct, 0, true, "Chrome", DateTimeOffset.UtcNow);
        using var json = JsonDocument.Parse(new SingBoxConfigurationGenerator().Generate(Profile, RoutingMode.AllUseVpnExceptSelected, [rule], FailSafePolicy.Block));
        Assert.Equal("proxy", json.RootElement.GetProperty("route").GetProperty("final").GetString());
        Assert.Contains(json.RootElement.GetProperty("route").GetProperty("rules").EnumerateArray(), item => item.TryGetProperty("process_name", out var name) && name[0].GetString() == "chrome.exe" && item.GetProperty("outbound").GetString() == "direct");
    }

    [Fact]
    public void EngineItself_IsAlwaysDirect()
    {
        using var json = JsonDocument.Parse(new SingBoxConfigurationGenerator().Generate(Profile, RoutingMode.AllUseVpnExceptSelected, [], FailSafePolicy.Block));
        var first = json.RootElement.GetProperty("route").GetProperty("rules")[0];
        Assert.Equal("sing-box.exe", first.GetProperty("process_name")[0].GetString());
        Assert.Equal("direct", first.GetProperty("outbound").GetString());
    }
}
