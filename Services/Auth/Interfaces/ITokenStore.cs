using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.Auth.Responses;

namespace DataGateWin.Services.Auth.Interfaces;

public interface ITokenStore
{
    AuthTokensResponse? Current { get; }
    void Set(AuthTokensResponse tokens);
    void Clear();
}