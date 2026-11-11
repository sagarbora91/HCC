// FX-RPT-D · rpt.stock.consumption — Consumption per occupied room-night. Server twin of built/stock.ts `stockConsumption`. Amenities and cleaning supplies: "use" moves; linen: "room-change"
// moves (the same sums as StockQueries.ConsumptionAsync). Occupied room-nights per date: live nights of in-house / departed stays, else the imported history, none after the business date
// (StockRules.Occupied). Per room-night = used ÷ room-nights (whole number as is, else two decimals); variance against the item standard in whole percent (half up); flagged beyond ±15 %.
// Last row: "Occupied room-nights N". No money column. View PERM-STK-001. A range is limited to 93 days (default; the prototype silently counted only the first 93 dates).
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Stock;
using Hcc.Domain.Errors;
using Hcc.Domain.Stock;
using static Hcc.Reporting.Reports.FxD.FxDSupport;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxD;

public sealed class StockConsumptionReport(IStockReadStore store) : IReportSelector
{
    public const string Id = "rpt.stock.consumption";
    public const int MaxDays = 93;

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("item", "Item"), T("kind", "Kind"), I("used", "Used"), T("perRN", "Per room-night"), T("std", "Standard"), I("variance", "Variance %"), T("flag", "Flag"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Consumption per occupied room-night", "stock", "P2", "built", false, null, [View], [], "PERM-RPT-003", ReportDateClass.Business,
        [From(), To()], Columns, "Amenities and cleaning supplies: use moves; linen: room-change moves. Per room-night = used ÷ occupied room-nights of the range (the first row of the total block).",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    public static List<ReportRow> Rows(IEnumerable<StockItemRecord> items, IReadOnlyDictionary<string, int> used, int roomNights)
    {
        var rows = items.Select(i =>
        {
            var u = used.GetValueOrDefault(i.Code);
            var std = (i.StdX100 ?? 0) / 100.0;
            var variance = StockRules.VariancePercent(u, roomNights, i.StdX100 ?? 0);
            return Row(ReportRowKind.Row, ("item", i.Name), ("kind", Kind(i.Kind)), ("used", (long)u), ("perRN", Fmt(roomNights > 0 ? (double)u / roomNights : 0)),
                ("std", std > 0 ? Fmt(std) : null), ("variance", variance is { } v ? (long)v : null), ("flag", StockRules.FlagVariance(variance) ? "Outside ±15%" : null));
        }).ToList();
        rows.Add(Row(ReportRowKind.Total, ("item", "Occupied room-nights " + roomNights.ToString(CultureInfo.InvariantCulture)), ("kind", null), ("used", null), ("perRN", null), ("std", null),
            ("variance", null), ("flag", null)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var (from, to) = (parameters.From!.Value, parameters.To!.Value);
        if (to.DayNumber - from.DayNumber + 1 > MaxDays) throw HccException.Validation($"At most {MaxDays} days at a time.");
        var bd = await store.BusinessDateAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var occ = (await store.OccupancyAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false)).ToDictionary(o => o.Date);
        var rn = 0;
        for (var d = from; d <= to; d = d.AddDays(1)) rn += occ.TryGetValue(d, out var o) ? StockRules.Occupied(d, bd, o.Live, o.History) : 0;
        var used = (await store.ConsumptionAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false)).ToDictionary(u => u.ItemCode, u => u.Used, StringComparer.Ordinal);
        var items = await store.ItemsAsync(caller.PropertyId, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(items, used, rn), About(Definition, parameters));
    }
}
