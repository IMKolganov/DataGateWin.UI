using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataGateWin.CrashReporting;
using DataGateWin.Localization;
using DataGateWin.Services.Access;
using DataGateWin.Services.Auth;
using DataGateWin.Services.VpnServers;
using DataGateMonitor.SharedModels.DataGateMonitor.VpnServers.Dto;

namespace DataGateWin.ViewModels;

public sealed partial class AccessViewModel : ObservableObject
{
    private readonly OpenVpnServersApiClient _serversApi;
    private readonly UserVpnAccessClient _quotaApi;
    private readonly AuthSession _session;

    private UserVpnAccessInfo? _lastQuota;
    private int _lastTotalClients;
    private bool _clientsLoaded;

    public AccessViewModel(OpenVpnServersApiClient serversApi, UserVpnAccessClient quotaApi, AuthSession session)
    {
        _serversApi = serversApi;
        _quotaApi = quotaApi;
        _session = session;

        UiLanguageService.LanguageChanged += OnUiLanguageChanged;

        RefreshCommand = LoadCommand;
        LoadCommand.Execute(null);
    }

    private void OnUiLanguageChanged(object? sender, EventArgs e)
    {
        TotalClientsLineText = !_clientsLoaded
            ? Loc.T("Access_TotalClientsUnknown")
            : Loc.T("Access_TotalClientsFmt", _lastTotalClients);
        if (_lastQuota is not null)
            ApplyQuotaUi(_lastQuota);
    }

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorText;

    [ObservableProperty]
    private IList<VpnServerWithStatusV2Dto> servers
        = new List<VpnServerWithStatusV2Dto>();

    [ObservableProperty]
    private string totalClientsLineText = Loc.T("Access_TotalClientsUnknown");

    [ObservableProperty]
    private string planLineText = Loc.T("Access_PlanDash");

    [ObservableProperty]
    private bool showTrafficQuotaTitle = true;

    [ObservableProperty]
    private string quotaMetaText = "";

    [ObservableProperty]
    private bool quotaMetaVisible;

    [ObservableProperty]
    private bool quotaBarVisible;

    [ObservableProperty]
    private double quotaBarValue;

    [ObservableProperty]
    private bool quotaBarIsOver;

    [ObservableProperty]
    private string quotaDetailsText = "";

    [ObservableProperty]
    private bool quotaDetailsVisible;

    [ObservableProperty]
    private string validityFooterText = Loc.T("Access_Dash");

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            ErrorText = null;

            var token = await _session.GetValidAccessTokenAsync(CancellationToken.None).ConfigureAwait(true);

            var resp = await _serversApi.GetAllWithStatusAsync(CancellationToken.None).ConfigureAwait(true);
            Servers = resp.Data?.VpnServerWithStatuses
                      ?? new List<VpnServerWithStatusV2Dto>();

            var totalClients = Servers.Sum(s => s.CountConnectedClients);
            _lastTotalClients = totalClients;
            _clientsLoaded = true;
            TotalClientsLineText = Loc.T("Access_TotalClientsFmt", totalClients);

