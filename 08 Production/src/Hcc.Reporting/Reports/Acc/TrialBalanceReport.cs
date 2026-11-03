// F7-R6 · rpt.acc.trial-balance — six-column trial balance. Adapter over AccountingReportQueries.TrialBalanceAsync (ACC-1) — same figures as the Accounting screen / CSV.
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts.Accounting;
using static Hcc.Reporting.Reports.Acc.AccShared;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Acc;

public sealed class TrialBalanceReport(AccQueries q) : IReportSelector
{
    public const string Id = "rpt.acc.trial-balance";

    public static readonly IReadOnlyList<ReportColumn> Columns = [T("code", "Code"), T("ledger", "Ledger"), T("group", "Group"), M("openDr", "Opening Dr"), M("openCr", "Opening Cr"),
        M("perDr", "Period Dr"), M("perCr", "Period Cr"), M("closeDr", "Closing Dr"), M("closeCr", "Closing Cr")];

    public ReportDefinition Definition { get; } = new(Id, "Trial balance (six columns)", "acc", "P1", "built", true, View, [View], [], "PERM-RPT-003", ReportDateClass.Financial,
        [Range(), new("view", "View", "enum", null, ["control ledgers", "with parties"], "control ledgers")], Columns,
        "Every voucher up to the To date; opening = vouchers before From (the opening entry included); opening and closing netted per ledger, period columns gross. Locked days read their snapshot.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    /// <summary>The report body from the query view (pure; the tests feed it views).</summary>
    public static List<ReportRow> Rows(TrialBalance6View tb, IReadOnlyList<ReportRow> stamp)
    {
        var rows = new List<ReportRow>(stamp);
        if (tb.OpeningPending) rows.Add(Row(ReportRowKind.Subtotal, ("code", ""), ("ledger", "Opening balances pending — no approved opening entry with a zero difference yet")));
        rows.AddRange(tb.Rows.Select(r => Row(ReportRowKind.Row, ("code", r.Account), ("ledger", r.Name), ("group", r.Group), ("openDr", r.OpeningDebitPaise), ("openCr", r.OpeningCreditPaise),
            ("perDr", r.DebitPaise), ("perCr", r.CreditPaise), ("closeDr", r.ClosingDebitPaise), ("closeCr", r.ClosingCreditPaise))));
        var t = tb.Totals;
        rows.Add(Row(ReportRowKind.Total, ("code", "Total"), ("ledger", tb.Balanced ? "Debits equal credits in every column" : "Debits and credits differ"), ("group", ""),
            ("openDr", t.OpeningDebitPaise), ("openCr", t.OpeningCreditPaise), ("perDr", t.DebitPaise), ("perCr", t.CreditPaise), ("closeDr", t.ClosingDebitPaise), ("closeCr", t.ClosingCreditPaise)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var (from, to) = (parameters.From!.Value, parameters.To!.Value);
        var tb = await q.Reports().TrialBalanceAsync(caller, from, to, parameters.Values.GetValueOrDefault("view") == "with parties", ct).ConfigureAwait(false);
        var stamp = await StampAsync(q, caller, from, to, "code", ct).ConfigureAwait(false);
        var rows = Rows(tb, stamp);
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters, tb.OpeningPending || stamp.Count > 0));
    }
}
