// F12 MEAS · rpt.rooms.utilities-per-room — Energy, water and LPG per occupied room-night (switch meas.hk.utilities, module Maintenance).
// Server twin of meas-hk/utilities-per-room.ts. Use = difference between consecutive monotonic readings, counted on the day of the later reading.
// Money: Unit cost and Cost per occupied night (Posted Utilities expenses) are money, so the report carries moneyFlag with gate PERM-RPT-002.
// Red row: electricity per occupied room-night more than 15 % above the average of the previous 3 months.
using System.Data;
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using static Hcc.Reporting.Reports.MeasHk.MeasHkRules;

namespace Hcc.Reporting.Reports.MeasHk;

public sealed class UtilitiesPerRoomReport(SqlPlatform platform) : MeasHkReportBase(platform)
{
    public const string Id = "rpt.rooms.utilities-per-room";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("day", "Day"), T("electricity", "Electricity kWh"), T("water", "Water kL"), T("lpg", "LPG kg"), T("diesel", "Diesel litres"), T("dgHours", "DG hours"),
        T("perOccupied", "Per occupied room-night"), T("perAvailable", "Per available room"), M("unitCost", "Unit cost"), M("costPerNight", "Cost per occupied night"), T("lastYear", "Last year same month"),
    ];

    private const string Basis = "Meter use by the day the reading was taken (use = difference from the previous reading; LPG cylinders × 19 kg). Per occupied room-night = electricity kWh ÷ occupied room-nights of that day; per available room = kWh ÷ rooms in service. Unit cost = the month's Posted Utilities expenses ÷ kWh; cost per occupied night = those expenses ÷ occupied room-nights (month row). Red: more than 15 % above the average per occupied night of the previous 3 months.";

    public override string MeasKey => "meas.hk.utilities";

    public override ReportDefinition Definition { get; } = new(Id, "Energy, water and LPG per occupied room-night", "rooms", "P2", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-001"], ["PERM-MNT-001"], "PERM-RPT-003", ReportDateClass.Month,
        [new("month", "Month", "month", ReportDateClass.Month), new("meter", "Meter", "text")], Columns, Basis, Std, ReportPhone.Yes);

    private static readonly string[] KindOrder = ["electricity", "water", "lpg", "diesel", "dg-hours"];

    /// <summary>Use per day per meter kind (LPG cylinders converted to kg), from rows (meter, kind, unit, date, value) in reading order.</summary>
    public static Dictionary<string, Dictionary<DateOnly, double>> Use(IEnumerable<(string Meter, string Kind, string Unit, DateOnly Date, double Value)> readings, string? meterId = null)
    {
        var use = KindOrder.ToDictionary(k => k, _ => new Dictionary<DateOnly, double>(), StringComparer.Ordinal);
        foreach (var g in readings.Where(r => string.IsNullOrEmpty(meterId) || r.Meter == meterId).GroupBy(r => r.Meter))
        {
            var l = g.ToList();
            for (var i = 1; i < l.Count; i++)
            {
                var k = l[i].Kind == "lpg" && l[i].Unit != "kg" ? LpgKgPerCylinder : 1;
                var u = Math.Round((l[i].Value - l[i - 1].Value) * 100) / 100 * k;
                use[l[i].Kind][l[i].Date] = use[l[i].Kind].GetValueOrDefault(l[i].Date) + u;
            }
        }

        return use;
    }

    private static double SumIn(Dictionary<DateOnly, double> m, int year, int month) => m.Where(kv => kv.Key.Year == year && kv.Key.Month == month).Sum(kv => kv.Value);

    public override async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await ClockAsync(caller, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 31, clock.BusinessDate);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        var monthStart = new DateOnly(from.Year, from.Month, 1);
        var count = (await QueryAsync("SELECT COUNT(*) FROM meas.meter_reading WHERE property_id = @p;", caller.PropertyId, null, ct).ConfigureAwait(false))[0][0];
        if (L(count) == 0) return Done(parameters, Columns, [], CaptureNote(Basis, "meter readings (meas-cap-hk)"));
        var meterId = P(parameters, "meter");
        var raw = await QueryAsync("""
            SELECT r.meter_id, m.kind, m.unit, r.reading_date, r.value FROM meas.meter_reading r JOIN meas.meter m ON m.property_id = r.property_id AND m.meter_id = r.meter_id
             WHERE r.property_id = @p AND r.reading_date <= @to AND r.reading_date >= DATEADD(MONTH, -14, @from) ORDER BY r.meter_id, r.reading_date, r.reading_id;
            """, caller.PropertyId, c => { GuestsSql.Add(c, "@from", SqlDbType.Date, GuestsSql.D(monthStart)); GuestsSql.Add(c, "@to", SqlDbType.Date, GuestsSql.D(to)); }, ct).ConfigureAwait(false);
        var use = Use(raw.Select(r => (S(r[0]), S(r[1]), S(r[2]), Dt(r[3]), Convert.ToDouble(r[4], CultureInfo.InvariantCulture))), meterId);
        var occ = await OccupiedAsync(caller.PropertyId, monthStart.AddMonths(-3), monthStart.AddMonths(1).AddDays(-1), ct).ConfigureAwait(false);
        var avail = Math.Max(1, (int)L((await QueryAsync("SELECT COUNT(*) FROM inv.room WHERE property_id = @p;", caller.PropertyId, null, ct).ConfigureAwait(false))[0][0]));

        // Baseline: average kWh per occupied room-night of the 3 months before the month (months with both readings and occupancy).
        var ratios = new List<double>();
        for (var i = 1; i <= 3; i++)
        {
            var m = monthStart.AddMonths(-i);
            var kwh = SumIn(use["electricity"], m.Year, m.Month);
            var rn = occ.Where(kv => kv.Key.Year == m.Year && kv.Key.Month == m.Month).Sum(kv => kv.Value);
            if (kwh > 0 && rn > 0) ratios.Add(kwh / rn);
        }

        double? baseline = ratios.Count == 0 ? null : ratios.Average();
        var rows = new List<ReportRow>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            double? Get(string k) => use[k].TryGetValue(d, out var v) ? v : null;
            if (KindOrder.All(k => Get(k) is null)) continue;
            var el = Get("electricity");
            var rn = occ.GetValueOrDefault(d);
            double? per = el is not null && rn > 0 ? el / rn : null;
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["day"] = GuestsSql.Iso(d), ["electricity"] = Dec2(el), ["water"] = Dec2(Get("water")), ["lpg"] = Dec2(Get("lpg")), ["diesel"] = Dec2(Get("diesel")), ["dgHours"] = Dec2(Get("dg-hours")),
                ["perOccupied"] = Dec2(per), ["perAvailable"] = el is null ? null : Dec2(el / avail), ["unitCost"] = null, ["costPerNight"] = null, ["lastYear"] = null,
            }, UtilityRed(per, baseline)));
        }

        var monthKwh = SumIn(use["electricity"], monthStart.Year, monthStart.Month);
        var monthRn = occ.Where(kv => kv.Key.Year == monthStart.Year && kv.Key.Month == monthStart.Month).Sum(kv => kv.Value);
        var spend = L((await QueryAsync("""
            SELECT ISNULL(SUM(amount_paise), 0) FROM ops.expense WHERE property_id = @p AND category = N'Utilities' AND status = 'Posted' AND business_date BETWEEN @from AND @to;
            """, caller.PropertyId, c => { GuestsSql.Add(c, "@from", SqlDbType.Date, GuestsSql.D(monthStart)); GuestsSql.Add(c, "@to", SqlDbType.Date, GuestsSql.D(monthStart.AddMonths(1).AddDays(-1))); }, ct).ConfigureAwait(false))[0][0]);
        var ly = SumIn(use["electricity"], monthStart.Year - 1, monthStart.Month);
        double? perMonth = monthKwh > 0 && monthRn > 0 ? monthKwh / monthRn : null;
        rows.Add(new ReportRow(ReportRowKind.Total, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["day"] = "Month", ["electricity"] = Dec2(monthKwh), ["water"] = Dec2(SumIn(use["water"], monthStart.Year, monthStart.Month)), ["lpg"] = Dec2(SumIn(use["lpg"], monthStart.Year, monthStart.Month)),
            ["diesel"] = Dec2(SumIn(use["diesel"], monthStart.Year, monthStart.Month)), ["dgHours"] = Dec2(SumIn(use["dg-hours"], monthStart.Year, monthStart.Month)), ["perOccupied"] = Dec2(perMonth),
            ["perAvailable"] = monthKwh > 0 ? Dec2(monthKwh / avail) : null, ["unitCost"] = monthKwh > 0 && spend > 0 ? (long)Math.Round(spend / monthKwh, MidpointRounding.AwayFromZero) : null,
            ["costPerNight"] = monthRn > 0 && spend > 0 ? (long)Math.Round((double)spend / monthRn, MidpointRounding.AwayFromZero) : null, ["lastYear"] = ly > 0 ? Dec2(ly) : null,
        }, UtilityRed(perMonth, baseline) ? RedGroup : null));
        return Done(parameters, Columns, rows);
    }
}
