namespace DataGateWin.Services.Auth;

public sealed class AuthStateStore
{
    public bool IsAuthorized { get; private set; }
    public string? AuthorizationCode { get; private set; }

    public void SetAuthorized(string authorizationCode)
    {
        AuthorizationCode = authorizationCode;
        IsAuthorized = true;
    }

    public void Clear()
    {
        AuthorizationCode = null;
        IsAuthorized = false;
    }
}