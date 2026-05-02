namespace DataGateWin.Services.Identity;

public static class AccountDisplay
{
    public static string? TryResolveDisplayName(string? bearerToken)
    {
        var fromJwt = JwtClaimReader.GetDisplayNameFromBearerToken(bearerToken);
        if (!string.IsNullOrWhiteSpace(fromJwt))
            return fromJwt.Trim();

        var email = JwtClaimReader.GetClaimFromBearerToken(bearerToken, "email");
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var at = email.IndexOf('@');
        return at > 0 ? email[..at].Trim() : email.Trim();
    }

    public static string GetInitials(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "?";

        var segments = displayName.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return "?";

        if (segments.Length >= 2)
            return string.Concat(FirstGlyphUpper(segments[0]), FirstGlyphUpper(segments[^1]));

        var s = segments[0];
        return s.Length >= 2 ? s[..2].ToUpperInvariant() : s.ToUpperInvariant();
    }

    private static string FirstGlyphUpper(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";

        var si = new System.Globalization.StringInfo(s);
        return si.SubstringByTextElements(0, 1).ToUpperInvariant();
    }
}
