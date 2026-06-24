using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DataGateWin.Configuration;
using DataGateWin.Controllers;
using DataGateWin.CrashReporting;
using DataGateWin.Localization;
using DataGateWin.Models.Ipc;
using DataGateWin.Services.VpnServers;

namespace DataGateWin.Pages.Home;

public partial class HomePage : Page
{
    private readonly HomeController _controller;
    private OpenVpnServersApiClient? _serversApi;
    private List<CachedVpnServerRow>? _cachedServerRows;

    public HomePage(HomeController controller)
    {
        InitializeComponent();
        _controller = controller;
    }

    private async void HomePage_OnLoaded(object sender, RoutedEventArgs e)
    {
        UiLanguageService.LanguageChanged += OnUiLanguageChanged;

        _serversApi ??= new OpenVpnServersApiClient(App.AuthedApiHttp);

        _controller.AttachUi(
            statusTextSetter: s => DispatchUi(() => StatusText.Text = s),
            uiStateApplier: (state, status) => DispatchUi(() => ApplyUiState(state, status)),
            logAppender: line => DispatchUi(() => AppendLog(line))
        );

        await EnsureAccessTokenForApiAsync().ConfigureAwait(true);
        await RefreshServerListAsync().ConfigureAwait(true);
        ApplyVpnHomeSettingsFromStore();
        UpdateManualRowVisibility();

        try
        {
            await _controller.OnLoadedAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "HomePage.OnLoaded");
            _controller.AppendLogLine(Loc.T("Home_Log_EngineAttachFmt", ex.Message));
        }
    }

    private void HomePage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        UiLanguageService.LanguageChanged -= OnUiLanguageChanged;
        SaveVpnHomeSettingsFromUi();
        _controller.OnUnloaded();
    }

    private void OnUiLanguageChanged(object? sender, EventArgs e)
        => DispatchUi(RebuildServerComboFromCache);

    private async void ConnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        var autoPick = ServerModeCombo.SelectedIndex <= 0;
        int? manualId = null;
        if (!autoPick)
        {
            if (ManualServerCombo.SelectedValue is not int sid || sid <= 0)
            {
                MessageBox.Show(
                    Loc.T("Msg_ChooseServerBody"),
                    Loc.T("Msg_ChooseServerTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            manualId = sid;
        }

        SaveVpnHomeSettingsFromUi();
        await _controller.ConnectAsync(autoPick, manualId);
    }

    private async void DisconnectButton_OnClick(object sender, RoutedEventArgs e)
        => await _controller.DisconnectAsync();

    private void ServerModeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateManualRowVisibility();
        SaveVpnHomeSettingsFromUi();
    }

    private void ManualServerCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        => SaveVpnHomeSettingsFromUi();

    private async void RefreshServersButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshServersButton.IsEnabled = false;
        try
        {
            await EnsureAccessTokenForApiAsync().ConfigureAwait(true);
            await RefreshServerListAsync().ConfigureAwait(true);
            ApplyVpnHomeSettingsFromStore();
        }
        finally
        {
            RefreshServersButton.IsEnabled = true;
            _controller.ReapplyUiToLastState();
        }
    }

    private void ApplyUiState(UiState state, string statusText)
    {
        StatusText.Text = statusText;

        var isBusy = state is UiState.Connecting or UiState.Disconnecting;
        var idle = state == UiState.Idle;

        ConnectButton.IsEnabled = !isBusy && idle;
        DisconnectButton.IsEnabled = !isBusy && state is UiState.Connected or UiState.Connecting;

        var canPickServer = !isBusy && idle;
        ServerModeCombo.IsEnabled = canPickServer;
        ManualServerCombo.IsEnabled = canPickServer && ServerModeCombo.SelectedIndex == 1;
        RefreshServersButton.IsEnabled = canPickServer;
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var ts = DateTime.Now.ToString("HH:mm:ss");
        LogTextBox.AppendText($"[{ts}] {line}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private void DispatchUi(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.BeginInvoke(action);
    }

    private void UpdateManualRowVisibility()
    {
        ManualServerRow.Visibility = ServerModeCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyVpnHomeSettingsFromStore()
    {
        var s = App.Settings;
        ServerModeCombo.SelectedIndex = s.HomeVpnAutoPickServer ? 0 : 1;
        if (!s.HomeVpnAutoPickServer && s.HomeVpnManualServerId > 0)
            ManualServerCombo.SelectedValue = s.HomeVpnManualServerId;
    }

    private void SaveVpnHomeSettingsFromUi()
    {
        var s = App.Settings;
        s.HomeVpnAutoPickServer = ServerModeCombo.SelectedIndex <= 0;
        if (ManualServerCombo.SelectedValue is int mid && mid > 0)
            s.HomeVpnManualServerId = mid;
        else if (!s.HomeVpnAutoPickServer)
            s.HomeVpnManualServerId = 0;

        AppSettingsStore.SaveSafe(s);
    }

    private static async Task EnsureAccessTokenForApiAsync()
    {
        for (var i = 0; i < 25; i++)
        {
            var t = await App.Session.GetValidAccessTokenAsync(CancellationToken.None).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(t))
                return;
            await Task.Delay(80).ConfigureAwait(true);
        }
    }

    private async Task RefreshServerListAsync()
    {
        if (_serversApi == null)
            _serversApi = new OpenVpnServersApiClient(App.AuthedApiHttp);

        List<HomeVpnServerListItem> items;
        var fetchFailed = false;
        try
        {
            var resp = await _serversApi.GetAllWithStatusAsync(CancellationToken.None).ConfigureAwait(true);
            var raw = resp.Data?.OpenVpnServerWithStatuses;
            var eligible = WssServerSelector.FilterEligible(raw);
            _cachedServerRows = eligible
                .Select(x =>
                {
                    var srv = x.OpenVpnServerResponses!.OpenVpnServer;
                    return new CachedVpnServerRow
                    {
                        Id = srv.Id,
                        Name = srv.ServerName ?? "",
                        Clients = x.CountConnectedClients,
                        Online = srv.IsOnline
                    };
                })
                .ToList();

            items = _cachedServerRows.Select(r => new HomeVpnServerListItem
            {
                Id = r.Id,
                Display = FormatServerDisplay(r)
            }).ToList();
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "HomePage.RefreshServerList");
            fetchFailed = true;
            _cachedServerRows = null;
            _controller.AppendLogLine(Loc.T("Home_Log_VpnListFmt", ex.Message));
            items = [];
        }

        void ApplyList()
        {
            ManualServerCombo.ItemsSource = items;
            if (items.Count == 0 && !fetchFailed)
                _controller.AppendLogLine(Loc.T("Home_Log_NoWss"));
        }

        if (Dispatcher.CheckAccess())
            ApplyList();
        else
            await Dispatcher.InvokeAsync(ApplyList);
    }

    private void RebuildServerComboFromCache()
    {
        if (_cachedServerRows is null || _cachedServerRows.Count == 0)
            return;

        var prev = ManualServerCombo.SelectedValue;
        var items = _cachedServerRows
            .Select(r => new HomeVpnServerListItem { Id = r.Id, Display = FormatServerDisplay(r) })
            .ToList();

        ManualServerCombo.ItemsSource = items;

        if (prev is int id && id > 0)
            ManualServerCombo.SelectedValue = id;
    }

    private static string FormatServerDisplay(CachedVpnServerRow r)
    {
        var name = string.IsNullOrWhiteSpace(r.Name)
            ? Loc.T("Home_ServerFallbackFmt", r.Id)
            : r.Name;
        var onOff = r.Online ? Loc.T("Common_Online") : Loc.T("Common_Offline");
        return Loc.T("Home_ServerRowFmt", name, r.Clients, onOff);
    }

    private sealed class CachedVpnServerRow
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public int Clients { get; init; }
        public bool Online { get; init; }
    }

    private sealed class HomeVpnServerListItem
    {
        public int Id { get; init; }
        public string Display { get; init; } = "";
    }
}
