using System.Text.Json;
using System.Text.Json.Nodes;
using FlowTunnel.Core;

namespace FlowTunnel.Networking;

public sealed class XrayConfigurationGenerator : IProxyConfigurationGenerator
{
    public const string SupportedVersion = "26.11.9";

    public string Generate(ProxyProfile profile, RoutingMode mode, IReadOnlyCollection<ApplicationRoutingRule> rules, FailSafePolicy failSafePolicy)
    {
        profile.Validate();
        foreach (var rule in rules) rule.Validate();

        var routeRules = new JsonArray
        {
            new JsonObject { ["type"] = "field", ["process"] = new JsonArray("xray/"), ["outboundTag"] = "direct" }
        };
        foreach (var rule in rules.Where(r => r.Enabled).OrderBy(r => r.Priority).ThenBy(r => r.Id))
        {
            var process = !string.IsNullOrWhiteSpace(rule.Identity.ExecutablePath)
                ? rule.Identity.ExecutablePath.Replace('\\', '/')
                : Path.GetFileNameWithoutExtension(rule.Identity.ExecutableName);
            routeRules.Add(new JsonObject
            {
                ["type"] = "field",
                ["process"] = new JsonArray(process),
                ["outboundTag"] = rule.Route == ApplicationRoute.Vpn ? "proxy" : "direct"
            });
        }

        var proxy = BuildProxyOutbound(profile);
        var direct = new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom", ["settings"] = new JsonObject() };
        var block = new JsonObject { ["tag"] = "block", ["protocol"] = "blackhole", ["settings"] = new JsonObject() };
        var outbounds = mode == RoutingMode.OnlySelectedUseVpn ? new JsonArray(direct, proxy, block) : new JsonArray(proxy, direct, block);

        var root = new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning" },
            ["dns"] = new JsonObject { ["servers"] = new JsonArray("1.1.1.1", "8.8.8.8"), ["queryStrategy"] = "UseIP" },
            ["inbounds"] = new JsonArray(new JsonObject
            {
                ["tag"] = "tun-in",
                ["protocol"] = "tun",
                ["settings"] = new JsonObject
                {
                    ["name"] = "FlowTunnel",
                    ["desc"] = "FlowTunnel",
                    ["mtu"] = 1500,
                    ["gateway"] = new JsonArray("172.19.0.1/30", "fdfe:dcba:9876::1/126"),
                    ["dns"] = new JsonArray("1.1.1.1", "8.8.8.8"),
                    ["autoSystemRoutingTable"] = new JsonArray("0.0.0.0/0", "::/0"),
                    ["autoOutboundsInterface"] = "auto"
                },
                ["sniffing"] = new JsonObject { ["enabled"] = true, ["destOverride"] = new JsonArray("http", "tls", "quic"), ["routeOnly"] = true }
            }),
            ["outbounds"] = outbounds,
            ["routing"] = new JsonObject { ["domainStrategy"] = "AsIs", ["rules"] = routeRules }
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject BuildProxyOutbound(ProxyProfile profile)
    {
        var settings = profile.Protocol switch
        {
            ProxyProtocol.Vless => new JsonObject { ["address"] = profile.Server, ["port"] = profile.Port, ["id"] = profile.UserId, ["encryption"] = "none", ["flow"] = profile.Flow ?? "" },
            ProxyProtocol.Vmess => new JsonObject { ["address"] = profile.Server, ["port"] = profile.Port, ["id"] = profile.UserId, ["security"] = "auto" },
            ProxyProtocol.Trojan => new JsonObject { ["address"] = profile.Server, ["port"] = profile.Port, ["password"] = profile.Password },
            ProxyProtocol.Shadowsocks => new JsonObject { ["address"] = profile.Server, ["port"] = profile.Port, ["method"] = profile.UserId, ["password"] = profile.Password },
            ProxyProtocol.Socks => new JsonObject { ["address"] = profile.Server, ["port"] = profile.Port, ["user"] = profile.UserId, ["pass"] = profile.Password },
            ProxyProtocol.Http => new JsonObject { ["address"] = profile.Server, ["port"] = profile.Port, ["user"] = profile.UserId, ["pass"] = profile.Password },
            _ => throw new NotSupportedException($"Unsupported Xray outbound protocol: {profile.Protocol}")
        };
        var outbound = new JsonObject { ["tag"] = "proxy", ["protocol"] = profile.Protocol.ToString().ToLowerInvariant(), ["settings"] = settings };
        outbound["streamSettings"] = BuildStreamSettings(profile);
        return outbound;
    }

    private static JsonObject BuildStreamSettings(ProxyProfile profile)
    {
        var sourceType = profile.Transport?.Type ?? "raw";
        var method = sourceType switch { "ws" => "websocket", "http" => "xhttp", "xhttp" => "xhttp", "grpc" => "grpc", "httpupgrade" => "httpupgrade", "mkcp" => "mkcp", _ => sourceType };
        var stream = new JsonObject { ["method"] = method, ["security"] = profile.Tls?.Reality is not null ? "reality" : profile.Tls is { Enabled: true } ? "tls" : "none" };
        var transport = profile.Transport;
        if (transport is not null)
        {
            var values = new JsonObject();
            if (!string.IsNullOrWhiteSpace(transport.Path)) values["path"] = transport.Path;
            if (!string.IsNullOrWhiteSpace(transport.Host)) values["host"] = transport.Host;
            if (method == "xhttp")
            {
                values["mode"] = transport.Mode ?? (sourceType == "http" ? "stream-one" : "auto");
                if (!string.IsNullOrWhiteSpace(transport.Extra))
                {
                    try { values["extra"] = JsonNode.Parse(transport.Extra); } catch (JsonException) { }
                }
            }
            if (method == "websocket" && !string.IsNullOrWhiteSpace(transport.Host)) values["headers"] = new JsonObject { ["Host"] = transport.Host };
            if (method == "grpc") { values.Remove("path"); values.Remove("host"); values["serviceName"] = transport.ServiceName ?? transport.Path ?? ""; }
            stream[method switch { "websocket" => "wsSettings", "xhttp" => "xhttpSettings", "grpc" => "grpcSettings", "httpupgrade" => "httpupgradeSettings", "mkcp" => "kcpSettings", _ => method + "Settings" }] = values;
        }

        if (profile.Tls?.Reality is { } reality)
        {
            stream["realitySettings"] = new JsonObject
            {
                ["serverName"] = profile.Tls.ServerName ?? profile.Server,
                ["fingerprint"] = profile.Tls.Fingerprint ?? "chrome",
                ["password"] = reality.PublicKey,
                ["shortId"] = reality.ShortId ?? "",
                ["spiderX"] = profile.Transport?.Path ?? "/"
            };
        }
        else if (profile.Tls is { Enabled: true } tls)
        {
            var tlsSettings = new JsonObject { ["serverName"] = tls.ServerName ?? profile.Server, ["allowInsecure"] = false, ["fingerprint"] = tls.Fingerprint ?? "chrome" };
            if (!string.IsNullOrWhiteSpace(tls.Alpn)) tlsSettings["alpn"] = new JsonArray(tls.Alpn.Split(',').Select(x => (JsonNode)x.Trim()).ToArray());
            stream["tlsSettings"] = tlsSettings;
        }
        return stream;
    }
}
