// F7-R7 · document layouts: each builder over its module's read model (InvoiceView, CreditNoteView, FolioView, PayslipView, CheckView, HandoverNoteView, TariffCardVersionView),
// totals = the view's own, the kitchen ticket and the rate card carry no hotel money, the Lodge page repeats every figure, and screen = Excel = PDF text = print (RendererTests.ThreeWay).
using System.Text.RegularExpressions;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Handover;
using Hcc.Contracts.Invoices;
using Hcc.Contracts.Payroll;
using Hcc.Contracts.Pos;
using Hcc.Contracts.Tariff;
using Hcc.Reporting.Documents;

namespace Hcc.Reporting.Tests.Documents;

public sealed class DocumentTests
{
    private const string Prop = "Hotel Parth Executive";
    private static readonly DateTimeOffset At = new(2026, 11, 3, 6, 30, 0, TimeSpan.Zero);
    private static readonly PartyView Hotel = new("Hotel Parth Executive", "27ABCDE1234F1Z5", "Pune", "27", "Maharashtra");
    private static readonly PartyView Buyer = new("Demo Industries", "27AAAAA0000A1Z5", "Mumbai", "27", "Maharashtra");

    private static InvoiceView Invoice() => new(1, "HPE/2627/0001", "invoice", "Tax invoice", new DateOnly(2026, 10, 27), "stay", 9, "HCC-1041", new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 27), "B2B", Hotel, Buyer, "Maharashtra", false,
        [new(1, 11, "room", new DateOnly(2026, 10, 25), "Room 101 night", "room", "996311", 300000, 500, 15000, 7500, 7500, 0, 0, 0),
         new(2, 12, "service", new DateOnly(2026, 10, 26), "Laundry", "laundry", "999711", 50000, 1800, 9000, 4500, 4500, 0, 0, 0)],
        350000, 12000, 12000, 0, 374000, "Rupees Three Thousand Seven Hundred Forty Only", 374000, 374000, [new(5, "HPE/CN/0001", new DateOnly(2026, 10, 28), "Laundry waived", 20000, 23600)], 1, At.UtcDateTime);

    private static CreditNoteView CreditNote() => new(5, "HPE/CN/0001", new DateOnly(2026, 10, 28), 1, "HPE/2627/0001", new DateOnly(2026, 10, 27), "amount", "Laundry waived", Hotel, Buyer, "Maharashtra", false,
        [new(1, 2, "laundry", "999711", 20000, 1800, 3600, 1800, 1800, 0, null)], 20000, 1800, 1800, 0, 23600, "Rupees Two Hundred Thirty Six Only", 1, 2, At.UtcDateTime);

    private static FolioView Folio() => new(7, 70, "In house", new DateOnly(2026, 10, 27), "v1",
        [new(1, "room", new DateOnly(2026, 10, 25), "Room 101 night", 300000, 500, 15000, null, false), new(2, "service", new DateOnly(2026, 10, 26), "Laundry", 50000, 1800, 9000, null, false)],
        [new(3, "payment", "UPI", 200000, new DateOnly(2026, 10, 25), "DEMO-UPI-1", null, 0, 200000)], [], new FolioTotalsView(350000, 24000, 374000, 200000, 0, 0, 174000));

    private static PayslipView Payslip() => new(4, "2026-10", "Locked",
        new PayLineView(12, "Asha Rao", "Front office", "Receptionist", "monthly", 30, 2000000, 1000000, 400000, 100000, 500000, 0, 2000000, 1000000, 120000, 120000, 2000000, 15000, 65000, 20000, 0, 155000, 1845000, 2240000, true, "", null),
        "Provident Fund (12%)", "ESIC (0.75%)", "Rupees Eighteen Thousand Four Hundred Fifty Only", "Illustrative - CA to confirm.");

    private static CheckView Check(bool settled) => new(31, 1, "Restaurant", "Table 4", "Walk-in", 2, new DateOnly(2026, 10, 27), settled ? "Settled" : "Open", "v1", null,
        [new(1, 1, "Paneer tikka", 25000, 2, 50000, 8, "Served", false, null), new(2, 2, "Lime soda", 8000, 1, 8000, 8, "Served", false, null), new(3, 3, "Void dish", 9000, 1, 9000, 8, "Void", true, "changed")],
        [new(8, 3, "RE-KOT 3", "Served", At.UtcDateTime, null, null)], 58000, 500, 2900, 60900, 3,
        settled ? new BillView(2, "RST-1", "outlet", "UPI", "DEMO-1", 58000, 500, 2900, 60900, new DateOnly(2026, 10, 27), "Settled", null, null, null, new PosBillInvoiceView(1, "RST/2627/0000001", "invoice", new DateOnly(2026, 10, 27), "B2C", null, null, "27ABCDE1234F1Z5", "996331", 1450, 1450, null)) : null);

    private static string Page(DocumentModel m) => DocumentHtml.Page(m).Body.Replace("&amp;", "&", StringComparison.Ordinal);

    /// <summary>Every pair value and every cell of every table appears on the Lodge page, and the engine renderers agree (screen = Excel = PDF text = print).</summary>
    private static void Same(DocumentModel m)
    {
        var page = Page(m);
        foreach (var (_, v) in m.Pairs) if (v.Length > 0) Assert.Contains(System.Net.WebUtility.HtmlEncode(v).Replace("&amp;", "&", StringComparison.Ordinal), page, StringComparison.Ordinal);
        foreach (var t in m.Tables)
            foreach (var r in t.Rows)
                foreach (var c in t.Columns)
                {
                    var text = CellText.Of(c, r.Cells.GetValueOrDefault(c.Key));
                    if (text.Length > 0) Assert.Contains(System.Net.WebUtility.HtmlEncode(text).Replace("&amp;", "&", StringComparison.Ordinal), page, StringComparison.Ordinal);
                }

        Assert.Contains("Hotel Control Centre", page, StringComparison.Ordinal);
        Assert.Contains("lg-head", page, StringComparison.Ordinal);
        RendererTests.ThreeWay(DocumentResults.ToReport(m, At, "Tester"));
    }

    [Fact]
    public void Invoice_totals_are_the_views_and_the_qr_text_has_no_irn()
    {
        var v = Invoice();
        var m = DocumentBuilders.Invoice(v, Prop);
        Assert.Equal("doc.invoice", m.Id);
        var total = m.Tables[0].Rows[^1];
        Assert.Equal(ReportRowKind.Total, total.Kind);
        Assert.Equal(v.TotalPaise, total.Cells["total"]);
        Assert.Equal(v.CgstPaise + v.SgstPaise, (long)total.Cells["cgst"]! + (long)total.Cells["sgst"]!);
        Assert.Equal(v.Lines.Count + 1, m.Tables[0].Rows.Count);
        Assert.Contains("HPE/2627/0001", m.QrText, StringComparison.Ordinal);
        Assert.DoesNotContain("irn", m.QrText!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(m.Notes, n => n.Contains("HPE/CN/0001", StringComparison.Ordinal));
        Same(m);
    }

    [Fact]
    public void Credit_note_total_is_taxable_plus_gst()
    {
        var m = DocumentBuilders.CreditNote(CreditNote(), Prop);
        var row = m.Tables[0].Rows[0].Cells;
        Assert.Equal(23600L, row["total"]);
        Assert.Equal((long)row["net"]! + (long)row["cgst"]! + (long)row["sgst"]!, (long)row["total"]!);
        Assert.Contains("HPE/2627/0001", m.Pairs.First(p => p.Label == "Against invoice").Value, StringComparison.Ordinal);
        Same(m);
    }

    [Fact]
    public void Folio_and_receipt_follow_the_folio_totals()
    {
        var f = Folio();
        var h = new FolioHeader("Asha Rao", "101", "HCC-1041", new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 27));
        var m = DocumentBuilders.Folio(f, h, Prop);
        Assert.Equal(f.Totals.TotalPaise, m.Tables[0].Rows[^1].Cells["total"]);
        Assert.Equal(f.Totals.BalancePaise, m.Tables[2].Rows.First(r => (string)r.Cells["item"]! == "Balance").Cells["amount"]);
        Assert.Equal("Not yet issued", m.Pairs.First(p => p.Label == "Invoice no").Value);
        Same(m);
        var r = DocumentBuilders.Receipt(f, 3, h, Prop);
        Assert.Contains("₹2,000", r.Pairs.First(p => p.Label == "Amount").Value, StringComparison.Ordinal);
        Assert.Matches("^Rupees .* Only$", r.Pairs.First(p => p.Label == "In words").Value);
        Same(r);
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => DocumentBuilders.Receipt(f, 99, h, Prop));
    }

    [Fact]
    public void Payslip_net_pay_is_the_stored_line()
    {
        var v = Payslip();
        var m = DocumentBuilders.Payslip(v, Prop);
        Assert.Equal("rpt.people.payslip", m.Id);
        Assert.Equal(v.Line.NetPaise, m.Tables[0].Rows[^1].Cells["earn"]);
        Assert.Equal(v.Line.DeductionsPaise, m.Tables[0].Rows.First(r => (string)r.Cells["item"]! == "Total deductions").Cells["ded"]);
        Same(m);
    }

    [Fact]
    public void Kot_has_no_money_and_the_bill_is_the_checks_total()
    {
        var kot = DocumentBuilders.Kot(Check(false), 8, Prop);
        Assert.False(kot.Money);
        Assert.All(kot.Tables, t => Assert.All(t.Columns, c => Assert.False(c.Money)));
        Assert.DoesNotContain("₹", Page(kot), StringComparison.Ordinal);
        Assert.Equal(3, kot.Tables[0].Rows.Count);
        Same(kot);
        var pro = DocumentBuilders.OutletBill(Check(false), Prop);
        Assert.Equal(60900L, pro.Tables[1].Rows[^1].Cells["amount"]);
        Assert.Equal(2, pro.Tables[0].Rows.Count);
        Assert.Null(pro.QrText);
        Same(pro);
        var inv = DocumentBuilders.OutletBill(Check(true), Prop);
        Assert.Equal("RST/2627/0000001", inv.Number);
        Assert.Equal(1450L, inv.Tables[1].Rows[1].Cells["amount"]);
        Assert.NotNull(inv.QrText);
        Same(inv);
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => DocumentBuilders.Kot(Check(false), 99, Prop));
    }

    [Fact]
    public void Rate_card_and_handover_pack_and_quotation()
    {
        var card = new TariffCardVersionView(1, "TC-1", "Winter card", "2026-11-01", "published", [6, 7],
            [new(1, "room", "Classic", "EP", null, null, null, null, "all", 250000, null, null, null, null, null, null, 500, 262500), new(2, "meal", null, null, null, "MAP", null, null, "all", 40000, null, null, null, null, null, null, 500, 42000)],
            1, "2026-10-30", 1, 1, "2026-10-31", null, null, []);
        var rc = DocumentBuilders.RateCard(card, Prop);
        Assert.False(rc.Money);
        Assert.Equal("Rooms", rc.Tables[0].Title);
        Assert.Equal(262500L, rc.Tables[0].Rows[0].Cells["incl"]);
        Same(rc);
        HandoverNoteView Note(HandoverCashView? cash) => new(1, "HN-1", "Front desk", "2026-10-27", "Morning", "Evening", 3, "Priya", "2026-10-27T08:00:00Z", "AC in 204", false, "Keys at desk", "VIP arrives", cash,
            [new(1, "Call plumber", false, null, null)], "Submitted", null, null, null, "v1");
        var withCash = DocumentBuilders.HandoverPack([Note(new HandoverCashView(500000, "counted"))], "2026-10-27", true, Prop);
        Assert.True(withCash.Money);
        Assert.Contains(withCash.Tables[0].Columns, c => c.Key == "cash");
        Assert.Equal(500000L, withCash.Tables[0].Rows[0].Cells["cash"]);
        Same(withCash);
        var noCash = DocumentBuilders.HandoverPack([Note(new HandoverCashView(500000, "counted"))], "2026-10-27", false, Prop);
        Assert.False(noCash.Money);
        Assert.DoesNotContain(noCash.Tables[0].Columns, c => c.Key == "cash");
        Assert.DoesNotContain("5,000", Page(noCash), StringComparison.Ordinal);
        Same(noCash);
        var q = DocumentBuilders.Quotation(new QuotationInput("HCC-1", "Asha Rao", "9800000000", "101", new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 7), 2, "Phone", "12:00", "11:00", 250000, 25000, 100000, null), Prop);
        Assert.Equal(525000L, q.Tables[0].Rows[0].Cells["total"]);
        Assert.Equal(425000L, q.Tables[1].Rows[^1].Cells["amount"]);
        Same(q);
    }

    [Fact]
    public void Pdf_of_a_document_carries_its_number_and_a_formula_cell_is_neutralised_in_csv()
    {
        var m = DocumentBuilders.Invoice(Invoice(), Prop);
        var r = DocumentResults.ToReport(m, At, "Tester");
        var (pages, lines) = PdfText.Extract(new PdfRenderer().Render(r, new RenderContext("HPE", Prop, ReportFormat.Pdf)).Bytes);
        Assert.True(pages >= 1);
        Assert.Contains(lines, l => l.Contains("HPE/2627/0001", StringComparison.Ordinal));
        var evil = Invoice() with { Recipient = new PartyView("=HYPERLINK(\"x\")", null, null, null, null) };
        var csv = System.Text.Encoding.UTF8.GetString(new CsvRenderer().Render(DocumentResults.ToReport(DocumentBuilders.Invoice(evil, Prop), At, "T"), new RenderContext("HPE", Prop, ReportFormat.Csv)).Bytes);
        Assert.DoesNotMatch(new Regex("(^|,)\"?=HYPERLINK", RegexOptions.Multiline), csv);
    }

    [Fact]
    public void Document_format_parsing_and_filename_safety()
    {
        Assert.Equal(ReportFormat.Print, DocumentService.ParseFormat(null));
        Assert.Equal(ReportFormat.Pdf, DocumentService.ParseFormat("PDF"));
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => DocumentService.ParseFormat("xlsx"));
    }
}
