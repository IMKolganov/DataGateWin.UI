using DataGateWin.Services.Auth.Interfaces;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.Auth.Responses;

namespace DataGateWin.Services.Auth;

public sealed class InMemoryTokenStore : ITokenStore
{
    private AuthTokensResponse? _current;

    public Task<AuthTokensResponse?> LoadAsync(CancellationToken ct)
        => Task.FromResult(_current);

    public Task SaveAsync(AuthTokensResponse tokens, CancellationToken ct)
    {
        _current = tokens;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct)
    {
        _current = null;
        return Task.CompletedTask;
    }
}