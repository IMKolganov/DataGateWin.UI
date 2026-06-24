using System.Globalization;
using System.Windows;
using DataGateWin.CrashReporting;

namespace DataGateWin.Localization;

public static class Loc
{
    public static string T(string key)
    {
        if (Application.Current?.TryFindResource(key) is string s && s.Length > 0)
            return s;
        return key;
    }

    public static string T(string key, params object?[] args)
    {
        var template = T(key);
        if (args is null || args.Length == 0)
            return template;
        try
        {
            return string.Format(CultureInfo.CurrentUICulture, template, args);
        }
        catch (FormatException ex)
        {
            CrashReporter.ReportNonFatal(ex, "Loc.Format");
            return template;
        }
    }
}
