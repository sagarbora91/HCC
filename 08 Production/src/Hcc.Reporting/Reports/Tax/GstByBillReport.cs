// F7-R5 · rpt.tax.gst-by-bill — GST by bill (catalogue A18–A20). Each tax invoice with taxable value by rate and CGST / SGST from the stored GST of the document
// (never recomputed); the month's credit notes are listed as negative rows so the totals tie to the GSTR-1 working summary. Server twin of tax/gst-by-bill.ts.
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Invoices;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Tax;

public sealed class GstByBillReport(IInvoiceStore store, SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.tax.gst-by-bill";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("billNo", "Bill no"), D("billDate", "Bill date"), T("party", "Guest or company", ReportMask.GuestName), T("gstin", "GSTIN"), T("room", "Room"), M("net", "Net amount"),
        M("tax0", "Taxable at 0 %"), M("tax5", "Taxable at 5 %"), M("tax12", "Taxable at 12 %"), M("tax18", "Taxable at 18 %"), M("tax28", "Taxable at 28 %"), M("cgst", "CGST"), M("sgst", "SGST"),
    ];

    private static readonly IReadOnlyList<ReportColumn> LineColumns =
    [
        Columns[0], Columns[1], Columns[2], Columns[3], Columns[4], T("item", "Item"), I("qty", "Qty"), P("rate", "Rate"), .. Columns.Skip(5),
    ];

    public ReportDefinition Definition { get; } = new(Id, "GST by bill", "tax", "P1", "built", true, "PERM-ACC-013", ["PERM-ACC-013"], [], "PERM-RPT-003", ReportDateClass.Month,
        [new("month", "Month", "month", ReportDateClass.Month), new("level", "Level", "enum", null, ["bill", "line"], "bill"), new("zeroTax", "Zero-tax bills", "enum", null, ["include", "exclude"], "include")],
        Columns,
        "Invoices by invoice date (a stay invoice is dated its departure or its correction), with the month's credit notes as negative rows. The bill's stored GST — never recomputed. Rates other than 0, 5, 12, 18 and 28 % count in Net amount and the tax columns only.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.No, false, ["A18", "A19", "A20"]);

    private static string RateKey(int bps) => "tax" + (bps / 100).ToString(CultureInfo.InvariantCulture);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var line = parameters.Values.GetValueOrDefault("level") == "line";
        var exclude = parameters.Values.GetValueOrDefault("zeroTax") == "exclude";
        var entries = (await EntriesAsync(store, platform.Connections, caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false))
            .Where(e => !(exclude && e.Gst == 0 && e.Lines.All(l => l.Bps == 0)))
            .OrderBy(e => e.Date).ThenBy(e => SeriesOrder(e.Series)).ThenBy(e => e.No, StringComparer.Ordinal).ToList();
        var rows = new List<ReportRow>();
        long net = 0, cgst = 0, sgst = 0;
        var rate = RateBps.ToDictionary(b => b, _ => 0L);
        foreach (var e in entries)
        {
            var parts = new List<(IReadOnlyList<TaxLine> Lines, string? Item)>();
            if (line) parts.AddRange(e.Lines.Select(l => ((IReadOnlyList<TaxLine>)[l], (string?)l.Description)));
            else parts.Add((e.Lines, null));
            foreach (var (ls, item) in parts)
            {
                var sg = e.Sign;
                long n = ls.Sum(l => l.Taxable) * sg, c = ls.Sum(l => l.Cgst) * sg, s = ls.Sum(l => l.Sgst) * sg;
                var cells = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["billNo"] = e.No, ["billDate"] = e.Date, ["party"] = e.Party, ["gstin"] = e.Gstin, ["room"] = e.Room, ["net"] = n, ["cgst"] = c, ["sgst"] = s,
                };
                if (line)
                {
                    cells["item"] = item;
                    cells["qty"] = 1L;
                    cells["rate"] = ls[0].Bps / 100.0;
                }

                foreach (var b in RateBps)
                {
                    var v = ls.Where(l => l.Bps == b).Sum(l => l.Taxable) * sg;
                    cells[RateKey(b)] = v;
                    rate[b] += v;
                }

                rows.Add(new ReportRow(ReportRowKind.Row, cells));
                net += n;
                cgst += c;
                sgst += s;
            }
        }

        var tot = new Dictionary<string, object?>(StringComparer.Ordinal) { ["billNo"] = "Total", ["net"] = net, ["cgst"] = cgst, ["sgst"] = sgst };
        foreach (var b in RateBps) tot[RateKey(b)] = rate[b];
        rows.Add(new ReportRow(ReportRowKind.Total, tot));
        return new ReportResult(Id, Definition.Title, parameters, line ? LineColumns : Columns, rows, About(Definition, parameters));
    }
}
