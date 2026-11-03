// F7-R5 · rpt.tax.documents-issued — Documents issued (GSTR-1 Table 13; catalogue A3, DEC-197). Adapter over InvoiceQueries.Gstr1UncheckedAsync(...).Documents:
// one row per series, the legacy Lodge series LGC first (lgc.bill), then INV, RST, BOS, CRN. Cancelled = real cancelled numbers.
using Hcc.Application.Billing;
using Hcc.Application.Invoices;
using Hcc.Application.Reporting.Rpt;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Tax;

public sealed class DocumentsIssuedReport(InvoiceQueries invoices) : IReportSelector
{
    public const string Id = "rpt.tax.documents-issued";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("nature", "Nature of document"), T("series", "Series"), T("fromNo", "From no"), T("toNo", "To no"), I("total", "Total"), I("cancelled", "Cancelled"), I("net", "Net issued"),
    ];

    private static string Nature(string series) => series switch
    {
        "LGC" => "Invoices for outward supply (legacy Lodge bills)",
        "INV" => "Invoices for outward supply",
        "RST" => "Invoices for outward supply (restaurant)",
        "BOS" => "Bills of supply",
        "CRN" => "Credit notes",
        _ => series,
    };

    public ReportDefinition Definition { get; } = new(Id, "Documents issued (Table 13)", "tax", "P1", "built", false, null, ["PERM-ACC-013"], [], "PERM-RPT-003", ReportDateClass.Month,
        [new("month", "Month", "month", ReportDateClass.Month)], Columns,
        "Documents by invoice date; first and last number of each series in the month. Cancelled = numbers actually cancelled (a voided restaurant bill). Legacy Lodge bills (LGC) by bill date.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.No, false, ["A3"]);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var g = await invoices.Gstr1UncheckedAsync(caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false);
        var rows = new List<ReportRow>();
        long total = 0, cancelled = 0;
        foreach (var d in g.Documents)
        {
            rows.Add(Row(ReportRowKind.Row, ("nature", Nature(d.DocType)), ("series", d.DocType), ("fromNo", d.First), ("toNo", d.Last), ("total", (long)d.Count), ("cancelled", (long)d.Cancelled),
                ("net", (long)(d.Count - d.Cancelled))));
            total += d.Count;
            cancelled += d.Cancelled;
        }

        rows.Add(Row(ReportRowKind.Total, ("nature", "Total"), ("total", total), ("cancelled", cancelled), ("net", total - cancelled)));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters));
    }
}
