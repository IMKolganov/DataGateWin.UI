using System.Linq;
using DataGateMonitor.SharedModels.DataGateMonitor.VpnServers.Dto;
using DataGateMonitor.SharedModels.Enums;

namespace DataGateWin.Services.VpnServers;

public sealed class WssServerSelector(OpenVpnServersApiClient apiClient)
{
    private int? _lastSelectedServerId;

    public Task<VpnServerV2Dto?> GetBestWssAsync(CancellationToken ct) =>
        GetServerAsync(autoPick: true, manualServerId: null, ct);

    /// <summary>
    /// Windows supports OpenVPN+WSS only (not Xray). Also requires quota plan access
    /// (Linux <c>parseWssServersFromStatusJson</c> parity).
    /// </summary>
    public static List<VpnServerWithStatusV2Dto> FilterEligible(
        IEnumerable<VpnServerWithStatusV2Dto>? source) =>
        FilterWindowsSupported(source)
            .Where(x => x.VpnServerResponses.VpnServer.IsAccessibleForUserQuotaPlanOrDefault())
            .OrderBy(x => x.VpnServerResponses.VpnServer.ServerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Access / lists: OpenVPN + WSS only (Xray rows are never shown on Windows).</summary>
    public static List<VpnServerWithStatusV2Dto> FilterWssEnabled(
        IEnumerable<VpnServerWithStatusV2Dto>? source) =>
        FilterWindowsSupported(source)
            .OrderBy(x => x.VpnServerResponses.VpnServer.ServerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>OpenVPN with WSS bridge — the only engine Windows can connect.</summary>
    public static bool IsWindowsSupported(VpnServerV2Dto? server) =>
        server is { ServerType: VpnServerType.OpenVpn, IsEnableWss: true };

    private static IEnumerable<VpnServerWithStatusV2Dto> FilterWindowsSupported(
        IEnumerable<VpnServerWithStatusV2Dto>? source) =>
        source?
            .Where(x => x.VpnServerResponses?.VpnServer != null)
            .Where(x => IsWindowsSupported(x.VpnServerResponses.VpnServer))
        ?? Enumerable.Empty<VpnServerWithStatusV2Dto>();

    /// <summary>
    /// Linux parity: WSS + quota filter; auto = online first, then least <see cref="VpnServerWithStatusV2Dto.CountConnectedClients"/>, with rotation.
    /// Manual = server by id if present in filtered list.
    /// </summary>
    public async Task<VpnServerWithStatusV2Dto?> GetServerRowAsync(bool autoPick, int? manualServerId, CancellationToken ct)
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
            _lastSelectedServerId = row.VpnServerResponses.VpnServer.Id;
            return row;
        }

        var ranked = eligible
            .OrderByDescending(x => x.VpnServerResponses.VpnServer.IsOnline)
            .ThenBy(x => x.CountConnectedClients)
            .ToList();

        if (ranked.Count == 1)
        {
            var only = ranked[0];
            _lastSelectedServerId = only.VpnServerResponses.VpnServer.Id;
            return only;
        }

        var index = 0;
        if (_lastSelectedServerId.HasValue)
        {
            var prevIndex = ranked.FindIndex(s => s.VpnServerResponses.VpnServer.Id == _lastSelectedServerId.Value);
            if (prevIndex >= 0)
                index = (prevIndex + 1) % ranked.Count;
        }

        var selected = ranked[index];
        _lastSelectedServerId = selected.VpnServerResponses.VpnServer.Id;
        return selected;
    }

    public async Task<VpnServerV2Dto?> GetServerAsync(bool autoPick, int? manualServerId, CancellationToken ct)
    {
        var row = await GetServerRowAsync(autoPick, manualServerId, ct).ConfigureAwait(false);
        return row?.VpnServerResponses?.VpnServer;
    }
}
