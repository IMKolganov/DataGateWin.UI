using System.Globalization;
using System.Windows;
using DataGateWin.Configuration;

namespace DataGateWin.Localization;

public static class UiLanguageService
{
    private static ResourceDictionary? _activeStrings;

    public static readonly string[] SupportedCodes = ["en", "fr", "ru", "el"];

    public static event EventHandler? LanguageChanged;

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "en";
        var c = code.Trim().ToLowerInvariant();
        if (c is "gr" or "el-gr" or "el")
            return "el";
        if (c.StartsWith("fr", StringComparison.Ordinal))
            return "fr";
        if (c.StartsWith("ru", StringComparison.Ordinal))
            return "ru";
        if (c.StartsWith("en", StringComparison.Ordinal))
            return "en";
        return SupportedCodes.Contains(c) ? c : "en";
    }

    public static void ApplyFromSettings()
    {
        Apply(App.Settings.UiLanguage, persist: false);
    }

    public static void Apply(string? languageCode, bool persist)
    {
        var code = Normalize(languageCode);
        if (persist)
        {
            App.Settings.UiLanguage = code;
            AppSettingsStore.SaveSafe(App.Settings);
        }

        var culture = code switch
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
            Source = new Uri($"/DataGateWin;component/Localization/Strings.{code}.xaml", UriKind.Relative)
        };
        merged.Add(_activeStrings);

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }
}
