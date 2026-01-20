using System.Net.Http;
using System.Net.Http.Json;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.Auth.Requests;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.Auth.Responses;
using OpenVPNGateMonitor.SharedModels.Responses;

public sealed class AuthApiClient
{
    private readonly HttpClient _http;

    public AuthApiClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<ApiResponse<GoogleLoginResponse>> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("/api/auth/google-login", request, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ApiResponse<GoogleLoginResponse>>(cancellationToken: ct).ConfigureAwait(false))!;
    }

    public async Task<ApiResponse<RefreshResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("/api/auth/refresh", request, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ApiResponse<RefreshResponse>>(cancellationToken: ct).ConfigureAwait(false))!;
    }
}