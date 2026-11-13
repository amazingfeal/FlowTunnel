using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using FlowTunnel.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace FlowTunnel.Windows;

public sealed class WindowsProcessDiscoveryService(ILogger<WindowsProcessDiscoveryService> logger) : IProcessDiscoveryService
{
    public Task<IReadOnlyList<RunningApplication>> GetRunningApplicationsAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<RunningApplication>>(() => Enumerate(cancellationToken), cancellationToken);

    public async IAsyncEnumerable<ProcessChange> WatchProcessChangesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var previous = new Dictionary<int, ProcessInstance>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var currentApps = await GetRunningApplicationsAsync(cancellationToken).ConfigureAwait(false);
            var current = currentApps.SelectMany(a => a.Instances).ToDictionary(p => p.ProcessId);
            foreach (var process in current.Values.Where(p => !previous.ContainsKey(p.ProcessId))) yield return new(ProcessChangeKind.Started, process);
            foreach (var process in previous.Values.Where(p => !current.ContainsKey(p.ProcessId))) yield return new(ProcessChangeKind.Stopped, process);
            previous = current;
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
    }

    private IReadOnlyList<RunningApplication> Enumerate(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var parents = ReadParentProcessIds();
        var instances = new List<ProcessInstance>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var name = process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? process.ProcessName : process.ProcessName + ".exe";
                    var path = TryGetPath(process.Id);
                    DateTimeOffset? started = null;
                    try { started = process.StartTime; } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
                    instances.Add(new(process.Id, name, path, parents.GetValueOrDefault(process.Id), started));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                { logger.LogDebug(ex, "Process {ProcessId} became inaccessible during enumeration", process.Id); }
            }
        }

        return instances.GroupBy(p => !string.IsNullOrWhiteSpace(p.ExecutablePath) ? p.ExecutablePath : p.ExecutableName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var display = first.ExecutablePath is null ? Path.GetFileNameWithoutExtension(first.ExecutableName) : GetDisplayName(first.ExecutablePath, first.ExecutableName);
                return new RunningApplication(new(first.ExecutableName, first.ExecutablePath), display, first.ExecutablePath, group.OrderBy(p => p.ProcessId).ToArray());
            }).OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static string GetDisplayName(string path, string fallback)
    {
        try { return FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description ? description : Path.GetFileNameWithoutExtension(fallback); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Path.GetFileNameWithoutExtension(fallback); }
    }

    private static string? TryGetPath(int processId)
    {
        using var handle = OpenProcess(0x1000, false, processId);
        if (handle.IsInvalid) return null;
        var capacity = 32768;
        var builder = new StringBuilder(capacity);
        return QueryFullProcessImageName(handle, 0, builder, ref capacity) ? builder.ToString() : null;
    }

    private static Dictionary<int, int?> ReadParentProcessIds()
    {
        var result = new Dictionary<int, int?>();
        using var snapshot = CreateToolhelp32Snapshot(0x00000002, 0);
        if (snapshot.IsInvalid) return result;
        var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
        if (!Process32First(snapshot, ref entry)) return result;
        do { result[checked((int)entry.ProcessId)] = entry.ParentProcessId == 0 ? null : checked((int)entry.ParentProcessId); }
        while (Process32Next(snapshot, ref entry));
        return result;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint processAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder executablePath, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExecutableFile;
    }
}
