using DataGateMonitor.SharedModels.DataGateMonitor.Auth.Responses;

namespace DataGateWin.Services.Auth;

public static class FreeTierOnboardingPolicy
{
    public static bool ShouldShow(FreeTierAccessStatusResponse? status)
    {
        return status is { IsApplicable: true, IsCompliant: false };
    }
}
