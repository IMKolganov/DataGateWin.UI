using System.IO;
using System.Net.Http;
using DataGateWin.Configuration;
using DataGateWin.CrashReporting;

namespace DataGateWin.Services.IpList;

public sealed class IpListUpdateResult(
    int routeCount,
    bool reachedRouteLimit,
    bool usedFallback,
    string? error)
{
    public int RouteCount { get; } = routeCount;
    public bool ReachedRouteLimit { get; } = reachedRouteLimit;
    public bool UsedFallback { get; } = usedFallback;
    public string? Error { get; } = error;
}

public sealed class IpListRoutesRepository
{
    private static readonly HttpClient Http = CreateHttp();

    private const int MaxBodyChars = 1_000_000;

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("DataGateWin/1.0");
        return c;
    }

    public async Task<IReadOnlyList<IpCidrRoute>> GetRoutesForConnectionAsync(CancellationToken ct)
    {
        var settings = IpListStore.LoadSettings();
        if (!settings.CidrListsEnabled)
            return Array.Empty<IpCidrRoute>();

        string? text;

        if (IpListStore.ShouldRefreshCachedList(settings))
        {
            var fetch = await FetchConfiguredListsAsync(settings.SourceUrls, ct).ConfigureAwait(false);
            if (fetch.Ok && fetch.Text is not null)
            {
                text = fetch.Text;
                var parsed = IpListRouteConfig.ParseCidrRoutesResult(text);
                IpListStore.WriteCachedList(text, parsed.Routes.Count, parsed.ReachedRouteLimit);
            }
            else
            {
                if (!string.IsNullOrEmpty(fetch.Error))
                    IpListStore.SaveLastError(fetch.Error);
                text = IpListStore.ReadCachedList() ?? await LoadFallbackListAsync(ct).ConfigureAwait(false);
            }
        }
        else
        {
            text = IpListStore.ReadCachedList();
        }

        text ??= await LoadFallbackListAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<IpCidrRoute>();

        var result = IpListRouteConfig.ParseCidrRoutesResult(text);
        ApplyParsedStatus(result);
        return result.Routes;
    }

    private static void ApplyParsedStatus(IpListParseResult result)
    {
        var doc = IpListStore.LoadState();
        doc.Status.LoadedRouteCount = result.Routes.Count;
        doc.Status.ReachedRouteLimit = result.ReachedRouteLimit;
        IpListStore.SaveDocument(doc);
    }

    public async Task<IpListUpdateResult> UpdateNowAsync(CancellationToken ct)
    {
        var settings = IpListStore.LoadSettings();
        if (!settings.CidrListsEnabled)
            return new IpListUpdateResult(0, false, false, null);

        var fetch = await FetchConfiguredListsAsync(settings.SourceUrls, ct).ConfigureAwait(false);
        if (fetch.Ok && fetch.Text is not null)
        {
            var result = IpListRouteConfig.ParseCidrRoutesResult(fetch.Text);
            IpListStore.WriteCachedList(fetch.Text, result.Routes.Count, result.ReachedRouteLimit);
            return new IpListUpdateResult(result.Routes.Count, result.ReachedRouteLimit, false, null);
        }

        var fallback = IpListStore.ReadCachedList() ?? await LoadFallbackListAsync(ct).ConfigureAwait(false);
        var parsed = IpListRouteConfig.ParseCidrRoutesResult(fallback ?? "");
        var doc = IpListStore.LoadState();
        doc.Status.LoadedRouteCount = parsed.Routes.Count;
        doc.Status.ReachedRouteLimit = parsed.ReachedRouteLimit;
        doc.Status.LastError = fetch.Error;
        IpListStore.SaveDocument(doc);

        return new IpListUpdateResult(
            parsed.Routes.Count,
            parsed.ReachedRouteLimit,
            fallback != null,
            fetch.Error);
    }

    private static async Task<(bool Ok, string? Text, string? Error)> FetchConfiguredListsAsync(
        IReadOnlyList<string> urls,
        CancellationToken ct)
    {
        var trimmed = urls
            .Select(u => u.Trim())
            .Where(u => u.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (trimmed.Count == 0)
            return (false, null, "No IP list URLs configured");

        var contents = new List<string>();
        var errors = new List<string>();
        foreach (var url in trimmed)
        {
            var one = await FetchListAsync(url, ct).ConfigureAwait(false);
            if (one.Ok && one.Text is not null)
                contents.Add(one.Text);
            else
                errors.Add($"{url}: {one.Error}");
        }

        if (contents.Count == 0)
            return (false, null, string.Join("; ", errors));

        var warn = errors.Count > 0 ? string.Join("; ", errors) : null;
        return (true, string.Join("\n", contents), warn);
    }

    private static async Task<(bool Ok, string? Text, string? Error)> FetchListAsync(
        string url,
        CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (false, null, $"HTTP {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (body.Length > MaxBodyChars)
                return (false, null, "response too large");

            return (true, body, null);
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "IpListRoutesRepository.FetchList");
            return (false, null, ex.Message);
        }
    }

    private static async Task<string?> LoadFallbackListAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var v4 = ReadFile(Path.Combine(baseDir, "Assets", "IpLists", "ru_ipv4_fallback.txt"));
            var v6 = ReadFile(Path.Combine(baseDir, "Assets", "IpLists", "ru_ipv6_fallback.txt"));
            var merged = string.Join("\n", new[] { v4, v6 }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return string.IsNullOrWhiteSpace(merged) ? null : merged;
        }, ct).ConfigureAwait(false);
    }

    private static string? ReadFile(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;
}
