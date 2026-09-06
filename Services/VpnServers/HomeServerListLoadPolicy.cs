namespace DataGateWin.Services.VpnServers;

/// <summary>
/// Decides when Home should hit the VPN server list API so Loaded / mode changes
/// / visibility do not stack duplicate refreshes.
/// </summary>
public static class HomeServerListLoadPolicy
{
    /// <summary>User switched to Manual: fetch only if we have nothing cached yet.</summary>
    public static bool ShouldFetchOnManualModeSelected(
        bool suppressFetch,
        bool isLoaded,
        bool isManualMode,
        bool hasCachedServers)
        => !suppressFetch && isLoaded && isManualMode && !hasCachedServers;

    /// <summary>Page became visible again with an empty cache (Loaded edge cases).</summary>
    public static bool ShouldFetchOnBecameVisible(
        bool isVisible,
        bool isLoaded,
        bool suppressFetch,
        bool hasCachedServers)
        => isVisible && isLoaded && !suppressFetch && !hasCachedServers;

    /// <summary>Explicit Refresh button or first Loaded pass always refreshes.</summary>
    public static bool ShouldForceRefreshOnHomeLoaded(bool suppressFetch)
        => !suppressFetch;
}
