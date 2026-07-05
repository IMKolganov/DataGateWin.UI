using System.Net.Http;
using System.Text;
using DataGateMonitor.SharedModels.DataGateMonitor.Auth.Responses;
using DataGateMonitor.SharedModels.Responses;
using Newtonsoft.Json;

namespace DataGateWin.Services.Auth;

public interface IFreeTierAccessApiClient
{
    Task<ApiResponse<FreeTierAccessStatusResponse>> GetStatusAsync(CancellationToken ct);
    Task<ApiResponse<RequestTelegramAccountLinkCodeResponse>> RequestAccountLinkCodeAsync(CancellationToken ct);
}

public sealed class FreeTierAccessApiClient(HttpClient http) : IFreeTierAccessApiClient
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<ApiResponse<FreeTierAccessStatusResponse>> GetStatusAsync(CancellationToken ct)
    {
        using var resp = await _http.GetAsync("api/auth/free-tier-access/status", ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {json}");

        var result = JsonConvert.DeserializeObject<ApiResponse<FreeTierAccessStatusResponse>>(json);
        if (result == null)
            throw new InvalidOperationException("Status response deserialization returned null.");

        return result;
    }

    public async Task<ApiResponse<RequestTelegramAccountLinkCodeResponse>> RequestAccountLinkCodeAsync(
        CancellationToken ct)
    {
        // TelegramId omitted: user completes linking in the bot (/link_account CODE).
        var content = new StringContent(
            "{}",
            Encoding.UTF8,
            "application/json");

        using var resp = await _http.PostAsync("api/auth/telegram/request-account-link-code", content, ct)
            .ConfigureAwait(false);

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {json}");

        var result = JsonConvert.DeserializeObject<ApiResponse<RequestTelegramAccountLinkCodeResponse>>(json);
        if (result == null)
            throw new InvalidOperationException("Link-code response deserialization returned null.");

        return result;
    }
}
