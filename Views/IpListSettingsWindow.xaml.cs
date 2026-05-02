using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DataGateWin.Configuration;
using DataGateWin.Localization;
using DataGateWin.Services.IpList;
namespace DataGateWin.Views;

public partial class IpListSettingsWindow
{
    private readonly IpListRoutesRepository _repo = new();

    public IpListSettingsWindow() => InitializeComponent();

    private void IpListSettingsWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (FrequencyCombo.Items.Count == 0)
        {
            foreach (IpListUpdateFrequency f in Enum.GetValues<IpListUpdateFrequency>())
            {
                FrequencyCombo.Items.Add(new ComboBoxItem
                {
                    Tag = f,
                    Content = FrequencyLabel(f)
                });
            }
        }

        ReloadUiFromStore();
    }

    private static string FrequencyLabel(IpListUpdateFrequency f) =>
        f switch
        {
            IpListUpdateFrequency.SixHours => Loc.T("IpList_Freq_6h"),
            IpListUpdateFrequency.Daily => Loc.T("IpList_Freq_Daily"),
            IpListUpdateFrequency.Weekly => Loc.T("IpList_Freq_Weekly"),
            IpListUpdateFrequency.Manual => Loc.T("IpList_Freq_Manual"),
            _ => f.ToString()
        };

    private void ReloadUiFromStore()
    {
        var s = IpListStore.LoadSettings();
        var st = IpListStore.LoadState().Status;

        CidrEnabledToggle.IsChecked = s.CidrListsEnabled;
        UrlsTextBox.Text = string.Join("\n", s.SourceUrls);
        SelectFrequency(s.UpdateFrequency);
        if (s.CoverageMode == IpListCoverageMode.Fast)
            CoverageFastRadio.IsChecked = true;
        else
            CoverageFullRadio.IsChecked = true;

        RouteLimitTextBox.Text = IpListRouteConfig.SanitizeAndroid12OvpnRouteLimit(s.OvpnRouteLimit)
            .ToString(CultureInfo.InvariantCulture);

        ApplyEnabledVisual(s.CidrListsEnabled);
        RefreshStatusTexts(st);
        SaveMessageText.Visibility = Visibility.Collapsed;
    }

    private void SelectFrequency(IpListUpdateFrequency target)
    {
        foreach (ComboBoxItem item in FrequencyCombo.Items)
        {
            if (item.Tag is IpListUpdateFrequency f && f == target)
            {
                FrequencyCombo.SelectedItem = item;
                return;
            }
        }

        if (FrequencyCombo.Items.Count > 0)
            FrequencyCombo.SelectedIndex = 0;
    }

    private void ApplyEnabledVisual(bool enabled)
    {
        DisabledNotice.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        UrlsTextBox.IsEnabled = enabled;
        FrequencyCombo.IsEnabled = enabled;
        CoverageFastRadio.IsEnabled = enabled;
        CoverageFullRadio.IsEnabled = enabled;
        RouteLimitTextBox.IsEnabled = enabled;
        SaveButton.IsEnabled = true;
        UpdateNowButton.IsEnabled = enabled;
    }

    private void RefreshStatusTexts(IpListRuntimeStatus st)
    {
        var updated = st.LastUpdatedEpochMs is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : Loc.T("IpList_LastUpdatedNever");

        LastUpdatedText.Text = $"{Loc.T("IpList_LastUpdated")} {updated}";
        var routesLine = Loc.T("IpList_LoadedRoutesFmt", st.LoadedRouteCount);
        if (st.ReachedRouteLimit)
            routesLine += "\n" + Loc.T("IpList_SourceListTruncated");
        LoadedRoutesText.Text = routesLine;
        LastErrorText.Text = $"{Loc.T("IpList_LastError")} {st.LastError ?? Loc.T("IpList_LastErrorNone")}";
    }

    private void CidrEnabledToggle_OnChecked(object sender, RoutedEventArgs e) =>
        ApplyEnabledVisual(true);

    private void CidrEnabledToggle_OnUnchecked(object sender, RoutedEventArgs e) =>
        ApplyEnabledVisual(false);

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var limit = int.TryParse(RouteLimitTextBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var l)
            ? l
            : IpListRouteConfig.DefaultAndroid12OvpnRouteLimit;

        if (limit < IpListRouteConfig.MinAndroid12OvpnRouteLimit ||
            limit > IpListRouteConfig.MaxAndroid12OvpnRouteLimit)
        {
            SaveMessageText.Text = Loc.T(
                "IpList_RouteLimitErrorFmt",
                IpListRouteConfig.MinAndroid12OvpnRouteLimit,
                IpListRouteConfig.MaxAndroid12OvpnRouteLimit);
            SaveMessageText.Visibility = Visibility.Visible;
            return;
        }

        var urls = UrlsTextBox.Text
            .Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (urls.Count == 0)
            urls = IpListDefaults.DefaultSourceUrls.ToList();

        var freq = FrequencyCombo.SelectedItem is ComboBoxItem { Tag: IpListUpdateFrequency f }
            ? f
            : IpListUpdateFrequency.Daily;

        var coverage = CoverageFastRadio.IsChecked == true
            ? IpListCoverageMode.Fast
            : IpListCoverageMode.Full;

        var settings = new IpListUserSettings
        {
            CidrListsEnabled = CidrEnabledToggle.IsChecked == true,
            SourceUrls = urls,
            UpdateFrequency = freq,
            CoverageMode = coverage,
            OvpnRouteLimit = limit
        };

        IpListStore.SaveSettings(settings);
        SaveMessageText.Text = Loc.T("IpList_Saved");
        SaveMessageText.Visibility = Visibility.Visible;
        RefreshStatusTexts(IpListStore.LoadState().Status);
    }

    private async void UpdateNowButton_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateNowButton.IsEnabled = false;
        try
        {
            var result = await _repo.UpdateNowAsync(CancellationToken.None).ConfigureAwait(true);
            RefreshStatusTexts(IpListStore.LoadState().Status);
            if (result.Error is null)
            {
                SaveMessageText.Text = Loc.T("IpList_UpdateReadyFmt", result.RouteCount);
            }
            else
            {
                SaveMessageText.Text = result.UsedFallback
                    ? Loc.T("IpList_UpdateFailedFallbackFmt", result.Error)
                    : Loc.T("IpList_UpdateFailedFmt", result.Error);
            }

            SaveMessageText.Visibility = Visibility.Visible;
        }
        finally
        {
            UpdateNowButton.IsEnabled = CidrEnabledToggle.IsChecked == true;
        }
    }
}
