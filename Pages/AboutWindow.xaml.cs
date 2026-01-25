using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace DataGateWin.Pages;

public partial class AboutWindow
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version}";
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