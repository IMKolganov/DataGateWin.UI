using System.Windows;
using DataGateWin.Pages;
using DataGateWin.Services;
using Wpf.Ui.Controls;

namespace DataGateWin;

public partial class MainWindow : FluentWindow
{
    private readonly AuthStateStore _authState;

    public MainWindow(AuthStateStore authState)
    {
        InitializeComponent();

        _authState = authState;

        ContentFrame.Navigate(new HomePage());
    }

    private void NavView_OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not NavigationView nav)
            return;

        if (nav.SelectedItem is not NavigationViewItem item)
            return;

        switch (item.Tag?.ToString())
        {
            case "home":
                ContentFrame.Navigate(new HomePage());
                break;

            case "connection":
                ContentFrame.Navigate(new ConnectionPage());
                break;

            case "certs":
                ContentFrame.Navigate(new CertificatesPage());
                break;

            case "settings":
                ContentFrame.Navigate(new SettingsPage());
                break;
        }
    }
}