using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServerClients.Requests;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServerClients.Responses;
using OpenVPNGateMonitor.SharedModels.Responses;

namespace DataGateWin.Services.Statistics;

public sealed class StatisticsApiClient(HttpClient http)
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<OverviewSeriesResponse> GetOverviewSeriesAsync(GetOverviewSeriesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.To <= request.From)
            throw new ArgumentException("To must be greater than From.", nameof(request));

        var url = BuildOverviewSeriesUrl(request);

        var apiResp = await _http.GetFromJsonAsync<ApiResponse<OverviewSeriesResponse>>(url, ct);
        if (apiResp is null)
            throw new InvalidOperationException("Overview series response is empty.");

        if (!apiResp.Success)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(apiResp.Message) ? "Overview series request failed." : apiResp.Message);

        return apiResp.Data ?? throw new InvalidOperationException("Overview series data is empty.");
    }

    private static string BuildOverviewSeriesUrl(GetOverviewSeriesRequest r)
    {
        var query = new List<string>(capacity: 4)
        {
            $"From={Uri.EscapeDataString(r.From.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}",
            $"To={Uri.EscapeDataString(r.To.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}",
            $"Grouping={Uri.EscapeDataString(((int)r.Grouping).ToString(CultureInfo.InvariantCulture))}"
        };

        if (!string.IsNullOrWhiteSpace(r.ExternalId))
            query.Add($"ExternalId={Uri.EscapeDataString(r.ExternalId)}");

        return "api/open-vpn-clients/overview/series?" + string.Join("&", query);
    }
}
