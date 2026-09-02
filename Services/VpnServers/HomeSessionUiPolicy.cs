namespace DataGateWin.Services.VpnServers;

/// <summary>Pure helpers for Home VPN session UI (unit-tested without WPF).</summary>
public static class HomeSessionUiPolicy
{
    /// <summary>
    /// Keep server/VPN identity across brief Idle/Disconnected while the user still wants VPN
    /// (auto-reconnect). Clearing would wipe the Home footer before StartSession rebuilds it.
    /// </summary>
    public static bool ShouldClearSessionIdentity(bool desiredConnected) => !desiredConnected;

    public static bool IsBareConnectionPhase(string? label) =>
        !string.IsNullOrWhiteSpace(label)
        && (label.Equals("connected", StringComparison.OrdinalIgnoreCase)
            || label.Equals("connecting", StringComparison.OrdinalIgnoreCase)
            || label.Equals("starting", StringComparison.OrdinalIgnoreCase));

    public static bool IsRawEnginePhaseStatus(string status) =>
        status.Contains("(connected)", StringComparison.OrdinalIgnoreCase)
        || status.Contains("(connecting)", StringComparison.OrdinalIgnoreCase)
        || status.Contains("(starting)", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Prefer server name, then VPN IP, then last rich status; never "Connected (connected)".
    /// </summary>
    public static string ComposeConnectedStatus(
        string? serverName,
        string? vpnIp,
        string? engineState,
        string? lastStatusText,
        bool lastWasConnected,
        string connectedPlain,
        string connectedServerFmt,
        string connectedIpFmt,
        string connectedFmt)
    {
        if (!string.IsNullOrWhiteSpace(serverName))
            return string.Format(connectedServerFmt, serverName);

        if (!string.IsNullOrWhiteSpace(vpnIp))
            return string.Format(connectedIpFmt, vpnIp);

        if (!string.IsNullOrWhiteSpace(lastStatusText)
            && lastWasConnected
            && !IsRawEnginePhaseStatus(lastStatusText))
            return lastStatusText;

        var label = string.IsNullOrWhiteSpace(engineState) ? null : engineState.Trim();
        if (IsBareConnectionPhase(label))
            return connectedPlain;

        if (!string.IsNullOrWhiteSpace(label))
            return string.Format(connectedFmt, label);

        return connectedPlain;
    }
}
