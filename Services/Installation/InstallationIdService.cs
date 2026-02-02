using System;
using System.Security.Cryptography;
using DataGateWin.Configuration;

namespace DataGateWin.Services.Installation;

public sealed class InstallationIdService
{
    public string GetOrCreate()
    {
        var settings = AppSettingsStore.LoadSafe();

        if (!string.IsNullOrWhiteSpace(settings.InstallationId))
            return settings.InstallationId;

        var installationId = $"{RandomTokenUrlSafe(22)}";

        settings.InstallationId = installationId;
        AppSettingsStore.SaveSafe(settings);

        return installationId;
    }

    private static string RandomTokenUrlSafe(int length)
    {
        while (true)
        {
            var byteLen = (int)Math.Ceiling(length * 0.75);
            var bytes = RandomNumberGenerator.GetBytes(byteLen);

            var s = Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');

            if (s.Length < length)
                continue;

            s = s[..length];

            if (!s.EndsWith("-", StringComparison.Ordinal))
                return s;
        }
    }
}