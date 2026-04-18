using System.Globalization;
using System.Windows;
using DataGateWin.Configuration;

namespace DataGateWin.Localization;

public static class UiLanguageService
{
    private static ResourceDictionary? _activeStrings;

    public static readonly string[] SupportedCodes = ["en", "fr", "ru", "el"];

    public const string SystemPreference = "system";

    public static event EventHandler? LanguageChanged;

    /// <summary>Value stored in <see cref="AppSettings.UiLanguage"/> for combo binding and persistence.</summary>
    public static string GetStoredLanguagePreference()
        => NormalizePreferenceForStorage(App.Settings.UiLanguage);

    /// <summary>Maps stored preference (including <see cref="SystemPreference"/>) to a supported resource code.</summary>
    public static string ResolveEffectiveLanguageCode(string? preference)
    {
        var p = NormalizePreferenceForStorage(preference);
        if (p == SystemPreference)
            return MapCultureToSupported(CultureInfo.CurrentUICulture);
        return p;
    }

    /// <summary>Normalizes a user-chosen or JSON value to <c>system</c> or one of <see cref="SupportedCodes"/>.</summary>
    public static string NormalizePreferenceForStorage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return SystemPreference;

        var s = languageCode.Trim().ToLowerInvariant();
        if (s is "system" or "auto" or "default" or "os")
            return SystemPreference;

        if (SupportedCodes.Contains(s))
            return s;

        if (s is "gr" or "el-gr" || s.StartsWith("el", StringComparison.Ordinal))
            return "el";
        if (s.StartsWith("fr", StringComparison.Ordinal))
            return "fr";
        if (s.StartsWith("ru", StringComparison.Ordinal))
            return "ru";
        if (s.StartsWith("en", StringComparison.Ordinal))
            return "en";

        return SystemPreference;
    }

    private static string MapCultureToSupported(CultureInfo culture)
    {
        var name = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        return name switch
        {
            "ru" => "ru",
            "fr" => "fr",
            "el" => "el",
            _ => "en"
        };
    }

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

        var culture = effective switch
        {
            "fr" => new CultureInfo("fr-FR"),
            "ru" => new CultureInfo("ru-RU"),
            "el" => new CultureInfo("el-GR"),
            _ => new CultureInfo("en-US")
        };

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;

        var app = Application.Current;
        if (app is null)
            return;

        var merged = app.Resources.MergedDictionaries;
        if (_activeStrings is not null)
        {
            merged.Remove(_activeStrings);
            _activeStrings = null;
        }

        _activeStrings = new ResourceDictionary
        {
            Source = new Uri($"/DataGateWin;component/Localization/Strings.{effective}.xaml", UriKind.Relative)
        };
        merged.Add(_activeStrings);

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }
}
