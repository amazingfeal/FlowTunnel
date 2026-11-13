using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlowTunnel.Core;

namespace FlowTunnel.Service;

public sealed class TunnelServiceWorker(IProxyEngine engine, IProxyConfigurationValidator validator, IConfiguration configuration, ILogger<TunnelServiceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The privileged tunnel service only runs on Windows.");
        var runtimeRoot = Path.GetFullPath(configuration["Service:RuntimeRoot"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FlowTunnel", "Runtime"));
        CreateSecureRuntimeDirectory(runtimeRoot);

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = CreatePipe();
            await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                if (!IsActiveConsoleUser(pipe))
                {
                    await WriteAsync(pipe, new(false, "FT-SERVICE-UNAUTHORIZED", "Only the active signed-in Windows user may control the tunnel."), stoppingToken).ConfigureAwait(false);
                    continue;
                }
                var request = await ReadAsync(pipe, stoppingToken).ConfigureAwait(false);
                var response = await HandleAsync(request, runtimeRoot, stoppingToken).ConfigureAwait(false);
                await WriteAsync(pipe, response, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "FT-SERVICE-001: Privileged request failed");
                if (pipe.IsConnected) await WriteAsync(pipe, new(false, "FT-SERVICE-001", ex.Message), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { await engine.StopAsync(cancellationToken).ConfigureAwait(false); }
        finally { await base.StopAsync(cancellationToken).ConfigureAwait(false); }
    }

    private async Task<ServiceResponse> HandleAsync(ServiceRequest request, string runtimeRoot, CancellationToken cancellationToken)
    {
        switch (request.Action)
        {
            case "start":
                var configurationJson = request.ConfigurationJson ?? throw new InvalidDataException("Configuration JSON is required.");
                if (configurationJson.Length > ServiceProtocol.MaxMessageBytes) return new(false, "FT-SERVICE-SIZE", "Configuration is too large.");
                var path = Path.Combine(runtimeRoot, "active.json");
                var temporary = path + ".tmp";
                await File.WriteAllTextAsync(temporary, configurationJson, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, path, true);
                var validation = await validator.ValidateAsync(path, cancellationToken).ConfigureAwait(false);
                if (!validation.IsValid) return new(false, "FT-SERVICE-CONFIG", validation.Error);
                await engine.StartAsync(path, cancellationToken).ConfigureAwait(false);
                return new(true);
            case "stop":
                await engine.StopAsync(cancellationToken).ConfigureAwait(false);
                return new(true);
            case "status": return new(true, Message: (await engine.IsRunningAsync(cancellationToken).ConfigureAwait(false)).ToString());
            default: return new(false, "FT-SERVICE-ACTION", "Unsupported service action.");
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(ServiceProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.WriteThrough, 4096, 4096, security);
    }

    private static void CreateSecureRuntimeDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static bool IsActiveConsoleUser(NamedPipeServerStream pipe)
    {
        SecurityIdentifier? clientSid = null;
        pipe.RunAsClient(() => clientSid = WindowsIdentity.GetCurrent(true)?.User);
        if (!WTSQueryUserToken(WTSGetActiveConsoleSessionId(), out var token)) return false;
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        {
            var activeSid = identity.User;
            return clientSid is not null && activeSid is not null && clientSid.Equals(activeSid);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);

    private static async Task<ServiceRequest> ReadAsync(Stream stream, CancellationToken cancellationToken) =>
        JsonSerializer.Deserialize<ServiceRequest>(await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false)) ?? throw new InvalidDataException("Empty request.");
    private static Task WriteAsync(Stream stream, ServiceResponse response, CancellationToken cancellationToken) => WriteFrameAsync(stream, JsonSerializer.SerializeToUtf8Bytes(response), cancellationToken);

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > ServiceProtocol.MaxMessageBytes) throw new InvalidDataException("Invalid request length.");
        var data = new byte[length]; await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false); return data;
    }
    private static async Task WriteFrameAsync(Stream stream, byte[] data, CancellationToken cancellationToken)
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false); await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false); await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
