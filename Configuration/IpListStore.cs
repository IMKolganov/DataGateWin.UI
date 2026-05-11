using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataGateWin.CrashReporting;
using DataGateWin.Services.IpList;

namespace DataGateWin.Configuration;

public static class IpListStore
{
    private static readonly string Dir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataGate");

    private static readonly string StatePath = Path.Combine(Dir, "ip-list-state.json");
    private static readonly string CachedListPath = Path.Combine(Dir, "ip-list-cached.txt");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static IpListStateDocument LoadState()
    {
        try
        {
            if (!File.Exists(StatePath))
                return new IpListStateDocument();

            var json = File.ReadAllText(StatePath);
            var doc = JsonSerializer.Deserialize<IpListStateDocument>(json, JsonOptions);
            return doc ?? new IpListStateDocument();
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "IpListStore.LoadState");
            return new IpListStateDocument();
        }
    }

    public static void SaveDocument(IpListStateDocument doc)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(doc, JsonOptions);
            File.WriteAllText(StatePath, json);
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "IpListStore.SaveDocument");
            // ignored
        }
    }

    public static IpListUserSettings LoadSettings() => LoadState().Settings;

    public static void SaveSettings(IpListUserSettings settings)
    {
        var doc = LoadState();
        doc.Settings = settings;
        SaveDocument(doc);
    }

    public static string? ReadCachedList()
    {
        try
        {
            return File.Exists(CachedListPath) ? File.ReadAllText(CachedListPath) : null;
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "IpListStore.ReadCachedList");
            return null;
        }
    }

    public static void WriteCachedList(string content, int routeCount, bool reachedRouteLimit)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(CachedListPath, content);
            var doc = LoadState();
            doc.Status = new IpListRuntimeStatus
            {
                LastUpdatedEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LoadedRouteCount = routeCount,
                LastError = null,
                ReachedRouteLimit = reachedRouteLimit
            };
            SaveDocument(doc);
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "IpListStore.WriteCachedList");
            // ignored
        }
    }

    public static void SaveStatusOnly(IpListRuntimeStatus status)
    {
        var doc = LoadState();
        doc.Status = status;
        SaveDocument(doc);
    }

    public static void SaveLastError(string error)
    {
        var doc = LoadState();
        doc.Status.LastError = error;
        SaveDocument(doc);
    }

    public static bool ShouldRefreshCachedList(IpListUserSettings settings)
    {
        if (settings.UpdateFrequency == IpListUpdateFrequency.Manual)
            return string.IsNullOrWhiteSpace(ReadCachedList());

        var doc = LoadState();
        var last = doc.Status.LastUpdatedEpochMs ?? 0L;
        if (string.IsNullOrWhiteSpace(ReadCachedList()))
            return true;

        var intervalMs = settings.UpdateFrequency.Hours() * 60L * 60L * 1000L;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - last >= intervalMs;
    }

    public static string CachedListFilePath => CachedListPath;
}
