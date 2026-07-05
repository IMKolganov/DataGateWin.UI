using DataGateMonitor.SharedModels.DataGateMonitor.Auth.Responses;

namespace DataGateWin.Services.Auth.Interfaces;

public interface ITokenStore
{
    Task<AuthTokensResponse?> LoadAsync(CancellationToken ct);
    Task SaveAsync(AuthTokensResponse tokens, CancellationToken ct);
    Task ClearAsync(CancellationToken ct);
}