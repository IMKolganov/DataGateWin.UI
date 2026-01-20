using System.Net.Http;
using Newtonsoft.Json;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServers.Responses;
using OpenVPNGateMonitor.SharedModels.Responses;

namespace DataGateWin.Services.VpnServers;

public sealed class OpenVpnServersApiClient(HttpClient http)
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<ApiResponse<OpenVpnServerWithStatusesResponse>> GetAllWithStatusAsync(
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/open-vpn-servers/get-all-with-status");

        using var resp = await _http.SendAsync(req, ct);

        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {json}");

        var result = JsonConvert.DeserializeObject<ApiResponse<OpenVpnServerWithStatusesResponse>>(json);
        if (result == null)
            throw new InvalidOperationException("Response deserialization returned null.");

        return result;
    }
}