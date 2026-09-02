namespace DataGateWin.Services.VpnServers;

/// <summary>Remembered server selection for the active/last StartSession (Home network footer).</summary>
public sealed class VpnConnectionSessionInfo
{
    public int ServerId { get; init; }
    public string ServerName { get; init; } = "";
    /// <summary>Egress IP from API status log (<c>ServerRemoteIp</c>) — VPN server public IP, not a live client STUN lookup.</summary>
    public string? ExternalIp { get; init; }
    /// <summary>Tunnel address from OpenVPN Connected event (<c>vpnIpv4</c>).</summary>
    public string? VpnIp { get; set; }

    public bool HasIdentity =>
        !string.IsNullOrWhiteSpace(ServerName)
        || !string.IsNullOrWhiteSpace(VpnIp)
        || !string.IsNullOrWhiteSpace(ExternalIp);
}
