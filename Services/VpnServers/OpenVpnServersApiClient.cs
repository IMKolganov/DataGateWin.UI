using System.Net.Http;
using Newtonsoft.Json;
using DataGateMonitor.SharedModels.DataGateMonitor.VpnServers.Responses;
using DataGateMonitor.SharedModels.Responses;

namespace DataGateWin.Services.VpnServers;

public sealed class OpenVpnServersApiClient(HttpClient http)
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<ApiResponse<VpnServerWithStatusesV3Response>> GetAllWithStatusAsync(
        CancellationToken ct)
    {
        // Latest public list endpoint (v3): includes ServerType (OpenVpn/Xray) + quota plan.
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            "api/v3/open-vpn-servers/get-all-with-status");

        using var resp = await _http.SendAsync(req, ct);

        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {json}");

        var result = JsonConvert.DeserializeObject<ApiResponse<VpnServerWithStatusesV3Response>>(json);
        if (result == null)
            throw new InvalidOperationException("Response deserialization returned null.");

        if (!result.Success)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.Message)
                    ? "Server list: API returned success=false."
                    : $"Server list: {result.Message}");

        if (result.Data?.VpnServerWithStatuses is { Count: > 0 } servers)
            result.Data.VpnServerWithStatuses = VpnServerListDeduper.ByServerId(servers);

        return result;
    }
}
