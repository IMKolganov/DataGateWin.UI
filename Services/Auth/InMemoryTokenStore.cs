using DataGateWin.Services.Auth.Interfaces;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.Auth.Responses;

namespace DataGateWin.Services.Auth;

public sealed class InMemoryTokenStore : ITokenStore
{
    private AuthTokensResponse? _current;

    public AuthTokensResponse? Current => _current;

    public void Set(AuthTokensResponse tokens)
        => _current = tokens;

    AuthTokensResponse? ITokenStore.Current => Current;

    void ITokenStore.Set(AuthTokensResponse tokens)
    {
        Set(tokens);
    }

    public void Clear()
        => _current = null;
}
