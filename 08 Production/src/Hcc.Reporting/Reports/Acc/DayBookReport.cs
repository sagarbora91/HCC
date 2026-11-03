// F7-R6 · rpt.acc.day-book — every voucher entry of a range. Adapter over AccountingReportQueries.DayBookAsync (ACC-1).
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts.Accounting;
using static Hcc.Reporting.Reports.Acc.AccShared;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Acc;

public sealed class DayBookReport(AccQueries q) : IReportSelector
{
    public const string Id = "rpt.acc.day-book";

    public static readonly IReadOnlyList<ReportColumn> Columns = [D("date", "Date"), T("voucher", "Voucher"), T("type", "Type"), T("ledger", "Ledger"), M("debit", "Debit"), M("credit", "Credit"),
        T("narration", "Narration"), T("source", "Source")];

    public ReportDefinition Definition { get; } = new(Id, "Day book", "acc", "P2", "built", true, View, [View], [], "PERM-RPT-003", ReportDateClass.Business,
        [Range("From", ReportDateClass.Business)], Columns,
        "Every voucher dated in the range, in date and voucher-number order, one line per ledger entry (an opening voucher dated on From belongs to the opening, not the day book).",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    /// <summary>The report body from the query view (pure; the tests feed it views).</summary>
    public static List<ReportRow> Rows(DayBookView db, IReadOnlyList<ReportRow> stamp)
    {
        var rows = new List<ReportRow>(stamp);
        rows.AddRange(db.Rows.Select(r => Row(ReportRowKind.Row, ("date", r.Date), ("voucher", r.No), ("type", r.Type), ("ledger", $"{r.Account} {r.Ledger}"), ("debit", r.DebitPaise), ("credit", r.CreditPaise),
            ("narration", r.Narration), ("source", r.Source))));
        rows.Add(Row(ReportRowKind.Total, ("voucher", "Total"), ("ledger", $"{db.Vouchers} voucher{(db.Vouchers == 1 ? "" : "s")}{(db.Balanced ? "" : " — debits and credits differ")}"),
            ("debit", db.DebitPaise), ("credit", db.CreditPaise)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var (from, to) = (parameters.From!.Value, parameters.To!.Value);
        var db = await q.Reports().DayBookAsync(caller, from, to, ct).ConfigureAwait(false);
        var stamp = await StampAsync(q, caller, from, to, "voucher", ct).ConfigureAwait(false);
        var rows = Rows(db, stamp);
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters, stamp.Count > 0));
    }
}