            var quota = await _quotaApi.FetchAsync(token, CancellationToken.None).ConfigureAwait(true);
            _lastQuota = quota;
            ApplyQuotaUi(quota);
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "AccessViewModel.Load");
            ErrorText = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    private void ApplyQuotaUi(UserVpnAccessInfo i)
    {
        if (!string.IsNullOrEmpty(i.QuotaApiError))
        {
            PlanLineText = Loc.T("Access_QuotaErrorFmt", i.QuotaApiError);
            ShowTrafficQuotaTitle = false;
            QuotaMetaVisible = false;
            QuotaBarVisible = false;
            QuotaDetailsVisible = false;
            ValidityFooterText = Loc.T("Access_Dash");
            return;
        }

        ShowTrafficQuotaTitle = true;
        PlanLineText = string.IsNullOrEmpty(i.PlanName)
            ? Loc.T("Access_PlanDash")
            : Loc.T("Access_PlanFmt", i.PlanName);

        var metaParts = new List<string>();
        if (!string.IsNullOrEmpty(i.PlanName))
            metaParts.Add(i.PlanName);
        if (i.QuotaPeriodIsMonthly && i.QuotaLimitBytes > 0)
            metaParts.Add(Loc.T("Access_QuotaMetaThisMonth"));
        else if (!i.QuotaPeriodIsMonthly && i.QuotaLimitBytes > 0)
            metaParts.Add(Loc.T("Access_QuotaMetaToday"));

        QuotaMetaText = string.Join(" · ", metaParts);
        QuotaMetaVisible = metaParts.Count > 0;

        if (i.TrafficUsageNeedsExternalId)
        {
            QuotaBarVisible = false;
            QuotaDetailsVisible = true;
            QuotaDetailsText = Loc.T("Access_ExternalIdNote");
            QuotaBarIsOver = false;
            QuotaBarValue = 0;
        }
        else if (i.QuotaLimitBytes <= 0)
        {
            QuotaBarVisible = false;
            QuotaDetailsVisible = true;
            QuotaDetailsText = Loc.T("Access_NoTrafficLimitNote");
            QuotaBarIsOver = false;
            QuotaBarValue = 0;
        }
        else if (i.TrafficUsedBytesForPeriod < 0)
        {
            QuotaBarVisible = false;
            QuotaDetailsVisible = true;
            QuotaDetailsText = Loc.T("Access_UsageUnavailable");
            QuotaBarIsOver = false;
            QuotaBarValue = 0;
        }
        else
        {
            var used = i.TrafficUsedBytesForPeriod;
            var lim = i.QuotaLimitBytes;
            var pct = lim > 0 ? Math.Min(100.0, 100.0 * used / (double)lim) : 0;
            var over = used > lim;
            QuotaBarVisible = true;
            QuotaDetailsVisible = true;
            QuotaBarValue = Math.Round(pct, MidpointRounding.AwayFromZero);
            QuotaBarIsOver = over;
            var uStr = FormatDataSizeBytes(used);
            var lStr = FormatDataSizeBytes(lim);
            var stats = Loc.T("Access_UsedLineFmt", uStr, lStr, pct.ToString("F1", CultureInfo.CurrentCulture))
                          + Environment.NewLine;
            stats += over
                ? Loc.T("Access_OverByFmt", FormatDataSizeBytes(used - lim))
                : Loc.T("Access_RemainingFmt", FormatDataSizeBytes(lim - used));
            QuotaDetailsText = stats;
        }

        var validityParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(i.EffectiveFrom))
            validityParts.Add(Loc.T("Access_EffectiveFromFmt", FormatIsoForDisplay(i.EffectiveFrom)));
        if (!string.IsNullOrWhiteSpace(i.AssignmentNote))
            validityParts.Add(Loc.T("Access_NoteFmt", i.AssignmentNote));
        if (!string.IsNullOrWhiteSpace(i.EffectiveTo)
            && DateTimeOffset.TryParse(i.EffectiveTo.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var until))
            validityParts.Add(Loc.T("Access_ValidUntilFmt", until.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));

        ValidityFooterText = validityParts.Count == 0 ? Loc.T("Access_Dash") : string.Join('\n', validityParts);
    }

    private static string FormatIsoForDisplay(string iso)
    {
        if (DateTimeOffset.TryParse(iso.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return dto.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        return iso.Trim();
    }

    private static string FormatDataSizeBytes(long bytes)
    {
        if (bytes < 0)
            return bytes.ToString(CultureInfo.InvariantCulture);
        const double step = 1024d;
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double size = bytes;
        var unitIndex = 0;
        while (size >= step && unitIndex < units.Length - 1)
        {
            size /= step;
            unitIndex++;
        }

        var decimals = unitIndex == 0 ? 0 : 1;
        return $"{Math.Round(size, decimals, MidpointRounding.AwayFromZero).ToString($"F{decimals}", CultureInfo.CurrentCulture)} {units[unitIndex]}";
    }
}
