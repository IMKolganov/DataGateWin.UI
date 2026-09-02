using DataGateMonitor.SharedModels.DataGateMonitor.VpnServers.Dto;

namespace DataGateWin.Services.VpnServers;

public static class VpnConnectionSessionInfoFactory
{
    public static VpnConnectionSessionInfo FromStatusRow(VpnServerWithStatusV2Dto row)
    {
        var server = row.VpnServerResponses?.VpnServer
            ?? throw new ArgumentException("Status row missing VpnServer.", nameof(row));

        var remote = row.VpnServerStatusLogResponse?.ServerRemoteIp?.Trim();
        if (string.IsNullOrWhiteSpace(remote) || remote == "-")
            remote = null;

        return new VpnConnectionSessionInfo
        {
            ServerId = server.Id,
            ServerName = server.ServerName?.Trim() ?? "",
            ExternalIp = remote,
        };
    }
}
