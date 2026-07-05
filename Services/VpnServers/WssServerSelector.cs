using System.Linq;
using DataGateMonitor.SharedModels.DataGateMonitor.VpnServers.Dto;

namespace DataGateWin.Services.VpnServers;

public sealed class WssServerSelector(OpenVpnServersApiClient apiClient)
{
    private int? _lastSelectedServerId;

    public Task<VpnServerDto?> GetBestWssAsync(CancellationToken ct) =>
        GetServerAsync(autoPick: true, manualServerId: null, ct);

    /// <summary>WSS-enabled and allowed by user quota plan (Linux <c>parseWssServersFromStatusJson</c> filter).</summary>
    public static List<VpnServerWithStatusDto> FilterEligible(
        IEnumerable<VpnServerWithStatusDto>? source) =>
        source?
            .Where(x => x.VpnServerResponses?.VpnServer != null)
            .Where(x => x.VpnServerResponses.VpnServer.IsEnableWss)
            .Where(x => x.VpnServerResponses.VpnServer.IsAccessibleForUserQuotaPlanOrDefault())
            .OrderBy(x => x.VpnServerResponses.VpnServer.ServerName, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<VpnServerWithStatusDto>();

    /// <summary>
    /// Linux parity: WSS + quota filter; auto = online first, then least <see cref="VpnServerWithStatusDto.CountConnectedClients"/>, with rotation.
    /// Manual = server by id if present in filtered list.
    /// </summary>
    public async Task<VpnServerDto?> GetServerAsync(bool autoPick, int? manualServerId, CancellationToken ct)
    {
        var resp = await apiClient.GetAllWithStatusAsync(ct).ConfigureAwait(false);
        var list = resp.Data?.VpnServerWithStatuses;
        if (list == null || list.Count == 0)
            return null;

        var eligible = FilterEligible(list);

        if (eligible.Count == 0)
            return null;

        if (!autoPick && manualServerId is int id && id > 0)
        {
            var row = eligible.FirstOrDefault(x => x.VpnServerResponses.VpnServer.Id == id);
            if (row == null)
                return null;
            var chosen = row.VpnServerResponses.VpnServer;
            _lastSelectedServerId = chosen.Id;
            return chosen;
        }

        var ranked = eligible
            .OrderByDescending(x => x.VpnServerResponses.VpnServer.IsOnline)
            .ThenBy(x => x.CountConnectedClients)
            .Select(x => x.VpnServerResponses.VpnServer)
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
