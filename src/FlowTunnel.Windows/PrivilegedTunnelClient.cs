using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using FlowTunnel.Core;

namespace FlowTunnel.Windows;

public sealed class PrivilegedTunnelClient : IPrivilegedTunnelClient
{
    public async Task StartTunnelAsync(string approvedConfigurationPath, CancellationToken cancellationToken) =>
        EnsureSuccess(await SendAsync(new("start", await File.ReadAllTextAsync(approvedConfigurationPath, cancellationToken).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false));
    public async Task StopTunnelAsync(CancellationToken cancellationToken) => EnsureSuccess(await SendAsync(new("stop"), cancellationToken).ConfigureAwait(false));
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAsync(new("status"), cancellationToken).ConfigureAwait(false);
            return response.Success && bool.TryParse(response.Message, out var running) && running;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException) { return false; }
    }

    private static async Task<ServiceResponse> SendAsync(ServiceRequest request, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", ServiceProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(3000, cancellationToken).ConfigureAwait(false);
        var data = JsonSerializer.SerializeToUtf8Bytes(request);
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
        await pipe.WriteAsync(header, cancellationToken).ConfigureAwait(false); await pipe.WriteAsync(data, cancellationToken).ConfigureAwait(false); await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        await pipe.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > ServiceProtocol.MaxMessageBytes) throw new InvalidDataException("Invalid service response.");
        data = new byte[length]; await pipe.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ServiceResponse>(data) ?? throw new InvalidDataException("Empty service response.");
    }
    private static void EnsureSuccess(ServiceResponse response) { if (!response.Success) throw new InvalidOperationException($"{response.ErrorCode}: {response.Message}"); }
}
