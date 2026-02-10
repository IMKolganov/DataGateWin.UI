using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DataGateWin.Controllers;
using DataGateWin.Pages;
using DataGateWin.Pages.Home;
using DataGateWin.Services.Auth;
using Wpf.Ui.Controls;

namespace DataGateWin;

public partial class MainWindow : FluentWindow
{
    private readonly AuthStateStore _authState;

    private readonly HomeController _homeController = new();
    private readonly HomePage _homePage;

    private readonly Access _accessPage = new();
    private readonly Statistics _statisticsPage;
    private readonly SettingsPage _settingsPage;

    public MainWindow(AuthStateStore authState, HttpClient authedApiHttp)
    {
        InitializeComponent();

        _authState = authState;

        _homePage = new HomePage(_homeController);
        _settingsPage = new SettingsPage(_authState);
        _statisticsPage = new Statistics(authedApiHttp, App.Session);

        Loaded += OnLoaded;

        NavView.AddHandler(
            UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(NavView_OnMouseLeftButtonUp),
            true
        );
    }
    
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        // Force immersive dark mode to avoid light fallback during moves
        EnableImmersiveDarkMode(hwnd, true);

        // Optional: reinforce backdrop (does not hurt)
        // TrySetSystemBackdrop(hwnd);
    }
    
    private static void EnableImmersiveDarkMode(IntPtr hwnd, bool enabled)
    {
        // Attribute id: 20 for older builds, 19 for newer - try both
        var useDark = enabled ? 1 : 0;

        DwmSetWindowAttribute(hwnd, 20, ref useDark, Marshal.SizeOf<int>());
        DwmSetWindowAttribute(hwnd, 19, ref useDark, Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NavigateTo("home");
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _homeController.Dispose();
    }

    private void NavView_OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not NavigationView nav)
            return;

        if (nav.SelectedItem is not NavigationViewItem item)
            return;

        NavigateTo(item.Tag?.ToString());
    }

    private void NavView_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<NavigationViewItem>(e.OriginalSource as DependencyObject);
        if (item is null)
            return;

        NavigateTo(item.Tag?.ToString());
    }

    private void NavigateTo(string? tag)
    {
        SetActive(tag);

        switch (tag)
        {
            case "home":
                MainFrame.Navigate(_homePage);
                break;

            case "access":
                MainFrame.Navigate(_accessPage);
                break;

            case "statistics":
                MainFrame.Navigate(_statisticsPage);
                break;

            case "settings":
                MainFrame.Navigate(_settingsPage);
                break;

            default:
                SetActive("home");
                MainFrame.Navigate(_homePage);
                break;
        }
    }

    private void SetActive(string? tag)
    {
        NavHome.IsActive = tag == "home";
        NavAccess.IsActive = tag == "access";
        NavStatistics.IsActive = tag == "statistics";
        NavSettings.IsActive = tag == "settings";
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

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
    
    private void Header_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

            e.Handled = true;
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch
        {
            // DragMove can throw if called in an invalid state (rare edge cases)
        }
    }
}
