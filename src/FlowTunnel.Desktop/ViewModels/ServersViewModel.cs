using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowTunnel.Core;

namespace FlowTunnel.Desktop.ViewModels;

public sealed class ServersViewModel(IProfileRepository repository, IProfileImporter importer) : ObservableObject
{
    private string _importText = ""; private string? _message;
    public ObservableCollection<ProxyProfile> Profiles { get; } = [];
    public string ImportText { get => _importText; set => SetProperty(ref _importText, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public IAsyncRelayCommand ImportCommand => new AsyncRelayCommand(ImportAsync);
    public IAsyncRelayCommand RefreshCommand => new AsyncRelayCommand(RefreshAsync);
    public async Task RefreshAsync() { Profiles.Clear(); foreach (var profile in await repository.GetAllAsync(CancellationToken.None)) Profiles.Add(profile); }
    private async Task ImportAsync()
    {
        try { var profile = importer.ImportUri(ImportText); var profiles = (await repository.GetAllAsync(CancellationToken.None)).ToList(); profiles.Add(profile); await repository.SaveAllAsync(profiles, CancellationToken.None); ImportText = ""; Message = $"Imported {profile.Name}."; await RefreshAsync(); }
        catch (Exception ex) { Message = ex.Message; }
    }
}
