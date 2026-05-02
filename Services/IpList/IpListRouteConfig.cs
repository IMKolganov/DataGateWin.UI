using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DataGateWin.Services.IpList;

public static class IpListRouteConfig
{
    public const int MaxRoutes = 12_000;
    public const int MaxAndroidExcludedRoutes = 3_000;
    public const int DefaultAndroid12OvpnRouteLimit = 800;
    public const int MinAndroid12OvpnRouteLimit = 50;
    public const int MaxAndroid12OvpnRouteLimit = 3_000;
    public const int MaxOpenVpnProfileBytes = 240 * 1024;

    public static int SanitizeAndroid12OvpnRouteLimit(int value) =>
        Math.Clamp(value, MinAndroid12OvpnRouteLimit, MaxAndroid12OvpnRouteLimit);

    public static IReadOnlyList<IpCidrRoute> SelectAndroidExcludedRoutes(IReadOnlyList<IpCidrRoute> routes) =>
        SelectBroadestRoutes(routes, MaxAndroidExcludedRoutes);

    public static IReadOnlyList<IpCidrRoute> SelectAndroid12OvpnRoutes(
        IReadOnlyList<IpCidrRoute> routes,
        int limit)
    {
        var v4 = routes.OfType<Ipv4CidrRoute>().Cast<IpCidrRoute>().ToList();
        return SelectBroadestRoutes(v4, SanitizeAndroid12OvpnRouteLimit(limit), sortAlways: true);
    }

    public static IpListConnectionRoutePlan PrepareConnectionRoutes(
        string config,
        IReadOnlyList<IpCidrRoute> routes,
        IpListCoverageMode coverageMode,
        int android12OvpnRouteLimit,
        bool supportsAndroidRouteExclusion)
    {
        IReadOnlyList<IpCidrRoute> selectedRoutes;
        if (supportsAndroidRouteExclusion)
        {
            selectedRoutes = coverageMode switch
            {
                IpListCoverageMode.Fast => SelectAndroidExcludedRoutes(routes).ToList(),
                IpListCoverageMode.Full => routes.ToList(),
                _ => routes.ToList()
            };
        }
        else
        {
            selectedRoutes = SelectAndroid12OvpnRoutes(routes, android12OvpnRouteLimit);
        }

        if (supportsAndroidRouteExclusion)
        {
            return new IpListConnectionRoutePlan(
                config,
                selectedRoutes.ToList(),
                selectedRoutes.Count,
                selectedRoutes.Count,
                false,
                IpListRouteDelivery.AndroidExcludeRoute);
        }

        var appendResult = AppendBypassRoutesResult(config, selectedRoutes);
        return new IpListConnectionRoutePlan(
            appendResult.Config,
            Array.Empty<IpCidrRoute>(),
            selectedRoutes.Count,
            appendResult.AppliedRouteCount,
            appendResult.ReachedProfileSizeLimit,
            IpListRouteDelivery.OvpnProfile);
    }

    private static List<IpCidrRoute> SelectBroadestRoutes(
        IReadOnlyList<IpCidrRoute> routes,
        int maxRoutes,
        bool sortAlways = false)
    {
        if (!sortAlways && routes.Count <= maxRoutes)
            return routes.ToList();

        return routes
            .OrderBy(r => r is Ipv6CidrRoute ? 1 : 0)
            .ThenBy(r => r.PrefixLength)
            .ThenBy(r => r.NetworkAddress, StringComparer.Ordinal)
            .Take(maxRoutes)
            .ToList();
    }

    public static string AppendBypassRoutes(string config, IReadOnlyList<IpCidrRoute> routes) =>
        AppendBypassRoutesResult(config, routes).Config;

    public static IpListAppendResult AppendBypassRoutesResult(string config, IReadOnlyList<IpCidrRoute> routes)
    {
        if (routes.Count == 0)
            return new IpListAppendResult(config, 0, false);

        var baseConfig = config.TrimEnd();
        var outSb = new StringBuilder(baseConfig);
        const string header = "\n\n# DataGate IP list bypass routes\n";
        var projectedBytes = Encoding.UTF8.GetByteCount(baseConfig);
        var headerBytes = Encoding.UTF8.GetByteCount(header);
        if (projectedBytes + headerBytes > MaxOpenVpnProfileBytes)
            return new IpListAppendResult(config, 0, true);

        outSb.Append(header);
        projectedBytes += headerBytes;
        var appliedRouteCount = 0;
        var reachedProfileSizeLimit = false;

        foreach (var route in routes)
        {
            var line = route.ToOpenVpnNetGatewayRoute() + '\n';
            var lineBytes = Encoding.UTF8.GetByteCount(line);
            if (projectedBytes + lineBytes > MaxOpenVpnProfileBytes)
            {
                reachedProfileSizeLimit = true;
                break;
            }

            outSb.Append(line);
            projectedBytes += lineBytes;
            appliedRouteCount++;
        }

        return new IpListAppendResult(outSb.ToString(), appliedRouteCount, reachedProfileSizeLimit);
    }

