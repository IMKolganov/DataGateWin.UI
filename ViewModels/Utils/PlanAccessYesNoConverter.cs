using System.Globalization;
using System.Windows.Data;
using DataGateWin.Localization;
using DataGateWin.Services.VpnServers;
using DataGateMonitor.SharedModels.DataGateMonitor.VpnServers.Dto;

namespace DataGateWin.ViewModels.Utils;

public sealed class PlanAccessYesNoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is VpnServerWithStatusV2Dto row
            ? (row.VpnServerResponses.VpnServer.IsAccessibleForUserQuotaPlanOrDefault()
                ? Loc.T("PlanAccess_Yes")
                : Loc.T("PlanAccess_No"))
            : Loc.T("PlanAccess_Dash");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
