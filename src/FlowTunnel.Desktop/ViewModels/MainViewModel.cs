using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowTunnel.Core;

namespace FlowTunnel.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IConnectionManager _connection;
    private readonly IProfileRepository _profiles;
    private string _status = "Disconnected";
    private string _profileName = "No server selected";
    private string? _errorMessage;
    private bool _isBusy;

    public MainViewModel(IConnectionManager connection, IProfileRepository profiles)
    {
        _connection = connection; _profiles = profiles;
        _connection.StateChanged += (_, snapshot) => Apply(snapshot);
        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync, () => !IsBusy);
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ProfileName { get => _profileName; private set => SetProperty(ref _profileName, value); }
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) ToggleConnectionCommand.NotifyCanExecuteChanged(); } }
    public string ToggleLabel => _connection.Snapshot.State == ConnectionState.Connected ? "Disconnect" : "Connect";
    public AsyncRelayCommand ToggleConnectionCommand { get; }

    public async Task InitializeAsync()
    {
        var profiles = await _profiles.GetAllAsync(CancellationToken.None);
        ProfileName = profiles.FirstOrDefault(p => p.IsDefault)?.Name ?? profiles.FirstOrDefault()?.Name ?? "No server selected";
    }

    private async Task ToggleConnectionAsync()
    {
        IsBusy = true; ErrorMessage = null;
        try
        {
            if (_connection.Snapshot.State == ConnectionState.Connected) await _connection.DisconnectAsync(CancellationToken.None);
            else
            {
                var profiles = await _profiles.GetAllAsync(CancellationToken.None);
                var profile = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles.FirstOrDefault() ?? throw new InvalidOperationException("Import a server profile before connecting.");
                await _connection.ConnectAsync(profile, CancellationToken.None);
            }
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; OnPropertyChanged(nameof(ToggleLabel)); }
    }

    private void Apply(ConnectionSnapshot snapshot)
    {
        Status = snapshot.State.ToString(); ProfileName = snapshot.ProfileName ?? ProfileName; ErrorMessage = snapshot.ErrorMessage;
        OnPropertyChanged(nameof(ToggleLabel));
    }
}
