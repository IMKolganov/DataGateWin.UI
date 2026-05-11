using System.Globalization;
using System.Windows;
using DataGateWin.Configuration;
using DataGateWin.CrashReporting;

namespace DataGateWin.Localization;

public static class UiLanguageService
{
    private static ResourceDictionary? _activeBase;
    private static ResourceDictionary? _activeOverlay;

    public static readonly string[] SupportedCodes = UiLocale.All.Select(l => l.Code).ToArray();

    /// <inheritdoc cref="UiLocale.GetLanguagePickerCodes"/>
    public static IReadOnlyList<string> GetLanguagePickerCodes() => UiLocale.GetLanguagePickerCodes();


    public const string SystemPreference = "system";

    public static event EventHandler? LanguageChanged;

    public static string GetStoredLanguagePreference()
        => NormalizePreferenceForStorage(App.Settings.UiLanguage);

    public static string ResolveEffectiveLanguageCode(string? preference)
    {
        var p = NormalizePreferenceForStorage(preference);
        if (p == SystemPreference)
            return MapCultureToSupported(CultureInfo.CurrentUICulture);
        return p;
    }

    public static string NormalizePreferenceForStorage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return SystemPreference;

        var s = languageCode.Trim().ToLowerInvariant();
        if (s is "system" or "auto" or "default" or "os")
            return SystemPreference;

        if (SupportedCodes.Contains(s, StringComparer.OrdinalIgnoreCase))
            return s;

        return SystemPreference;
    }

    public static string MapCultureToSupported(CultureInfo culture)
        => CultureMapping.MapCultureToSupportedCode(culture);

    public static void ApplyFromSettings()
    {
        Apply(App.Settings.UiLanguage, persist: false);
    }

    public static void Apply(string? languageCode, bool persist)
    {
        var preference = persist
            ? NormalizePreferenceForStorage(languageCode)
            : NormalizePreferenceForStorage(App.Settings.UiLanguage);

        if (persist)
        {
            App.Settings.UiLanguage = preference;
            AppSettingsStore.SaveSafe(App.Settings);
        }

        var effective = ResolveEffectiveLanguageCode(preference);

        try
        {
            var loc = UiLocale.FindByCode(effective);
            var ci = loc != null
                ? CultureInfo.GetCultureInfo(loc.CultureName)
                : CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = ci;
            CultureInfo.DefaultThreadCurrentCulture = ci;
        }
        catch (CultureNotFoundException ex)
        {
            CrashReporter.ReportNonFatal(ex, "UiLanguageService.ApplyCulture");
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        }

        var app = Application.Current;
        if (app is null)
            return;

        var merged = app.Resources.MergedDictionaries;
        if (_activeOverlay is not null)
        {
            merged.Remove(_activeOverlay);
            _activeOverlay = null;
        }

        if (_activeBase is not null)
        {
            merged.Remove(_activeBase);
            _activeBase = null;
        }

        _activeBase = new ResourceDictionary
        {
            Source = new Uri("/DataGateWin;component/Localization/Strings.en.xaml", UriKind.Relative),
        };
        merged.Add(_activeBase);

        if (!string.Equals(effective, "en", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var packPath = $"/DataGateWin;component/Localization/Strings.{effective}.xaml";
                _activeOverlay = new ResourceDictionary
                {
                    Source = new Uri(packPath, UriKind.Relative),
                };
                merged.Add(_activeOverlay);
            }
            catch (Exception ex)
            {
                CrashReporter.ReportNonFatal(ex, "UiLanguageService.LoadOverlay");
                _activeOverlay = null;
            }
        }

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Display name for language list (usually <see cref="CultureInfo.NativeName"/>).</summary>
    public static string GetLanguageDisplayName(string code)
    {
        if (string.Equals(code, SystemPreference, StringComparison.OrdinalIgnoreCase))
        {
            if (Application.Current?.TryFindResource("Lang_Name_system") is string sys && !string.IsNullOrWhiteSpace(sys))
                return sys;
            return "Same as Windows display language";
        }

        var loc = UiLocale.FindByCode(code);
        if (loc is null)
            return code;
        try
        {
            var ci = CultureInfo.GetCultureInfo(loc.CultureName);
            return ci.NativeName;
        }
        catch (CultureNotFoundException ex)
        {
            CrashReporter.ReportNonFatal(ex, "UiLanguageService.GetLanguageDisplayName");
            return code;
        }
    }
}
