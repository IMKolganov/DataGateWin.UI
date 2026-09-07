namespace DataGateWin.Services.Ipc;

public static class EngineState
{
    public static bool IsIdle(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return true;

        return state.Trim().Equals("idle", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUnknown(string? state) => state is null;

    public static bool EqualsIgnore(string? state, string expected) =>
        !string.IsNullOrWhiteSpace(state)
        && state.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);

    public static bool IsLiveSession(string? state) =>
        EqualsIgnore(state, "connected")
        || EqualsIgnore(state, "connecting")
        || EqualsIgnore(state, "starting")
        || EqualsIgnore(state, "stopping");

    public static bool IsConnected(string? state) => EqualsIgnore(state, "connected");

    public static bool NeedsCleanup(string? state) =>
        EqualsIgnore(state, "stopped")
        || EqualsIgnore(state, "error");
}
