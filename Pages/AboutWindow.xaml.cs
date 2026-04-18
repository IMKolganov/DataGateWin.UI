using System.Diagnostics;
using System.Reflection;
using System.Windows;
using DataGateWin.Localization;
using DataGateWin.Services.Ui;

namespace DataGateWin.Pages;

public partial class AboutWindow
{
    public AboutWindow()
    {
        InitializeComponent();

        FluentWindowChrome.Attach(this);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var v = version?.ToString();
        VersionText.Text = string.IsNullOrEmpty(v)
            ? Loc.T("About_VersionFmt", Loc.T("Settings_UnknownVersion"))
            : Loc.T("About_VersionFmt", v);
    }

    private void Website_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://datagateapp.com",
            UseShellExecute = true
        });
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}