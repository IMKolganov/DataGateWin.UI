using System.Windows;
using DataGateWin.Pages;
using DataGateWin.Services;
using Wpf.Ui.Controls;

namespace DataGateWin;

public partial class MainWindow : FluentWindow
{
    private readonly AuthStateStore _authState;

    private const double MenuExpandedWidth = 280;
    private const double MenuCollapsedWidth = 56;

    public MainWindow(AuthStateStore authState)
    {
        InitializeComponent();

        _authState = authState;

        ApplyMenuState(NavView.IsPaneOpen);

        ContentFrame.Navigate(new HomePage());
    }

    private void ApplyMenuState(bool isOpen)
    {
        MenuColumn.Width = new GridLength(isOpen ? MenuExpandedWidth : MenuCollapsedWidth);

        MenuHost.Padding = isOpen
            ? new Thickness(12, 12, 8, 12)
            : new Thickness(8, 12, 8, 12);
    }

    private void NavView_OnPaneOpened(object sender, RoutedEventArgs e) => ApplyMenuState(true);

    private void NavView_OnPaneClosed(object sender, RoutedEventArgs e) => ApplyMenuState(false);

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

            case "access":
                ContentFrame.Navigate(new Access());
                break;

            case "statistics":
                ContentFrame.Navigate(new Statistics());
                break;

            case "settings":
                ContentFrame.Navigate(new SettingsPage());
                break;
        }
    }
}