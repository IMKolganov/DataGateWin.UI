namespace DataGateWin.Configuration;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";

    /// <summary>UI language: <c>system</c> (follow Windows display language) or a supported code from <see cref="Localization.UiLocale"/> (same list as the Android app).</summary>
    public string UiLanguage { get; set; } = "system";

    public string? InstallationId { get; set; }

    /// <summary>Home: pick best WSS server automatically (Linux default).</summary>
    public bool HomeVpnAutoPickServer { get; set; } = true;

    /// <summary>Home: when <see cref="HomeVpnAutoPickServer"/> is false, OpenVPN server id from get-all-with-status.</summary>
    public int HomeVpnManualServerId { get; set; }
}