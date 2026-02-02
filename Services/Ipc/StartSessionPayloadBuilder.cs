using System.Text;
using DataGateWin.Services.Auth;
using DataGateWin.Services.Identity;
using DataGateWin.Services.Installation;
using DataGateWin.Services.OpenVpnFiles;
using DataGateWin.Services.VpnServers;
using Newtonsoft.Json.Linq;

namespace DataGateWin.Services.Ipc;

public sealed class StartSessionPayloadBuilder(
    WssServerSelector wssServerSelector,
    InstallationIdService installationIdService,
    OpenVpnFilesApiClient filesApi,
    AuthSession session)
{
    public async Task<JObject?> BuildAsync(CancellationToken ct)
    {
        var server = await wssServerSelector.GetBestWssAsync(ct).ConfigureAwait(false);
        if (server == null)
            return null;

        var installationId = installationIdService.GetOrCreate();

        var token = await session.GetValidAccessTokenAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Access token not available");

        var externalId =
            JwtClaimReader.GetClaimFromBearerToken(token, "externalId")
            ?? JwtClaimReader.GetClaimFromBearerToken(token, "sub")
            ?? JwtClaimReader.GetClaimFromBearerToken(token, "nameid");

        if (string.IsNullOrWhiteSpace(externalId))
            throw new InvalidOperationException("ExternalId not available");

        var cn = $"wdg-{server.Id}-{externalId}-{installationId}";
        var issuedTo = $"datagate windows user {externalId} device {installationId}";

        var downloaded = await filesApi.EnsureAndDownloadDeviceFileAsync(
            vpnServerId: server.Id,
            commonName: cn,
            externalId: externalId,
            issuedTo: issuedTo,
            ct: ct).ConfigureAwait(false);

        if (downloaded.Content == null || downloaded.Content.Length == 0)
            throw new InvalidOperationException("Downloaded OVPN content is empty");

        var ovpnContent = Encoding.UTF8.GetString(downloaded.Content);

        var apiUri = new Uri(server.ApiUrl);
        var host = apiUri.Host;
        var port = apiUri.IsDefaultPort ? (apiUri.Scheme == "http" ? 80 : 443) : apiUri.Port;

        return new JObject
        {
            ["installationId"] = installationId,
            ["cn"] = cn,
            ["ovpnFileName"] = downloaded.IssuedOvpn?.FileName ?? "client.ovpn",
            ["ovpnContent"] = ovpnContent,

            ["host"] = host,
            ["port"] = port.ToString(),
            ["path"] = "/api/proxy",
            ["sni"] = host,

            ["listenIp"] = "127.0.0.1",
            ["listenPort"] = 18080,
            ["verifyServerCert"] = false
        };
    }
}
