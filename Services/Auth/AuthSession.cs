using DataGateWin.Services.Auth.Interfaces;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.Auth.Requests;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.Auth.Responses;

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
        _current = login;
        await store.SaveAsync(login, ct).ConfigureAwait(false);
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

        var refreshed = await RefreshAsync(ct).ConfigureAwait(false);
        return refreshed ? _current?.Token : null;
    }

    public async Task<bool> RefreshAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = _current;
            if (current == null)
                return false;

            if (IsAccessValid(current))
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

            var response = await authApi.RefreshAsync(request, ct).ConfigureAwait(false);
            if (!response.Success || response.Data == null)
                return false;

            _current = response.Data;
            await store.SaveAsync(_current, ct).ConfigureAwait(false);
            return true;
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
}
