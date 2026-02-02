using System;
using System.IO;

namespace DataGateWin.Services.Auth;

public static class DeviceInfo
{
    public static string GetOrCreateDeviceId()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DataGateWin");

        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "device.id");

        if (File.Exists(path))
            return File.ReadAllText(path).Trim();

        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, id);
        return id;
    }

    public static string GetUserAgent()
        => $"DataGateWin/1.0 (Windows)";
}