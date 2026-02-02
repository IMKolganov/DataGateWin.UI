namespace DataGateWin.Services.Ipc;

public static class EngineState
{
    public static bool IsIdle(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return true;

        return state.Trim().Equals("idle", StringComparison.OrdinalIgnoreCase);
    }
}