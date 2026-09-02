namespace DataGateWin.Services.Ipc;

/// <summary>
/// Port defaults shared with engine <c>EnginePortDefaults.h</c>.
/// Keep values in sync via <see cref="EnginePortDefaultsContractTests"/>.
/// </summary>
public static class EnginePortDefaults
{
    /// <summary>OpenVPN default when <c>remote host</c> omits the port.</summary>
    public const int OpenVpnDefaultRemotePort = 1194;

    /// <summary>Preferred local WSS bridge listen port (loopback).</summary>
    public const int LocalBridgeDefaultListenPort = 18080;

    /// <summary>Engine tries this many consecutive ports if the preferred one is busy.</summary>
    public const int LocalBridgeListenPortAttempts = 64;
}
