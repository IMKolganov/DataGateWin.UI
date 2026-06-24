using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using DataGateWin.CrashReporting;
using DataGateWin.Services.Identity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataGateWin.Services.Access;

public sealed class UserVpnAccessClient(HttpClient http)
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<UserVpnAccessInfo> FetchAsync(string? bearerJwt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bearerJwt))
            return new UserVpnAccessInfo();

        var userIdStr = JwtClaimReader.GetNumericUserIdFromBearerToken(bearerJwt)?.Trim();
        if (!int.TryParse(userIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId)
            || userId <= 0)
            return new UserVpnAccessInfo();

        var quotaPlansJson = JsonConvert.SerializeObject(new { includeInactive = true });
        var quotaPlansBody = new ByteArrayContent(Encoding.UTF8.GetBytes(quotaPlansJson));
        quotaPlansBody.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var plansReq = new HttpRequestMessage(HttpMethod.Post, "api/quota-plans/get-all")
        {
            Content = quotaPlansBody
        };

        using var plansResp = await _http.SendAsync(plansReq, ct).ConfigureAwait(false);
        var plansRaw = await plansResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!plansResp.IsSuccessStatusCode)
        {
            return new UserVpnAccessInfo
            {
                QuotaApiError = $"Quota plans: {(int)plansResp.StatusCode} — {Truncate(plansRaw, 300)}"
            };
        }

        var plansRoot = SafeObject(plansRaw);
        if (plansRoot is null)
            return new UserVpnAccessInfo { QuotaApiError = "Invalid JSON from quota-plans/get-all." };

        if (!(plansRoot.Value<bool?>("success") ?? false))
        {
            var msg = plansRoot.Value<string>("message") ?? plansRoot.Value<string>("Message") ?? "Quota plans request failed.";
            return new UserVpnAccessInfo
            {
                QuotaApiError = string.IsNullOrWhiteSpace(msg) ? "Quota plans request failed." : msg
            };
        }

        var plansById = ParseQuotaPlans(plansRoot);

        using var userReq = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/user-quota-plans/get-by-user-id/{userId}");
        using var userResp = await _http.SendAsync(userReq, ct).ConfigureAwait(false);
        var userRaw = await userResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!userResp.IsSuccessStatusCode)
        {
            return new UserVpnAccessInfo
            {
                QuotaApiError = $"User quota plans: {(int)userResp.StatusCode} — {Truncate(userRaw, 300)}"
            };
        }

        var userRoot = SafeObject(userRaw);
        if (userRoot is null)
            return new UserVpnAccessInfo { QuotaApiError = "Invalid JSON from user-quota-plans." };

        if (!(userRoot.Value<bool?>("success") ?? false))
        {
            var msg = userRoot.Value<string>("message") ?? userRoot.Value<string>("Message") ?? "User quota plans request failed.";
            return new UserVpnAccessInfo
            {
                QuotaApiError = string.IsNullOrWhiteSpace(msg) ? "User quota plans request failed." : msg
            };
        }

        var assignments = ParseUserAssignments(userRoot);

        var ext =
            JwtClaimReader.GetClaimFromBearerToken(bearerJwt, "externalId")?.Trim()
            ?? JwtClaimReader.GetClaimFromBearerToken(bearerJwt, "sub")?.Trim()
            ?? JwtClaimReader.GetClaimFromBearerToken(bearerJwt, "nameid")?.Trim()
            ?? "";

        var trafficNeedsExt = string.IsNullOrEmpty(ext);
        long quotaLimit = 0;
        var periodMonthly = true;
        var planName = "";
        var effectiveFrom = "";
        var effectiveTo = "";
        var assignmentNote = "";
        long trafficUsed = -1;

        var active = PickActiveAssignment(assignments);
        if (active is not null)
        {
            effectiveFrom = active.EffectiveFrom.Trim();
            effectiveTo = active.EffectiveTo.Trim();
            assignmentNote = active.Note.Trim();

            if (plansById.TryGetValue(active.QuotaPlanId, out var planRow))
            {
                if (!string.IsNullOrEmpty(planRow.Name))
                    planName = planRow.Name;
                else if (active.QuotaPlanId >= 0)
                    planName = $"Quota plan #{active.QuotaPlanId}";

                if (planRow.MonthlyQuotaBytes > 0)
                {
                    quotaLimit = planRow.MonthlyQuotaBytes;
                    periodMonthly = true;
                }
                else if (planRow.DailyQuotaBytes > 0)
                {
                    quotaLimit = planRow.DailyQuotaBytes;
                    periodMonthly = false;
                }
            }
            else if (active.QuotaPlanId >= 0)
                planName = $"Quota plan #{active.QuotaPlanId}";
        }

        if (!string.IsNullOrEmpty(ext) && quotaLimit > 0)
        {
            var today = DateTime.Today;
            DateTime fromLocal;
            DateTime toLocal;
            if (periodMonthly)
            {
                fromLocal = new DateTime(today.Year, today.Month, 1, 0, 0, 0, 0, DateTimeKind.Local);
                toLocal = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month), 23, 59, 59, 999, DateTimeKind.Local);
            }
            else
            {
                fromLocal = new DateTime(today.Year, today.Month, today.Day, 0, 0, 0, 0, DateTimeKind.Local);
                toLocal = new DateTime(today.Year, today.Month, today.Day, 23, 59, 59, 999, DateTimeKind.Local);
            }

            var fromUtc = TimeZoneInfo.ConvertTimeToUtc(fromLocal);
            var toUtc = TimeZoneInfo.ConvertTimeToUtc(toLocal);
            var fromIso = ((DateTimeOffset)fromUtc).ToString("o", CultureInfo.InvariantCulture);
            var toIso = ((DateTimeOffset)toUtc).ToString("o", CultureInfo.InvariantCulture);

            var uri = $"api/open-vpn-clients/overview/summary?From={Uri.EscapeDataString(fromIso)}&To={Uri.EscapeDataString(toIso)}&ExternalId={Uri.EscapeDataString(ext)}";
            using var ovReq = new HttpRequestMessage(HttpMethod.Get, uri);

            using var ovResp = await _http.SendAsync(ovReq, ct).ConfigureAwait(false);
            var ovRaw = await ovResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (ovResp.IsSuccessStatusCode)
            {
                var ovRoot = SafeObject(ovRaw);
                if (ovRoot is not null && (ovRoot.Value<bool?>("success") ?? true))
                    trafficUsed = ReadOverviewTrafficUsedBytes(ovRoot);
            }
        }

        return new UserVpnAccessInfo
        {
            PlanName = planName,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            AssignmentNote = assignmentNote,
            QuotaApiError = "",
            QuotaLimitBytes = quotaLimit,
            QuotaPeriodIsMonthly = periodMonthly,
            TrafficUsedBytesForPeriod = trafficUsed,
            TrafficUsageNeedsExternalId = trafficNeedsExt
        };
    }

    private static JObject? SafeObject(string json)
    {
        try
        {
            var t = JToken.Parse(json);
            return t as JObject;
        }
        catch (Exception ex)
        {
            CrashReporter.ReportNonFatal(ex, "UserVpnAccessClient.ParseJson");
            return null;
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    private sealed record QuotaPlanRow(int Id, string Name, long MonthlyQuotaBytes, long DailyQuotaBytes);

    private sealed record UserQuotaAssignment(
        int Id,
        int QuotaPlanId,
        string EffectiveFrom,
        string EffectiveTo,
        string Note);

    private static Dictionary<int, QuotaPlanRow> ParseQuotaPlans(JObject root)
    {
        var byId = new Dictionary<int, QuotaPlanRow>();
        var data = root["data"] as JObject ?? new JObject();
        var arr = (JArray?)data["quotaPlans"] ?? (JArray?)data["QuotaPlans"] ?? new JArray();
        foreach (var v in arr)
        {
            if (v is not JObject o)
                continue;
            var id = o.Value<int?>("id") ?? o.Value<int?>("Id");
            if (id is null or < 0)
                continue;

            var name = o.Value<string>("name") ?? o.Value<string>("Name") ?? "";
            var monthly = ReadLongQuota(o, "monthlyQuotaBytes", "MonthlyQuotaBytes");
            var daily = ReadLongQuota(o, "dailyQuotaBytes", "DailyQuotaBytes");
            byId[(int)id] = new QuotaPlanRow((int)id, name, monthly, daily);
        }

        return byId;
    }

    private static long ReadLongQuota(JObject o, string camel, string pascal)
    {
        var t = o[camel] ?? o[pascal];
        if (t is null || t.Type == JTokenType.Null)
            return -1;
        if (t.Type == JTokenType.Integer)
            return t.Value<long>();
        if (t.Type == JTokenType.Float)
            return (long)t.Value<double>();
        if (t.Type == JTokenType.String && long.TryParse(t.Value<string>(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return t.Value<long?>() ?? -1;
    }

    private static List<UserQuotaAssignment> ParseUserAssignments(JObject root)
    {
        var list = new List<UserQuotaAssignment>();
        var data = root["data"] as JObject ?? new JObject();
        var arr = (JArray?)data["items"] ?? (JArray?)data["Items"] ?? new JArray();
        foreach (var v in arr)
        {
            if (v is not JObject o)
                continue;
            var id = o.Value<int?>("id") ?? o.Value<int?>("Id") ?? -1;
            var qid = o.Value<int?>("quotaPlanId") ?? o.Value<int?>("QuotaPlanId") ?? -1;
            var from = o.Value<string>("effectiveFrom") ?? o.Value<string>("EffectiveFrom") ?? "";
            var to = o.Value<string>("effectiveTo") ?? o.Value<string>("EffectiveTo") ?? "";
            var note = o.Value<string>("note") ?? o.Value<string>("Note") ?? "";
            list.Add(new UserQuotaAssignment(id, qid, from, to, note));
        }

        return list;
    }

    private static UserQuotaAssignment? PickActiveAssignment(List<UserQuotaAssignment> items)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var valid = new List<UserQuotaAssignment>();
        foreach (var a in items)
        {
            var fromMs = ParseIsoToMs(a.EffectiveFrom, long.MinValue);
            var toMs = string.IsNullOrWhiteSpace(a.EffectiveTo)
                ? long.MaxValue
                : ParseIsoToMs(a.EffectiveTo, long.MaxValue);
            if (fromMs <= nowMs && nowMs <= toMs)
                valid.Add(a);
        }

        if (valid.Count == 0)
            return null;

        valid.Sort((a, b) =>
        {
            var af = ParseIsoToMs(a.EffectiveFrom, 0);
            var bf = ParseIsoToMs(b.EffectiveFrom, 0);
            var c = af.CompareTo(bf);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        });

        return valid[^1];
    }

    private static long ParseIsoToMs(string s, long fallback)
    {
        if (string.IsNullOrWhiteSpace(s))
            return fallback;
        return DateTimeOffset.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto.ToUnixTimeMilliseconds()
            : fallback;
    }

    private static long ReadOverviewTrafficUsedBytes(JObject root)
    {
        var data = root["data"] as JObject;
        if (data is null || !data.HasValues)
            data = root;

        var totals = data["totals"] as JObject ?? data["Totals"] as JObject ?? new JObject();
        var tt = totals["trafficTotalBytes"] ?? totals["TrafficTotalBytes"];
        if (tt is not null && tt.Type != JTokenType.Null)
        {
            var v = tt.Type == JTokenType.String
                ? long.TryParse(tt.Value<string>(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : -1L
                : (long)tt.Value<double>();
            if (v >= 0)
                return v;
        }

        var inn = ReadLong(totals, "trafficInBytes", "TrafficInBytes");
        var oout = ReadLong(totals, "trafficOutBytes", "TrafficOutBytes");
        return inn + oout;
    }

    private static long ReadLong(JObject o, string camel, string pascal)
    {
        var t = o[camel] ?? o[pascal];
        if (t is null || t.Type == JTokenType.Null)
            return 0;
        return (long)t.Value<double>();
    }
}
