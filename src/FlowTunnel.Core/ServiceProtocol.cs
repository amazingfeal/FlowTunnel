namespace FlowTunnel.Core;

public static class ServiceProtocol
{
    public const string PipeName = "FlowTunnel.Service.v1";
    public const int MaxMessageBytes = 1_048_576;
}

public sealed record ServiceRequest(string Action, string? ConfigurationJson = null);
public sealed record ServiceResponse(bool Success, string? ErrorCode = null, string? Message = null);
