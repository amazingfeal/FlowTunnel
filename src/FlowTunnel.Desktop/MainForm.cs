using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Reflection;
using FlowTunnel.Application;
using FlowTunnel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FlowTunnel.Desktop;

internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(15, 23, 42);
    private static readonly Color Sidebar = Color.FromArgb(11, 18, 32);
    private static readonly Color Surface = Color.FromArgb(25, 33, 52);
    private static readonly Color SurfaceRaised = Color.FromArgb(33, 43, 65);
    private static readonly Color Border = Color.FromArgb(52, 65, 88);
    private static readonly Color TextPrimary = Color.FromArgb(248, 250, 252);
    private static readonly Color TextMuted = Color.FromArgb(160, 174, 192);
    private static readonly Color Accent = Color.FromArgb(16, 185, 129);
    private static readonly Color AccentDark = Color.FromArgb(5, 150, 105);
    private static readonly Color Blue = Color.FromArgb(59, 130, 246);
    private static readonly Color Danger = Color.FromArgb(239, 68, 68);

    private readonly IConnectionManager _connection;
    private readonly IProfileRepository _profiles;
    private readonly IProfileImporter _importer;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IProfileLatencyService _latencyService;
    private readonly IProcessDiscoveryService _processes;
    private readonly IRoutingRuleRepository _ruleRepository;
    private readonly INetworkRoutingRuleRepository _networkRuleRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly RoutingRuleService _routing;
    private readonly string _dataRoot;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Background };
    private readonly Dictionary<string, Control> _pages = [];
    private readonly Dictionary<string, Button> _navButtons = [];
    private readonly Label _connectionBadge = MakeLabel("OFFLINE", 9, FontStyle.Bold, TextMuted);
    private readonly Label _statusTitle = MakeLabel("Disconnected", 28, FontStyle.Bold, TextPrimary);
    private readonly Label _profileLabel = MakeLabel("No active server", 11, FontStyle.Regular, TextMuted);
    private readonly Label _connectionHint = MakeLabel("Import a compatible server, then connect.", 10, FontStyle.Regular, TextMuted);
    private readonly Button _toggle = MakeButton("Connect", Accent, Color.White, 168, 46);
    private readonly ComboBox _profilePicker = new();
    private readonly TextBox _quickUri = MakeTextBox("Paste a VLESS, VMess, Trojan, Shadowsocks, SOCKS or HTTP link");
    private readonly TextBox _uri = MakeTextBox("Paste a VLESS, VMess, Trojan, Shadowsocks, SOCKS or HTTP link");
    private readonly TreeView _serverList = new();
    private readonly TextBox _subscriptionName = MakeTextBox("Subscription name (optional)");
    private readonly TextBox _subscriptionUrl = MakeTextBox("https://example.com/subscription");
    private readonly Label _serverFeedback = MakeLabel("", 9, FontStyle.Bold, Accent);
    private readonly Dictionary<Guid, ProfileLatency> _latencies = [];
    private readonly TextBox _search = MakeTextBox("Search by app, executable or path...");
    private readonly DataGridView _apps = new();
    private readonly ComboBox _networkKind = new();
    private readonly TextBox _networkValue = MakeTextBox("example.com or 10.20.0.0/16");
    private readonly ComboBox _networkRoute = new();
    private readonly DataGridView _networkRulesGrid = new();
    private readonly ComboBox _routingMode = new();
    private readonly Label _appSummary = MakeLabel("Refresh to discover running applications", 9, FontStyle.Regular, TextMuted);
    private readonly Label _routeFeedback = MakeLabel("", 9, FontStyle.Bold, Accent);
    private readonly NotifyIcon _tray;
    private readonly List<RunningApplication> _allApplications = [];
    private IReadOnlyList<ApplicationRoutingRule> _rules = [];
    private IReadOnlyList<NetworkRoutingRule> _networkRules = [];
    private readonly Dictionary<string, Image> _appIcons = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings = new();
    private bool _updatingGrid;
    private readonly SemaphoreSlim _routeChangeGate = new(1, 1);

    public MainForm(IServiceProvider services, string dataRoot)
    {
        _connection = services.GetRequiredService<IConnectionManager>();
        _profiles = services.GetRequiredService<IProfileRepository>();
        _importer = services.GetRequiredService<IProfileImporter>();
        _subscriptionRepository = services.GetRequiredService<ISubscriptionRepository>();
        _subscriptionService = services.GetRequiredService<ISubscriptionService>();
        _latencyService = services.GetRequiredService<IProfileLatencyService>();
        _processes = services.GetRequiredService<IProcessDiscoveryService>();
        _ruleRepository = services.GetRequiredService<IRoutingRuleRepository>();
        _networkRuleRepository = services.GetRequiredService<INetworkRoutingRuleRepository>();
        _settingsRepository = services.GetRequiredService<ISettingsRepository>();
        _routing = services.GetRequiredService<RoutingRuleService>();
        _dataRoot = dataRoot;

        Text = "FlowTunnel";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(940, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 10);
        KeyPreview = true;

        Controls.Add(_content);
        Controls.Add(BuildSidebar());
        BuildPages();
        ShowPage("Dashboard");

        _tray = new NotifyIcon { Icon = SystemIcons.Shield, Text = "FlowTunnel", Visible = true };
        _tray.DoubleClick += (_, _) => RestoreWindow();
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Items.Add("Open FlowTunnel", null, (_, _) => RestoreWindow());
        _tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => { _tray.Visible = false; System.Windows.Forms.Application.Exit(); });
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosed += (_, _) => { _tray.Dispose(); foreach (var image in _appIcons.Values) image.Dispose(); _appIcons.Clear(); };
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.K) { ShowPage("Applications"); _search.Focus(); e.SuppressKeyPress = true; } };
        _connection.StateChanged += (_, snapshot) => BeginInvoke(() => ApplyConnection(snapshot));
        Shown += async (_, _) =>
        {
            _settings = await _settingsRepository.GetAsync(CancellationToken.None);
            _routingMode.SelectedIndex = _settings.RoutingMode == RoutingMode.OnlySelectedUseVpn ? 0 : 1;
            await RefreshServersAsync();
            await RefreshApplicationsAsync();
            await RefreshNetworkRulesAsync();
            ApplyConnection(_connection.Snapshot);
        };
    }

    private Panel BuildSidebar()
    {
        var panel = new Panel { Dock = DockStyle.Left, Width = 220, BackColor = Sidebar, Padding = new Padding(16, 20, 16, 16) };
        var brand = MakeLabel("FLOWTUNNEL", 16, FontStyle.Bold, TextPrimary);
        brand.Location = new Point(20, 23);
        var subtitle = MakeLabel("PRIVATE ROUTING", 8, FontStyle.Bold, TextMuted);
        subtitle.Location = new Point(21, 52);
        panel.Controls.Add(brand);
        panel.Controls.Add(subtitle);

        var nav = new FlowLayoutPanel { Location = new Point(12, 100), Width = 196, Height = 300, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Sidebar };
        foreach (var item in new[] { "Dashboard", "Servers", "Applications", "Logs" })
        {
            var button = MakeNavButton(item);
            button.Click += (_, _) => ShowPage(item);
            nav.Controls.Add(button);
            _navButtons[item] = button;
        }
        panel.Controls.Add(nav);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 66, BackColor = Sidebar };
        var shortcut = MakeLabel("Ctrl + K  Search apps", 9, FontStyle.Regular, TextMuted);
        shortcut.AutoSize = false; shortcut.Dock = DockStyle.Top; shortcut.Height = 30; shortcut.TextAlign = ContentAlignment.MiddleCenter;
        var version = MakeLabel($"Version {InstalledVersion()}", 8.5f, FontStyle.Bold, TextMuted);
        version.AutoSize = false; version.Dock = DockStyle.Bottom; version.Height = 30; version.TextAlign = ContentAlignment.MiddleCenter;
        version.AccessibleName = $"Installed version {InstalledVersion()}";
        footer.Controls.AddRange([shortcut, version]); panel.Controls.Add(footer);
        return panel;
    }

    private void BuildPages()
    {
        _pages["Dashboard"] = BuildDashboard();
        _pages["Servers"] = BuildServers();
        _pages["Applications"] = BuildApplications();
        _pages["Logs"] = BuildLogs();
        foreach (var page in _pages.Values) { page.Dock = DockStyle.Fill; page.Visible = false; _content.Controls.Add(page); }
    }

    private Control BuildDashboard()
    {
        var page = Page("Connection overview", "Control the tunnel and see its current state at a glance.");
        var body = (Panel)page.Controls[0];
        var card = new RoundedPanel { Location = new Point(36, 126), Size = new Size(650, 292), BackColor = Surface, Radius = 18, Padding = new Padding(32) };
        _connectionBadge.AutoSize = false; _connectionBadge.Size = new Size(96, 28); _connectionBadge.TextAlign = ContentAlignment.MiddleCenter; _connectionBadge.BackColor = SurfaceRaised; _connectionBadge.Location = new Point(32, 30);
        _statusTitle.Location = new Point(30, 78); _profileLabel.Location = new Point(33, 128);
        _connectionHint.AutoSize = false; _connectionHint.Size = new Size(570, 44); _connectionHint.Location = new Point(33, 160);
        _toggle.Location = new Point(32, 220); _toggle.Click += async (_, _) => await ToggleAsync();
        card.Controls.AddRange([_connectionBadge, _statusTitle, _profileLabel, _connectionHint, _toggle]);
        body.Controls.Add(card);

        var privacy = new RoundedPanel { Location = new Point(712, 126), Size = new Size(300, 292), BackColor = Surface, Radius = 18, Padding = new Padding(26) };
        var privacyTitle = MakeLabel("Routing model", 14, FontStyle.Bold, TextPrimary); privacyTitle.Location = new Point(24, 28);
        var privacyBody = MakeLabel("Only the applications you mark as VPN are tunneled by default. Rules persist after applications restart.", 10, FontStyle.Regular, TextMuted);
        privacyBody.AutoSize = false; privacyBody.Size = new Size(248, 110); privacyBody.Location = new Point(25, 74);
        var secure = MakeLabel("PROCESS-AWARE", 9, FontStyle.Bold, Accent); secure.Location = new Point(25, 220);
        privacy.Controls.AddRange([privacyTitle, privacyBody, secure]); body.Controls.Add(privacy);

        var profiles = new RoundedPanel { Location = new Point(36, 442), Size = new Size(976, 190), BackColor = Surface, Radius = 18, Padding = new Padding(24) };
        var profilesTitle = MakeLabel("Server", 14, FontStyle.Bold, TextPrimary); profilesTitle.Location = new Point(24, 20);
        var profilesHint = MakeLabel("Add, select, activate or remove a configuration without leaving this page.", 9, FontStyle.Regular, TextMuted); profilesHint.Location = new Point(24, 49);
        ConfigureProfilePicker(); _profilePicker.Location = new Point(24, 80); _profilePicker.Size = new Size(430, 36);
        var activate = MakeButton("Activate", Blue, Color.White, 112, 38); activate.Location = new Point(470, 78); activate.Click += async (_, _) => await ActivatePickedProfileAsync();
        var delete = MakeButton("Remove", SurfaceRaised, TextPrimary, 104, 38); delete.Location = new Point(594, 78); delete.Click += async (_, _) => await RemovePickedProfileAsync();
        _quickUri.Location = new Point(24, 134); _quickUri.Size = new Size(674, 36); _quickUri.AccessibleName = "Quick configuration link";
        var add = MakeButton("Add config", Accent, Color.White, 150, 38); add.Location = new Point(716, 132); add.Click += async (_, _) => await QuickAddProfileAsync();
        var manage = MakeButton("Subscriptions", SurfaceRaised, TextPrimary, 154, 38); manage.Location = new Point(802, 78); manage.Click += (_, _) => ShowPage("Servers");
        profiles.Controls.AddRange([profilesTitle, profilesHint, _profilePicker, activate, delete, _quickUri, add, manage]); body.Controls.Add(profiles);
        return page;
    }

    private Control BuildServers()
    {
        var page = Page("Servers & subscriptions", "Import one server or keep a complete subscription synchronized.");
        var body = (Panel)page.Controls[0];
        var importCard = new RoundedPanel { Location = new Point(36, 126), Size = new Size(976, 150), BackColor = Surface, Radius = 16, Padding = new Padding(24) };
        var label = MakeLabel("Configuration link", 10, FontStyle.Bold, TextPrimary); label.Location = new Point(24, 22);
        _uri.Location = new Point(24, 54); _uri.Size = new Size(750, 36); _uri.AccessibleName = "Configuration link";
        var import = MakeButton("Import server", Accent, Color.White, 160, 38); import.Location = new Point(792, 52); import.Click += async (_, _) => await ImportServerAsync();
        var help = MakeLabel("Supported transports include RAW/TCP, WebSocket, HTTP, gRPC, HTTPUpgrade and XHTTP powered by Xray.", 9, FontStyle.Regular, TextMuted);
        help.Location = new Point(24, 104);
        importCard.Controls.AddRange([label, _uri, import, help]); body.Controls.Add(importCard);

        var subscriptionCard = new RoundedPanel { Location = new Point(36, 300), Size = new Size(976, 150), BackColor = Surface, Radius = 16, Padding = new Padding(24) };
        var subscriptionTitle = MakeLabel("Add subscription", 10, FontStyle.Bold, TextPrimary); subscriptionTitle.Location = new Point(24, 20);
        _subscriptionName.Location = new Point(24, 54); _subscriptionName.Size = new Size(230, 36); _subscriptionName.AccessibleName = "Subscription name";
        _subscriptionUrl.Location = new Point(270, 54); _subscriptionUrl.Size = new Size(500, 36); _subscriptionUrl.AccessibleName = "Subscription URL";
        var addSubscription = MakeButton("Add subscription", Accent, Color.White, 166, 38); addSubscription.Location = new Point(790, 52); addSubscription.Click += async (_, _) => await AddSubscriptionAsync(addSubscription);
        var subscriptionHelp = MakeLabel("Base64 and plain-text subscription lists are supported. Updating replaces only that subscription's servers.", 9, FontStyle.Regular, TextMuted); subscriptionHelp.Location = new Point(24, 104);
        subscriptionCard.Controls.AddRange([subscriptionTitle, _subscriptionName, _subscriptionUrl, addSubscription, subscriptionHelp]); body.Controls.Add(subscriptionCard);

        var listCard = new RoundedPanel { Location = new Point(36, 474), Size = new Size(976, 390), BackColor = Surface, Radius = 16, Padding = new Padding(20) };
        ConfigureServerList(); _serverList.Location = new Point(20, 56); _serverList.Size = new Size(936, 260);
        var title = MakeLabel("Profiles grouped by subscription", 13, FontStyle.Bold, TextPrimary); title.Location = new Point(20, 20);
        var active = MakeButton("Set active", Blue, Color.White, 124, 38); active.Location = new Point(20, 334); active.Click += async (_, _) => await SetActiveServerAsync();
        var update = MakeButton("Update subscription", AccentDark, Color.White, 176, 38); update.Location = new Point(156, 334); update.Click += async (_, _) => await UpdateSelectedSubscriptionAsync(update);
        var ping = MakeButton("Test all latency", SurfaceRaised, TextPrimary, 150, 38); ping.Location = new Point(344, 334); ping.Click += async (_, _) => await PingAllAsync(ping);
        var remove = MakeButton("Remove", SurfaceRaised, TextPrimary, 112, 38); remove.Location = new Point(506, 334); remove.Click += async (_, _) => await RemoveServerAsync();
        _serverFeedback.Location = new Point(634, 342); _serverFeedback.AutoSize = false; _serverFeedback.Size = new Size(300, 24); _serverFeedback.TextAlign = ContentAlignment.MiddleRight;
        listCard.Controls.AddRange([title, _serverList, active, update, ping, remove, _serverFeedback]); body.Controls.Add(listCard);
        return page;
    }

    private Control BuildApplications()
    {
        var page = Page("Application routing", "Search every running process and choose an explicit route. Changes apply immediately.");
        var body = (Panel)page.Controls[0];
        var toolbar = new RoundedPanel { Location = new Point(36, 126), Size = new Size(976, 104), BackColor = Surface, Radius = 16 };
        _search.Location = new Point(20, 20); _search.Size = new Size(430, 36); _search.AccessibleName = "Search running applications"; _search.TextChanged += (_, _) => RenderApplications();
        var searchHint = MakeLabel("Search updates instantly", 8, FontStyle.Regular, TextMuted); searchHint.Location = new Point(22, 65);
        ConfigureRoutingMode(); _routingMode.Location = new Point(470, 20); _routingMode.Size = new Size(310, 36);
        var refresh = MakeButton("Refresh", Blue, Color.White, 156, 38); refresh.Location = new Point(800, 19); refresh.Click += async (_, _) => await RefreshApplicationsAsync();
        toolbar.Controls.AddRange([_search, searchHint, _routingMode, refresh]); body.Controls.Add(toolbar);

        var gridCard = new RoundedPanel { Location = new Point(36, 252), Size = new Size(976, 386), BackColor = Surface, Radius = 16, Padding = new Padding(16) };
        ConfigureApplicationsGrid(); _apps.Location = new Point(16, 16); _apps.Size = new Size(944, 314);
        _appSummary.Location = new Point(18, 345); _routeFeedback.Location = new Point(640, 345); _routeFeedback.AutoSize = false; _routeFeedback.Size = new Size(300, 24); _routeFeedback.TextAlign = ContentAlignment.MiddleRight;
        gridCard.Controls.AddRange([_apps, _appSummary, _routeFeedback]); body.Controls.Add(gridCard);

        var networkCard = new RoundedPanel { Location = new Point(36, 662), Size = new Size(976, 340), BackColor = Surface, Radius = 16, Padding = new Padding(16) };
        var networkTitle = MakeLabel("Domain & IP routing", 13, FontStyle.Bold, TextPrimary); networkTitle.Location = new Point(18, 16);
        var networkHelp = MakeLabel("A domain rule includes its subdomains. IP rules accept a single IPv4/IPv6 address or CIDR range.", 9, FontStyle.Regular, TextMuted); networkHelp.Location = new Point(18, 45);
        ConfigureNetworkRuleControls();
        _networkKind.Location = new Point(18, 76); _networkKind.Size = new Size(150, 36);
        _networkValue.Location = new Point(182, 76); _networkValue.Size = new Size(420, 36); _networkValue.AccessibleName = "Domain, IP address or CIDR range";
        _networkRoute.Location = new Point(616, 76); _networkRoute.Size = new Size(142, 36);
        var addNetworkRule = MakeButton("Add rule", Accent, Color.White, 178, 38); addNetworkRule.Location = new Point(778, 74); addNetworkRule.Click += async (_, _) => await AddNetworkRuleAsync();
        ConfigureNetworkRulesGrid(); _networkRulesGrid.Location = new Point(18, 130); _networkRulesGrid.Size = new Size(938, 190);
        networkCard.Controls.AddRange([networkTitle, networkHelp, _networkKind, _networkValue, _networkRoute, addNetworkRule, _networkRulesGrid]); body.Controls.Add(networkCard);
        return page;
    }

    private Control BuildLogs()
    {
        var page = Page("Logs & diagnostics", "Use logs when a server cannot connect or the engine exits unexpectedly.");
        var body = (Panel)page.Controls[0];
        var card = new RoundedPanel { Location = new Point(36, 126), Size = new Size(620, 180), BackColor = Surface, Radius = 16, Padding = new Padding(24) };
        var title = MakeLabel("Local diagnostic files", 14, FontStyle.Bold, TextPrimary); title.Location = new Point(24, 24);
        var copy = MakeLabel("Logs stay on this computer unless you choose to share them.", 10, FontStyle.Regular, TextMuted); copy.Location = new Point(24, 62);
        var open = MakeButton("Open logs folder", Blue, Color.White, 180, 40); open.Location = new Point(24, 108);
        open.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(_dataRoot, "Logs")) { UseShellExecute = true });
        card.Controls.AddRange([title, copy, open]); body.Controls.Add(card); return page;
    }

    private static Panel Page(string title, string subtitle)
    {
        var page = new Panel { BackColor = Background };
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Background };
        var heading = MakeLabel(title, 22, FontStyle.Bold, TextPrimary); heading.Location = new Point(36, 34);
        var description = MakeLabel(subtitle, 10, FontStyle.Regular, TextMuted); description.Location = new Point(38, 78);
        body.Controls.AddRange([heading, description]); page.Controls.Add(body); return page;
    }

    private void ConfigureServerList()
    {
        _serverList.HideSelection = false; _serverList.BorderStyle = BorderStyle.None; _serverList.ShowLines = true; _serverList.ShowRootLines = true;
        _serverList.BackColor = SurfaceRaised; _serverList.ForeColor = TextPrimary; _serverList.Font = new Font("Segoe UI", 10); _serverList.ItemHeight = 30;
        _serverList.AccessibleName = "Subscriptions and proxy profiles";
    }

    private void ConfigureRoutingMode()
    {
        _routingMode.DropDownStyle = ComboBoxStyle.DropDownList; _routingMode.FlatStyle = FlatStyle.Flat; _routingMode.BackColor = SurfaceRaised; _routingMode.ForeColor = TextPrimary;
        _routingMode.Items.AddRange(["Selected apps use VPN", "All apps use VPN except Direct"]);
        _routingMode.AccessibleName = "Default routing mode";
        _routingMode.SelectedIndexChanged += async (_, _) =>
        {
            if (_routingMode.SelectedIndex < 0) return;
            var mode = _routingMode.SelectedIndex == 0 ? RoutingMode.OnlySelectedUseVpn : RoutingMode.AllUseVpnExceptSelected;
            if (mode == _settings.RoutingMode) return;
            _settings = _settings with { RoutingMode = mode };
            await _settingsRepository.SaveAsync(_settings, CancellationToken.None);
            await _connection.ReapplyRoutingAsync(CancellationToken.None);
            RenderApplications();
            ShowRouteFeedback("Routing mode applied");
        };
    }

    private void ConfigureProfilePicker()
    {
        _profilePicker.DropDownStyle = ComboBoxStyle.DropDownList; _profilePicker.FlatStyle = FlatStyle.Flat; _profilePicker.BackColor = SurfaceRaised; _profilePicker.ForeColor = TextPrimary;
        _profilePicker.DisplayMember = nameof(ProfileChoice.Label); _profilePicker.AccessibleName = "Selected server profile";
    }

    private void ConfigureApplicationsGrid()
    {
        _apps.AllowUserToAddRows = false; _apps.AllowUserToDeleteRows = false; _apps.AllowUserToResizeRows = false; _apps.AutoGenerateColumns = false; _apps.ReadOnly = false; _apps.EditMode = DataGridViewEditMode.EditOnEnter;
        _apps.BackgroundColor = Surface; _apps.BorderStyle = BorderStyle.None; _apps.GridColor = Border; _apps.RowHeadersVisible = false; _apps.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _apps.EnableHeadersVisualStyles = false; _apps.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = SurfaceRaised, ForeColor = TextMuted, Font = new Font("Segoe UI", 9, FontStyle.Bold), SelectionBackColor = SurfaceRaised, Padding = new Padding(8) };
        _apps.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = TextPrimary, SelectionBackColor = Color.FromArgb(35, 58, 82), SelectionForeColor = TextPrimary, Padding = new Padding(8), Font = new Font("Segoe UI", 9.5f) };
        _apps.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(22, 31, 48), ForeColor = TextPrimary, SelectionBackColor = Color.FromArgb(35, 58, 82), SelectionForeColor = TextPrimary, Padding = new Padding(8) };
        _apps.RowTemplate.Height = 46; _apps.ColumnHeadersHeight = 44; _apps.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _apps.Columns.Add(new DataGridViewImageColumn { Name = "Icon", HeaderText = "", Width = 48, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ReadOnly = true, ImageLayout = DataGridViewImageCellLayout.Zoom });
        _apps.Columns.Add(new DataGridViewTextBoxColumn { Name = "App", HeaderText = "APPLICATION", FillWeight = 145, ReadOnly = true });
        _apps.Columns.Add(new DataGridViewTextBoxColumn { Name = "Executable", HeaderText = "EXECUTABLE", FillWeight = 120, ReadOnly = true });
        _apps.Columns.Add(new DataGridViewTextBoxColumn { Name = "Instances", HeaderText = "INSTANCES", FillWeight = 54, ReadOnly = true });
        _apps.Columns.Add(new DataGridViewComboBoxColumn { Name = "Rule", HeaderText = "RULE", FillWeight = 86, FlatStyle = FlatStyle.Flat, DataSource = Enum.GetValues<ApplicationRoute>() });
        _apps.Columns.Add(new DataGridViewTextBoxColumn { Name = "Effective", HeaderText = "EFFECTIVE ROUTE", FillWeight = 90, ReadOnly = true });
        _apps.CurrentCellDirtyStateChanged += (_, _) => { if (_apps.IsCurrentCellDirty) _apps.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _apps.CellValueChanged += async (_, e) => await RouteCellChangedAsync(e);
        _apps.CellFormatting += (_, e) =>
        {
            if (_apps.Columns[e.ColumnIndex].Name != "Effective" || e.Value is not string route) return;
            e.CellStyle.ForeColor = route == "VPN" ? Accent : Blue;
            e.CellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        };
        _apps.DataError += (_, e) => e.ThrowException = false;
        _apps.AccessibleName = "Running applications and routing rules";
    }

    private void ConfigureNetworkRuleControls()
    {
        foreach (var combo in new[] { _networkKind, _networkRoute })
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList; combo.FlatStyle = FlatStyle.Flat; combo.BackColor = SurfaceRaised; combo.ForeColor = TextPrimary;
        }
        _networkKind.Items.AddRange(["Domain", "IP / CIDR"]); _networkKind.SelectedIndex = 0; _networkKind.AccessibleName = "Network rule type";
        _networkRoute.Items.AddRange(["VPN", "Direct"]); _networkRoute.SelectedIndex = 0; _networkRoute.AccessibleName = "Network route";
    }

    private void ConfigureNetworkRulesGrid()
    {
        _networkRulesGrid.AllowUserToAddRows = false; _networkRulesGrid.AllowUserToDeleteRows = false; _networkRulesGrid.AllowUserToResizeRows = false;
        _networkRulesGrid.AutoGenerateColumns = false; _networkRulesGrid.ReadOnly = true; _networkRulesGrid.BackgroundColor = Surface; _networkRulesGrid.BorderStyle = BorderStyle.None;
        _networkRulesGrid.GridColor = Border; _networkRulesGrid.RowHeadersVisible = false; _networkRulesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _networkRulesGrid.EnableHeadersVisualStyles = false; _networkRulesGrid.ColumnHeadersHeight = 40; _networkRulesGrid.RowTemplate.Height = 42; _networkRulesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _networkRulesGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = SurfaceRaised, ForeColor = TextMuted, Font = new Font("Segoe UI", 9, FontStyle.Bold), SelectionBackColor = SurfaceRaised, Padding = new Padding(8) };
        _networkRulesGrid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = TextPrimary, SelectionBackColor = Color.FromArgb(35, 58, 82), SelectionForeColor = TextPrimary, Padding = new Padding(8), Font = new Font("Segoe UI", 9.5f) };
        _networkRulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "TYPE", FillWeight = 60 });
        _networkRulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "DOMAIN / IP / CIDR", FillWeight = 210 });
        _networkRulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Route", HeaderText = "ROUTE", FillWeight = 70 });
        _networkRulesGrid.Columns.Add(new DataGridViewButtonColumn { Name = "Remove", HeaderText = "", Text = "Remove", UseColumnTextForButtonValue = true, FillWeight = 55, FlatStyle = FlatStyle.Flat });
        _networkRulesGrid.CellContentClick += async (_, e) => { if (e.RowIndex >= 0 && _networkRulesGrid.Columns[e.ColumnIndex].Name == "Remove") await RemoveNetworkRuleAsync(e.RowIndex); };
        _networkRulesGrid.AccessibleName = "Domain and IP routing rules";
    }

    private async Task ToggleAsync()
    {
        _toggle.Enabled = false; _connectionHint.ForeColor = TextMuted;
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
        catch (Exception ex)
        {
            var message = FriendlyError(ex.Message); _connectionHint.Text = message; _connectionHint.ForeColor = Danger;
            MessageBox.Show(this, message, "Connection failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _toggle.Enabled = true; ApplyConnection(_connection.Snapshot); }
    }

    private async Task ImportServerAsync()
    {
        try
        {
            var profile = _importer.ImportUri(_uri.Text);
            var items = (await _profiles.GetAllAsync(CancellationToken.None)).ToList();
            if (items.Count == 0) profile = profile with { IsDefault = true };
            items.Add(profile); await _profiles.SaveAllAsync(items, CancellationToken.None); _uri.Clear(); await RefreshServersAsync();
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "Configuration not supported", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task QuickAddProfileAsync()
    {
        _uri.Text = _quickUri.Text;
        await ImportServerAsync();
        _quickUri.Clear();
    }

    private async Task ActivatePickedProfileAsync()
    {
        if (_profilePicker.SelectedItem is not ProfileChoice choice) return;
        var items = (await _profiles.GetAllAsync(CancellationToken.None)).Select(p => p with { IsDefault = p.Id == choice.Profile.Id }).ToArray();
        await _profiles.SaveAllAsync(items, CancellationToken.None); await RefreshServersAsync();
    }

    private async Task RemovePickedProfileAsync()
    {
        if (_profilePicker.SelectedItem is not ProfileChoice choice) return;
        var items = (await _profiles.GetAllAsync(CancellationToken.None)).Where(p => p.Id != choice.Profile.Id).ToList();
        if (items.Count > 0 && items.All(p => !p.IsDefault)) items[0] = items[0] with { IsDefault = true };
        await _profiles.SaveAllAsync(items, CancellationToken.None); await RefreshServersAsync();
    }

    private async Task SetActiveServerAsync()
    {
        if (_serverList.SelectedNode?.Tag is not ProxyProfile selected) return;
        var items = (await _profiles.GetAllAsync(CancellationToken.None)).Select(p => p with { IsDefault = p.Id == selected.Id }).ToArray();
        await _profiles.SaveAllAsync(items, CancellationToken.None); await RefreshServersAsync();
    }

    private async Task RemoveServerAsync()
    {
        if (_serverList.SelectedNode?.Tag is ProxySubscription subscription)
        {
            if (MessageBox.Show(this, $"Remove '{subscription.Name}' and all of its profiles?", "Remove subscription", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            { await _subscriptionService.RemoveAsync(subscription.Id, CancellationToken.None); await RefreshServersAsync(); }
            return;
        }
        if (_serverList.SelectedNode?.Tag is not ProxyProfile selected) return;
        var items = (await _profiles.GetAllAsync(CancellationToken.None)).Where(p => p.Id != selected.Id).ToList();
        if (items.Count > 0 && items.All(p => !p.IsDefault)) items[0] = items[0] with { IsDefault = true };
        await _profiles.SaveAllAsync(items, CancellationToken.None); await RefreshServersAsync();
    }

    private async Task RefreshServersAsync()
    {
        var items = await _profiles.GetAllAsync(CancellationToken.None);
        var subscriptions = await _subscriptionRepository.GetAllAsync(CancellationToken.None);
        _serverList.BeginUpdate(); _serverList.Nodes.Clear();
        var manual = new TreeNode("Manual profiles") { ForeColor = TextMuted };
        foreach (var item in items.Where(x => x.SubscriptionId is null)) manual.Nodes.Add(ProfileNode(item));
        if (manual.Nodes.Count > 0) _serverList.Nodes.Add(manual);
        foreach (var subscription in subscriptions.OrderBy(x => x.Name))
        {
            var updated = subscription.LastUpdatedAt?.LocalDateTime.ToString("g") ?? "Never";
            var root = new TreeNode($"{subscription.Name}  •  Updated {updated}") { Tag = subscription, ForeColor = Accent };
            foreach (var item in items.Where(x => x.SubscriptionId == subscription.Id)) root.Nodes.Add(ProfileNode(item));
            _serverList.Nodes.Add(root);
        }
        _serverList.ExpandAll(); _serverList.EndUpdate();
        var current = items.FirstOrDefault(p => p.IsDefault) ?? items.FirstOrDefault();
        _profileLabel.Text = current is null ? "No active server" : current.Name;
        _profilePicker.BeginUpdate(); _profilePicker.Items.Clear();
        foreach (var item in items) _profilePicker.Items.Add(new ProfileChoice(item, $"{(item.IsDefault ? "ACTIVE • " : "")}{item.Name} — {item.Protocol}/{item.Transport?.Type ?? "raw"}"));
        if (_profilePicker.Items.Count > 0) _profilePicker.SelectedIndex = Math.Max(0, items.ToList().FindIndex(x => x.IsDefault));
        _profilePicker.EndUpdate();
    }

    private TreeNode ProfileNode(ProxyProfile profile)
    {
        var state = profile.IsDefault ? "ACTIVE" : "Saved";
        var latency = _latencies.TryGetValue(profile.Id, out var result) ? result.IsReachable ? $"{Math.Round(result.Duration!.Value.TotalMilliseconds)} ms" : result.Error : "Not tested";
        return new TreeNode($"{profile.Name}   [{profile.Protocol} · {profile.Transport?.Type ?? "raw"}]   {profile.Server}:{profile.Port}   • {latency}   • {state}") { Tag = profile, ForeColor = profile.IsDefault ? Accent : TextPrimary };
    }

    private async Task AddSubscriptionAsync(Button button)
    {
        if (string.IsNullOrWhiteSpace(_subscriptionUrl.Text)) { MessageBox.Show(this, "Enter a subscription URL.", "Subscription", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        button.Enabled = false; _serverFeedback.Text = "Downloading subscription...";
        try
        {
            await _subscriptionService.AddOrUpdateAsync(new ProxySubscription(Guid.NewGuid(), _subscriptionName.Text, _subscriptionUrl.Text.Trim(), null), CancellationToken.None);
            _subscriptionName.Clear(); _subscriptionUrl.Clear(); _serverFeedback.Text = "Subscription added"; await RefreshServersAsync();
        }
        catch (Exception ex) { _serverFeedback.Text = "Update failed"; MessageBox.Show(this, FriendlyError(ex.Message), "Subscription failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { button.Enabled = true; }
    }

    private async Task UpdateSelectedSubscriptionAsync(Button button)
    {
        var subscription = _serverList.SelectedNode?.Tag as ProxySubscription ?? _serverList.SelectedNode?.Parent?.Tag as ProxySubscription;
        if (subscription is null) { _serverFeedback.Text = "Select a subscription or one of its profiles"; return; }
        button.Enabled = false; _serverFeedback.Text = "Updating subscription...";
        try { await _subscriptionService.AddOrUpdateAsync(subscription, CancellationToken.None); _serverFeedback.Text = "Subscription updated"; await RefreshServersAsync(); }
        catch (Exception ex) { _serverFeedback.Text = "Update failed"; MessageBox.Show(this, FriendlyError(ex.Message), "Subscription failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { button.Enabled = true; }
    }

    private async Task PingAllAsync(Button button)
    {
        button.Enabled = false; _serverFeedback.Text = "Testing real TCP latency...";
        try
        {
            var profiles = await _profiles.GetAllAsync(CancellationToken.None);
            using var gate = new SemaphoreSlim(6);
            var tasks = profiles.Select(async profile =>
            {
                await gate.WaitAsync();
                try { return await _latencyService.MeasureAsync(profile, TimeSpan.FromSeconds(4), CancellationToken.None); }
                finally { gate.Release(); }
            });
            foreach (var latency in await Task.WhenAll(tasks)) _latencies[latency.ProfileId] = latency;
            _serverFeedback.Text = $"Tested {profiles.Count} profiles"; await RefreshServersAsync();
        }
        finally { button.Enabled = true; }
    }

    private async Task RefreshApplicationsAsync()
    {
        _appSummary.Text = "Discovering running applications...";
        var appsTask = _processes.GetRunningApplicationsAsync(CancellationToken.None);
        var rulesTask = _ruleRepository.GetAllAsync(CancellationToken.None);
        await Task.WhenAll(appsTask, rulesTask);
        _allApplications.Clear(); _allApplications.AddRange(await appsTask); _rules = await rulesTask;
        RenderApplications();
    }

    private void RenderApplications()
    {
        if (_apps.Columns.Count == 0) return;
        _updatingGrid = true; _apps.Rows.Clear();
        var query = _search.Text.Trim();
        var filtered = _allApplications.Where(app => query.Length == 0 || app.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) || app.Identity.ExecutableName.Contains(query, StringComparison.OrdinalIgnoreCase) || (app.Identity.ExecutablePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToArray();
        foreach (var app in filtered)
        {
            var explicitRule = FindRule(app.Identity);
            var configured = explicitRule?.Route ?? ApplicationRoute.Default;
            var effective = _routing.Resolve(app.Identity, _settings.RoutingMode, _rules);
            var index = _apps.Rows.Add(GetApplicationIcon(app), app.DisplayName, app.Identity.ExecutableName, app.ProcessCount, configured, effective == ApplicationRoute.Vpn ? "VPN" : "DIRECT");
            _apps.Rows[index].Tag = app;
        }
        _updatingGrid = false;
        _appSummary.Text = filtered.Length == 0 ? "No matching processes. Try a shorter name or refresh the list." : $"Showing {filtered.Length} of {_allApplications.Count} running applications";
    }

    private Image GetApplicationIcon(RunningApplication app)
    {
        var path = app.IconPath ?? app.Identity.ExecutablePath;
        var key = path ?? app.Identity.ExecutableName;
        if (_appIcons.TryGetValue(key, out var cached)) return cached;
        Image image;
        try
        {
            using var icon = !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? Icon.ExtractAssociatedIcon(path) : null;
            image = icon?.ToBitmap() ?? SystemIcons.Application.ToBitmap();
        }
        catch { image = SystemIcons.Application.ToBitmap(); }
        _appIcons[key] = image;
        return image;
    }

    private async Task RefreshNetworkRulesAsync()
    {
        _networkRules = await _networkRuleRepository.GetAllAsync(CancellationToken.None);
        _networkRulesGrid.Rows.Clear();
        foreach (var rule in _networkRules.OrderBy(x => x.Priority).ThenBy(x => x.Id))
        {
            var index = _networkRulesGrid.Rows.Add(rule.Kind == NetworkRuleKind.Domain ? "Domain" : "IP / CIDR", rule.Value, rule.Route == ApplicationRoute.Vpn ? "VPN" : "DIRECT");
            _networkRulesGrid.Rows[index].Tag = rule;
        }
    }

    private async Task AddNetworkRuleAsync()
    {
        try
        {
            var value = _networkValue.Text.Trim();
            var kind = _networkKind.SelectedIndex == 0 ? NetworkRuleKind.Domain : NetworkRuleKind.IpCidr;
            if (kind == NetworkRuleKind.Domain)
            {
                value = value.TrimEnd('.').ToLowerInvariant();
                if (value.StartsWith("*.", StringComparison.Ordinal)) value = value[2..];
            }
            var route = _networkRoute.SelectedIndex == 0 ? ApplicationRoute.Vpn : ApplicationRoute.Direct;
            var rule = new NetworkRoutingRule(Guid.NewGuid(), kind, value, route, _networkRules.Count == 0 ? 10 : _networkRules.Max(x => x.Priority) + 10, true, DateTimeOffset.UtcNow);
            rule.Validate();
            if (_networkRules.Any(x => x.Kind == kind && string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("This domain or IP rule already exists. Remove it first to choose another route.");
            await _networkRuleRepository.SaveAllAsync(_networkRules.Append(rule).ToArray(), CancellationToken.None);
            _networkValue.Clear();
            await RefreshNetworkRulesAsync();
            await _connection.ReapplyRoutingAsync(CancellationToken.None);
            ShowRouteFeedback($"{value}: {route} saved");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(this, FriendlyError(ex.Message), "Could not add rule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RemoveNetworkRuleAsync(int rowIndex)
    {
        if (rowIndex >= _networkRulesGrid.Rows.Count || _networkRulesGrid.Rows[rowIndex].Tag is not NetworkRoutingRule rule) return;
        await _networkRuleRepository.SaveAllAsync(_networkRules.Where(x => x.Id != rule.Id).ToArray(), CancellationToken.None);
        await RefreshNetworkRulesAsync();
        await _connection.ReapplyRoutingAsync(CancellationToken.None);
        ShowRouteFeedback($"{rule.Value} removed");
    }

    private async Task RouteCellChangedAsync(DataGridViewCellEventArgs e)
    {
        if (_updatingGrid || e.RowIndex < 0 || _apps.Columns[e.ColumnIndex].Name != "Rule" || _apps.Rows[e.RowIndex].Tag is not RunningApplication app) return;
        var value = _apps.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
        if (value is not ApplicationRoute route && (value is not string text || !Enum.TryParse(text, true, out route))) return;
        await _routeChangeGate.WaitAsync();
        try
        {
            await _routing.SetRouteAsync(app.Identity, app.DisplayName, route, CancellationToken.None);
            _rules = await _ruleRepository.GetAllAsync(CancellationToken.None);
            var saved = FindRule(app.Identity)?.Route ?? ApplicationRoute.Default;
            var effective = _routing.Resolve(app.Identity, _settings.RoutingMode, _rules);
            if (e.RowIndex < _apps.Rows.Count && ReferenceEquals(_apps.Rows[e.RowIndex].Tag, app))
            {
                _updatingGrid = true;
                _apps.Rows[e.RowIndex].Cells["Rule"].Value = saved;
                _apps.Rows[e.RowIndex].Cells["Effective"].Value = effective == ApplicationRoute.Vpn ? "VPN" : "DIRECT";
                _updatingGrid = false;
            }
            ShowRouteFeedback($"{app.DisplayName}: {saved} saved");
        }
        catch (Exception ex) { MessageBox.Show(this, FriendlyError(ex.Message), "Could not apply route", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _updatingGrid = false; _routeChangeGate.Release(); }
    }

    private ApplicationRoutingRule? FindRule(ProcessIdentity identity) => _rules.FirstOrDefault(r => r.Enabled &&
        (string.Equals(r.Identity.StableKey, identity.StableKey, StringComparison.OrdinalIgnoreCase)
         || ((string.IsNullOrWhiteSpace(r.Identity.ExecutablePath) || string.IsNullOrWhiteSpace(identity.ExecutablePath))
             && string.Equals(r.Identity.ExecutableName, identity.ExecutableName, StringComparison.OrdinalIgnoreCase))));

    private void ShowRouteFeedback(string message)
    {
        _routeFeedback.Text = message;
        var timer = new System.Windows.Forms.Timer { Interval = 3200 };
        timer.Tick += (_, _) => { _routeFeedback.Text = ""; timer.Stop(); timer.Dispose(); };
        timer.Start();
    }

    private void ApplyConnection(ConnectionSnapshot snapshot)
    {
        _statusTitle.Text = snapshot.State.ToString(); _profileLabel.Text = snapshot.ProfileName ?? _profileLabel.Text;
        var connected = snapshot.State == ConnectionState.Connected;
        _connectionBadge.Text = connected ? "PROTECTED" : snapshot.State == ConnectionState.Error ? "ERROR" : "OFFLINE";
        _connectionBadge.ForeColor = connected ? Accent : snapshot.State == ConnectionState.Error ? Danger : TextMuted;
        _toggle.Text = connected ? "Disconnect" : "Connect"; _toggle.BackColor = connected ? Danger : Accent; _toggle.FlatAppearance.MouseOverBackColor = connected ? Color.FromArgb(220, 38, 38) : AccentDark;
        if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage)) { _connectionHint.Text = FriendlyError(snapshot.ErrorMessage); _connectionHint.ForeColor = Danger; }
        else if (connected) { _connectionHint.Text = "Tunnel is active. Application rules are being enforced."; _connectionHint.ForeColor = TextMuted; }
        else if (_connectionHint.ForeColor != Danger) _connectionHint.Text = "Import a compatible server, then connect.";
        _tray.Text = $"FlowTunnel - {snapshot.State}";
    }

    private void ShowPage(string name)
    {
        foreach (var page in _pages) page.Value.Visible = page.Key == name;
        foreach (var button in _navButtons) { button.Value.BackColor = button.Key == name ? SurfaceRaised : Sidebar; button.Value.ForeColor = button.Key == name ? TextPrimary : TextMuted; }
        if (_pages.TryGetValue(name, out var selected)) selected.BringToFront();
    }

    private void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    private static string FriendlyError(string value)
    {
        var clean = Regex.Replace(value, "\\x1B(?:[@-Z\\\\-_]|\\[[0-?]*[ -/]*[@-~])", "").Trim();
        return clean;
    }

    private static string InstalledVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? "unknown";
        return version.Split('+')[0];
    }

    private static Label MakeLabel(string text, float size, FontStyle style, Color color) => new() { Text = text, AutoSize = true, Font = new Font("Segoe UI", size, style), ForeColor = color, BackColor = Color.Transparent };
    private static TextBox MakeTextBox(string placeholder) => new() { PlaceholderText = placeholder, BorderStyle = BorderStyle.FixedSingle, BackColor = SurfaceRaised, ForeColor = TextPrimary, Font = new Font("Segoe UI", 10), AccessibleDescription = placeholder };
    private static Button MakeButton(string text, Color back, Color fore, int width, int height)
    {
        var button = new Button { Text = text, Size = new Size(width, height), BackColor = back, ForeColor = fore, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10, FontStyle.Bold), AccessibleName = text };
        button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, .08f); button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, .08f); return button;
    }
    private static Button MakeNavButton(string text)
    {
        var button = MakeButton(text, Sidebar, TextMuted, 196, 48); button.TextAlign = ContentAlignment.MiddleLeft; button.Padding = new Padding(18, 0, 0, 0); button.Margin = new Padding(0, 0, 0, 8); return button;
    }
}

internal sealed record ProfileChoice(ProxyProfile Profile, string Label);

internal sealed class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 16;
    protected override void OnResize(EventArgs eventargs) { base.OnResize(eventargs); using var path = Path(); Region = new Region(path); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var path = Path(); using var pen = new Pen(Color.FromArgb(52, 65, 88)); e.Graphics.DrawPath(pen, path); base.OnPaint(e);
    }
    private GraphicsPath Path()
    {
        var d = Radius * 2; var path = new GraphicsPath(); var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90); path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
}
