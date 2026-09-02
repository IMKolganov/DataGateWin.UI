using DataGateMonitor.SharedModels.DataGateMonitor.VpnServers.Dto;

namespace DataGateWin.Services.VpnServers;

/// <summary>
/// v3 API may include both <c>vpnServerWithStatuses</c> and legacy <c>openVpnServerWithStatuses</c>;
/// SharedModels merges both into one list via JsonProperty aliases.
/// </summary>
internal static class VpnServerListDeduper
{
    public static List<VpnServerWithStatusV2Dto> ByServerId(IEnumerable<VpnServerWithStatusV2Dto>? source)
    {
        if (source is null)
            return new List<VpnServerWithStatusV2Dto>();

        return source
            .Where(x => x.VpnServerResponses?.VpnServer is { Id: > 0 })
            .GroupBy(x => x.VpnServerResponses!.VpnServer.Id)
            .Select(g => g.First())
            .ToList();
    }
}