    public static IpListParseResult ParseCidrRoutesResult(string content)
    {
        var routes = new HashSet<IpCidrRoute>(IpCidrRouteComparer.Instance);
        var reachedRouteLimit = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var token = rawLine.Split('#')[0].Trim();
            if (string.IsNullOrEmpty(token))
                continue;

            var parts = token.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            var first = parts.Length > 0 ? parts[0] : null;
            if (string.IsNullOrEmpty(first))
                continue;

            var route = ParseIpCidr(first);
            if (route is null || route.PrefixLength == 0)
                continue;

            routes.Add(route);
            if (routes.Count >= MaxRoutes)
            {
                reachedRouteLimit = true;
                break;
            }
        }

        return new IpListParseResult(routes.ToList(), reachedRouteLimit);
    }

    private static IpCidrRoute? ParseIpCidr(string value) =>
        value.Contains(':') ? ParseIpv6Cidr(value) : ParseIpv4Cidr(value);

    private static Ipv4CidrRoute? ParseIpv4Cidr(string value)
    {
        var parts = value.Split('/', 2);
        var ip = parts[0].Trim();
        if (string.IsNullOrEmpty(ip))
            return null;

        var prefixLength = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var pl) ? pl : 32;
        if (prefixLength is < 0 or > 32)
            return null;

        var ipLong = ParseIpv4ToLong(ip);
        if (ipLong is null)
            return null;

        var mask = PrefixToMask(prefixLength);
        var network = ipLong.Value & mask;

        return new Ipv4CidrRoute(LongToIpv4(network), LongToIpv4(mask), prefixLength);
    }

    private static Ipv6CidrRoute? ParseIpv6Cidr(string value)
    {
        var parts = value.Split('/', 2);
        var ip = parts[0].Trim();
        if (string.IsNullOrEmpty(ip))
            return null;

        var prefixLength = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var pl) ? pl : 128;
        if (prefixLength is < 0 or > 128)
            return null;

        IPAddress? addr;
        try
        {
            addr = IPAddress.Parse(ip);
        }
        catch
        {
            return null;
        }

        if (addr.AddressFamily != AddressFamily.InterNetworkV6)
            return null;

        var bytes = addr.GetAddressBytes();
        ApplyIpv6Prefix(bytes, prefixLength);

        try
        {
            var normalized = new IPAddress(bytes);
            return new Ipv6CidrRoute(normalized.ToString(), prefixLength);
        }
        catch
        {
            return null;
        }
    }

    private static long? ParseIpv4ToLong(string value)
    {
        var octets = value.Split('.');
        if (octets.Length != 4)
            return null;

        long result = 0;
        foreach (var octet in octets)
        {
            if (!int.TryParse(octet, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return null;
            if (n is < 0 or > 255)
                return null;
            result = (result << 8) | (uint)n;
        }

        return result & 0xffffffffL;
    }

    private static long PrefixToMask(int prefixLength)
    {
        if (prefixLength == 0)
            return 0L;
        return (0xffffffffL << (32 - prefixLength)) & 0xffffffffL;
    }

    private static string LongToIpv4(long value) =>
        $"{(value >> 24) & 0xff}.{(value >> 16) & 0xff}.{(value >> 8) & 0xff}.{value & 0xff}";

    private static void ApplyIpv6Prefix(byte[] bytes, int prefixLength)
    {
        var remaining = prefixLength;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (remaining >= 8)
            {
                remaining -= 8;
            }
            else if (remaining <= 0)
            {
                bytes[i] = 0;
            }
            else
            {
                var mask = (0xff << (8 - remaining)) & 0xff;
                bytes[i] = (byte)(bytes[i] & mask);
                remaining = 0;
            }
        }
    }

    private sealed class IpCidrRouteComparer : IEqualityComparer<IpCidrRoute>
    {
        public static readonly IpCidrRouteComparer Instance = new();

        public bool Equals(IpCidrRoute? x, IpCidrRoute? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;
            return x.PrefixLength == y.PrefixLength
                   && string.Equals(x.NetworkAddress, y.NetworkAddress, StringComparison.OrdinalIgnoreCase)
                   && x.GetType() == y.GetType();
        }

        public int GetHashCode(IpCidrRoute obj) =>
            HashCode.Combine(
                obj.GetType(),
                obj.PrefixLength,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.NetworkAddress));
    }
}
