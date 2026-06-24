using System.IO;
using DataGateWin.CrashReporting;
using Newtonsoft.Json;

namespace DataGateWin.Configuration;

/// <summary>
/// Reads/writes <c>appsettings.json</c> (Api + GoogleAuth) next to the executable.
/// </summary>
public static class AppsettingsConnection
{
    public const int DefaultRedirectPort = 51723;

    public sealed class DocumentDto
    {
        public ApiSettings? Api { get; set; }
        public GoogleAuthSettings? GoogleAuth { get; set; }
        public CrashReportingConfiguration CrashReporting { get; set; } = CreateDefaultCrashReporting();
    }

    public static bool TryLoadFile(string path, out ApiSettings? api, out GoogleAuthSettings? google)
    {
        api = null;
        google = null;

        if (!File.Exists(path))
            return false;

        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonConvert.DeserializeObject<DocumentDto>(json);
            if (dto is null)
                return false;

            api = dto.Api;
            google = dto.GoogleAuth;
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "AppsettingsConnection.TryLoadFile");
            return false;
        }
    }

    public static void SaveFile(string directory, ApiSettings api, GoogleAuthSettings google)
    {
        var path = Path.Combine(directory, "appsettings.json");
        var dto = new DocumentDto
        {
            Api = api,
            GoogleAuth = google,
            CrashReporting = CreateDefaultCrashReporting()
        };
        File.WriteAllText(path, JsonConvert.SerializeObject(dto, Formatting.Indented));
    }

    public static CrashReportingConfiguration CreateDefaultCrashReporting() =>
        new()
        {
            Enabled = true,
            ProcessName = CrashReporter.DefaultProcessName,
            CrashToken = ""
        };

    public static bool IsComplete(ApiSettings? api, GoogleAuthSettings? google)
    {
        if (api is null || google is null)
            return false;

        if (string.IsNullOrWhiteSpace(api.BaseUrl))
            return false;

        if (!Uri.TryCreate(api.BaseUrl.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (string.IsNullOrWhiteSpace(google.ClientId))
            return false;

        var port = google.RedirectPort;
        return port is > 0 and <= 65535;
    }
}
