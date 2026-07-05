using System.Net;
using System.Net.Http;
using DataGateWin.Services.Auth.Interfaces;
using DataGateMonitor.SharedModels.DataGateMonitor.Auth.Requests;
using DataGateMonitor.SharedModels.DataGateMonitor.Auth.Responses;

namespace DataGateWin.Services.Auth;

public sealed class AuthSession(
    AuthApiClient authApi,
    ITokenStore store,
    string deviceId,
    string userAgent)
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private AuthTokensResponse? _current;

    public AuthTokensResponse? Current => _current;

    public async Task InitializeAsync(CancellationToken ct)
    {
        _current = await store.LoadAsync(ct).ConfigureAwait(false);
    }

    public async Task SetFromLoginAsync(GoogleLoginResponse login, CancellationToken ct)
    {
        _current = ToAuthTokens(login);
        await store.SaveAsync(_current, ct).ConfigureAwait(false);
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        _current = null;
        await store.ClearAsync(ct).ConfigureAwait(false);
    }

    public async Task<string?> GetValidAccessTokenAsync(CancellationToken ct)
    {
        var current = _current;
        if (current == null)
            return null;

        if (IsAccessValid(current))
            return current.Token;

        var refreshed = await RefreshAsync(ct, forceRefresh: false).ConfigureAwait(false);
        return refreshed ? _current?.Token : null;
    }

    /// <param name="forceRefresh">If true, calls the refresh endpoint even when the access token is still within the local validity window (e.g. after HTTP 401).</param>
    public async Task<bool> RefreshAsync(CancellationToken ct, bool forceRefresh = false)
    {
        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = _current;
            if (current == null)
                return false;

            if (!forceRefresh && IsAccessValid(current))
                return true;

            if (current.RefreshToken == null || current.RefreshExpiration == null)
                return false;

            if (current.RefreshExpiration <= DateTimeOffset.UtcNow.AddSeconds(5))
                return false;

            var request = new RefreshRequest
            {
                RefreshToken = current.RefreshToken,
                DeviceId = deviceId,
                UserAgent = userAgent
            };

            try
            {
                var response = await authApi.RefreshAsync(request, ct).ConfigureAwait(false);

                if (!response.Success || response.Data == null)
                    return false;

                _current = ToAuthTokens(response.Data);
                await store.SaveAsync(_current, ct).ConfigureAwait(false);
                return true;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LogoutAsync(ct).ConfigureAwait(false);
                return false;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                await LogoutAsync(ct).ConfigureAwait(false);
                return false;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static bool IsAccessValid(AuthTokensResponse t)
    {
        var now = DateTimeOffset.UtcNow;
        return t.Expiration > now.AddSeconds(60);
    }

    private static AuthTokensResponse ToAuthTokens(GoogleLoginResponse login) => ToAuthTokens(
        login.Token,
        login.Expiration,
        login.RefreshToken,
        login.RefreshExpiration);

    private static AuthTokensResponse ToAuthTokens(RefreshResponse refresh) => ToAuthTokens(
        refresh.Token,
        refresh.Expiration,
        refresh.RefreshToken,
        refresh.RefreshExpiration);

    private static AuthTokensResponse ToAuthTokens(
        string? token,
        DateTimeOffset expiration,
        string? refreshToken,
        DateTimeOffset? refreshExpiration) => new()
    {
        Token = token ?? throw new InvalidOperationException("Auth response is missing access token."),
        Expiration = expiration,
        RefreshToken = refreshToken,
        RefreshExpiration = refreshExpiration
    };
}
