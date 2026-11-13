using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using FlowTunnel.Core;

namespace FlowTunnel.Networking;

public sealed class SubscriptionService(
    HttpClient httpClient,
    ISubscriptionRepository subscriptions,
    IProfileRepository profiles,
    IProfileImporter importer) : ISubscriptionService
{
    public async Task<ProxySubscription> AddOrUpdateAsync(ProxySubscription subscription, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(subscription.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Subscription URL must be a valid HTTP or HTTPS address.");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("FlowTunnel/0.2");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.Length > 5 * 1024 * 1024) throw new InvalidDataException("Subscription is larger than 5 MB.");

        var lines = DecodeLines(Encoding.UTF8.GetString(bytes));
        var imported = new List<ProxyProfile>();
        var errors = new List<string>();
        foreach (var line in lines)
        {
            try { imported.Add(importer.ImportUri(line) with { SubscriptionId = subscription.Id, IsDefault = false }); }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { errors.Add(ex.Message); }
        }
        if (imported.Count == 0)
            throw new InvalidDataException(errors.Count == 0 ? "The subscription contains no supported configurations." : $"No supported configuration was found. {errors[0]}");

        var existingProfiles = (await profiles.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var oldDefault = existingProfiles.FirstOrDefault(p => p.SubscriptionId == subscription.Id && p.IsDefault);
        existingProfiles.RemoveAll(p => p.SubscriptionId == subscription.Id);
        if (oldDefault is not null)
        {
            var replacement = imported.FirstOrDefault(p => SameEndpoint(p, oldDefault));
            if (replacement is not null) imported[imported.IndexOf(replacement)] = replacement with { IsDefault = true };
        }
        if (existingProfiles.All(p => !p.IsDefault) && imported.All(p => !p.IsDefault)) imported[0] = imported[0] with { IsDefault = true };
        existingProfiles.AddRange(imported);
        await profiles.SaveAllAsync(existingProfiles, cancellationToken).ConfigureAwait(false);

        var saved = subscription with { Name = string.IsNullOrWhiteSpace(subscription.Name) ? uri.Host : subscription.Name.Trim(), LastUpdatedAt = DateTimeOffset.Now, LastError = null };
        var all = (await subscriptions.GetAllAsync(cancellationToken).ConfigureAwait(false)).Where(x => x.Id != saved.Id).Append(saved).ToArray();
        await subscriptions.SaveAllAsync(all, cancellationToken).ConfigureAwait(false);
        return saved;
    }

    public async Task RemoveAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var allSubscriptions = (await subscriptions.GetAllAsync(cancellationToken).ConfigureAwait(false)).Where(x => x.Id != subscriptionId).ToArray();
        await subscriptions.SaveAllAsync(allSubscriptions, cancellationToken).ConfigureAwait(false);
        var allProfiles = (await profiles.GetAllAsync(cancellationToken).ConfigureAwait(false)).Where(x => x.SubscriptionId != subscriptionId).ToList();
        if (allProfiles.Count > 0 && allProfiles.All(x => !x.IsDefault)) allProfiles[0] = allProfiles[0] with { IsDefault = true };
        await profiles.SaveAllAsync(allProfiles, cancellationToken).ConfigureAwait(false);
    }

    public static IReadOnlyList<string> DecodeLines(string content)
    {
        var value = content.Trim().TrimStart('\uFEFF');
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            try
            {
                var normalized = value.Replace('-', '+').Replace('_', '/');
                normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
                value = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            }
            catch (FormatException) { }
        }
        return value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !x.StartsWith('#') && x.Contains("://", StringComparison.Ordinal)).Distinct().ToArray();
    }

    private static bool SameEndpoint(ProxyProfile left, ProxyProfile right) =>
        left.Protocol == right.Protocol && left.Server.Equals(right.Server, StringComparison.OrdinalIgnoreCase) && left.Port == right.Port && left.Name == right.Name;
}

public sealed class TcpProfileLatencyService : IProfileLatencyService
{
    public async Task<ProfileLatency> MeasureAsync(ProxyProfile profile, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(profile.Server, profile.Port, timeoutSource.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new(profile.Id, stopwatch.Elapsed, null);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return new(profile.Id, null, ex is OperationCanceledException ? "Timeout" : "Unreachable");
        }
    }
}
