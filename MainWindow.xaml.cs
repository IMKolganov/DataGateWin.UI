using System.Windows;
using System.Windows.Input;
using DataGateWin.Pages;
using DataGateWin.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace DataGateWin;

public partial class MainWindow : FluentWindow
{
    private readonly AuthStateStore _authState;

    public MainWindow(AuthStateStore authState)
    {
        InitializeComponent();
        
        var current = ApplicationThemeManager.GetAppTheme();
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        _authState = authState;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Start page
        NavView.Navigate(typeof(HomePage));
    }
    
    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void NavView_OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not NavigationView nav)
            return;

        if (nav.SelectedItem is not NavigationViewItem item)
            return;

        // If TargetPageType is set in XAML, NavigationView can navigate itself.
        // This call ensures navigation happens even if selection changed was triggered externally.
        if (item.TargetPageType is not null)
        {
            nav.Navigate(item.TargetPageType);
            return;
        }

        // Fallback (if you ever remove TargetPageType)
        switch (item.Tag?.ToString())
        {
            case "home":
                nav.Navigate(typeof(HomePage));
                break;

            case "access":
                nav.Navigate(typeof(Access));
                break;

            case "statistics":
                nav.Navigate(typeof(Statistics));
                break;

            case "settings":
                nav.Navigate(typeof(SettingsPage));
                break;
        }
    }
}