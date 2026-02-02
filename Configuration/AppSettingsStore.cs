// AppSettingsStore.cs

using System.IO;
using System.Text.Json;

namespace DataGateWin.Configuration;

public static class AppSettingsStore
{
    private static readonly string Dir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataGate"
        );

    private static readonly string FilePath =
        Path.Combine(Dir, "settings.json");

    public static AppSettings LoadSafe()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);

            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void SaveSafe(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);

            var json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions { WriteIndented = true }
            );

            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Intentionally ignored.
        }
    }
}