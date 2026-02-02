namespace DataGateWin.Models;

public sealed record GoogleAuthResult(
    bool IsSuccess,
    string? AuthorizationCode,
    string? Error,
    string? ErrorDescription
);