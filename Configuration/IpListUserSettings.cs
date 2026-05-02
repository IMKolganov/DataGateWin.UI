using DataGateWin.Services.IpList;

namespace DataGateWin.Configuration;

public sealed class IpListUserSettings
{
    public bool CidrListsEnabled { get; set; } = true;

    public List<string> SourceUrls { get; set; } = new(IpListDefaults.DefaultSourceUrls);

    public IpListUpdateFrequency UpdateFrequency { get; set; } = IpListUpdateFrequency.Daily;

    public IpListCoverageMode CoverageMode { get; set; } = IpListCoverageMode.Full;

    public int OvpnRouteLimit { get; set; } = IpListRouteConfig.DefaultAndroid12OvpnRouteLimit;
}

public static class IpListDefaults
{
    public const string DefaultSourceUrl =
        "https://raw.githubusercontent.com/ipverse/country-ip-blocks/master/country/ru/ipv4-aggregated.txt";

    public const string DefaultIpv6SourceUrl =
        "https://raw.githubusercontent.com/ipverse/country-ip-blocks/master/country/ru/ipv6-aggregated.txt";

    public static IReadOnlyList<string> DefaultSourceUrls { get; } =
        [DefaultSourceUrl, DefaultIpv6SourceUrl];
}

public sealed class IpListRuntimeStatus
{
    public long? LastUpdatedEpochMs { get; set; }
    public int LoadedRouteCount { get; set; }
    public string? LastError { get; set; }
    public bool ReachedRouteLimit { get; set; }
}

public sealed class IpListStateDocument
{
    public IpListUserSettings Settings { get; set; } = new();
    public IpListRuntimeStatus Status { get; set; } = new();
}
