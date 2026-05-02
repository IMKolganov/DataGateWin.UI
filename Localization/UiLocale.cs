namespace DataGateWin.Localization;

/// <summary>
/// Supported UI locales aligned with Android <c>values-*</c> folders (same language list).
/// <see cref="Code"/> is stored in settings (lowercase, BCP 47 style).
/// </summary>
public sealed record UiLocale(string Code, string CultureName)
{
    /// <summary>Shown first in language dropdowns (English, Russian, Ukrainian, Greek), then <see cref="All"/> in original order.</summary>
    private static readonly string[] LanguagePickerPriority =
        ["en", "ru", "uk", "el"];

    /// <summary>Same entries as <see cref="F:\Android\DataGateAndroid\app\src\main\res"/> locale folders.</summary>
    public static readonly UiLocale[] All =
    [
        new("en", "en-US"),
        new("ar", "ar-SA"),
        new("bg", "bg-BG"),
        new("cs", "cs-CZ"),
        new("da", "da-DK"),
        new("de", "de-DE"),
        new("el", "el-GR"),
        new("es", "es-ES"),
        new("es-mx", "es-MX"),
        new("et", "et-EE"),
        new("fa", "fa-IR"),
        new("fi", "fi-FI"),
        new("fil", "fil-PH"),
        new("fr", "fr-FR"),
        new("ga", "ga-IE"),
        new("hi", "hi-IN"),
        new("hr", "hr-HR"),
        new("hu", "hu-HU"),
        new("id", "id-ID"),
        new("it", "it-IT"),
        new("ja", "ja-JP"),
        new("ko", "ko-KR"),
        new("lt", "lt-LT"),
        new("lv", "lv-LV"),
        new("mt", "mt-MT"),
        new("nl", "nl-NL"),
        new("pl", "pl-PL"),
        new("pt", "pt-PT"),
        new("pt-br", "pt-BR"),
        new("ro", "ro-RO"),
        new("ru", "ru-RU"),
        new("sk", "sk-SK"),
        new("sl", "sl-SI"),
        new("sv", "sv-SE"),
        new("th", "th-TH"),
        new("tr", "tr-TR"),
        new("uk", "uk-UA"),
        new("vi", "vi-VN"),
        new("zh-hans", "zh-Hans"),
        new("zh-hant", "zh-Hant"),
    ];

    /// <inheritdoc cref="LanguagePickerPriority"/>
    public static IReadOnlyList<string> GetLanguagePickerCodes()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(All.Length);

        foreach (var p in LanguagePickerPriority)
        {
            var loc = FindByCode(p);
            if (loc is not null && seen.Add(loc.Code))
                result.Add(loc.Code);
        }

        foreach (var loc in All)
        {
            if (seen.Contains(loc.Code))
                continue;
            result.Add(loc.Code);
            seen.Add(loc.Code);
        }

        return result;
    }

    public static UiLocale? FindByCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var s = code.Trim().ToLowerInvariant();
        return All.FirstOrDefault(l =>
            string.Equals(l.Code, s, StringComparison.OrdinalIgnoreCase));
    }
}
