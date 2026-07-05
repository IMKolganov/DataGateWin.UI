using DataGateMonitor.SharedModels.DataGateMonitor.Auth.Responses;

namespace DataGateWin.Services.Auth;

public enum FreeTierOnboardingCopyMode
{
    LinkAccount,
    SubscribeOnly,
    Generic,
}

public static class FreeTierOnboardingPolicy
{
    public const int ResumeRefreshMinIntervalSeconds = 45;
    public const int LinkCodeExpiryWarningSeconds = 300;
    public const string DefaultRequiredTelegramChannelHandle = "DataGateVPNBot";
    public const string DefaultTelegramBotUrl = "https://t.me/DataGateVPNBot";

    public static bool ShouldShow(FreeTierAccessStatusResponse? status) =>
        status is { IsApplicable: true, IsCompliant: false };

    public static bool ShouldRefreshOnPoll(DateTimeOffset lastFetchUtc, DateTimeOffset nowUtc) =>
        lastFetchUtc == default ||
        (nowUtc - lastFetchUtc).TotalSeconds >= ResumeRefreshMinIntervalSeconds;

    public static FreeTierOnboardingCopyMode GetCopyMode(FreeTierAccessStatusResponse status) =>
        status.CanRequestAccountLinkCode ? FreeTierOnboardingCopyMode.LinkAccount
        : status.IsLinkedToTelegram ? FreeTierOnboardingCopyMode.SubscribeOnly
        : FreeTierOnboardingCopyMode.Generic;

    public static bool IsLinkCodeExpired(DateTimeOffset expiresAtUtc, DateTimeOffset nowUtc) =>
        expiresAtUtc != default && nowUtc >= expiresAtUtc;

    public static bool ShouldWarnLinkCodeExpiringSoon(int secondsLeft) =>
        secondsLeft is > 0 and <= LinkCodeExpiryWarningSeconds;

    public static string FormatCountdown(int totalSeconds)
    {
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{minutes}:{seconds:D2}";
    }

    public static string ToTelegramChannelUrl(string? requiredChannel)
    {
        if (string.IsNullOrWhiteSpace(requiredChannel))
            return $"https://t.me/{DefaultRequiredTelegramChannelHandle}";

        var value = requiredChannel.Trim();
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return value;

        if (value.StartsWith('@'))
            value = value[1..];

        return $"https://t.me/{value}";
    }

    public static string ResolveChannelLabel(FreeTierAccessStatusResponse status) =>
        string.IsNullOrWhiteSpace(status.RequiredChannel)
            ? DefaultRequiredTelegramChannelHandle
            : status.RequiredChannel.Trim();
}
