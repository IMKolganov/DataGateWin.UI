using DataGateMonitor.SharedModels.DataGateMonitor.VpnServers.Dto;
using DataGateMonitor.SharedModels.DataGateMonitor.VpnServers.Responses;
using DataGateMonitor.SharedModels.Responses;

namespace DataGateWin.Services.VpnServers;

public sealed class OpenVpnServersService(OpenVpnServersApiClient api)
{
    private readonly OpenVpnServersApiClient _api = api ?? throw new ArgumentNullException(nameof(api));

    public Task<ApiResponse<VpnServerWithStatusesV3Response>> GetAllWithStatusAsync(CancellationToken ct)
        => _api.GetAllWithStatusAsync(ct);

    public async Task<IReadOnlyList<VpnServerWithStatusV2Dto>> GetItemsAsync(CancellationToken ct)
    {
        var resp = await _api.GetAllWithStatusAsync(ct);
        if (!resp.Success || resp.Data == null)
            return Array.Empty<VpnServerWithStatusV2Dto>();

        return resp.Data.VpnServerWithStatuses;
    }
}
