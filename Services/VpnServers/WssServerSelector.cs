using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServers.Dto;

namespace DataGateWin.Services.VpnServers;

public sealed class WssServerSelector(OpenVpnServersApiClient apiClient)
{
    // Remember last selected server to rotate on next call
    private int? _lastSelectedServerId;

    public async Task<OpenVpnServerDto?> GetBestWssAsync(CancellationToken ct)
    {
        var resp = await apiClient.GetAllWithStatusAsync(ct).ConfigureAwait(false);
        var list = resp.Data?.OpenVpnServerWithStatuses;
        if (list == null || list.Count == 0)
            return null;

        // Build ranked list of WSS-enabled servers
        var ranked = list
            .Where(x => x.OpenVpnServerResponses.OpenVpnServer.IsEnableWss)
            .OrderByDescending(x => x.OpenVpnServerResponses.OpenVpnServer.IsOnline)
            .ThenByDescending(x => x.CountConnectedClients)
            .Select(x => x.OpenVpnServerResponses.OpenVpnServer)
            .ToList();

        if (ranked.Count == 0)
            return null;

        // Only one candidate → no rotation needed
        if (ranked.Count == 1)
        {
            var only = ranked[0];
            _lastSelectedServerId = only.Id;
            return only;
        }

        // Multiple candidates → rotate
        var index = 0;

        if (_lastSelectedServerId.HasValue)
        {
            var prevIndex = ranked.FindIndex(s => s.Id == _lastSelectedServerId.Value);
            if (prevIndex >= 0)
                index = (prevIndex + 1) % ranked.Count;
        }

        var selected = ranked[index];
        _lastSelectedServerId = selected.Id;
        return selected;
    }
}