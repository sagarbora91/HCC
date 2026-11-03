// F7-R5 · rpt.tax.sac-summary — SAC summary (GSTR-1 Table 12; catalogue A2). Adapter over InvoiceQueries.Gstr1UncheckedAsync (the permission is the engine's rule V:
// PERM-INV-001): rows = Gstr1View.Sac net of credit notes, quantity = invoices carrying the SAC. Legacy Lodge bills are not in this table.
using Hcc.Application.Billing;
using Hcc.Application.Invoices;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Invoices;
using Hcc.Infrastructure.SqlServer.Connection;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Tax;

public sealed class SacSummaryReport(InvoiceQueries invoices, IInvoiceStore store) : IReportSelector
{
    public const string Id = "rpt.tax.sac-summary";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("sac", "SAC"), T("description", "Description"), T("uqc", "UQC"), I("quantity", "Total quantity"), M("totalValue", "Total value"), M("taxable", "Taxable value"),
        M("cgst", "CGST"), M("sgst", "SGST"), P("rate", "Rate"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "SAC summary (Table 12)", "tax", "P1", "built", true, "PERM-ACC-013", ["PERM-ACC-013"], [], "PERM-RPT-003", ReportDateClass.Month,
        [new("month", "Month", "month", ReportDateClass.Month)], Columns,
        "Documents by invoice date (a stay invoice is dated its departure or its correction); net of credit notes. Quantity = invoices carrying the SAC. Legacy Lodge bills are not in this table.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.No, false, ["A2"]);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        var g = await invoices.Gstr1UncheckedAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var inv = await store.InvoicesInRangeAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var docs = inv.SelectMany(i => i.Lines.Select(l => (l.Sac, i.InvoiceNo))).Distinct().GroupBy(x => x.Sac).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var rows = new List<ReportRow>();
        long taxable = 0, cgst = 0, sgst = 0, value = 0;
        foreach (var r in g.Sac)
        {
            var total = r.TaxablePaise + r.CgstPaise + r.SgstPaise + r.IgstPaise;
            rows.Add(Row(ReportRowKind.Row, ("sac", r.Sac), ("description", Gstr1Summary.SacDescription(r.Sac)), ("uqc", "OTH"), ("quantity", (long)docs.GetValueOrDefault(r.Sac)), ("totalValue", total),
                ("taxable", r.TaxablePaise), ("cgst", r.CgstPaise), ("sgst", r.SgstPaise), ("rate", r.TaxBps / 100.0)));
            taxable += r.TaxablePaise;
            cgst += r.CgstPaise;
            sgst += r.SgstPaise;
            value += total;
        }

        rows.Add(Row(ReportRowKind.Total, ("sac", "Total"), ("description", "Legacy Lodge bills are not in this table: their GST was reported by the CA from the old software."),
            ("totalValue", value), ("taxable", taxable), ("cgst", cgst), ("sgst", sgst)));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters));
    }
}
