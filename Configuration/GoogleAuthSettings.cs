namespace DataGateWin.Configuration;

public sealed class GoogleAuthSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string? ClientSecret { get; set; }
    public int RedirectPort { get; set; }
}