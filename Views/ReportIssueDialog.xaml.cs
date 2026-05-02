using System.Diagnostics;
using System.Windows;
using DataGateWin.Localization;
using DataGateWin.Services.Support;
using DataGateWin.Services.Ui;

namespace DataGateWin.Views;

public partial class ReportIssueDialog
{
    public ReportIssueDialog()
    {
        InitializeComponent();
        FluentWindowChrome.Attach(this);
    }

    private void Telegram_OnClick(object sender, RoutedEventArgs e)
    {
        OpenUrl(SupportLinks.TelegramBotUrl);
        Close();
    }

    private void Email_OnClick(object sender, RoutedEventArgs e)
    {
        var subject = Uri.EscapeDataString(Loc.T("Home_ReportEmailSubject"));
        var mailto = $"mailto:{SupportLinks.ContactEmail}?subject={subject}";
        OpenUrl(mailto);
        Close();
    }

    private void GitHub_OnClick(object sender, RoutedEventArgs e)
    {
        OpenUrl(SupportLinks.GitHubIssuesUrl);
        Close();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e) => Close();
}
