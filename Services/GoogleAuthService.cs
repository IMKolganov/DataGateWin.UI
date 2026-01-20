using System;
using System.Collections.Specialized;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataGateWin.Models;

namespace DataGateWin.Services;

public sealed class GoogleAuthService
{
    private readonly GoogleAuthLoopback _loopback;

    public GoogleAuthService(GoogleAuthLoopback loopback)
    {
        _loopback = loopback;
    }

    public async Task<GoogleAuthResult> SignInAsync(
        string clientId,
        int port,
        CancellationToken ct)
    {
        var redirectUri = $"http://127.0.0.1:{port}/";
        var state = GenerateState();

        var authorizationUrl = BuildAuthorizationUrl(
            clientId: clientId,
            redirectUri: redirectUri,
            state: state
        );

        try
        {
            var query = await GetQueryAsync(authorizationUrl, port, ct);

            var error = query["error"];
            if (!string.IsNullOrWhiteSpace(error))
            {
                return new GoogleAuthResult(
                    IsSuccess: false,
                    AuthorizationCode: null,
                    Error: error,
                    ErrorDescription: query["error_description"]
                );
            }

            var returnedState = query["state"];
            if (!string.Equals(state, returnedState, StringComparison.Ordinal))
                return new GoogleAuthResult(false, null, "invalid_state", "State validation failed.");

            var code = query["code"];
            if (string.IsNullOrWhiteSpace(code))
                return new GoogleAuthResult(false, null, "missing_code", "Authorization code was not returned.");

            return new GoogleAuthResult(true, code, null, null);
        }
        catch (Exception ex)
        {
            return new GoogleAuthResult(false, null, "exception", ex.Message);
        }
    }

    private static string BuildAuthorizationUrl(string clientId, string redirectUri, string state)
    {
        // Minimal scopes for sign-in
        var scope = Uri.EscapeDataString("openid email profile");

        // response_type=code is OAuth2 Authorization Code flow
        // prompt=select_account forces account picker (optional)
        var url =
            "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&response_type=code" +
            $"&scope={scope}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&access_type=offline" +
            $"&prompt=select_account";

        return url;
    }

    private async Task<NameValueCollection> GetQueryAsync(string authorizationUrl, int port, CancellationToken ct)
    {
        // Slightly enhanced loopback: we need full query, not just code.
        // We'll reuse the existing loopback by generating a URL and then listening,
        // but to keep your current class unchanged, we can parse the request here.

        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = authorizationUrl,
            UseShellExecute = true
        });

        using var reg = ct.Register(() =>
        {
            try { listener.Stop(); } catch { }
        });

        var context = await listener.GetContextAsync();
        var query = context.Request.QueryString;

        var responseHtml = "<html><body>You can close this window now.</body></html>";
        var buffer = Encoding.UTF8.GetBytes(responseHtml);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct);
        context.Response.Close();

        return query;
    }

    private static string GenerateState()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
