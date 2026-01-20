namespace DataGateWin.Configuration;

public sealed class GoogleAuthSettings
{
    public string ClientId { get; init; } = string.Empty;
    public int RedirectPort { get; init; }
}