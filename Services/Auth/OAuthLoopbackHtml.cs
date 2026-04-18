using System.Net;

namespace DataGateWin.Services.Auth;

/// <summary>
/// OAuth loopback pages — same markup and CSS as DataGateLinux <c>src/GoogleAuthHelper.cpp</c>
/// (<c>oauthLoopbackPageCss</c>, <c>oauthCallbackShell</c>, footer, success/error/bad-request shells).
/// </summary>
internal static class OAuthLoopbackHtml
{
    // BrandColors.h — Dark / Light (must stay in sync with Linux Qt app).
    private const string DarkBgDefault = "#0d1117";
    private const string DarkBgMuted = "#161b22";
    private const string DarkBgSubtle = "#21262d";
    private const string DarkTextDefault = "#c9d1d9";
    private const string DarkTextMuted = "#8b949e";
    private const string DarkAccentFg = "#58a6ff";
    private const string DarkAccentEmphasis = "#1f6feb";
    private const string DarkBorderDefault = "#30363d";
    private const string DarkDanger = "#f85149";
    private const string DarkSuccess = "#3fb950";

    private const string LightBgDefault = "#f6f8fa";
    private const string LightBgMuted = "#ffffff";
    private const string LightBgSubtle = "#f6f8fa";
    private const string LightTextDefault = "#24292f";
    private const string LightTextMuted = "#656d76";
    private const string LightAccentFg = "#0969da";
    private const string LightBorderDefault = "#d0d7de";

    private static string OauthLoopbackPageCss()
    {
        var vars =
            ":root{" +
            $"--bg-default:{DarkBgDefault};--bg-muted:{DarkBgMuted};--bg-subtle:{DarkBgSubtle};" +
            $"--text-default:{DarkTextDefault};--text-muted:{DarkTextMuted};--accent-fg:{DarkAccentFg};--accent-emphasis:{DarkAccentEmphasis};--border-default:{DarkBorderDefault};" +
            $"--danger-fg:{DarkDanger};--success-fg:{DarkSuccess};" +
            "}" +
            "@media (prefers-color-scheme:light){:root{" +
            $"--bg-default:{LightBgDefault};--bg-muted:{LightBgMuted};--bg-subtle:{LightBgSubtle};" +
            $"--text-default:{LightTextDefault};--text-muted:{LightTextMuted};--accent-fg:{LightAccentFg};--accent-emphasis:{LightAccentFg};--border-default:{LightBorderDefault};" +
            "}}";
        return vars +
               "*{box-sizing:border-box;}" +
               "html{font-size:16px;-webkit-font-smoothing:antialiased;}" +
               "body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI','Noto Sans',Helvetica,Arial,sans-serif;" +
               "background:var(--bg-default);color:var(--text-default);min-height:100vh;margin:0;" +
               "display:flex;align-items:center;justify-content:center;padding:24px;line-height:1.5;}" +
               ".oauth-wrap{width:100%;max-width:480px;}" +
               ".oauth-card{background:var(--bg-muted);border:1px solid var(--border-default);border-radius:8px;" +
               "padding:32px 28px 28px;text-align:center;" +
               "box-shadow:0 8px 24px rgba(1,4,9,.4);}" +
               "@media (prefers-color-scheme:light){.oauth-card{box-shadow:0 8px 24px rgba(31,35,40,.1);}}" +
               ".brand{font-size:1.0625rem;font-weight:600;color:var(--text-default);margin-bottom:20px;}" +
               "h1{font-size:1.25rem;font-weight:600;margin:0 0 12px;line-height:1.35;color:var(--text-default);}" +
               ".oauth-main p{font-size:.95rem;color:var(--text-muted);margin:0;line-height:1.55;}" +
               ".ok{width:64px;height:64px;margin:0 auto 20px;border-radius:50%;background:var(--bg-subtle);" +
               "color:var(--success-fg);display:flex;align-items:center;justify-content:center;font-size:32px;font-weight:600;}" +
               ".bad{width:64px;height:64px;margin:0 auto 20px;border-radius:50%;background:var(--bg-subtle);" +
               "color:var(--danger-fg);display:flex;align-items:center;justify-content:center;font-size:30px;font-weight:600;}" +
               ".oauth-detail{margin-top:16px;font-size:.85rem;color:var(--text-muted);word-break:break-word;}" +
               ".oauth-footer{margin-top:24px;padding-top:20px;border-top:1px solid var(--border-default);text-align:center;}" +
               ".oauth-more-title{font-weight:600;font-size:.9375rem;color:var(--text-default);margin:14px 0 8px;}" +
               ".oauth-muted{font-size:.8125rem;color:var(--text-muted);margin:0 0 8px;line-height:1.45;}" +
               ".oauth-line{margin:6px 0;font-size:.875rem;}" +
               ".oauth-a{color:var(--accent-fg);text-decoration:none;}" +
               ".oauth-a:hover{text-decoration:underline;}";
    }

