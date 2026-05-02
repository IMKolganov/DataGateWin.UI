namespace DataGateWin.Configuration;

/// <summary>Public client defaults (not secrets) — same source as shipped <c>appsettings.json</c>.</summary>
public static class DataGatePublicDefaults
{
    public const string ApiBaseUrl = "https://api.datagateapp.com/";

    /// <summary>Google OAuth web/desktop client id for the DataGate Windows app (public identifier).</summary>
    public const string GoogleDesktopClientId =
        "590050741192-tu6ad1ti9bjbv6u14dqbhnhbgf69cf06.apps.googleusercontent.com";
}
