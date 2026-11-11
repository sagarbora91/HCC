// CU-RPT-B · rpt.rooms.occupancy — Occupancy, ADR and RevPAR (catalogue A35). Server twin of the prototype's closing-pack tab "Occupancy" (pack.ts).
// One row per business date from the same source records and definitions as the daily pack (DailyPackCalculator): sellable = rooms not blocked that night,
// occupied = stays whose interval covers the night, room revenue = room charges by service date. Ratios are text (percent 1 dp half up, ADR / RevPAR in rupees,
// "—" when the divisor is zero); the total row recomputes them from the totals. Rule M: room revenue, ADR and RevPAR are money (PERM-RPT-002).
using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Billing;
using Hcc.Domain.Reporting;
using Hcc.Reporting.Reports.Money;

namespace Hcc.Reporting.Reports.CuB;

public sealed class RoomsOccupancyReport(IReportSource source) : IReportSelector
{
    public const string Id = "rpt.rooms.occupancy";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        MoneyRules.Dt("date", "Business date"), MoneyRules.I("sellable", "Sellable"), MoneyRules.I("occupied", "Occupied"), MoneyRules.T("occupancy", "Occupancy"),
        MoneyRules.M("roomRevenue", "Room revenue"), new("adr", "ADR", ReportColumnType.Text, true), new("revpar", "RevPAR", ReportColumnType.Text, true),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Occupancy, ADR and RevPAR", "rooms", "P1", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003",
        ReportDateClass.Financial, [MoneyRules.Range(ReportDateClass.Financial), MoneyRules.RangeTo(ReportDateClass.Financial)], Columns,
        "Occupancy = occupied ÷ sellable room-nights; ADR = room revenue ÷ occupied; RevPAR = room revenue ÷ sellable (rounded half-up, dash when the divisor is zero). The total row recomputes the ratios from the totals.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, false, ["A35"]);

    /// <summary>ADR / RevPAR as rupee text: half away from zero to the paisa, "—" when the divisor is zero.</summary>
    public static string RatioText(long num, long den) => DailyPackCalculator.Ratio(num, den) is { } v ? CellText.Rupees(v) : "—";

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        var data = await source.LoadAsync(null, caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var revenue = data.Lines.Where(l => l.Kind == ChargeKind.Room).GroupBy(l => l.ServiceDate).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount.Value));
        var rows = new List<ReportRow>();
        long sellable = 0, occupied = 0, room = 0;
        foreach (var d in CuBSupport.Dates(from, to))
        {
            var sl = data.RoomIds.Count(r => !data.Blocks.Any(b => b.RoomId == r && b.Covers(d)));
            var oc = data.Stays.Count(s => s.Arrival <= d && d < s.End);
            var rr = revenue.GetValueOrDefault(d);
            sellable += sl;
            occupied += oc;
            room += rr;
            rows.Add(CuBSupport.Row(ReportRowKind.Row, ("date", d), ("sellable", (long)sl), ("occupied", (long)oc), ("occupancy", DailyPackCalculator.Pct(oc, sl)), ("roomRevenue", rr),
                ("adr", RatioText(rr, oc)), ("revpar", RatioText(rr, sl))));
        }

        rows.Add(CuBSupport.Row(ReportRowKind.Total, ("date", "Total"), ("sellable", sellable), ("occupied", occupied), ("occupancy", DailyPackCalculator.Pct(occupied, sellable)), ("roomRevenue", room),
            ("adr", RatioText(room, occupied)), ("revpar", RatioText(room, sellable))));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, CuBSupport.About(Definition, parameters));
    }
}
