// F7-R6 · rpt.acc.ageing — money owed to the hotel or by the hotel, by party and age, bill by bill (ACC-3 wired). Adapter over AgeingQueries.AgeingAsync.
// A party row (subtotal) is followed by its open bills; a party whose buckets do not tie to its ledger balance gets a note row in words.
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts.Accounting;
using static Hcc.Reporting.Reports.Acc.AccShared;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Acc;

public sealed class AgeingReport(AccQueries q) : IReportSelector
{
    public const string Id = "rpt.acc.ageing";

    public static readonly IReadOnlyList<ReportColumn> Columns = [T("party", "Party"), T("control", "Control ledger"), T("billRef", "Bill ref"), D("billDate", "Bill date"), D("due", "Due"),
        M("amount", "Amount"), M("paid", "Paid"), M("open", "Open"), I("days", "Days"), M("b0_30", "0-30"), M("b31_60", "31-60"), M("b61_90", "61-90"), M("b90", "90+")];

    public ReportDefinition Definition { get; } = new(Id, "Outstanding with ageing", "acc", "P1", "ACC-3", true, View, [View], [], "PERM-RPT-003", ReportDateClass.AsOf,
        [new("asOf", "As on", "date", ReportDateClass.AsOf), new("side", "Side", "enum", null, ["receivable", "payable"], "receivable")], Columns,
        "Every voucher up to and including the date. Credits are allocated to bills (explicit vendor-payment allocations first, then oldest first); age = days since the bill date (no credit terms master).",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, LegacyIds: ["A22", "A28"]);

    /// <summary>The report body from the query view (pure; the tests feed it views).</summary>
    public static List<ReportRow> Rows(AgeingView a, IReadOnlyList<ReportRow> stamp)
    {
        var rows = new List<ReportRow>(stamp);
        foreach (var r in a.Rows)
        {
            rows.Add(Row(ReportRowKind.Subtotal, ("party", r.Name), ("control", r.Control), ("amount", r.Bills.Sum(b => b.Amount)), ("paid", r.Bills.Sum(b => b.Paid)), ("open", r.Open),
                ("b0_30", r.B0_30), ("b31_60", r.B31_60), ("b61_90", r.B61_90), ("b90", r.B90)));
            foreach (var b in r.Bills)
            {
                var k = AgeingRules.Bucket(b.Days);
                rows.Add(Row(ReportRowKind.Row, ("party", r.Name), ("control", r.Control), ("billRef", b.Ref), ("billDate", b.Date), ("due", b.Due), ("amount", b.Amount), ("paid", b.Paid),
                    ("open", b.Open), ("days", (long)b.Days), ("b0_30", k == "0-30" ? b.Open : 0L), ("b31_60", k == "31-60" ? b.Open : 0L), ("b61_90", k == "61-90" ? b.Open : 0L), ("b90", k == "90+" ? b.Open : 0L)));
            }

            if (r.Flagged) rows.Add(Row(ReportRowKind.Row, ("party", r.Name), ("control", r.Control), ("billRef", "Does not tie to the party ledger: " + (r.Note ?? ""))));
        }

        var t = a.Totals;
        rows.Add(Row(ReportRowKind.Total, ("party", "Total"), ("open", t.Open), ("b0_30", t.B0_30), ("b31_60", t.B31_60), ("b61_90", t.B61_90), ("b90", t.B90)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var asOf = parameters.To!.Value;
        var a = await q.Ageing().AgeingAsync(caller, asOf, parameters.Values.GetValueOrDefault("side") ?? AgeingRules.Receivable, ct).ConfigureAwait(false);
        var stamp = await StampAsync(q, caller, Fy(a.AsOf), a.AsOf, "party", ct).ConfigureAwait(false);
        var rows = Rows(a, stamp);
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters, stamp.Count > 0));
    }
}
