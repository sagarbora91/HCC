// FX-RPT-C · shared pieces of the seven reports moved onto the server in wave FX (export log, report dictionary, configuration history, closing records,
// cash-out register, expenses month, closed bills). Server twins of 03 Prototype/src/domain/reports/built/{admin,money,rest}.ts. Plain parameterised SQL scoped to the
// caller's property; money = long paise; hotel-local time = UTC + 05:30 (no daylight saving); no wall clock.
using System.Data;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.CuA;
using Hcc.Reporting.Reports.Money;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.FxC;

internal static class FxCSupport
{
    public const string Finance = "PERM-RPT-002";

    public static ReportRow Row(ReportRowKind kind, params (string Key, object? Value)[] cells) => MoneyRules.Row(kind, null, cells);

    public static ReportAbout About(ReportDefinition d, BoundParams p) => MoneyRules.About(d, p);

    /// <summary>UTC bound of the start of a hotel-local date.</summary>
    public static DateTime LocalStartUtc(DateOnly d) => d.ToDateTime(TimeOnly.MinValue).AddMinutes(-330);

    /// <summary>Display names of every user of the property by id (the reports show a name, never a login or token).</summary>
    public static async Task<IReadOnlyDictionary<int, string>> NamesAsync(SqlPlatform platform, int propertyId, CancellationToken ct)
    {
        var map = new Dictionary<int, string>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT user_id, display_name FROM plat.app_user WHERE property_id = @p;", conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) map[rd.GetInt32(0)] = rd.GetString(1);
        return map;
    }

    public static string NameOf(IReadOnlyDictionary<int, string> names, int? id) => id is { } i ? MoneyRules.NameOf(names, i) : string.Empty;

    public static string Local(DateTime utc) => CuASql.Local(utc);

    public static string Param(BoundParams p, string key) => MoneyRules.Param(p, key);

    /// <summary>True when the caller's current roles include one of <paramref name="codes"/>.</summary>
    public static async Task<bool> HasRoleAsync(IReportPrincipalRoles roles, int principalId, CancellationToken ct, params string[] codes) =>
        (await roles.RolesAsync(principalId, ct).ConfigureAwait(false)).Any(r => codes.Contains(r, StringComparer.Ordinal));
}
