using System.IO;
using Newtonsoft.Json.Linq;

namespace DataGateWin.Services.Ipc;

public sealed class StartSessionPayloadBuilder
{
    public async Task<JObject?> BuildAsync(CancellationToken ct)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        var ovpnPath = Path.Combine(baseDir, "ovpnfiles", "test-win-wss.ovpn");
        if (!File.Exists(ovpnPath))
            return null;

        var ovpnContent = await File.ReadAllTextAsync(ovpnPath, ct).ConfigureAwait(false);

        return new JObject
        {
            ["ovpnContent"] = ovpnContent,
            ["host"] = "dev-s1.datagateapp.com",
            ["port"] = "443",
            ["path"] = "/api/proxy",
            ["sni"] = "dev-s1.datagateapp.com",
            ["listenIp"] = "127.0.0.1",
            ["listenPort"] = 18080,
            ["verifyServerCert"] = false
        };
    }
}