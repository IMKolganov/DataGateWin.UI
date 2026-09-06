using DataGateWin.CrashReporting;
using DataGateWin.Models.Ipc;

namespace DataGateWin.Pages.Home;

public sealed class HomeStateStore
{
    private readonly object _lock = new();

    public UiState State { get; private set; } = UiState.Idle;
    public string StatusText { get; private set; } = "Idle";

    private readonly List<string> _log = new();

    public IReadOnlyList<string> GetLogSnapshot()
    {
        lock (_lock)
            return _log.ToArray();
    }

    public void SetState(UiState state, string statusText)
    {
        lock (_lock)
        {
            State = state;
            StatusText = statusText;
        }
    }

    public void AppendLog(string line, int maxLines = InMemoryLogBudget.MaxLines)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (_lock)
            InMemoryLogBudget.AppendLine(_log, line, maxLines);
    }
}
