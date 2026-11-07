// F12 MEAS · meas-rpt-hk — base of the six reports: SQL access, property clock, and the switch key (IMeasuredReport, so ReportRunner hides the
// report and refuses MEAS_OFF while its item / pack / module is off).
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.MeasHk;

public abstract class MeasHkReportBase(SqlPlatform platform) : IReportSelector, IMeasuredReport
{
    public abstract ReportDefinition Definition { get; }

    public abstract string MeasKey { get; }

    public abstract Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default);

    protected async Task<GuestsSql.Clock> ClockAsync(Caller caller, CancellationToken ct) => await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);

    /// <summary>Runs a query with the property id as @p; <paramref name="bind"/> adds the other parameters. Rows come back as value arrays.</summary>
    protected async Task<List<object?[]>> QueryAsync(string sql, int propertyId, Action<SqlCommand>? bind, CancellationToken ct)
    {
        var rows = new List<object?[]>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        GuestsSql.Add(cmd, "@p", SqlDbType.Int, propertyId);
        bind?.Invoke(cmd);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var v = new object?[rd.FieldCount];
            rd.GetValues(v);
            for (var i = 0; i < v.Length; i++) if (v[i] is DBNull) v[i] = null;
            rows.Add(v);
        }

        return rows;
    }

    protected static long L(object? o) => o is null ? 0 : Convert.ToInt64(o, System.Globalization.CultureInfo.InvariantCulture);

    protected static string S(object? o) => o is null ? "" : Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture) ?? "";

    protected static DateOnly Dt(object? o) => DateOnly.FromDateTime((DateTime)o!);

    protected static DateTime? Utc(object? o) => o is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;

    protected static string P(BoundParams p, string key) => p.Values.GetValueOrDefault(key) ?? "";

    protected ReportResult Done(BoundParams parameters, IReadOnlyList<ReportColumn> columns, List<ReportRow> rows, string? basisOverride = null)
    {
        var about = new ReportAbout(DateTimeOffset.MinValue, "", basisOverride ?? Definition.DateBasis, false, parameters.Legacy, parameters.From, parameters.To);
        return new ReportResult(Definition.Id, Definition.Title, parameters, columns, rows, about);
    }

    protected static void BindRange(SqlCommand c, BoundParams p)
    {
        GuestsSql.Add(c, "@from", SqlDbType.Date, GuestsSql.D(p.From!.Value));
        GuestsSql.Add(c, "@to", SqlDbType.Date, GuestsSql.D(p.To!.Value));
    }

    /// <summary>Occupied room-nights of the night (stays in house / departed on inv.room_night; the stock occupancy history before go-live).</summary>
    protected async Task<Dictionary<DateOnly, int>> OccupiedAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await QueryAsync("""
            SELECT n.night, COUNT(*) FROM inv.room_night n JOIN inv.reservation r ON r.reservation_id = n.reservation_id
             WHERE r.property_id = @p AND r.status IN ('InHouse','Departed') AND n.night BETWEEN @from AND @to GROUP BY n.night;
            """, propertyId, c => { GuestsSql.Add(c, "@from", SqlDbType.Date, GuestsSql.D(from)); GuestsSql.Add(c, "@to", SqlDbType.Date, GuestsSql.D(to)); }, ct).ConfigureAwait(false);
        var map = rows.ToDictionary(r => Dt(r[0]), r => (int)L(r[1]));
        var hist = await QueryAsync("SELECT night, rooms FROM stk.occupancy_history WHERE property_id = @p AND night BETWEEN @from AND @to;", propertyId,
            c => { GuestsSql.Add(c, "@from", SqlDbType.Date, GuestsSql.D(from)); GuestsSql.Add(c, "@to", SqlDbType.Date, GuestsSql.D(to)); }, ct).ConfigureAwait(false);
        foreach (var h in hist) if (!map.ContainsKey(Dt(h[0]))) map[Dt(h[0])] = (int)L(h[1]);
        return map;
    }

    protected static readonly IReadOnlyList<ReportFormat> Std = [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf];
}
