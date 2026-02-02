using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.Auth.Requests;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.Auth.Responses;
using OpenVPNGateMonitor.SharedModels.Responses;

namespace DataGateWin.Services.Auth;

public sealed class AuthApiClient(HttpClient http)
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<ApiResponse<GoogleLoginResponse>> GoogleLoginAsync(
        GoogleLoginRequest request,
        CancellationToken ct)
    {
        var content = new StringContent(
            JsonConvert.SerializeObject(request),
            Encoding.UTF8,
            "application/json");

        using var resp = await _http.PostAsync("/api/auth/google-login", content, ct)
            .ConfigureAwait(false);

        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonConvert.DeserializeObject<ApiResponse<GoogleLoginResponse>>(json)!;
    }

    public async Task<ApiResponse<RefreshResponse>> RefreshAsync(
        RefreshRequest request,
        CancellationToken ct)
    {
        var content = new StringContent(
            JsonConvert.SerializeObject(request),
            Encoding.UTF8,
            "application/json");

        using var resp = await _http.PostAsync("/api/auth/refresh", content, ct)
            .ConfigureAwait(false);

        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonConvert.DeserializeObject<ApiResponse<RefreshResponse>>(json)!;
    }
}