using FlowTunnel.Core;
using FlowTunnel.Infrastructure;
using Xunit;

namespace FlowTunnel.Networking.Tests;

public sealed class ProfileImporterTests
{
    [Fact]
    public void ImportsVlessRealityWithoutDroppingSecurityFields()
    {
        var result = new ProfileUriImporter().ImportUri("vless://01890c8f-2c46-7a35-b721-cf3a5f9ebd89@example.com:443?security=reality&sni=cdn.example.com&pbk=test-public-key&sid=0123abcd&flow=xtls-rprx-vision&fp=chrome#Discord");
        Assert.Equal(ProxyProtocol.Vless, result.Protocol);
        Assert.Equal("test-public-key", result.Tls?.Reality?.PublicKey);
        Assert.Equal("0123abcd", result.Tls?.Reality?.ShortId);
        Assert.Equal("chrome", result.Tls?.Fingerprint);
    }

    [Fact]
    public void RejectsRealityWithoutPublicKey() =>
        Assert.Throws<FormatException>(() => new ProfileUriImporter().ImportUri("vless://01890c8f-2c46-7a35-b721-cf3a5f9ebd89@example.com:443?security=reality"));

    [Fact]
    public void RejectsUnsupportedScheme() =>
        Assert.Throws<NotSupportedException>(() => new ProfileUriImporter().ImportUri("hysteria2://secret@example.com:443"));
}
