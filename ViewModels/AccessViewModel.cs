using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataGateWin.Services.Access;
using DataGateWin.Services.Auth;
using DataGateWin.Services.VpnServers;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServers.Dto;

namespace DataGateWin.ViewModels;

public sealed partial class AccessViewModel : ObservableObject
{
    private readonly OpenVpnServersApiClient _serversApi;
    private readonly UserVpnAccessClient _quotaApi;
    private readonly AuthSession _session;

    public AccessViewModel(OpenVpnServersApiClient serversApi, UserVpnAccessClient quotaApi, AuthSession session)
    {
        _serversApi = serversApi;
        _quotaApi = quotaApi;
        _session = session;

        RefreshCommand = LoadCommand;
        LoadCommand.Execute(null);
    }

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorText;

    [ObservableProperty]
    private IList<OpenVpnServerWithStatusDto> servers
        = new List<OpenVpnServerWithStatusDto>();

    [ObservableProperty]
    private string totalClientsLineText = "Total clients: —";

    [ObservableProperty]
    private string planLineText = "Plan: —";

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
    private string validityFooterText = "—";

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            ErrorText = null;

            var token = await _session.GetValidAccessTokenAsync(CancellationToken.None).ConfigureAwait(true);

            // Sequential: avoids overlapping 401-refresh on the same handler/session (burst was flaky for some backends).
            var resp = await _serversApi.GetAllWithStatusAsync(CancellationToken.None).ConfigureAwait(true);
            Servers = resp.Data?.OpenVpnServerWithStatuses
                      ?? new List<OpenVpnServerWithStatusDto>();

            var totalClients = Servers.Sum(s => s.CountConnectedClients);
            TotalClientsLineText = $"Total clients: {totalClients}";

            var quota = await _quotaApi.FetchAsync(token, CancellationToken.None).ConfigureAwait(true);
            ApplyQuotaUi(quota);
        }
        catch (Exception ex)
        {
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
            PlanLineText = $"Quota: {i.QuotaApiError}";
            ShowTrafficQuotaTitle = false;
            QuotaMetaVisible = false;
            QuotaBarVisible = false;
            QuotaDetailsVisible = false;
            ValidityFooterText = "—";
            return;
        }

        ShowTrafficQuotaTitle = true;
        PlanLineText = string.IsNullOrEmpty(i.PlanName) ? "Plan: —" : $"Plan: {i.PlanName}";

        var metaParts = new List<string>();
        if (!string.IsNullOrEmpty(i.PlanName))
            metaParts.Add(i.PlanName);
        if (i.QuotaPeriodIsMonthly && i.QuotaLimitBytes > 0)
            metaParts.Add("This calendar month");
        else if (!i.QuotaPeriodIsMonthly && i.QuotaLimitBytes > 0)
            metaParts.Add("Today");

        QuotaMetaText = string.Join(" · ", metaParts);
        QuotaMetaVisible = metaParts.Count > 0;

        if (i.TrafficUsageNeedsExternalId)
        {
            QuotaBarVisible = false;
            QuotaDetailsVisible = true;
            QuotaDetailsText =
                "Traffic usage needs an OpenVPN client ID (external ID) on your account.";
            QuotaBarIsOver = false;
            QuotaBarValue = 0;
        }
        else if (i.QuotaLimitBytes <= 0)
        {
            QuotaBarVisible = false;
            QuotaDetailsVisible = true;
            QuotaDetailsText =
                "No daily or monthly traffic limit on the active quota plan for today, or no plan is active.";
            QuotaBarIsOver = false;
            QuotaBarValue = 0;
        }
        else if (i.TrafficUsedBytesForPeriod < 0)
        {
            QuotaBarVisible = false;
            QuotaDetailsVisible = true;
            QuotaDetailsText = "Usage data unavailable.";
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
            var stats = $"Used {uStr} / {lStr} ({pct:F1}%)\n";
            stats += over
                ? $"Over by {FormatDataSizeBytes(used - lim)}"
                : $"Remaining {FormatDataSizeBytes(lim - used)}";
            QuotaDetailsText = stats;
        }

        var validityParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(i.EffectiveFrom))
            validityParts.Add($"Effective from: {FormatIsoForDisplay(i.EffectiveFrom)}");
        if (!string.IsNullOrWhiteSpace(i.AssignmentNote))
            validityParts.Add($"Note: {i.AssignmentNote}");
        if (!string.IsNullOrWhiteSpace(i.EffectiveTo)
            && DateTimeOffset.TryParse(i.EffectiveTo.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var until))
            validityParts.Add($"Valid until: {until.ToLocalTime():g}");

        ValidityFooterText = validityParts.Count == 0 ? "—" : string.Join('\n', validityParts);
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
