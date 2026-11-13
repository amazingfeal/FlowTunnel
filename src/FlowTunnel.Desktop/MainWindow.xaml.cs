using FlowTunnel.Desktop.ViewModels;
using FlowTunnel.Desktop.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FlowTunnel.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _main;
    private readonly ApplicationsViewModel _applications;
    private readonly ServersViewModel _servers;
    private readonly SettingsViewModel _settings;
    private readonly TrayIcon _trayIcon;

    public MainWindow(MainViewModel main, ApplicationsViewModel applications, ServersViewModel servers, SettingsViewModel settings)
    {
        InitializeComponent();
        _main = main; _applications = applications; _servers = servers; _settings = settings;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));
        _trayIcon = new TrayIcon(this);
        AppWindow.Closing += (_, args) => { args.Cancel = true; _trayIcon.HideToTray(); };
        Navigation.SelectedItem = Navigation.MenuItems[0];
        PageHost.Content = new DashboardPage { DataContext = _main };
        _ = _main.InitializeAsync();
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected) { PageHost.Content = new SettingsPage { DataContext = _settings }; _ = _settings.InitializeAsync(); return; }
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        PageHost.Content = tag switch
        {
            "dashboard" => new DashboardPage { DataContext = _main },
            "servers" => new ServersPage { DataContext = _servers },
            "applications" => new ApplicationsPage { DataContext = _applications },
            "routing" => new RoutingRulesPage { DataContext = _applications },
            "connections" => new ConnectionsPage(),
            "logs" => new LogsPage(),
            _ => new DashboardPage { DataContext = _main }
        };
        if (tag == "applications" || tag == "routing") _ = _applications.RefreshAsync();
        if (tag == "servers") _ = _servers.RefreshAsync();
    }
}
