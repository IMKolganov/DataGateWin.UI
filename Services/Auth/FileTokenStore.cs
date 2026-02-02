using System.IO;
using DataGateWin.Services.Auth.Interfaces;
using Newtonsoft.Json;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.Auth.Responses;

namespace DataGateWin.Services.Auth;

public sealed class FileTokenStore : ITokenStore
{
    private readonly string _path;

    public FileTokenStore(string appName)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(root, appName);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "auth.json");
    }

    public async Task<AuthTokensResponse?> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return null;

        var json = await File.ReadAllTextAsync(_path, ct);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonConvert.DeserializeObject<AuthTokensResponse>(json);
    }

    public async Task SaveAsync(AuthTokensResponse tokens, CancellationToken ct)
    {
        var json = JsonConvert.SerializeObject(tokens, Formatting.Indented);
        await File.WriteAllTextAsync(_path, json, ct);
    }

    public Task ClearAsync(CancellationToken ct)
    {
        if (File.Exists(_path))
            File.Delete(_path);

        return Task.CompletedTask;
    }
}