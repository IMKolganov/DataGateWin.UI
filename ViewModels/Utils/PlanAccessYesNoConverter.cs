using System.Globalization;
using System.Windows.Data;
using DataGateWin.Services.VpnServers;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServers.Dto;

namespace DataGateWin.ViewModels.Utils;

public sealed class PlanAccessYesNoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is OpenVpnServerWithStatusDto row
            ? (row.OpenVpnServerResponses.OpenVpnServer.IsAccessibleForUserQuotaPlanOrDefault() ? "yes" : "no")
            : "—";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
