using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowTunnel.Application;
using FlowTunnel.Core;

namespace FlowTunnel.Desktop.ViewModels;

public sealed class ApplicationsViewModel(IProcessDiscoveryService discovery, IRoutingRuleRepository repository, ISettingsRepository settings, RoutingRuleService ruleService) : ObservableObject
{
    private string _searchText = "";
    private bool _isBusy;
    private string? _error;
    public ObservableCollection<ApplicationRowViewModel> Applications { get; } = [];
    public ObservableCollection<ApplicationRoutingRule> SavedRules { get; } = [];
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) _ = RefreshAsync(); } }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public IAsyncRelayCommand RefreshCommand => new AsyncRelayCommand(RefreshAsync);

    public async Task RefreshAsync()
    {
        IsBusy = true; Error = null;
        try
        {
            var runningTask = discovery.GetRunningApplicationsAsync(CancellationToken.None);
            var rulesTask = repository.GetAllAsync(CancellationToken.None);
            var settingsTask = settings.GetAsync(CancellationToken.None);
            await Task.WhenAll(runningTask, rulesTask, settingsTask);
            var rules = await rulesTask; var appSettings = await settingsTask;
            Applications.Clear(); SavedRules.Clear();
            foreach (var rule in rules.OrderBy(r => r.Priority)) SavedRules.Add(rule);
            foreach (var app in (await runningTask).Where(a => string.IsNullOrWhiteSpace(SearchText) || a.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) || a.Identity.ExecutableName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
            {
                var explicitRule = rules.FirstOrDefault(r => r.Identity.StableKey == app.Identity.StableKey);
                Applications.Add(new(app, explicitRule?.Route ?? ApplicationRoute.Default, ruleService, RefreshAsync));
            }
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }
}

public sealed class ApplicationRowViewModel
{
    private readonly RunningApplication _app;
    private readonly RoutingRuleService _rules;
    private readonly Func<Task> _refresh;
    public ApplicationRowViewModel(RunningApplication app, ApplicationRoute route, RoutingRuleService rules, Func<Task> refresh)
    {
        _app = app; Route = route; _rules = rules; _refresh = refresh;
        UseVpnCommand = new AsyncRelayCommand(() => SetAsync(ApplicationRoute.Vpn));
        UseDirectCommand = new AsyncRelayCommand(() => SetAsync(ApplicationRoute.Direct));
        UseDefaultCommand = new AsyncRelayCommand(() => SetAsync(ApplicationRoute.Default));
    }
    public string DisplayName => _app.DisplayName;
    public string ExecutableName => _app.Identity.ExecutableName;
    public string Path => _app.Identity.ExecutablePath ?? "Path unavailable (name matching will affect every executable with this filename)";
    public int ProcessCount => _app.ProcessCount;
    public string ProcessIds => string.Join(", ", _app.Instances.Select(i => i.ProcessId));
    public ApplicationRoute Route { get; }
    public AsyncRelayCommand UseVpnCommand { get; }
    public AsyncRelayCommand UseDirectCommand { get; }
    public AsyncRelayCommand UseDefaultCommand { get; }
    private async Task SetAsync(ApplicationRoute route) { await _rules.SetRouteAsync(_app.Identity, _app.DisplayName, route, CancellationToken.None); await _refresh(); }
}
