using FlowTunnel.Core;
using FlowTunnel.Networking;
using FlowTunnel.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "FlowTunnel Tunnel Service");
builder.Services.AddSingleton(sp =>
{
    var installRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(installRoot, "engine.json")));
    return new SingBoxOptions(Path.Combine(installRoot, "sing-box.exe"), metadata.RootElement.GetProperty("sha256").GetString() ?? throw new InvalidDataException("engine.json has no SHA-256."));
});
builder.Services.AddSingleton<SingBoxProcess>();
builder.Services.AddSingleton<IProxyEngine>(sp => sp.GetRequiredService<SingBoxProcess>());
builder.Services.AddSingleton<IProxyConfigurationValidator>(sp => sp.GetRequiredService<SingBoxProcess>());
builder.Services.AddHostedService<TunnelServiceWorker>();
await builder.Build().RunAsync();
