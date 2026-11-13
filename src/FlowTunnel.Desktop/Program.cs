using System.Text.Json;
using FlowTunnel.Application;
using FlowTunnel.Core;
using FlowTunnel.Infrastructure;
using FlowTunnel.Networking;
using FlowTunnel.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FlowTunnel.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowTunnel");
        var runtimeRoot = Path.Combine(dataRoot, "Runtime");
        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FlowTunnel");
        Directory.CreateDirectory(Path.Combine(dataRoot, "Logs"));
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
            .WriteTo.File(Path.Combine(dataRoot, "Logs", "flowtunnel-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        var engineHash = ReadEngineHash(Path.Combine(installRoot, "engine.json"));
        using var host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IProcessDiscoveryService, WindowsProcessDiscoveryService>();
                services.AddSingleton<IPrivilegedTunnelClient, PrivilegedTunnelClient>();
                services.AddSingleton<IProxyEngine, ServiceProxyEngine>();
                services.AddSingleton<IProxyConfigurationGenerator, SingBoxConfigurationGenerator>();
                services.AddSingleton(new SingBoxOptions(Path.Combine(installRoot, "sing-box.exe"), engineHash));
                services.AddSingleton<SingBoxProcess>();
                services.AddSingleton<IProxyConfigurationValidator>(sp => sp.GetRequiredService<SingBoxProcess>());
                services.AddSingleton<IRoutingRuleRepository>(sp => new RoutingRuleRepository(Path.Combine(dataRoot, "rules.json"), sp.GetRequiredService<ILogger<RoutingRuleRepository>>()));
                services.AddSingleton<IProfileRepository>(sp => new ProfileRepository(Path.Combine(dataRoot, "profiles.json"), sp.GetRequiredService<ILogger<ProfileRepository>>()));
                services.AddSingleton<ISettingsRepository>(sp => new SettingsRepository(Path.Combine(dataRoot, "settings.json"), sp.GetRequiredService<ILogger<SettingsRepository>>()));
                services.AddSingleton<IProfileImporter, ProfileUriImporter>();
                services.AddSingleton<IConnectionManager>(sp => new ConnectionManager(sp.GetRequiredService<IProxyEngine>(), sp.GetRequiredService<IProxyConfigurationGenerator>(), sp.GetRequiredService<IProxyConfigurationValidator>(), sp.GetRequiredService<IRoutingRuleRepository>(), sp.GetRequiredService<ISettingsRepository>(), Path.Combine(runtimeRoot, "active.json"), sp.GetRequiredService<ILogger<ConnectionManager>>()));
                services.AddSingleton<RoutingRuleService>();
            }).Build();

        host.Start();
        System.Windows.Forms.Application.Run(new MainForm(host.Services, dataRoot));
        host.StopAsync().GetAwaiter().GetResult();
    }

    private static string ReadEngineHash(string path)
    {
        if (!File.Exists(path)) return "NOT_CONFIGURED";
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty("sha256").GetString() ?? "NOT_CONFIGURED";
    }
}
