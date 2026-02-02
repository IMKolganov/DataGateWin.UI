using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServers.Dto;

namespace DataGateWin.Services.VpnServers;

public sealed class WssServerSelector(OpenVpnServersApiClient apiClient)
{
    private readonly OpenVpnServersApiClient _apiClient = apiClient;

    public async Task<OpenVpnServerDto?> GetBestWssAsync(CancellationToken ct)
    {
        var resp = await _apiClient.GetAllWithStatusAsync(ct).ConfigureAwait(false);
        var list = resp.Data?.OpenVpnServerWithStatuses;
        if (list == null || list.Count == 0)
            return null;

        var best = list
            .Where(x => x.OpenVpnServerResponses.OpenVpnServer.IsEnableWss)
            .OrderByDescending(x => x.OpenVpnServerResponses.OpenVpnServer.IsOnline)
            .ThenByDescending(x => x.CountConnectedClients)
            .Select(x => x.OpenVpnServerResponses.OpenVpnServer)
            .FirstOrDefault();

        return best;
    }
}