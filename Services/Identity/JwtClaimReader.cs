using System.Text;
using Newtonsoft.Json.Linq;

namespace DataGateWin.Services.Identity;

public static class JwtClaimReader
{
    public static string? GetClaimFromBearerToken(string? bearerToken, string claimName)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return null;

        var parts = bearerToken.Split('.');
        if (parts.Length < 2)
            return null;

        var payload = parts[1]
            .Replace('-', '+')
            .Replace('_', '/');

        switch (payload.Length % 4)
        {
            case 2: payload += "=="; break;
            case 3: payload += "="; break;
        }

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        var obj = JObject.Parse(json);

        return obj.Value<string>(claimName);
    }
}