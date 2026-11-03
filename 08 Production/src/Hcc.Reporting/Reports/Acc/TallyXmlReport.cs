// F7-R6 · rpt.acc.tally-xml — Tally export (masters per FY + vouchers per range), PERM-ACC-006. The report is the file summary; the XML download itself stays the ACC-3
// product (GET /accounting/tally/masters, /vouchers; recorded as ACC_TALLY_EXPORT). Adapter over TallyQueries built WITHOUT the file log: a report run records no export.
// Nothing is sent to Tally. The 31-day cap is a file rule.
using System.Globalization;
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using static Hcc.Reporting.Reports.Acc.AccShared;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Acc;

public sealed class TallyXmlReport(AccQueries q) : IReportSelector
{
    public const string Id = "rpt.acc.tally-xml";

    public static readonly IReadOnlyList<ReportColumn> Columns = [T("file", "File"), I("vouchers", "Vouchers"), I("ledgers", "Ledgers"), T("notes", "Notes")];

    public ReportDefinition Definition { get; } = new(Id, "Tally export (masters and vouchers)", "acc", "P1", "ACC-3", false, null, [Tally], [], Tally, ReportDateClass.FinancialYear,
        [new("fy", "Masters FY", "fy", ReportDateClass.FinancialYear), new("vFrom", "Vouchers from", "date"), new("vTo", "Vouchers to", "date")], Columns,
        "Masters: the chart and every party ledger with the opening balance at the FY start. Vouchers: the range (at most 31 days). A simulated export — nothing is sent to Tally.",
        [ReportFormat.Screen, ReportFormat.Xml], ReportPhone.Yes);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var vTo = parameters.Values.TryGetValue("vTo", out var vt) ? D(vt) : parameters.To!.Value;
        var vFrom = parameters.Values.TryGetValue("vFrom", out var vf) ? D(vf) : vTo.AddDays(-6);
        var tally = q.Tally();
        var m = await tally.MastersAsync(caller, parameters.Values["fy"], ct).ConfigureAwait(false);
        var v = await tally.VouchersAsync(caller, vFrom, vTo, ct).ConfigureAwait(false);
        var yr = await q.YearProvisionalAsync(caller.PropertyId, vFrom, vTo, ct).ConfigureAwait(false) || await q.YearProvisionalAsync(caller.PropertyId, m.From, m.From, ct).ConfigureAwait(false);
        string Note(bool prov, string extra = "") => string.Join(" · ", new[] { prov || yr ? "PROVISIONAL" : "", yr ? YearLockRules.ProvisionalText : "", extra }.Where(x => x.Length > 0));
        var rows = new List<ReportRow>
        {
            Row(ReportRowKind.Row, ("file", "masters.xml"), ("vouchers", null), ("ledgers", (long)m.Ledgers), ("notes", Note(m.Provisional))),
            Row(ReportRowKind.Row, ("file", "vouchers.xml"), ("vouchers", (long)v.VoucherCount), ("ledgers", null),
                ("notes", Note(v.ProvisionalDates.Count > 0, v.ReexportDates.Count > 0 ? $"re-export {v.ReexportDates.Count} date(s)" : ""))),
        };
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, new ReportAbout(DateTimeOffset.MinValue, "", Definition.DateBasis, m.Provisional || v.ProvisionalDates.Count > 0 || yr, parameters.Legacy, vFrom, vTo));
    }
}
