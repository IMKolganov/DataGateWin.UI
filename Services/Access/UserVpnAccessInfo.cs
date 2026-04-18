namespace DataGateWin.Services.Access;

public sealed class UserVpnAccessInfo
{
    public string PlanName { get; init; } = "";
    public string EffectiveFrom { get; init; } = "";
    public string EffectiveTo { get; init; } = "";
    public string AssignmentNote { get; init; } = "";
    public string QuotaApiError { get; init; } = "";
    public long QuotaLimitBytes { get; init; }
    public bool QuotaPeriodIsMonthly { get; init; } = true;
    public long TrafficUsedBytesForPeriod { get; init; } = -1;
    public bool TrafficUsageNeedsExternalId { get; init; }
}
