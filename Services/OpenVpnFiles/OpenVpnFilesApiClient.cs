using System.Net;
using System.Net.Http;
using System.Text;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnFiles.Requests;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnFiles.Responses;
using OpenVPNGateMonitor.SharedModels.Responses;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataGateWin.Services.OpenVpnFiles;

public sealed class OpenVpnFilesApiClient(HttpClient http)
{
    private readonly HttpClient _http = http;

    public async Task<DownloadFileResponse> EnsureAndDownloadDeviceFileAsync(
        int vpnServerId,
        string commonName,
        string externalId,
        string issuedTo,
        CancellationToken ct)
    {
        var first = await TryDownloadAsync(vpnServerId, commonName, ct);
        if (first != null)
            return first;

        await AddWithTokenAsync(vpnServerId, commonName, externalId, issuedTo, ct);

        var second = await TryDownloadAsync(vpnServerId, commonName, ct);
        if (second != null)
            return second;

        throw new InvalidOperationException(
            $"OVPN file not found after create. vpnServerId={vpnServerId}, cn={commonName}");
    }

    private async Task<DownloadFileResponse?> TryDownloadAsync(
        int vpnServerId,
        string commonName,
        CancellationToken ct)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            "api/open-vpn-files/download-file-by-cn")
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new DownloadFileByCnRequest
                {
                    VpnServerId = vpnServerId,
                    CommonName = commonName
                }),
                Encoding.UTF8,
                "application/json")
        };

        var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!resp.IsSuccessStatusCode)
        {
            if (IsNotFoundApiMessage(raw))
                return null;

            throw new InvalidOperationException(raw);
        }

        var api = JsonConvert.DeserializeObject<ApiResponse<DownloadFileResponse>>(raw);
        if (api?.Success != true || api.Data == null)
            return null;

        return api.Data;
    }

    private async Task AddWithTokenAsync(
        int vpnServerId,
        string commonName,
        string externalId,
        string issuedTo,
        CancellationToken ct)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            "api/open-vpn-files/add-with-token")
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new AddFileRequest
                {
                    VpnServerId = vpnServerId,
                    CommonName = commonName,
                    ExternalId = externalId,
                    IssuedTo = issuedTo
                }),
                Encoding.UTF8,
                "application/json")
        };

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                await resp.Content.ReadAsStringAsync(ct));
    }

    private static bool IsNotFoundApiMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            var obj = JObject.Parse(body);
            return obj.Value<bool?>("success") == false
                   && obj.Value<string>("message")?
                       .Contains("not found", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }
}
