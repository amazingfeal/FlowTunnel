using System.Diagnostics;
using System.Security.Cryptography;
using System.Security;
using System.Text.RegularExpressions;
using FlowTunnel.Core;
using Microsoft.Extensions.Logging;

namespace FlowTunnel.Networking;

public sealed record SingBoxOptions(string BinaryPath, string ExpectedSha256, string RequiredVersion = SingBoxConfigurationGenerator.SupportedVersion);

public sealed partial class SingBoxProcess(SingBoxOptions options, ILogger<SingBoxProcess> logger) : IProxyEngine, IProxyConfigurationValidator, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private bool _stopping;
    public event EventHandler<int>? UnexpectedExit;

    public async Task StartAsync(string configurationPath, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false }) throw new InvalidOperationException("sing-box is already running.");
            await VerifyBinaryAsync(cancellationToken).ConfigureAwait(false);
            var validation = await ValidateAsync(configurationPath, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid) throw new InvalidDataException($"sing-box configuration validation failed: {validation.Error}");

            _stopping = false;
            _process = CreateProcess("run", "-c", configurationPath);
            _process.EnableRaisingEvents = true;
            _process.Exited += OnExited;
            _process.OutputDataReceived += (_, e) => { if (e.Data is not null) logger.LogInformation("sing-box: {Message}", Redact(e.Data)); };
            _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logger.LogWarning("sing-box: {Message}", Redact(e.Data)); };
            if (!_process.Start()) throw new InvalidOperationException("Could not start sing-box.");
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            await Task.Delay(750, cancellationToken).ConfigureAwait(false);
            if (_process.HasExited) throw new InvalidOperationException($"sing-box exited during startup with code {_process.ExitCode}.");
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is null || _process.HasExited) return;
            _stopping = true;
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            _process.Dispose();
            _process = null;
        }
        finally { _gate.Release(); }
    }

    public Task<bool> IsRunningAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_process is { HasExited: false });
    }

    public async Task<ValidationResult> ValidateAsync(string configurationPath, CancellationToken cancellationToken)
    {
        await VerifyBinaryAsync(cancellationToken).ConfigureAwait(false);
        using var process = CreateProcess("check", "-c", configurationPath);
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var error = Redact(await errorTask.ConfigureAwait(false));
        return process.ExitCode == 0 ? new(true, null) : new(false, string.IsNullOrWhiteSpace(error) ? $"Exit code {process.ExitCode}" : error);
    }

    public async Task VerifyBinaryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.BinaryPath)) throw new FileNotFoundException("The pinned sing-box binary is not installed.", options.BinaryPath);
        await using var stream = File.OpenRead(options.BinaryPath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!hash.Equals(options.ExpectedSha256, StringComparison.OrdinalIgnoreCase)) throw new SecurityException("sing-box integrity verification failed.");
        using var version = CreateProcess("version");
        version.Start();
        var output = await version.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await version.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (version.ExitCode != 0 || !output.Contains(options.RequiredVersion, StringComparison.Ordinal))
            throw new NotSupportedException($"FlowTunnel requires sing-box {options.RequiredVersion}.");
    }

    private Process CreateProcess(params string[] arguments)
    {
        var start = new ProcessStartInfo(options.BinaryPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(options.BinaryPath)! };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return new Process { StartInfo = start };
    }

    private void OnExited(object? sender, EventArgs e)
    {
        var process = (Process)sender!;
        if (!_stopping) UnexpectedExit?.Invoke(this, process.ExitCode);
    }

    private static string Redact(string input) => SecretPattern().Replace(input, "$1=<redacted>");
    [GeneratedRegex("(?i)(uuid|password|private_key|public_key|token)(?:\\\"?\\s*[:=]\\s*\\\"?)([^\\\"\\s,}]+)")]
    private static partial Regex SecretPattern();
    public void Dispose() { _process?.Dispose(); _gate.Dispose(); }
}
