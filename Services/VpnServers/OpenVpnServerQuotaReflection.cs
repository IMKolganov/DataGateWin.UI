using System.Reflection;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServers.Dto;

namespace DataGateWin.Services.VpnServers;

/// <summary>
/// Linux parity: JSON may expose <c>isAccessibleForUserQuotaPlan</c>; older SharedModels builds may omit the property.
/// Undefined / missing ⇒ treat as <c>true</c> (same as Qt client when both JSON keys are absent).
/// </summary>
public static class OpenVpnServerQuotaReflection
{
    private static readonly Lazy<PropertyInfo?> ServerProp = new(() =>
        typeof(OpenVpnServerDto).GetProperty(
            "IsAccessibleForUserQuotaPlan",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));

    public static bool IsAccessibleForUserQuotaPlanOrDefault(this OpenVpnServerDto server)
    {
        var p = ServerProp.Value;
        if (p?.GetValue(server) is bool b)
            return b;
        return true;
    }
}
