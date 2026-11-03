// F7-R7 · the document builders (server twin of documents/{folio,payslip,pos,ratecard,handover}.ts). Pure: a module read model in, a DocumentModel out; permissions are
// those of the read (PERM-INV-001 / PERM-FOL-001 / PERM-HR-006 / PERM-POS-001 are enforced by the query that produced the view), so a document never widens a role. Money
// documents are only built from views the caller could already read; the kitchen ticket and the rate card carry no hotel money.
using System.Globalization;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Handover;
using Hcc.Contracts.Invoices;
using Hcc.Contracts.Payroll;
using Hcc.Contracts.Pos;
using Hcc.Contracts.Tariff;
using Hcc.Domain.Payroll;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Reports.Guests;

namespace Hcc.Reporting.Documents;

/// <summary>Guest facts a folio view does not carry (the caller reads them with the reservation).</summary>
public sealed record FolioHeader(string Guest, string Room, string Booking, DateOnly Arrival, DateOnly Departure, string? InvoiceNo = null, string? BillTo = null, string? BuyerGstin = null);

/// <summary>A quotation of a stay: nights x rate, GST of the dated master (computed by the caller, never here), advance.</summary>
public sealed record QuotationInput(string Booking, string Guest, string? Phone, string Room, DateOnly Arrival, DateOnly Departure, int Guests, string Source, string CheckIn, string CheckOut,
    long RatePaise, long TaxPaise, long AdvancePaise, string? Notes);

