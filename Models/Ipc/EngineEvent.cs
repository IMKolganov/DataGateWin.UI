namespace DataGateWin.Models.Ipc;

public sealed class EngineEvent
{
    public EngineEventKind Kind { get; init; }

    public string? RawType { get; init; }

    public string? Message { get; init; }
    public string? Code { get; init; }

    public string? State { get; init; }
    public string? Ip { get; init; }
    public string? Reason { get; init; }

    public int? ExitCode { get; init; }
}