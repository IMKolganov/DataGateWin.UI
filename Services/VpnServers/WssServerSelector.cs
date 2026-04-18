using System.Linq;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServers.Dto;

namespace DataGateWin.Services.VpnServers;

public sealed class WssServerSelector(OpenVpnServersApiClient apiClient)
{
    private int? _lastSelectedServerId;

    public Task<OpenVpnServerDto?> GetBestWssAsync(CancellationToken ct) =>
        GetServerAsync(autoPick: true, manualServerId: null, ct);

    /// <summary>WSS-enabled and allowed by user quota plan (Linux <c>parseWssServersFromStatusJson</c> filter).</summary>
    public static List<OpenVpnServerWithStatusDto> FilterEligible(
        IEnumerable<OpenVpnServerWithStatusDto>? source) =>
        source?
            .Where(x => x.OpenVpnServerResponses?.OpenVpnServer != null)
            .Where(x => x.OpenVpnServerResponses.OpenVpnServer.IsEnableWss)
            .Where(x => x.OpenVpnServerResponses.OpenVpnServer.IsAccessibleForUserQuotaPlanOrDefault())
            .OrderBy(x => x.OpenVpnServerResponses.OpenVpnServer.ServerName, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<OpenVpnServerWithStatusDto>();

    /// <summary>
    /// Linux parity: WSS + quota filter; auto = online first, then least <see cref="OpenVpnServerWithStatusDto.CountConnectedClients"/>, with rotation.
    /// Manual = server by id if present in filtered list.
    /// </summary>
    public async Task<OpenVpnServerDto?> GetServerAsync(bool autoPick, int? manualServerId, CancellationToken ct)
    {
        var resp = await apiClient.GetAllWithStatusAsync(ct).ConfigureAwait(false);
        var list = resp.Data?.OpenVpnServerWithStatuses;
        if (list == null || list.Count == 0)
            return null;

        var eligible = FilterEligible(list);

        if (eligible.Count == 0)
            return null;

        if (!autoPick && manualServerId is int id && id > 0)
        {
            var row = eligible.FirstOrDefault(x => x.OpenVpnServerResponses.OpenVpnServer.Id == id);
            if (row == null)
                return null;
            var chosen = row.OpenVpnServerResponses.OpenVpnServer;
            _lastSelectedServerId = chosen.Id;
            return chosen;
        }

        var ranked = eligible
            .OrderByDescending(x => x.OpenVpnServerResponses.OpenVpnServer.IsOnline)
            .ThenBy(x => x.CountConnectedClients)
            .Select(x => x.OpenVpnServerResponses.OpenVpnServer)
            .ToList();

        if (ranked.Count == 1)
        {
            var only = ranked[0];
            _lastSelectedServerId = only.Id;
            return only;
        }

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
