namespace DataGateWin.Services.IpList;

public abstract record IpCidrRoute
{
    public abstract int PrefixLength { get; }
    public abstract string NetworkAddress { get; }
    public abstract string ToOpenVpnNetGatewayRoute();
    public string ToCidrString() => $"{NetworkAddress}/{PrefixLength}";
}

public sealed record Ipv4CidrRoute(string Network, string Netmask, int Prefix) : IpCidrRoute
{
    public override int PrefixLength => Prefix;
    public override string NetworkAddress => Network;

    public override string ToOpenVpnNetGatewayRoute() =>
        $"route {Network} {Netmask} net_gateway";
}

public sealed record Ipv6CidrRoute(string Network, int Prefix) : IpCidrRoute
{
    public override int PrefixLength => Prefix;
    public override string NetworkAddress => Network;

    public override string ToOpenVpnNetGatewayRoute() =>
        $"route-ipv6 {Network}/{Prefix} net_gateway";
}

public sealed class IpListParseResult(IReadOnlyList<IpCidrRoute> routes, bool reachedRouteLimit)
{
    public IReadOnlyList<IpCidrRoute> Routes { get; } = routes;
    public bool ReachedRouteLimit { get; } = reachedRouteLimit;
}

public sealed class IpListAppendResult(string config, int appliedRouteCount, bool reachedProfileSizeLimit)
{
    public string Config { get; } = config;
    public int AppliedRouteCount { get; } = appliedRouteCount;
    public bool ReachedProfileSizeLimit { get; } = reachedProfileSizeLimit;
}

public enum IpListRouteDelivery
{
    AndroidExcludeRoute,
    OvpnProfile
}

public sealed class IpListConnectionRoutePlan(
    string config,
    IReadOnlyList<IpCidrRoute> androidExcludedRoutes,
    int selectedRouteCount,
    int appliedRouteCount,
    bool reachedProfileSizeLimit,
    IpListRouteDelivery delivery)
{
    public string Config { get; } = config;
    public IReadOnlyList<IpCidrRoute> AndroidExcludedRoutes { get; } = androidExcludedRoutes;
    public int SelectedRouteCount { get; } = selectedRouteCount;
    public int AppliedRouteCount { get; } = appliedRouteCount;
    public bool ReachedProfileSizeLimit { get; } = reachedProfileSizeLimit;
    public IpListRouteDelivery Delivery { get; } = delivery;
}
