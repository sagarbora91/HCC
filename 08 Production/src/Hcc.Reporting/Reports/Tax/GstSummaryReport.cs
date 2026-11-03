// F7-R5 · rpt.tax.gst-summary — GST summary, ties to GSTR-1 and GSTR-3B (ACC-3 wired onto the engine). Adapter over GstSummaryQueries.SummaryAsync — the ACC-3
// figures are unchanged (it requires PERM-ACC-001 itself). Server twin of tax/gst-summary.ts: same lines, same order, same words.
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Tax;

public sealed class GstSummaryReport(GstSummaryQueries summary) : IReportSelector
{
    public const string Id = "rpt.tax.gst-summary";

    public static readonly IReadOnlyList<ReportColumn> Columns = [T("line", "Line"), M("amount", "Amount"), B("tiesGstr1", "Ties to GSTR-1"), B("tiesGstr3b", "Ties to GSTR-3B")];

    public ReportDefinition Definition { get; } = new(Id, "GST summary (ties to GSTR-1 and GSTR-3B)", "tax", "P1", "ACC-3", true, "PERM-ACC-001", ["PERM-ACC-001"], [], "PERM-RPT-003", ReportDateClass.Month,
        [new("month", "Month", "month", ReportDateClass.Month)], Columns,
        "Output tax by invoice date from the GSTR-1 working summary; advances, ITC, RCM, set-off and the cash ledger from the books for the month. Amounts are GST (tax) paise.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var g = await summary.SummaryAsync(caller, parameters.Values["month"], ct).ConfigureAwait(false);
        bool r1 = g.Ties.Gstr1, r3 = g.Ties.Gstr3b;
        static ReportRow L(string line, long? amount, bool? t1 = null, bool? t3 = null) => Row(ReportRowKind.Row, ("line", line), ("amount", amount), ("tiesGstr1", t1), ("tiesGstr3b", t3));
        static ReportRow Sec(string line) => Row(ReportRowKind.Subtotal, ("line", line), ("amount", null), ("tiesGstr1", null), ("tiesGstr3b", null));
        var rows = new List<ReportRow>
        {
            Sec("Output GST by rate"), L("Rate 5 %", g.Output.Rate5, r1), L("Rate 18 %", g.Output.Rate18, r1),
            Sec("Output GST by section"), L("B2B", g.Output.B2b, r1), L("B2CS", g.Output.B2cs, r1), L("Credit notes (CDNR)", g.Output.CreditNotes, r1),
            Sec("Advances"), L("Advance GST raised (11A)", g.Output.Advances11A), L("Advance GST adjusted (11B)", g.Output.Advances11B),
            Sec("Input tax credit"), L("ITC CGST", g.Itc.CgstPaise, null, r3), L("ITC SGST", g.Itc.SgstPaise, null, r3), L("ITC IGST", g.Itc.IgstPaise, null, r3), L("Reverse charge (RCM)", g.Rcm, null, r3),
            Sec("Book output (vouchers)"), L("CGST", g.BookOutput.CgstPaise, r1, r3), L("SGST", g.BookOutput.SgstPaise, r1, r3), L("IGST", g.BookOutput.IgstPaise, r1, r3),
            Sec("Set-off (s.49)"), L("Output CGST", g.Setoff.Output.CgstPaise, null, r3), L("Output SGST", g.Setoff.Output.SgstPaise, null, r3), L("Output IGST", g.Setoff.Output.IgstPaise, null, r3),
            L("Payable in cash", g.Setoff.Payable, null, r3), L("Challan to deposit", g.Setoff.Challan, null, r3),
            Sec("Cash ledger"), L("Cash paid in the month", g.CashPaid), L("TCS credit", g.TcsCredit), L("Cash ledger balance", g.CashLedgerBalance),
        };
        rows.AddRange(g.Ties.Differences.Select(d => L("Difference: " + d, null)));
        rows.Add(Row(ReportRowKind.Total, ("line", r1 && r3 ? "Ties to GSTR-1 and GSTR-3B" : "Does not tie — see the differences"), ("amount", null), ("tiesGstr1", r1), ("tiesGstr3b", r3)));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters));
    }
}
