namespace DataGateWin.Services.Ipc;

public static class ReconnectPolicy
{
    public static TimeSpan GetDelay(int attempt)
    {
        var seconds = attempt switch
        {
            <= 1 => 2,
            2 => 4,
            3 => 8,
            _ => 15
        };

        return TimeSpan.FromSeconds(seconds);
    }
}