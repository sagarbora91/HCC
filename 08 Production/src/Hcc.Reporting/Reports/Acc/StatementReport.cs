// F7-R6 · rpt.acc.statement — ledger or party statement. Adapter over AccountingReportQueries.StatementAsync (ACC-1). Balance is signed (Dr positive, Cr negative).
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Errors;
using static Hcc.Reporting.Reports.Acc.AccShared;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Acc;

public sealed class StatementReport(AccQueries q) : IReportSelector
{
    public const string Id = "rpt.acc.statement";

    public static readonly IReadOnlyList<ReportColumn> Columns = [D("date", "Date"), T("voucher", "Voucher"), T("type", "Type"), T("narration", "Narration"), T("counter", "Counter ledger"),
        M("debit", "Debit"), M("credit", "Credit"), M("balance", "Balance (Dr / Cr)")];

    public ReportDefinition Definition { get; } = new(Id, "Ledger or party statement", "acc", "P1", "built", true, View, [View], [], "PERM-RPT-003", ReportDateClass.Financial,
        [new("account", "Ledger or party", "party", null, null, null, true), Range()], Columns,
        "Vouchers of the ledger (a control ledger includes its parties) in the range, oldest first; opening = every voucher before From. Balance: Dr positive, Cr negative.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, LegacyIds: ["A27"]);

    private static string Side(long n) => n > 0 ? " — Dr" : n < 0 ? " — Cr" : "";

    /// <summary>The report body from the query view (pure; the tests feed it views).</summary>
    public static List<ReportRow> Rows(StatementView st, IReadOnlyList<ReportRow> stamp)
    {
        var rows = new List<ReportRow>(stamp) { Row(ReportRowKind.Subtotal, ("narration", $"Opening balance · {st.Name}{Side(st.OpeningPaise)}"), ("balance", st.OpeningPaise)) };
        rows.AddRange(st.Rows.Select(r => Row(ReportRowKind.Row, ("date", r.Date), ("voucher", r.No), ("type", r.Type), ("narration", r.Narration), ("counter", r.Counter),
            ("debit", r.DebitPaise), ("credit", r.CreditPaise), ("balance", r.RunningPaise))));
        rows.Add(Row(ReportRowKind.Total, ("narration", $"Closing balance{Side(st.ClosingPaise)}"), ("debit", st.DebitPaise), ("credit", st.CreditPaise), ("balance", st.ClosingPaise)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var (from, to) = (parameters.From!.Value, parameters.To!.Value);
        var st = await q.Reports().StatementAsync(caller, parameters.Values["account"], from, to, ct).ConfigureAwait(false);
        var stamp = await StampAsync(q, caller, from, to, "voucher", ct).ConfigureAwait(false);
        var rows = Rows(st, stamp);
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters, stamp.Count > 0));
    }
}
