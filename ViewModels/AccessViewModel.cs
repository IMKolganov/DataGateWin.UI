using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataGateWin.Services.VpnServers;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServers.Dto;

namespace DataGateWin.ViewModels;

public sealed partial class AccessViewModel : ObservableObject
{
    private readonly OpenVpnServersApiClient _api;

    public AccessViewModel(OpenVpnServersApiClient api)
    {
        _api = api;
        LoadCommand.Execute(null);
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private IList<OpenVpnServerWithStatusDto> _servers = new List<OpenVpnServerWithStatusDto>();

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            ErrorText = null;

            var resp = await _api.GetAllWithStatusAsync(CancellationToken.None);
            Servers = resp.Data?.OpenVpnServerWithStatuses
                      ?? new List<OpenVpnServerWithStatusDto>();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}