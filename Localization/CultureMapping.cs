using System.Globalization;

namespace DataGateWin.Localization;

public static class CultureMapping
{
    /// <summary>Maps a Windows/UI culture to the closest supported app language code.</summary>
    public static string MapCultureToSupportedCode(CultureInfo culture)
    {
        var chain = new List<CultureInfo>();
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
            chain.Add(c);

        foreach (var c in chain)
        {
            foreach (var loc in UiLocale.All)
            {
                try
                {
                    var target = CultureInfo.GetCultureInfo(loc.CultureName);
                    if (NamesMatchUiCulture(c, target))
                        return loc.Code;
                }
                catch (CultureNotFoundException)
                {
                    // ignored
                }
            }
        }

        var two = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        foreach (var loc in UiLocale.All)
        {
            try
            {
                var target = CultureInfo.GetCultureInfo(loc.CultureName);
                if (string.Equals(target.TwoLetterISOLanguageName, two, StringComparison.OrdinalIgnoreCase))
                    return loc.Code;
            }
            catch (CultureNotFoundException)
            {
                // ignored
            }
        }

        return "en";
    }

    static bool NamesMatchUiCulture(CultureInfo user, CultureInfo candidate)
    {
        if (string.Equals(user.Name, candidate.Name, StringComparison.OrdinalIgnoreCase))
            return true;

        if (user.Name.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
        {
            if (candidate.Name.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
            {
                var userIsHant = user.Name.Contains("TW", StringComparison.OrdinalIgnoreCase)
                    || user.Name.Contains("HK", StringComparison.OrdinalIgnoreCase)
                    || user.Name.Contains("MO", StringComparison.OrdinalIgnoreCase)
                    || user.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase);

                var candIsHant = candidate.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase);

                if (userIsHant && candIsHant)
                    return true;
                if (!userIsHant && !candIsHant && candidate.Name.Contains("Hans", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
