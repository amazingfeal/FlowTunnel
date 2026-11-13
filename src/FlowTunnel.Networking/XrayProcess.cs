using System.Diagnostics;
using System.Collections.Concurrent;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FlowTunnel.Core;
using Microsoft.Extensions.Logging;

namespace FlowTunnel.Networking;

public sealed record XrayOptions(string BinaryPath, string ExpectedSha256, string RequiredVersion = XrayConfigurationGenerator.SupportedVersion);

public sealed partial class XrayProcess(XrayOptions options, ILogger<XrayProcess> logger) : IProxyEngine, IProxyConfigurationValidator, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private readonly ConcurrentQueue<string> _startupErrors = new();
    private bool _stopping;
    public event EventHandler<int>? UnexpectedExit;

    public async Task StartAsync(string configurationPath, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false }) throw new InvalidOperationException("Xray is already running.");
            await VerifyBinaryAsync(cancellationToken).ConfigureAwait(false);
            var validation = await ValidateAsync(configurationPath, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid) throw new InvalidDataException($"Xray configuration validation failed: {validation.Error}");
            _stopping = false;
            while (_startupErrors.TryDequeue(out _)) { }
            _process = CreateProcess("run", "-config", configurationPath);
            _process.EnableRaisingEvents = true;
            _process.Exited += OnExited;
            _process.OutputDataReceived += (_, e) => { if (e.Data is not null) logger.LogInformation("Xray: {Message}", Redact(e.Data)); };
            _process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data is null) return;
                var message = Redact(e.Data);
                _startupErrors.Enqueue(message);
                while (_startupErrors.Count > 8) _startupErrors.TryDequeue(out _);
                logger.LogWarning("Xray: {Message}", message);
            };
            if (!_process.Start()) throw new InvalidOperationException("Could not start Xray.");
            _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
            await Task.Delay(750, cancellationToken).ConfigureAwait(false);
            if (_process.HasExited)
            {
                var details = string.Join(Environment.NewLine, _startupErrors);
                throw new InvalidOperationException($"Xray exited during startup with code {_process.ExitCode}.{(string.IsNullOrWhiteSpace(details) ? "" : Environment.NewLine + details)}");
            }
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is null || _process.HasExited) return;
            _stopping = true; _process.Kill(true); await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); _process.Dispose(); _process = null;
        }
        finally { _gate.Release(); }
    }

    public Task<bool> IsRunningAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(_process is { HasExited: false }); }

    public async Task<ValidationResult> ValidateAsync(string configurationPath, CancellationToken cancellationToken)
    {
        await VerifyBinaryAsync(cancellationToken).ConfigureAwait(false);
        using var process = CreateProcess("run", "-test", "-config", configurationPath);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = Redact((await stderr.ConfigureAwait(false)) + " " + (await stdout.ConfigureAwait(false))).Trim();
        return process.ExitCode == 0 ? new(true, null) : new(false, string.IsNullOrWhiteSpace(output) ? $"Exit code {process.ExitCode}" : output);
    }

    private async Task VerifyBinaryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.BinaryPath)) throw new FileNotFoundException("The pinned Xray binary is not installed.", options.BinaryPath);
        await using var stream = File.OpenRead(options.BinaryPath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!hash.Equals(options.ExpectedSha256, StringComparison.OrdinalIgnoreCase)) throw new SecurityException("Xray integrity verification failed.");
        using var version = CreateProcess("version"); version.Start();
        var output = await version.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false); await version.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (version.ExitCode != 0 || !output.Contains(options.RequiredVersion, StringComparison.Ordinal)) throw new NotSupportedException($"FlowTunnel requires Xray {options.RequiredVersion}.");
    }

    private Process CreateProcess(params string[] arguments)
    {
        var start = new ProcessStartInfo(options.BinaryPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(options.BinaryPath)! };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return new Process { StartInfo = start };
    }
    private void OnExited(object? sender, EventArgs e) { var process = (Process)sender!; if (!_stopping) UnexpectedExit?.Invoke(this, process.ExitCode); }
    private static string Redact(string input) => SecretPattern().Replace(input, "$1=<redacted>");
    [GeneratedRegex("(?i)(uuid|password|private_key|public_key|token)(?:\\\"?\\s*[:=]\\s*\\\"?)([^\\\"\\s,}]+)")]
    private static partial Regex SecretPattern();
    public void Dispose() { _process?.Dispose(); _gate.Dispose(); }
}
