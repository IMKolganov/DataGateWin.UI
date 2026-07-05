using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using DataGateWin.Controllers;
using DataGateWin.CrashReporting;
using DataGateWin.Localization;
using DataGateWin.Pages;
using DataGateWin.Pages.Home;
using DataGateWin.Services.Auth;
using DataGateWin.Services.Identity;
using DataGateWin.Services.Security;
using DataGateWin.Services.Support;
using DataGateWin.Services.Ui;
using DataGateWin.Views;
using Wpf.Ui.Controls;

namespace DataGateWin;

public partial class MainWindow : FluentWindow
{
    private ImageSource? _taskbarAvatarOverlay;
    private readonly FreeTierAccessApiClient _freeTierAccessApi;
    private bool _isOnboardingDialogOpen;
    private DateTimeOffset _lastOnboardingCheckUtc = DateTimeOffset.MinValue;

    private readonly AuthStateStore _authState;

    private readonly HomeController _homeController = new();
    private readonly HomePage _homePage;

    private readonly Access _accessPage = new();
    private readonly Statistics _statisticsPage;
    private readonly SettingsPage _settingsPage;
    private readonly TorrentClientMonitor _torrentClientMonitor;

    public MainWindow(AuthStateStore authState, HttpClient authedApiHttp)
    {
        InitializeComponent();

        _authState = authState;
        _freeTierAccessApi = new FreeTierAccessApiClient(authedApiHttp);

        _homePage = new HomePage(_homeController);
        _settingsPage = new SettingsPage(_authState);
        _statisticsPage = new Statistics(authedApiHttp, App.Session);
        _torrentClientMonitor = new TorrentClientMonitor(this);

        Loaded += OnLoadedAsync;

        NavView.AddHandler(
            UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(NavView_OnMouseLeftButtonUp),
            true
        );

        FluentWindowChrome.Attach(this);

        StateChanged += (_, _) => UpdateTaskbarOverlay();
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        NavigateTo("home");
        await ApplyUserPaneFooterAsync().ConfigureAwait(true);
        await CheckAndShowFreeTierOnboardingIfNeededAsync(force: true).ConfigureAwait(true);
        _torrentClientMonitor.Start();
    }

    private async Task ApplyUserPaneFooterAsync()
    {
        void ShowUserAvatarFallback()
        {
            UserAvatarImage.Source = null;
            UserAvatarImage.Visibility = Visibility.Collapsed;
            UserAvatarInitials.Visibility = Visibility.Visible;
            _taskbarAvatarOverlay = null;
            UpdateTaskbarOverlay();
        }

        var token = App.Session.Current?.Token;
        var displayName = AccountDisplay.TryResolveDisplayName(token) ?? Loc.T("Common_Unknown");
        UserDisplayName.Text = displayName;
        UserAvatarInitials.Text = AccountDisplay.GetInitials(displayName);
        UserProfileRoot.ToolTip = displayName;

        var picUrl = JwtClaimReader.GetProfileImageUrlFromBearerToken(token);
        if (string.IsNullOrWhiteSpace(picUrl))
        {
            ShowUserAvatarFallback();
            return;
        }

        var userId = JwtClaimReader.GetNumericUserIdFromBearerToken(token);

        try
        {
            var bmp = await UserAvatarCache.TryLoadOrDownloadAsync(picUrl, userId, CancellationToken.None)
                .ConfigureAwait(false);

            await Dispatcher.InvokeAsync(() =>
            {
                if (bmp is not null)
                {
                    UserAvatarImage.Source = bmp;
                    UserAvatarImage.Visibility = Visibility.Visible;
                    UserAvatarInitials.Visibility = Visibility.Collapsed;
                    _taskbarAvatarOverlay = CreateTaskbarOverlaySource(bmp);
                    UpdateTaskbarOverlay();
                }
                else
                    ShowUserAvatarFallback();
            });
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "MainWindow.ApplyUserPaneFooter");
            await Dispatcher.InvokeAsync(ShowUserAvatarFallback);
        }
    }

    private static ImageSource? CreateTaskbarOverlaySource(BitmapSource source)
    {
        try
        {
            const int size = 16;
            var scaleX = size / (double)source.PixelWidth;
            var scaleY = size / (double)source.PixelHeight;
            var scaled = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));
            scaled.Freeze();

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushClip(new EllipseGeometry(new Rect(0, 0, size, size)));
                dc.DrawImage(scaled, new Rect(0, 0, size, size));
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "MainWindow.CreateTaskbarOverlay");
            return null;
        }
    }

    private void UpdateTaskbarOverlay()
    {
        if (ShellTaskbarItemInfo is null)
            return;

        ShellTaskbarItemInfo.Overlay = WindowState == WindowState.Minimized && _taskbarAvatarOverlay is not null
            ? _taskbarAvatarOverlay
            : null;
    }

    private void ReportIssue_OnClick(object sender, RoutedEventArgs e)
    {
        new ReportIssueDialog { Owner = this }.ShowDialog();
    }

    private void TelegramChannel_OnClick(object sender, RoutedEventArgs e)
    {
        TelegramChannel.OpenPublicChannel();
    }

    protected override void OnClosed(EventArgs e)
    {
        _torrentClientMonitor.Dispose();
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

        if (tag is "home" or "access")
            _ = CheckAndShowFreeTierOnboardingIfNeededAsync(force: false);
    }

    private async Task CheckAndShowFreeTierOnboardingIfNeededAsync(bool force)
    {
        if (_isOnboardingDialogOpen)
            return;

        if (!force && DateTimeOffset.UtcNow - _lastOnboardingCheckUtc < TimeSpan.FromSeconds(8))
            return;

        _lastOnboardingCheckUtc = DateTimeOffset.UtcNow;

        try
        {
            var resp = await _freeTierAccessApi.GetStatusAsync(CancellationToken.None).ConfigureAwait(true);
            var status = resp.Data;
            if (!FreeTierOnboardingPolicy.ShouldShow(status) || status == null)
                return;

            _isOnboardingDialogOpen = true;
            var wnd = new FreeTierOnboardingWindow(_freeTierAccessApi, status)
            {
                Owner = this
            };
            wnd.ShowDialog();
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "MainWindow.CheckFreeTierOnboarding");
        }
        finally
        {
            _isOnboardingDialogOpen = false;
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
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "MainWindow.HeaderDragMove");
            // DragMove can throw if called in an invalid state (rare edge cases)
        }
    }
}
