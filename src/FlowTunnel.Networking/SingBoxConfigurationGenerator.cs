using System.Text.Json;
using System.Text.Json.Nodes;
using FlowTunnel.Core;

namespace FlowTunnel.Networking;

public sealed class SingBoxConfigurationGenerator : IProxyConfigurationGenerator
{
    public const string SupportedVersion = "1.14.4";

    public string Generate(ProxyProfile profile, RoutingMode mode, IReadOnlyCollection<ApplicationRoutingRule> rules, FailSafePolicy failSafePolicy)
    {
        profile.Validate();
        foreach (var rule in rules) rule.Validate();

        var routeRules = new JsonArray
        {
            RouteByName("sing-box.exe", "direct"),
            new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" }
        };

        foreach (var rule in rules.Where(r => r.Enabled).OrderBy(r => r.Priority).ThenBy(r => r.Id))
        {
            var route = rule.Route == ApplicationRoute.Vpn ? "proxy" : "direct";
            var json = new JsonObject { ["action"] = "route", ["outbound"] = route };
            if (!string.IsNullOrWhiteSpace(rule.Identity.ExecutablePath))
                json["process_path"] = new JsonArray(rule.Identity.ExecutablePath);
            else
                json["process_name"] = new JsonArray(rule.Identity.ExecutableName);
            routeRules.Add(json);
        }

        var root = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "info", ["timestamp"] = true },
            ["dns"] = new JsonObject
            {
                ["servers"] = new JsonArray(new JsonObject { ["type"] = "local", ["tag"] = "local-dns" }),
                ["final"] = "local-dns",
                ["strategy"] = "prefer_ipv4",
                ["reverse_mapping"] = true
            },
            ["inbounds"] = new JsonArray(new JsonObject
            {
                ["type"] = "tun",
                ["tag"] = "tun-in",
                ["interface_name"] = "FlowTunnel",
                ["address"] = new JsonArray("172.19.0.1/30", "fdfe:dcba:9876::1/126"),
                ["mtu"] = 9000,
                ["auto_route"] = true,
                ["strict_route"] = true,
                ["dns_mode"] = "hijack"
            }),
            ["outbounds"] = new JsonArray(BuildProxyOutbound(profile),
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" },
                new JsonObject { ["type"] = "block", ["tag"] = "block" }),
            ["route"] = new JsonObject
            {
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = "local-dns",
                ["rules"] = routeRules,
                ["final"] = mode == RoutingMode.OnlySelectedUseVpn ? "direct" : "proxy"
            }
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject BuildProxyOutbound(ProxyProfile profile)
    {
        var outbound = new JsonObject
        {
            ["type"] = profile.Protocol.ToString().ToLowerInvariant(),
            ["tag"] = "proxy",
            ["server"] = profile.Server,
            ["server_port"] = profile.Port
        };
        switch (profile.Protocol)
        {
            case ProxyProtocol.Vless:
                outbound["uuid"] = profile.UserId;
                if (!string.IsNullOrWhiteSpace(profile.Flow)) outbound["flow"] = profile.Flow;
                break;
            case ProxyProtocol.Vmess:
                outbound["uuid"] = profile.UserId;
                outbound["security"] = "auto";
                break;
            case ProxyProtocol.Trojan: outbound["password"] = profile.Password; break;
            case ProxyProtocol.Shadowsocks:
                outbound["method"] = profile.UserId;
                outbound["password"] = profile.Password;
                break;
            case ProxyProtocol.Socks:
            case ProxyProtocol.Http:
                if (!string.IsNullOrWhiteSpace(profile.UserId)) outbound["username"] = profile.UserId;
                if (!string.IsNullOrWhiteSpace(profile.Password)) outbound["password"] = profile.Password;
                break;
        }

        if (profile.Tls is { Enabled: true } tls)
        {
            var tlsJson = new JsonObject { ["enabled"] = true, ["server_name"] = tls.ServerName, ["insecure"] = false };
            if (!string.IsNullOrWhiteSpace(tls.Alpn)) tlsJson["alpn"] = new JsonArray(tls.Alpn.Split(',').Select(x => (JsonNode)x.Trim()).ToArray());
            if (!string.IsNullOrWhiteSpace(tls.Fingerprint)) tlsJson["utls"] = new JsonObject { ["enabled"] = true, ["fingerprint"] = tls.Fingerprint };
            if (tls.Reality is not null) tlsJson["reality"] = new JsonObject { ["enabled"] = true, ["public_key"] = tls.Reality.PublicKey, ["short_id"] = tls.Reality.ShortId ?? "" };
            outbound["tls"] = tlsJson;
        }

        if (profile.Transport is not null)
        {
            if (profile.Transport.Type is not ("ws" or "http" or "grpc" or "quic" or "httpupgrade"))
                throw new NotSupportedException($"Transport '{profile.Transport.Type}' is not supported by the bundled sing-box engine.");
            var transport = new JsonObject { ["type"] = profile.Transport.Type };
            if (!string.IsNullOrWhiteSpace(profile.Transport.Path)) transport["path"] = profile.Transport.Path;
            if (!string.IsNullOrWhiteSpace(profile.Transport.Host))
            {
                if (profile.Transport.Type == "http") transport["host"] = new JsonArray(profile.Transport.Host);
                else if (profile.Transport.Type is "ws" or "httpupgrade") transport["headers"] = new JsonObject { ["Host"] = profile.Transport.Host };
            }
            if (!string.IsNullOrWhiteSpace(profile.Transport.ServiceName)) transport["service_name"] = profile.Transport.ServiceName;
            outbound["transport"] = transport;
        }
        return outbound;
    }

    private static JsonObject RouteByName(string processName, string outbound) => new()
    {
        ["process_name"] = new JsonArray(processName), ["action"] = "route", ["outbound"] = outbound
    };
}
