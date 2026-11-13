using FlowTunnel.Core;
using FlowTunnel.Networking;
using FlowTunnel.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "FlowTunnel.Service");
builder.Services.AddSingleton(sp =>
{
    var installRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(installRoot, "engine.json")));
    return new XrayOptions(Path.Combine(installRoot, "xray.exe"), metadata.RootElement.GetProperty("sha256").GetString() ?? throw new InvalidDataException("engine.json has no SHA-256."));
});
builder.Services.AddSingleton<XrayProcess>();
builder.Services.AddSingleton<IProxyEngine>(sp => sp.GetRequiredService<XrayProcess>());
builder.Services.AddSingleton<IProxyConfigurationValidator>(sp => sp.GetRequiredService<XrayProcess>());
builder.Services.AddHostedService<TunnelServiceWorker>();
await builder.Build().RunAsync();