    private static string OauthHtmlFooter()
    {
        const string more = "More apps";
        const string hint = "You can download other DataGate apps for your devices from the website:";
        const string site = "https://datagateapp.com/";
        const string download = "https://datagateapp.com/download";
        return
            "<footer class=\"oauth-footer\" role=\"contentinfo\">" +
            $"<p class=\"oauth-line\"><a class=\"oauth-a\" href=\"{site}\" rel=\"noopener noreferrer\">{site}</a></p>" +
            $"<p class=\"oauth-more-title\">{WebUtility.HtmlEncode(more)}</p>" +
            $"<p class=\"oauth-muted\">{WebUtility.HtmlEncode(hint)}</p>" +
            $"<p class=\"oauth-line\"><a class=\"oauth-a\" href=\"{download}\" rel=\"noopener noreferrer\">{download}</a></p>" +
            "</footer>";
    }

    private static string OauthCallbackShell(string innerBody)
    {
        return "<!DOCTYPE html><html lang=\"en\"><head>" +
               "<meta charset=\"utf-8\">" +
               "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
               "<meta name=\"color-scheme\" content=\"dark light\">" +
               "<meta name=\"theme-color\" content=\"#0d1117\">" +
               "<title>DataGate</title><style>" +
               OauthLoopbackPageCss() +
               "</style></head><body><div class=\"oauth-wrap\"><div class=\"oauth-card\">" +
               innerBody +
               OauthHtmlFooter() +
               "</div></div></body></html>";
    }

    public static string SuccessDocument()
    {
        const string h = "You're signed in";
        const string p = "You can close this tab and return to the DataGate app.";
        return OauthCallbackShell(
            "<div class=\"brand\">DataGate</div><div class=\"oauth-main\"><div class=\"ok\">✓</div><h1>" +
            WebUtility.HtmlEncode(h) + "</h1><p>" + WebUtility.HtmlEncode(p) + "</p></div>");
    }

    public static string GoogleErrorDocument(string errorCode, string? errorDescription)
    {
        const string h = "Sign-in did not complete";
        const string hint = "You can close this tab and try again from DataGate.";
        var detail = $"{errorCode} — {errorDescription ?? string.Empty}";
        return OauthCallbackShell(
            "<div class=\"brand\">DataGate</div><div class=\"oauth-main\"><div class=\"bad\">✕</div><h1>" +
            WebUtility.HtmlEncode(h) + "</h1><p>" + WebUtility.HtmlEncode(hint) + "</p><p class=\"oauth-detail\">" +
            WebUtility.HtmlEncode(detail) + "</p></div>");
    }

    public static string BadRequestDocument()
    {
        const string h = "Invalid sign-in request";
        const string p = "Close this tab and start sign-in again from DataGate.";
        return OauthCallbackShell(
            "<div class=\"brand\">DataGate</div><div class=\"oauth-main\"><div class=\"bad\">!</div><h1>" +
            WebUtility.HtmlEncode(h) + "</h1><p>" + WebUtility.HtmlEncode(p) + "</p></div>");
    }
}
