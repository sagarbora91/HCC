// CU-RPT-B · shared helpers of the four reports moved from the prototype to the server (receipts-deposits, rest.day-summary, rooms.occupancy, stock.stock-sheet).
// Money = long paise; no wall clock; every query is parameterised and scoped to the caller's property.
using System.Data;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Money;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.CuB;

internal static class CuBSupport
{
    /// <summary>Which place of the payment-method master a list is read for.</summary>
    public enum Place { FrontDesk, Pos }

    /// <summary>Active method names of the Payment methods master usable at a place, in list order (prototype <c>mnyNames</c>).</summary>
    public static async Task<IReadOnlyList<string>> MethodNamesAsync(SqlPlatform platform, int propertyId, Place place, CancellationToken ct)
    {
        var column = place == Place.FrontDesk ? "usable_frontdesk" : "usable_pos";
        var sql = $"SELECT name FROM mst.payment_method WHERE property_id = @p AND active = 1 AND {column} = 1 ORDER BY sort_order, payment_method_id;";
        var names = new List<string>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) names.Add(rd.GetString(0));
        return names;
    }

    public static ReportRow Row(ReportRowKind kind, params (string Key, object? Value)[] cells) => MoneyRules.Row(kind, null, cells);

    public static ReportAbout About(ReportDefinition d, BoundParams p) => MoneyRules.About(d, p);

    /// <summary>Every date of the inclusive range, oldest first.</summary>
    public static IEnumerable<DateOnly> Dates(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1)) yield return d;
    }
}