public static class DocumentBuilders
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", C);

    private static (long Cgst, long Sgst) Split(long tax) { var sgst = tax / 2; return (tax - sgst, sgst); }

    private static string Qr(string property, string no, DateOnly date, long total) => $"HCC|{property}|{no}|{Iso(date)}|{(total / 100m).ToString("0.00", C)}";

    private static readonly IReadOnlyList<ReportColumn> SumCols = [Doc.T("item", "Item"), Doc.M("amount", "Amount")];

    /// <summary>The Lodge layouts of R3 (registration card, day end) as documents.</summary>
    public static DocumentModel FromLodge(LodgeLayout l, string id, string title, string number, string property, bool money) =>
        new(id, title, number, property, [], [], [], [], money, "Business date.", null, l);

    public static DocumentModel Invoice(InvoiceView v, string property)
    {
        ArgumentNullException.ThrowIfNull(v);
        var rows = v.Lines.Select(l => Doc.Row(("desc", l.Description), ("date", l.ServiceDate), ("net", l.TaxablePaise), ("rate", l.TaxBps / 100.0), ("cgst", l.CgstPaise), ("sgst", l.SgstPaise), ("total", l.TaxablePaise + l.TaxPaise))).ToList();
        rows.Add(Doc.Row(ReportRowKind.Total, ("desc", "Total"), ("net", v.TaxablePaise), ("cgst", v.CgstPaise), ("sgst", v.SgstPaise), ("total", v.TotalPaise)));
        List<ReportRow> sum = [Doc.Row(("item", "Taxable value"), ("amount", v.TaxablePaise)), Doc.Row(("item", "CGST"), ("amount", v.CgstPaise)), Doc.Row(("item", "SGST"), ("amount", v.SgstPaise))];
        if (v.IgstPaise != 0) sum.Add(Doc.Row(("item", "IGST"), ("amount", v.IgstPaise)));
        sum.Add(Doc.Row(ReportRowKind.Total, ("item", "Invoice total"), ("amount", v.TotalPaise)));
        List<(string, string)> pairs =
        [
            ("Tax invoice no", v.InvoiceNo), ("Date", Iso(v.InvoiceDate)), ("Source", v.SourceRef), ("Supply", v.SupplyType), ("Hotel GSTIN", v.Supplier.Gstin ?? ""),
            ("Bill to", v.Recipient.Name ?? ""), ("Buyer GSTIN", v.Recipient.Gstin ?? ""), ("Address", v.Recipient.Address ?? ""), ("Place of supply", v.PlaceOfSupply),
            ("Period", v.PeriodFrom is { } f && v.PeriodTo is { } t ? $"{Iso(f)} to {Iso(t)}" : ""),
        ];
        var notes = v.CreditNotes.Select(c => $"Credit note {c.CreditNoteNo} · {Iso(c.NoteDate)} · {CellText.Rupees(c.TaxablePaise)} before tax · {c.Reason}").ToList();
        notes.Add(v.AmountInWords);
        notes.Add("GST from the dated GST master, intra-state (CGST + SGST), per line, half away from zero.");
        return new DocumentModel("doc.invoice", "Tax invoice", v.InvoiceNo, property, pairs,
            [new("lines", "Charges", [Doc.T("desc", "Description"), Doc.D("date", "Date"), Doc.M("net", "Net"), Doc.P("rate", "GST rate"), Doc.M("cgst", "CGST"), Doc.M("sgst", "SGST"), Doc.M("total", "Total")], rows), new("summary", "Summary", SumCols, sum)],
            notes, ["Authorised signatory"], true, "Charges by service date.", Qr(property, v.InvoiceNo, v.InvoiceDate, v.TotalPaise));
    }

    public static DocumentModel CreditNote(CreditNoteView v, string property)
    {
        ArgumentNullException.ThrowIfNull(v);
        var rows = v.Lines.Select(l => Doc.Row(("desc", $"{l.Category} · SAC {l.Sac}"), ("net", l.TaxablePaise), ("rate", l.TaxBps / 100.0), ("cgst", l.CgstPaise), ("sgst", l.SgstPaise), ("total", l.TaxablePaise + l.TaxPaise))).ToList();
        rows.Add(Doc.Row(ReportRowKind.Total, ("desc", "Total"), ("net", v.TaxablePaise), ("cgst", v.CgstPaise), ("sgst", v.SgstPaise), ("total", v.TotalPaise)));
        return new DocumentModel("doc.credit-note", "Credit note", v.CreditNoteNo, property,
            [("Credit note no", v.CreditNoteNo), ("Date", Iso(v.NoteDate)), ("Against invoice", $"{v.InvoiceNo} of {Iso(v.InvoiceDate)}"), ("Bill to", v.Recipient.Name ?? ""), ("Buyer GSTIN", v.Recipient.Gstin ?? ""), ("Reason", v.Reason)],
            [new("lines", "Credited", [Doc.T("desc", "Description"), Doc.M("net", "Before GST"), Doc.P("rate", "GST rate"), Doc.M("cgst", "CGST"), Doc.M("sgst", "SGST"), Doc.M("total", "Credit total")], rows)],
            [v.AmountInWords, "Issued against the tax invoice shown; GST reversed at the rate of the original line."], ["Authorised signatory"], true, "Credit note date.", Qr(property, v.CreditNoteNo, v.NoteDate, v.TotalPaise));
    }

    /// <summary>Folio summary (no invoice number yet) or the folio behind an issued invoice.</summary>
    public static DocumentModel Folio(FolioView v, FolioHeader h, string property)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(h);
        var live = v.Lines.Where(l => !l.Reversed && l.ReversesChargeId is null).ToList();
        var rows = live.Select(l => { var (cg, sg) = Split(l.TaxPaise); return Doc.Row(("desc", l.Label), ("date", l.ServiceDate), ("net", l.AmountPaise), ("rate", l.TaxBps / 100.0), ("cgst", cg), ("sgst", sg), ("total", l.AmountPaise + l.TaxPaise)); }).ToList();
        var t = v.Totals;
        var (tcg, tsg) = Split(live.Sum(l => l.TaxPaise));
        rows.Add(Doc.Row(ReportRowKind.Total, ("desc", "Total"), ("net", t.NetPaise), ("cgst", tcg), ("sgst", tsg), ("total", t.TotalPaise)));
        var rec = v.Receipts.Select(r => Doc.Row(("id", $"R{r.ReceiptId}"), ("date", r.BusinessDate), ("kind", r.Kind), ("tender", r.Tender), ("ref", r.Reference), ("amount", r.AmountPaise))).ToList();
        List<ReportRow> sum =
        [
            Doc.Row(("item", "Net charges"), ("amount", t.NetPaise)), Doc.Row(("item", "CGST"), ("amount", tcg)), Doc.Row(("item", "SGST"), ("amount", tsg)),
            Doc.Row(ReportRowKind.Total, ("item", "Total including GST"), ("amount", t.TotalPaise)), Doc.Row(("item", "Payments received (net)"), ("amount", t.PaidPaise)),
            Doc.Row(("item", "On company credit (receivable)"), ("amount", t.CompanyCreditPaise)), Doc.Row(ReportRowKind.Total, ("item", "Balance"), ("amount", t.BalancePaise)),
            Doc.Row(("item", "Refundable deposit held separately"), ("amount", t.DepositPaise)),
        ];
        var inv = h.InvoiceNo is { Length: > 0 };
        return new DocumentModel("doc.folio", "Guest folio", h.Booking, property,
            [("Guest", h.Guest), ("Room", h.Room), ("Booking", h.Booking), ("Arrival", Iso(h.Arrival)), ("Departure", Iso(h.Departure)), ("Nights", (h.Departure.DayNumber - h.Arrival.DayNumber).ToString(C)),
             ("Bill to", h.BillTo ?? h.Guest), ("Buyer GSTIN", h.BuyerGstin ?? ""), ("Invoice no", inv ? h.InvoiceNo! : "Not yet issued")],
            [new("lines", "Charges", [Doc.T("desc", "Description"), Doc.D("date", "Date"), Doc.M("net", "Net"), Doc.P("rate", "GST rate"), Doc.M("cgst", "CGST"), Doc.M("sgst", "SGST"), Doc.M("total", "Total")], rows),
             new("receipts", "Receipts", [Doc.T("id", "Receipt"), Doc.D("date", "Date"), Doc.T("kind", "Kind"), Doc.T("tender", "Tender"), Doc.T("ref", "Reference"), Doc.M("amount", "Amount")], rec),
             new("summary", "Summary", SumCols, sum)],
            ["GST from the dated GST master, intra-state (CGST + SGST). The issued tax invoice is a separate document."], ["Guest signature", "Reception"], true, "Charges by service date, receipts by receipt date.");
    }

    public static DocumentModel Receipt(FolioView v, long receiptId, FolioHeader h, string property)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(h);
        var r = v.Receipts.FirstOrDefault(x => x.ReceiptId == receiptId) ?? throw new Hcc.Domain.Errors.HccException(Hcc.Contracts.HccErrorCode.NOT_FOUND, "No such receipt.");
        var refund = r.Kind is "refund" or "deposit-return";
        return new DocumentModel("doc.receipt", refund ? "Payment voucher" : "Receipt", $"R{r.ReceiptId}", property,
            [("Receipt no", $"R{r.ReceiptId}"), ("Date", Iso(r.BusinessDate)), (refund ? "Paid to" : "Received from", h.Guest), ("Booking", h.Booking), ("Room", h.Room), ("Towards", r.Kind), ("Mode", r.Tender),
             ("Reference", r.Reference), ("Amount", CellText.Rupees(r.AmountPaise)), ("In words", PayrollFiles.AmountInWords(r.AmountPaise))],
            [], ["Subject to realisation of cheque or card payment. A tax invoice is issued at check-out."], ["Guest", "Cashier"], true, "Receipt date.");
    }

    public static DocumentModel Quotation(QuotationInput q, string property)
    {
        ArgumentNullException.ThrowIfNull(q);
        var n = q.Departure.DayNumber - q.Arrival.DayNumber;
        var net = q.RatePaise * n;
        return new DocumentModel("doc.quotation", "Quotation", q.Booking, property,
            [("Guest", q.Guest), ("Phone", q.Phone ?? ""), ("Room", q.Room), ("Arrival", Iso(q.Arrival)), ("Departure", Iso(q.Departure)), ("Nights", n.ToString(C)), ("Guests", q.Guests.ToString(C)), ("Source", q.Source),
             ("Check-in from", q.CheckIn), ("Check-out by", q.CheckOut)],
            [new("quote", "Stay charges", [Doc.T("desc", "Description"), Doc.I("qty", "Nights"), Doc.M("rate", "Rate per night"), Doc.M("net", "Before GST"), Doc.M("gst", "GST"), Doc.M("total", "Total")],
                [Doc.Row(ReportRowKind.Total, ("desc", $"Room {q.Room}"), ("qty", (long)n), ("rate", q.RatePaise), ("net", net), ("gst", q.TaxPaise), ("total", net + q.TaxPaise))]),
             new("terms", "Payment", SumCols, [Doc.Row(("item", "Advance received"), ("amount", q.AdvancePaise)), Doc.Row(ReportRowKind.Total, ("item", "Balance on this quote"), ("amount", net + q.TaxPaise - q.AdvancePaise))])],
            ["A quotation, not a tax invoice. GST applies per the dated GST master on each night." + (string.IsNullOrWhiteSpace(q.Notes) ? "" : " Notes: " + q.Notes)], ["Guest", "Reception"], true, "Stay dates.");
    }

    public static DocumentModel Payslip(PayslipView v, string property)
    {
        ArgumentNullException.ThrowIfNull(v);
        var l = v.Line;
        List<ReportRow> rows = [Doc.Row(("item", "Basic"), ("earn", l.BasicPaise))];
        if (l.HraPaise != 0 || l.WashingPaise != 0 || l.SpecialPaise != 0)
            rows.AddRange([Doc.Row(("item", "HRA"), ("earn", l.HraPaise)), Doc.Row(("item", "Washing allowance"), ("earn", l.WashingPaise)), Doc.Row(("item", "Special allowance"), ("earn", l.SpecialPaise))]);
        rows.Add(Doc.Row(("item", l.Days is { } dd ? $"OT days ({dd.OtDays.ToString("0.##", C)})" : "Overtime"), ("earn", l.OtPaise)));
        rows.Add(Doc.Row(ReportRowKind.Subtotal, ("item", "Gross"), ("earn", l.GrossPaise)));
        rows.AddRange([Doc.Row(("item", v.PfLabel), ("ded", l.PfPaise)), Doc.Row(("item", v.EsicLabel), ("ded", l.EsicPaise)), Doc.Row(("item", "Professional tax"), ("ded", l.PtPaise)), Doc.Row(("item", "Advance recovery"), ("ded", l.AdvancePaise))]);
        rows.Add(Doc.Row(ReportRowKind.Subtotal, ("item", "Total deductions"), ("ded", l.DeductionsPaise)));
        rows.Add(Doc.Row(ReportRowKind.Total, ("item", "Net pay"), ("earn", l.NetPaise)));
        return new DocumentModel("rpt.people.payslip", "Payslip", $"{v.Month} · {l.StaffId}", property,
            [("Employee", l.Name), ("Staff ID", l.StaffId.ToString(C)), ("Designation", l.Designation), ("Department", l.Dept), ("Month", v.Month), ("Run", v.RunId.ToString(C)), ("Pay type", l.PayType),
             ("Payable days", l.PayableDays.ToString("0.##", C)), ("Status", v.Status)],
            [new("pay", "Earnings and deductions", [Doc.T("item", "Item"), Doc.M("earn", "Earnings"), Doc.M("ded", "Deductions")], rows),
             new("er", "Employer contributions (not deducted)", SumCols, [Doc.Row(("item", "PF employer"), ("amount", l.PfErPaise)), Doc.Row(("item", "ESIC employer"), ("amount", l.EsicErPaise))])],
            [v.NetInWords, v.Disclaimer], ["Employee", "Authorised signatory"], true, "Pay month.");
    }

    public static DocumentModel Kot(CheckView c, long kotId, string property)
    {
        ArgumentNullException.ThrowIfNull(c);
        var k = c.Kots.FirstOrDefault(x => x.KotId == kotId) ?? throw new Hcc.Domain.Errors.HccException(Hcc.Contracts.HccErrorCode.NOT_FOUND, "No such KOT.");
        return new DocumentModel("doc.kot", "Kitchen order ticket", k.Label, property,
            [("Outlet", c.Outlet), ("Table / room", c.Spot), ("Check", c.OrderId.ToString(C)), ("Time", k.CreatedUtc.ToString("HH:mm", C)), ("Covers", c.Covers.ToString(C)), ("Status", k.Status)],
            [new("items", null, [Doc.I("qty", "Qty"), Doc.T("item", "Item")], c.Lines.Where(l => l.KotId == kotId).Select(l => Doc.Row(("qty", (long)l.Qty), ("item", l.Name + (l.Voided ? " - VOID" : "")))).ToList())],
            [], [], false, "KOT time.");
    }

    public static DocumentModel OutletBill(CheckView c, string property)
    {
        ArgumentNullException.ThrowIfNull(c);
        var inv = c.Bill?.Invoice;
        var rows = c.Lines.Where(l => !l.Voided).Select(l => Doc.Row(("item", l.Name), ("qty", (long)l.Qty), ("rate", l.PricePaise), ("amount", l.ValuePaise))).ToList();
        var half = (c.TaxBps / 200.0).ToString("0.##", C) + "%";
        List<ReportRow> sum = [Doc.Row(("item", inv is null ? "Net" : "Taxable value"), ("amount", c.NetPaise))];
        if (inv is not null) sum.AddRange([Doc.Row(("item", $"CGST @{half}"), ("amount", inv.CgstPaise)), Doc.Row(("item", $"SGST @{half}"), ("amount", inv.SgstPaise))]);
        else sum.Add(Doc.Row(("item", $"GST {(c.TaxBps / 100.0).ToString("0.##", C)}%"), ("amount", c.TaxPaise)));
        sum.Add(Doc.Row(ReportRowKind.Total, ("item", "Total"), ("amount", c.TotalPaise)));
        List<(string, string)> pairs = [("Outlet", c.Outlet), ("Table / room", c.Spot), ("Check", c.OrderId.ToString(C)), ("Guest", c.Guest ?? ""), ("Date", Iso(c.BusinessDate)), ("Covers", c.Covers.ToString(C)),
            (inv is null ? "Bill" : "Tax invoice no", inv?.InvoiceNo ?? c.Bill?.BillNo ?? "Pro-forma (not a bill)")];
        if (inv is not null) pairs.AddRange([("SAC", inv.Sac), ("Hotel GSTIN", inv.SupplierGstin), ("Buyer", inv.BuyerName ?? ""), ("Buyer GSTIN", inv.BuyerGstin ?? ""), ("Supply", inv.SupplyType)]);
        if (c.Bill is { } b) pairs.Add(("Settled", b.Mode == "folio" ? "Charged to room folio" : $"Paid by {b.Tender}{(string.IsNullOrEmpty(b.Reference) ? "" : " · " + b.Reference)}"));
        return new DocumentModel("doc.outlet-bill", inv is null ? "Outlet bill" : "Tax invoice", inv?.InvoiceNo ?? c.Bill?.BillNo ?? c.OrderId.ToString(C), property, pairs,
            [new("items", "Items", [Doc.T("item", "Item"), Doc.I("qty", "Qty"), Doc.M("rate", "Rate"), Doc.M("amount", "Amount")], rows), new("sum", "Total", SumCols, sum)],
            [inv?.CreditNoteNo is { } cn ? $"Cancelled: reversed in full by credit note {cn}." : inv is not null ? "GST from the dated GST master, intra-state (CGST + SGST)." : "Not a tax invoice."], [], true, "Bill date.",
            inv is null ? null : Qr(property, inv.InvoiceNo, inv.InvoiceDate, c.TotalPaise));
    }

    /// <summary>Hand-over pack of one business date. <paramref name="seesCash"/> = the caller may see the handover cash float (the view already omits it otherwise; this keeps the column off).</summary>
    public static DocumentModel HandoverPack(IReadOnlyList<HandoverNoteView> notes, string date, bool seesCash, string property)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var list = notes.Where(n => n.Date == date).ToList();
        List<ReportColumn> cols = [Doc.T("desk", "Desk"), Doc.T("shift", "Shift"), Doc.T("author", "By"), Doc.T("issues", "Issues"), Doc.T("keys", "Keys"), Doc.T("guests", "Guests"), Doc.T("carry", "Carried over"), Doc.T("status", "Status")];
        if (seesCash) cols.Add(Doc.M("cash", "Cash float"));
        var rows = list.Select(n =>
        {
            List<(string, object?)> cells = [("desk", n.Desk), ("shift", $"{n.Shift} to {n.ToShift}"), ("author", n.AuthorName), ("issues", n.Nothing ? "Nothing to report" : n.Issues), ("keys", n.Keys), ("guests", n.Guests),
                ("carry", string.Join("; ", n.Carry.Select(c => c.Text + (c.Done ? " (done)" : "")))), ("status", n.Status)];
            if (seesCash) cells.Add(("cash", n.Cash?.AmountPaise));
            return Doc.Row(cells.ToArray());
        }).ToList();
        return new DocumentModel("doc.handover-pack", "Shift hand-over pack", date, property, [("Business date", date), ("Notes", list.Count.ToString(C))], [new("notes", "Hand-over notes", cols, rows)],
            ["Same figures as the Checklists and handover page."], ["Handing over", "Taking over"], seesCash, "Business date of the note.");
    }

    /// <summary>Rate card of a published tariff card version (prices as the card shows them; no hotel money).</summary>
    public static DocumentModel RateCard(TariffCardVersionView v, string property)
    {
        ArgumentNullException.ThrowIfNull(v);
        static string Group(string kind) => kind switch { "room" => "Rooms", "occupancy" => "Extra persons", "meal" or "extra" => "Meal plans & extras", "day-use" => "Day use", "hall" => "Halls", _ => kind };
        static string Title(TariffCardLineDto l) => l.Kind switch
        {
            "room" => $"{l.RoomType} · {l.Plan}", "occupancy" => $"{l.RoomType} · {l.OccKind}", "meal" => l.MealPlan ?? "Meal plan", "extra" => l.Extra ?? "Extra", "hall" => l.Hall ?? "Hall", _ => l.RoomType ?? l.Kind,
        };
        var order = new[] { "Rooms", "Extra persons", "Meal plans & extras", "Day use", "Halls" };
        var tables = v.Lines.GroupBy(l => Group(l.Kind)).OrderBy(g => Array.IndexOf(order, g.Key)).Select(g => new DocTable(g.Key, g.Key,
            [Doc.T("item", "Item"), Doc.T("nights", "Nights"), Doc.M("excl", "Before GST"), Doc.T("gst", "GST"), Doc.M("incl", "Price incl. GST")],
            g.Select(l => Doc.Row(("item", Title(l) + (string.IsNullOrEmpty(l.Note) ? "" : $" ({l.Note})")), ("nights", l.DaySet), ("excl", l.RateExcl), ("gst", (l.TaxBps / 100.0).ToString("0.##", C) + "%"), ("incl", l.DisplayIncl))).ToList())).ToList();
        return new DocumentModel("doc.rate-card", "Tariff card", v.Label, property, [("Card", v.Code), ("Valid from", v.EffectiveFrom), ("Status", v.Status)], tables,
            ["Prices per room per night unless stated; meal plans per guest per night."], [], false, "Valid from the card date.");
    }
}
