using CommunityToolkit.Mvvm.ComponentModel;
using FlowTunnel.Core;

namespace FlowTunnel.Desktop.ViewModels;

public sealed class SettingsViewModel(ISettingsRepository repository, IConnectionManager connectionManager) : ObservableObject
{
    private bool _isAllAppsMode;
    private bool _isLoaded;
    private string? _message;
    public bool IsAllAppsMode
    {
        get => _isAllAppsMode;
        set
        {
            if (SetProperty(ref _isAllAppsMode, value) && _isLoaded) _ = SaveModeAsync();
        }
    }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }

    public async Task InitializeAsync()
    {
        var settings = await repository.GetAsync(CancellationToken.None);
        _isAllAppsMode = settings.RoutingMode == RoutingMode.AllUseVpnExceptSelected;
        OnPropertyChanged(nameof(IsAllAppsMode));
        _isLoaded = true;
    }

    private async Task SaveModeAsync()
    {
        try
        {
            var settings = await repository.GetAsync(CancellationToken.None);
            await repository.SaveAsync(settings with { RoutingMode = IsAllAppsMode ? RoutingMode.AllUseVpnExceptSelected : RoutingMode.OnlySelectedUseVpn }, CancellationToken.None);
            Message = "Routing mode saved. The connected tunnel was restarted to apply it.";
            await connectionManager.ReapplyRoutingAsync(CancellationToken.None);
        }
        catch (Exception ex) { Message = ex.Message; }
    }
}
