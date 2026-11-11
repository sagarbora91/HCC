// CU-RPT-A · shared pieces of the four reports moved onto the server in wave CU: audit log, daily operations, cash book, expenses and cash.
// Server twins of 03 Prototype/src/domain/reports/built/{admin,pack,money}.ts. Column builders are the money lane's (MoneyRules); the audit read is plain SQL on plat.audit_event.
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Audit;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Reporting.Reports.Money;

namespace Hcc.Reporting.Reports.CuA;

/// <summary>One audit event as the report needs it. <see cref="AtLocal"/> = hotel-local time (UTC + 05:30) as "yyyy-MM-dd HH:mm".</summary>
public sealed record AuditFact(long AuditId, string AtLocal, string Actor, string Action, string Target, string Detail);

public static partial class CuASql
{
    /// <summary>Role codes that read an audit detail (prototype adminAuditDetailVisible: Owner, Manager, Accountant).</summary>
    public static readonly IReadOnlySet<string> AuditDetailRoles = new HashSet<string>(StringComparer.Ordinal) { "OWN", "MGR", "ACC" };

    public const string AuditView = "PERM-ADM-004";
    public const string Finance = "PERM-RPT-002";

    [GeneratedRegex(@"\b[A-Z]{2,6}-\d+\b")]
    private static partial Regex RefToken();

    [GeneratedRegex(@"^(?:Setting|Master|Room|User|Rate)\s+([A-Za-z0-9_.:-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex KeyToken();

    /// <summary>Prototype avEntityId: the first reference-like token of a detail ("HCC-1041", "APR-H029"), else the key after "Setting" / "Master" / "Room" ...; empty when none.</summary>
    public static string EntityIdFromDetail(string detail)
    {
        var d = detail ?? string.Empty;
        var r = RefToken().Match(d);
        if (r.Success) return r.Value;
        var k = KeyToken().Match(d);
        return k.Success ? k.Groups[1].Value : string.Empty;
    }

    /// <summary>The Id column: the id part of the audit target ("user:12" gives 12), else the token found in the detail, else the audit row id.</summary>
    public static string AuditId(AuditFact f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var t = AuditViewRules.EntityIdOf(f.Target);
        if (t.Length > 0) return t;
        var d = EntityIdFromDetail(f.Detail);
        return d.Length > 0 ? d : f.AuditId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>UTC from the database to the hotel clock as "yyyy-MM-dd HH:mm" (Asia/Kolkata, +05:30, no daylight saving).</summary>
    public static string Local(DateTime utc) => utc.AddMinutes(330).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static ReportRow Row(ReportRowKind kind, params (string Key, object? Value)[] cells) => MoneyRules.Row(kind, null, cells);

    public static ReportParam From(ReportDateClass dc) => MoneyRules.Range(dc);

    public static ReportParam To(ReportDateClass dc) => MoneyRules.RangeTo(dc);

    /// <summary>
    /// Audit events of the property in [from, to] (hotel-local dates), newest first, at most <paramref name="take"/> rows. Actor matches the display or login name; action is exact;
    /// text is a contains over action, actor and (only when <paramref name="searchDetail"/>) detail.
    /// </summary>
    public static async Task<IReadOnlyList<AuditFact>> AuditAsync(SqlConnectionFactory connections, int propertyId, DateOnly from, DateOnly to, string? actor, string? action, string? text,
        bool searchDetail, int take, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connections);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT TOP (@take) a.audit_id, a.at_utc, COALESCE(u.display_name, N'System'), a.action, a.target, a.detail
              FROM plat.audit_event a LEFT JOIN plat.app_user u ON u.user_id = a.principal_id
             WHERE a.property_id = @p AND a.at_utc >= @from AND a.at_utc < @to
               AND (@actor IS NULL OR u.display_name = @actor OR u.login_name = @actor OR (@actor = N'System' AND u.user_id IS NULL))
               AND (@action IS NULL OR a.action = @action)
               AND (@text IS NULL OR CHARINDEX(@text, a.action + N' ' + COALESCE(u.display_name, N'System') + CASE WHEN @withDetail = 1 THEN N' ' + a.detail ELSE N'' END) > 0)
             ORDER BY a.at_utc DESC, a.audit_id DESC;
            """, conn);
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = take;
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = from.ToDateTime(TimeOnly.MinValue).AddMinutes(-330);
        cmd.Parameters.Add("@to", SqlDbType.DateTime2).Value = to.AddDays(1).ToDateTime(TimeOnly.MinValue).AddMinutes(-330);
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 120).Value = Nz(actor);
        cmd.Parameters.Add("@action", SqlDbType.VarChar, 60).Value = Nz(action);
        cmd.Parameters.Add("@text", SqlDbType.NVarChar, 200).Value = Nz(text);
        cmd.Parameters.Add("@withDetail", SqlDbType.Bit).Value = searchDetail;
        var list = new List<AuditFact>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new AuditFact(rd.GetInt64(0), Local(rd.GetDateTime(1)), rd.GetString(2), rd.GetString(3), rd.GetString(4), rd.GetString(5)));
        return list;
    }

    private static object Nz(string? v) => string.IsNullOrWhiteSpace(v) ? DBNull.Value : v.Trim();

    public static string Param(BoundParams p, string key) => MoneyRules.Param(p, key);
}
