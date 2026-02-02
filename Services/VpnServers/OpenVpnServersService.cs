using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServers.Dto;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServers.Responses;
using OpenVPNGateMonitor.SharedModels.Responses;

namespace DataGateWin.Services.VpnServers;

public sealed class OpenVpnServersService(OpenVpnServersApiClient api)
{
    private readonly OpenVpnServersApiClient _api = api ?? throw new ArgumentNullException(nameof(api));

    public Task<ApiResponse<OpenVpnServerWithStatusesResponse>> GetAllWithStatusAsync(CancellationToken ct)
        => _api.GetAllWithStatusAsync(ct);

    public async Task<IReadOnlyList<OpenVpnServerWithStatusDto>> GetItemsAsync(CancellationToken ct)
    {
        var resp = await _api.GetAllWithStatusAsync(ct);
        if (!resp.Success || resp.Data == null)
            return Array.Empty<OpenVpnServerWithStatusDto>();

        return resp.Data.OpenVpnServerWithStatuses;
    }
}