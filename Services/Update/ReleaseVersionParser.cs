namespace DataGateWin.Services.Update;

public static class ReleaseVersionParser
{
    public static Version ParseTag(string tag)
    {
        tag = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(tag, out var version)
            ? version
            : new Version(0, 0, 0);
    }

    public static bool IsUpgradeAvailable(Version latest, Version current)
        => latest > current;

    public static string FormatForDisplay(Version version)
        => version.ToString(3);
}
