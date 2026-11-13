using System.Text;
using System.Text.Json;
using FlowTunnel.Core;

namespace FlowTunnel.Infrastructure;

public sealed class ProfileUriImporter : IProfileImporter
{
    public ProxyProfile ImportUri(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim();
        if (value.Length > 16_384) throw new FormatException("Configuration URI is too long.");
        if (value.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)) return ParseVmess(value[8..]);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) throw new FormatException("Invalid configuration URI.");

        return uri.Scheme.ToLowerInvariant() switch
        {
            "vless" => ParseStandard(uri, ProxyProtocol.Vless),
            "trojan" => ParseStandard(uri, ProxyProtocol.Trojan),
            "ss" => ParseShadowsocks(uri),
            "socks" or "socks5" => ParseStandard(uri, ProxyProtocol.Socks),
            "http" or "https" => ParseStandard(uri, ProxyProtocol.Http),
            _ => throw new NotSupportedException($"Unsupported configuration protocol: {uri.Scheme}")
        };
    }

    private static ProxyProfile ParseStandard(Uri uri, ProxyProtocol protocol)
    {
        var query = ParseQuery(uri.Query);
        var tlsEnabled = Get(query, "security") is "tls" or "reality" || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        RealityOptions? reality = null;
        if (Get(query, "security") == "reality")
        {
            var publicKey = Get(query, "pbk") ?? throw new FormatException("REALITY public key (pbk) is required.");
            reality = new(publicKey, Get(query, "sid"));
        }
        var type = Get(query, "type");
        var transport = type is null or "tcp" ? null : new TransportOptions(type, Get(query, "path"), Get(query, "host"), Get(query, "serviceName"));
        var userInfo = Uri.UnescapeDataString(uri.UserInfo);
        string? user = userInfo;
        string? password = null;
        if (protocol is ProxyProtocol.Trojan) { password = userInfo; user = null; }
        if ((protocol is ProxyProtocol.Http or ProxyProtocol.Socks) && userInfo.Contains(':'))
        {
            var parts = userInfo.Split(':', 2); user = parts[0]; password = parts[1];
        }

        var profile = new ProxyProfile(Guid.NewGuid(), Uri.UnescapeDataString(uri.Fragment.TrimStart('#')) is { Length: > 0 } name ? name : uri.Host,
            protocol, uri.Host, checked((ushort)(uri.Port > 0 ? uri.Port : 443)), user, password, Get(query, "flow"),
            tlsEnabled ? new(true, Get(query, "sni") ?? uri.Host, Get(query, "alpn"), reality, Get(query, "fp")) : null, transport);
        profile.Validate();
        return profile;
    }

    private static ProxyProfile ParseVmess(string payload)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(payload)));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var host = Required(root, "add");
            var port = ushort.Parse(Required(root, "port"), System.Globalization.CultureInfo.InvariantCulture);
            var tls = root.TryGetProperty("tls", out var tlsElement) && tlsElement.GetString() is { Length: > 0 };
            var network = root.TryGetProperty("net", out var net) ? net.GetString() : null;
            var profile = new ProxyProfile(Guid.NewGuid(), root.TryGetProperty("ps", out var ps) ? ps.GetString() ?? host : host,
                ProxyProtocol.Vmess, host, port, Required(root, "id"), null, null,
                tls ? new(true, root.TryGetProperty("sni", out var sni) ? sni.GetString() : host, null, null, null) : null,
                network is null or "tcp" ? null : new(network, root.TryGetProperty("path", out var p) ? p.GetString() : null, root.TryGetProperty("host", out var h) ? h.GetString() : null, null));
            profile.Validate();
            return profile;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or OverflowException)
        { throw new FormatException("Invalid VMess URI.", ex); }
    }

    private static ProxyProfile ParseShadowsocks(Uri uri)
    {
        var userInfo = Uri.UnescapeDataString(uri.UserInfo);
        if (!userInfo.Contains(':')) userInfo = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(userInfo)));
        var split = userInfo.Split(':', 2);
        if (split.Length != 2) throw new FormatException("Shadowsocks URI requires method and password.");
        return new(Guid.NewGuid(), Uri.UnescapeDataString(uri.Fragment.TrimStart('#')) is { Length: > 0 } name ? name : uri.Host,
            ProxyProtocol.Shadowsocks, uri.Host, checked((ushort)uri.Port), split[0], split[1], null, null, null);
    }

    private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "", StringComparer.OrdinalIgnoreCase);
    private static string? Get(Dictionary<string, string> query, string key) => query.TryGetValue(key, out var value) ? value : null;
    private static string Required(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.ToString() is { Length: > 0 } value ? value : throw new FormatException($"VMess field '{name}' is required.");
    private static string PadBase64(string value) { value = value.Replace('-', '+').Replace('_', '/'); return value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '='); }
}
