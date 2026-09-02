namespace DataGateWin.Services.Ipc;

/// <summary>
/// Documents the UI shutdown/startup DNS safety sequence after a forced engine kill.
/// </summary>
internal static class EngineDnsShutdownSequence
{
    public static readonly string[] ForcedKillRecoverySteps =
    [
        "kill_engine",
        "recover_dns",
    ];
}
