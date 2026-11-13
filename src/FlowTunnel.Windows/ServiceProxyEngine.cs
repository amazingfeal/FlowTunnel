using FlowTunnel.Core;

namespace FlowTunnel.Windows;

public sealed class ServiceProxyEngine(IPrivilegedTunnelClient client) : IProxyEngine, IDisposable
{
    private CancellationTokenSource? _monitorCancellation;
    private bool _stopping;
    public event EventHandler<int>? UnexpectedExit;

    public async Task StartAsync(string configurationPath, CancellationToken cancellationToken)
    {
        _stopping = false;
        await client.StartTunnelAsync(configurationPath, cancellationToken).ConfigureAwait(false);
        _monitorCancellation?.Cancel();
        _monitorCancellation = new();
        _ = MonitorAsync(_monitorCancellation.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        _monitorCancellation?.Cancel();
        await client.StopTunnelAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> IsRunningAsync(CancellationToken cancellationToken) => client.IsAvailableAsync(cancellationToken);

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                if (!await client.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!_stopping) UnexpectedExit?.Invoke(this, -1);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public void Dispose() { _monitorCancellation?.Cancel(); _monitorCancellation?.Dispose(); }
}
