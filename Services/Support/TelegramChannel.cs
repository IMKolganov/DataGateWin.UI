using System.Diagnostics;
using DataGateWin.CrashReporting;
using DataGateWin.Localization;

namespace DataGateWin.Services.Support;

public static class TelegramChannel
{
    public static void OpenPublicChannel()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Loc.T("Telegram_ChannelUrl"),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "TelegramChannel.OpenPublicChannel");
            // ignore
        }
    }
}
