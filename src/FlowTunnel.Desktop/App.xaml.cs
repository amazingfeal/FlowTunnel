using FlowTunnel.Application;
using FlowTunnel.Core;
using FlowTunnel.Infrastructure;
using FlowTunnel.Networking;
using FlowTunnel.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;
using System.Text.Json;

namespace FlowTunnel.Desktop;

public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly IHost _host;
    private Window? _window;

    public App()
    {
        InitializeComponent();
        var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowTunnel");
        var runtimeRoot = Path.Combine(dataRoot, "Runtime");
        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FlowTunnel");
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.File(Path.Combine(dataRoot, "Logs", "flowtunnel-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7).CreateLogger();

        var engineMetadata = ReadEngineMetadata(Path.Combine(installRoot, "engine.json"));
        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IProcessDiscoveryService, WindowsProcessDiscoveryService>();
                services.AddSingleton<IPrivilegedTunnelClient, PrivilegedTunnelClient>();
                services.AddSingleton<IProxyEngine, ServiceProxyEngine>();
                services.AddSingleton<IProxyConfigurationGenerator, SingBoxConfigurationGenerator>();
                services.AddSingleton(sp => new SingBoxOptions(Path.Combine(installRoot, "sing-box.exe"), engineMetadata.Sha256));
                services.AddSingleton<SingBoxProcess>();
                services.AddSingleton<IProxyConfigurationValidator>(sp => sp.GetRequiredService<SingBoxProcess>());
                services.AddSingleton<IRoutingRuleRepository>(sp => new RoutingRuleRepository(Path.Combine(dataRoot, "rules.json"), sp.GetRequiredService<ILogger<RoutingRuleRepository>>()));
                services.AddSingleton<IProfileRepository>(sp => new ProfileRepository(Path.Combine(dataRoot, "profiles.json"), sp.GetRequiredService<ILogger<ProfileRepository>>()));
                services.AddSingleton<ISettingsRepository>(sp => new SettingsRepository(Path.Combine(dataRoot, "settings.json"), sp.GetRequiredService<ILogger<SettingsRepository>>()));
                services.AddSingleton<IProfileImporter, ProfileUriImporter>();
                services.AddSingleton<IConnectionManager>(sp => new ConnectionManager(sp.GetRequiredService<IProxyEngine>(), sp.GetRequiredService<IProxyConfigurationGenerator>(), sp.GetRequiredService<IProxyConfigurationValidator>(), sp.GetRequiredService<IRoutingRuleRepository>(), sp.GetRequiredService<ISettingsRepository>(), Path.Combine(runtimeRoot, "active.json"), sp.GetRequiredService<ILogger<ConnectionManager>>()));
                services.AddSingleton<RoutingRuleService>();
                services.AddSingleton<ViewModels.MainViewModel>();
                services.AddSingleton<ViewModels.ApplicationsViewModel>();
                services.AddSingleton<ViewModels.ServersViewModel>();
                services.AddSingleton<ViewModels.SettingsViewModel>();
                services.AddSingleton<MainWindow>();
            }).Build();
    }

    private static EngineMetadata ReadEngineMetadata(string path)
    {
        if (!File.Exists(path)) return new("NOT_CONFIGURED");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return new(json.RootElement.GetProperty("sha256").GetString() ?? "NOT_CONFIGURED");
    }

    private sealed record EngineMetadata(string Sha256);

    public IServiceProvider Services => _host.Services;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await _host.StartAsync();
        _window = _host.Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }
}
