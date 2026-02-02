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

    public void AppendLog(string line, int maxLines = 2000)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (_lock)
        {
            _log.Add(line);

            if (_log.Count > maxLines)
                _log.RemoveRange(0, _log.Count - maxLines);
        }
    }
}