using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.Auth.Requests;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.Auth.Responses;
using OpenVPNGateMonitor.SharedModels.Responses;

namespace DataGateWin.Services.Auth;

public sealed class GoogleAuthService(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<ApiResponse<GoogleLoginResponse>> SignInAndLoginAsync(
        string clientId,
        int port,
        string dataGateApiBaseUrl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("ClientId is required.", nameof(clientId));

        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(dataGateApiBaseUrl))
            throw new ArgumentException("Api base url is required.", nameof(dataGateApiBaseUrl));

        var redirectUri = $"http://127.0.0.1:{port}/";
        var state = GenerateState();
        var pkce = PkcePair.CreateS256();

        var authorizationUrl = BuildAuthorizationUrl(
            clientId: clientId,
            redirectUri: redirectUri,
            state: state,
            codeChallenge: pkce.CodeChallenge
        );

        NameValueCollection query;

        try
        {
            query = await GetQueryAsync(authorizationUrl, port, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Authorization failed: {ex.Message}", ex);
        }

        var error = query["error"];
        if (!string.IsNullOrWhiteSpace(error))
        {
            var desc = query["error_description"];
            throw new InvalidOperationException($"Authorization error: {error}. {desc}");
        }

        var returnedState = query["state"];
        if (!string.Equals(state, returnedState, StringComparison.Ordinal))
            throw new InvalidOperationException("State validation failed.");

        var code = query["code"];
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Authorization code was not returned.");

        var idToken = await ExchangeCodeForIdTokenAsync(
            clientId: clientId,
            code: code,
            redirectUri: redirectUri,
            codeVerifier: pkce.CodeVerifier,
            ct: ct
        ).ConfigureAwait(false);

        var loginRequest = new GoogleLoginRequest
        {
            IdToken = idToken
        };

        var apiUrl = $"{dataGateApiBaseUrl.TrimEnd('/')}/api/auth/google-login";

        using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(loginRequest, JsonOptions), Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"API login failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");

        var apiResponse = JsonSerializer.Deserialize<ApiResponse<GoogleLoginResponse>>(body, JsonOptions);
        if (apiResponse == null)
            throw new InvalidOperationException("API response deserialization returned null.");

        return apiResponse;
    }

    private static string BuildAuthorizationUrl(string clientId, string redirectUri, string state, string codeChallenge)
    {
        var scope = Uri.EscapeDataString("openid email profile");

        var url =
            "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&response_type=code" +
            $"&scope={scope}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            $"&code_challenge_method=S256" +
            $"&access_type=offline" +
            $"&prompt=select_account";

        return url;
    }

    private async Task<NameValueCollection> GetQueryAsync(string authorizationUrl, int port, CancellationToken ct)
    {
        var prefix = $"http://127.0.0.1:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        using var reg = ct.Register(() =>
        {
            try { listener.Stop(); } catch { }
        });

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = authorizationUrl,
            UseShellExecute = true
        });

        HttpListenerContext context;

        try
        {
            context = await listener.GetContextAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ct.IsCancellationRequested && (ex is HttpListenerException || ex is ObjectDisposedException))
        {
            throw new OperationCanceledException(ct);
        }

        var query = context.Request.QueryString;

        var responseHtml = "<html><body>You can close this window now.</body></html>";
        var buffer = Encoding.UTF8.GetBytes(responseHtml);

        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;

        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
        context.Response.Close();

        return query;
    }

    private async Task<string> ExchangeCodeForIdTokenAsync(
        string clientId,
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("code_verifier", codeVerifier),
        });

        using var resp = await _http.PostAsync("https://oauth2.googleapis.com/token", form, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {json}");

        var tr = JsonSerializer.Deserialize<TokenResponse>(json, JsonOptions);

        if (tr == null || string.IsNullOrWhiteSpace(tr.IdToken))
            throw new InvalidOperationException("Token response did not contain id_token.");

        return tr.IdToken!;
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

    private sealed class TokenResponse
    {
        public string? IdToken { get; set; }
    }

    private sealed class PkcePair
    {
        public string CodeVerifier { get; }
        public string CodeChallenge { get; }

        private PkcePair(string codeVerifier, string codeChallenge)
        {
            CodeVerifier = codeVerifier;
            CodeChallenge = codeChallenge;
        }

        public static PkcePair CreateS256()
        {
            var verifierBytes = new byte[32];
            RandomNumberGenerator.Fill(verifierBytes);
            var verifier = Base64UrlEncode(verifierBytes);

            using var sha = SHA256.Create();
            var challengeBytes = sha.ComputeHash(Encoding.ASCII.GetBytes(verifier));
            var challenge = Base64UrlEncode(challengeBytes);

            return new PkcePair(verifier, challenge);
        }
    }
}
