using System.Text;
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
    public void OnlySelectedMode_UsesDirectAsDefault_AndProcessPathRule()
    {
        var rule = new ApplicationRoutingRule(Guid.NewGuid(), new("Discord.exe", @"C:\Discord\Discord.exe"), ApplicationRoute.Vpn, 10, true, "Discord", DateTimeOffset.UtcNow);
        using var json = JsonDocument.Parse(new XrayConfigurationGenerator().Generate(Profile, RoutingMode.OnlySelectedUseVpn, [rule], FailSafePolicy.Block));
        Assert.Equal("direct", json.RootElement.GetProperty("outbounds")[0].GetProperty("tag").GetString());
        Assert.Contains(json.RootElement.GetProperty("routing").GetProperty("rules").EnumerateArray(), item => item.GetProperty("process")[0].GetString() == "C:/Discord/Discord.exe" && item.GetProperty("outboundTag").GetString() == "proxy");
    }

    [Fact]
    public void ExceptSelectedMode_UsesProxyAsDefault_AndDirectException()
    {
        var rule = new ApplicationRoutingRule(Guid.NewGuid(), new("chrome.exe", null), ApplicationRoute.Direct, 0, true, "Chrome", DateTimeOffset.UtcNow);
        using var json = JsonDocument.Parse(new XrayConfigurationGenerator().Generate(Profile, RoutingMode.AllUseVpnExceptSelected, [rule], FailSafePolicy.Block));
        Assert.Equal("proxy", json.RootElement.GetProperty("outbounds")[0].GetProperty("tag").GetString());
        Assert.Contains(json.RootElement.GetProperty("routing").GetProperty("rules").EnumerateArray(), item => item.GetProperty("process")[0].GetString() == "chrome" && item.GetProperty("outboundTag").GetString() == "direct");
    }

    [Fact]
    public void EngineItself_IsAlwaysDirect()
    {
        using var json = JsonDocument.Parse(new XrayConfigurationGenerator().Generate(Profile, RoutingMode.AllUseVpnExceptSelected, [], FailSafePolicy.Block));
        var first = json.RootElement.GetProperty("routing").GetProperty("rules")[0];
        Assert.Equal("xray/", first.GetProperty("process")[0].GetString());
        Assert.Equal("direct", first.GetProperty("outboundTag").GetString());
    }

    [Fact]
    public void XhttpTransport_PreservesModePathAndReality()
    {
        var profile = Profile with { Transport = new("xhttp", "/tunnel", "cdn.example.com", null, "auto") };
        using var json = JsonDocument.Parse(new XrayConfigurationGenerator().Generate(profile, RoutingMode.AllUseVpnExceptSelected, [], FailSafePolicy.Block));
        var stream = json.RootElement.GetProperty("outbounds")[0].GetProperty("streamSettings");
        Assert.Equal("xhttp", stream.GetProperty("method").GetString());
        Assert.Equal("auto", stream.GetProperty("xhttpSettings").GetProperty("mode").GetString());
        Assert.Equal("/tunnel", stream.GetProperty("xhttpSettings").GetProperty("path").GetString());
        Assert.Equal("public-key", stream.GetProperty("realitySettings").GetProperty("password").GetString());
    }

    [Fact]
    public void SubscriptionDecoder_AcceptsBase64AndPlainText()
    {
        const string profile = "vless://id@example.com:443";
        Assert.Equal(profile, SubscriptionService.DecodeLines(profile)[0]);
        Assert.Equal(profile, SubscriptionService.DecodeLines(Convert.ToBase64String(Encoding.UTF8.GetBytes(profile + "\n")))[0]);
    }
}
