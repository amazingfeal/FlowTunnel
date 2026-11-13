using FlowTunnel.Application;
using FlowTunnel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FlowTunnel.Desktop;

internal sealed class MainForm : Form
{
    private readonly IConnectionManager _connection;
    private readonly IProfileRepository _profiles;
    private readonly IProfileImporter _importer;
    private readonly IProcessDiscoveryService _processes;
    private readonly RoutingRuleService _routing;
    private readonly string _dataRoot;
    private readonly Label _status = new() { AutoSize = true, Font = new Font("Segoe UI", 18, FontStyle.Bold), Text = "Disconnected" };
    private readonly Label _profile = new() { AutoSize = true, ForeColor = Color.DimGray, Text = "No server selected" };
    private readonly Button _toggle = new() { Text = "Connect", Width = 160, Height = 42 };
    private readonly TextBox _uri = new() { Dock = DockStyle.Top, PlaceholderText = "Paste vmess://, vless://, trojan:// or ss:// link" };
    private readonly ListBox _serverList = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _apps = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, ReadOnly = true };
    private readonly NotifyIcon _tray;

    public MainForm(IServiceProvider services, string dataRoot)
    {
        _connection = services.GetRequiredService<IConnectionManager>();
        _profiles = services.GetRequiredService<IProfileRepository>();
        _importer = services.GetRequiredService<IProfileImporter>();
        _processes = services.GetRequiredService<IProcessDiscoveryService>();
        _routing = services.GetRequiredService<RoutingRuleService>();
        _dataRoot = dataRoot;

        Text = "FlowTunnel"; Width = 980; Height = 680; MinimumSize = new Size(760, 520);
        Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildDashboard()); tabs.TabPages.Add(BuildServers()); tabs.TabPages.Add(BuildApplications()); tabs.TabPages.Add(BuildLogs());
        Controls.Add(tabs);

        _tray = new NotifyIcon { Icon = SystemIcons.Shield, Text = "FlowTunnel", Visible = true };
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Items.Add("Open", null, (_, _) => { Show(); Activate(); });
        _tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => { _tray.Visible = false; System.Windows.Forms.Application.Exit(); });
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosed += (_, _) => _tray.Dispose();
        _connection.StateChanged += (_, snapshot) => BeginInvoke(() => Apply(snapshot));
        Shown += async (_, _) => { await RefreshServersAsync(); Apply(_connection.Snapshot); };
    }

    private TabPage BuildDashboard()
    {
        var page = new TabPage("Dashboard") { Padding = new Padding(32) };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(new Label { Text = "FlowTunnel", AutoSize = true, Font = new Font("Segoe UI", 28, FontStyle.Bold), Margin = new Padding(0, 0, 0, 24) });
        layout.Controls.Add(_status); layout.Controls.Add(_profile); layout.Controls.Add(_toggle);
        _toggle.Margin = new Padding(0, 24, 0, 0); _toggle.Click += async (_, _) => await ToggleAsync();
        page.Controls.Add(layout); return page;
    }

    private TabPage BuildServers()
    {
        var page = new TabPage("Servers") { Padding = new Padding(18) };
        var import = new Button { Text = "Import", Dock = DockStyle.Top, Height = 36 };
        import.Click += async (_, _) =>
        {
            try { var items = (await _profiles.GetAllAsync(CancellationToken.None)).ToList(); items.Add(_importer.ImportUri(_uri.Text)); await _profiles.SaveAllAsync(items, CancellationToken.None); _uri.Clear(); await RefreshServersAsync(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        page.Controls.Add(_serverList); page.Controls.Add(import); page.Controls.Add(_uri); return page;
    }

    private TabPage BuildApplications()
    {
        var page = new TabPage("Applications") { Padding = new Padding(12) };
        _apps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Application", Width = 220 });
        _apps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Executable", Width = 220 });
        _apps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Processes", Width = 90 });
        _apps.Columns.Add(new DataGridViewButtonColumn { HeaderText = "Route", Text = "Use VPN", UseColumnTextForButtonValue = true, Width = 100 });
        _apps.Columns.Add(new DataGridViewButtonColumn { Text = "Direct", UseColumnTextForButtonValue = true, Width = 90 });
        _apps.CellContentClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || _apps.Rows[e.RowIndex].Tag is not RunningApplication app) return;
            if (e.ColumnIndex == 3) await _routing.SetRouteAsync(app.Identity, app.DisplayName, ApplicationRoute.Vpn, CancellationToken.None);
            if (e.ColumnIndex == 4) await _routing.SetRouteAsync(app.Identity, app.DisplayName, ApplicationRoute.Direct, CancellationToken.None);
        };
        var refresh = new Button { Text = "Refresh running applications", Dock = DockStyle.Top, Height = 38 };
        refresh.Click += async (_, _) => await RefreshApplicationsAsync();
        page.Controls.Add(_apps); page.Controls.Add(refresh); return page;
    }

    private TabPage BuildLogs()
    {
        var page = new TabPage("Logs") { Padding = new Padding(18) };
        var open = new Button { Text = "Open logs folder", Width = 180, Height = 38 };
        open.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(_dataRoot, "Logs")) { UseShellExecute = true });
        page.Controls.Add(open); return page;
    }

    private async Task ToggleAsync()
    {
        _toggle.Enabled = false;
        try
        {
            if (_connection.Snapshot.State == ConnectionState.Connected) await _connection.DisconnectAsync(CancellationToken.None);
            else
            {
                var profiles = await _profiles.GetAllAsync(CancellationToken.None);
                var selected = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles.FirstOrDefault() ?? throw new InvalidOperationException("Import a server profile first.");
                await _connection.ConnectAsync(selected, CancellationToken.None);
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "FlowTunnel", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _toggle.Enabled = true; Apply(_connection.Snapshot); }
    }

    private void Apply(ConnectionSnapshot snapshot)
    {
        _status.Text = snapshot.State.ToString(); _profile.Text = snapshot.ProfileName ?? "No server selected";
        _toggle.Text = snapshot.State == ConnectionState.Connected ? "Disconnect" : "Connect";
        _tray.Text = $"FlowTunnel - {snapshot.State}";
    }

    private async Task RefreshServersAsync()
    {
        var items = await _profiles.GetAllAsync(CancellationToken.None); _serverList.Items.Clear();
        foreach (var item in items) _serverList.Items.Add($"{item.Name} — {item.Protocol} — {item.Server}:{item.Port}");
        _profile.Text = items.FirstOrDefault(p => p.IsDefault)?.Name ?? items.FirstOrDefault()?.Name ?? "No server selected";
    }

    private async Task RefreshApplicationsAsync()
    {
        _apps.Rows.Clear();
        foreach (var app in await _processes.GetRunningApplicationsAsync(CancellationToken.None))
        {
            var index = _apps.Rows.Add(app.DisplayName, app.Identity.ExecutableName, app.ProcessCount); _apps.Rows[index].Tag = app;
        }
    }
}
