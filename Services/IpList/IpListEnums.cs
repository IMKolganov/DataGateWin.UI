namespace DataGateWin.Services.IpList;

public enum IpListCoverageMode
{
    Fast,
    Full
}

public enum IpListUpdateFrequency
{
    SixHours,
    Daily,
    Weekly,
    Manual
}

public static class IpListFrequencyExtensions
{
    public static int Hours(this IpListUpdateFrequency f) =>
        f switch
        {
            IpListUpdateFrequency.SixHours => 6,
            IpListUpdateFrequency.Daily => 24,
            IpListUpdateFrequency.Weekly => 24 * 7,
            IpListUpdateFrequency.Manual => 0,
            _ => 24
        };
}
