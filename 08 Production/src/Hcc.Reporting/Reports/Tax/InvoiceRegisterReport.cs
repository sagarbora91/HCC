// F7-R5 · rpt.tax.invoice-register — Invoice register (catalogue A45, C1.BillSummary, C4.SalesBill, C4.LodgingBillReceipt). Tax invoices and credit notes of a range with the
// tariff / other / discount split, GST, net bill and the payment split (guest folio receipts on the current invoice, restaurant and counter settlement). Round-off is
// always 0 (HCC bills carry exact paise). Credit notes are negative rows. Sorted by series (INV, RST, BOS, CRN) then number. Server twin of tax/invoice-register.ts.
using Hcc.Application.Billing;
using Hcc.Application.Invoices;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Tax;

public sealed class InvoiceRegisterReport(IInvoiceStore store, SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.tax.invoice-register";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("invoiceNo", "Invoice no"), D("date", "Date"), T("party", "Guest or company", ReportMask.GuestName), T("room", "Room"), M("tariff", "Tariff"), M("other", "Food, laundry, other"),
        M("discount", "Discount"), M("gst", "GST"), M("roundOff", "Round-off"), M("net", "Net bill"), M("cash", "Cash"), M("cardUpi", "Card, UPI, cheque"), M("credit", "Credit"), T("status", "Status"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Invoice register", "tax", "P1", "built", true, "PERM-ACC-013", ["PERM-ACC-013"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("series", "Series", "enum", null, ["all", "INV", "RST", "BOS", "CRN"], "all"),
         new("billType", "Bill type", "enum", null, ["all", "room", "counter", "credit note"], "all")],
        Columns,
        "Invoices and credit notes by document date (a stay invoice is dated its departure or its correction), range up to 92 days. Tariff = room-SAC charges; discount = negative lines; credit notes are negative rows. Payment split: guest folio receipts on the current invoice, restaurant and counter settlement.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.No, false, ["A45", "C1.BillSummary", "C1.BillSummerShort", "C4.SalesBill", "C4.LodgingBillReceipt"]);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        if (to.DayNumber - from.DayNumber + 1 > 92) throw HccException.Validation("The range is longer than 92 days. Narrow the range.");
        var series = parameters.Values.GetValueOrDefault("series") ?? "all";
        var type = parameters.Values.GetValueOrDefault("billType") ?? "all";
        bool Want(TaxEntry e) => (series == "all" || e.Series == series)
            && (type == "all" || (type == "credit note" ? e.IsNote : type == "room" ? !e.IsNote && e.Series == "INV" && e.Room != "" : !e.IsNote && (e.Series == "RST" || (e.Series == "INV" && e.Room == ""))));
        var list = (await EntriesAsync(store, platform.Connections, caller.PropertyId, from, to, ct).ConfigureAwait(false))
            .Where(Want).OrderBy(e => SeriesOrder(e.Series)).ThenBy(e => e.No, StringComparer.Ordinal).ToList();
        var acc = new long[9];
        var rows = new List<ReportRow>();
        foreach (var e in list)
        {
            var sg = e.Sign;
            long tariff = 0, other = 0, discount = 0;
            foreach (var l in e.Lines)
            {
                var v = l.Taxable * sg;
                if (sg * v > 0) { if (l.Sac == RoomSac) tariff += v; else other += v; }
                else if (sg * v < 0) discount += v;
            }

            long[] c = [tariff, other, discount, e.Gst, 0, e.Total, e.Cash, e.CardUpi, e.Credit];
            for (var k = 0; k < c.Length; k++) acc[k] += c[k];
            rows.Add(Row(ReportRowKind.Row, ("invoiceNo", e.No), ("date", e.Date), ("party", e.Party), ("room", e.Room), ("tariff", tariff), ("other", other), ("discount", discount), ("gst", e.Gst),
                ("roundOff", 0L), ("net", e.Total), ("cash", e.Cash), ("cardUpi", e.CardUpi), ("credit", e.Credit), ("status", e.Status)));
        }

        rows.Add(Row(ReportRowKind.Total, ("invoiceNo", "Total"), ("tariff", acc[0]), ("other", acc[1]), ("discount", acc[2]), ("gst", acc[3]), ("roundOff", acc[4]), ("net", acc[5]),
            ("cash", acc[6]), ("cardUpi", acc[7]), ("credit", acc[8])));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters));
    }
}
