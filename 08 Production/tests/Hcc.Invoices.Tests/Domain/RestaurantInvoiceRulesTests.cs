using Hcc.Domain.Errors;
using Hcc.Domain.Invoices;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Invoices.Tests.Domain;

/// <summary>DEC-072 (audit A03a-01): the restaurant tax-invoice series RST and its place in the GSTR-1 working summary (pure rules).</summary>
public class RestaurantInvoiceRulesTests
{
    private static Paise P(long v) => new(v);

    /// <summary>One POS bill as its invoice line: the bill net at the bill's rate, tax and stored split (migration 1201).</summary>
    private static IReadOnlyList<InvoiceLine> Bill(long billId, long net, int bps = 500)
    {
        var tax = MoneyMath.TaxOf(P(net), bps);
        var split = GstMath.Split(tax);
        return InvoiceRules.BuildLines([new SourceLine(billId, "restaurant", new DateOnly(2026, 10, 24), "Restaurant service · Restaurant · Table T3", GstCategory.Food,
            P(net), bps, tax, split.Cgst, split.Sgst)], interState: false);
    }

    [Fact]
    public void RST_series_is_16_characters_restarts_each_1_April_and_overflows_with_a_refusal_never_a_wrap()
    {
        Assert.Equal(("RST", "RST", "Tax invoice"), (InvoiceNumbering.Code(InvoiceDocType.RestaurantInvoice), InvoiceNumbering.DefaultPrefix(InvoiceDocType.RestaurantInvoice),
            InvoiceNumbering.Label(InvoiceDocType.RestaurantInvoice)));
        Assert.Equal(InvoiceDocType.RestaurantInvoice, InvoiceNumbering.Parse("RST"));
        // 31 Mar 2027 is the last day of FY 2026-27; 1 Apr 2027 opens FY 2027-28 and its own series from 1.
        Assert.Equal("RST/2627/0000001", InvoiceNumbering.Format("RST", InvoiceNumbering.FyCode(new DateOnly(2026, 4, 1)), 1));
        Assert.Equal("RST/2627/0000412", InvoiceNumbering.Format("RST", InvoiceNumbering.FyCode(new DateOnly(2027, 3, 31)), 412));
        Assert.Equal("RST/2728/0000001", InvoiceNumbering.Format("RST", InvoiceNumbering.FyCode(new DateOnly(2027, 4, 1)), 1));
        var last = InvoiceNumbering.Format("RST", "2627", 9_999_999);
        Assert.Equal(("RST/2627/9999999", 16), (last, last.Length));
        var full = Assert.Throws<HccException>(() => InvoiceNumbering.Format("RST", "2627", 10_000_000));
        Assert.Contains("is full", full.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RST")]
    [InlineData("rst")]
    [InlineData("CN")]
    [InlineData("BOS")]
    public void The_room_invoice_prefix_may_not_reuse_a_fixed_series_prefix(string prefix)
    {
        // With invoicePrefix = RST the INV series would print RST/2627/0000001 too — the same number as the first restaurant invoice.
        var e = Assert.Throws<HccException>(() => InvoiceNumbering.ValidatePrefix(prefix));
        Assert.Contains("reserved", e.Message, StringComparison.Ordinal);
        Assert.Equal("INV", InvoiceNumbering.ValidatePrefix("inv"));
    }

    [Fact]
    public void A_bill_line_keeps_the_bills_stored_tax_and_split_and_prints_SAC_996331()
    {
        // Paneer ₹380 + 3 naan ₹210 = ₹590 net; 5 % = 2950 paise; SGST = 2950 / 2 toward zero = 1475, CGST = the rest = 1475.
        var l = Assert.Single(Bill(7, 59000));
        Assert.Equal(("996331", GstCategory.Food, 59000L, 500, 2950L, 1475L, 1475L, 0L),
            (l.Sac, l.Category, l.Taxable.Value, l.Bps, l.Tax.Value, l.Cgst.Value, l.Sgst.Value, l.Igst.Value));
        // A split stored on the bill that disagrees with its tax is a data fault, never silently re-split.
        var bad = new SourceLine(8, "restaurant", new DateOnly(2026, 10, 24), "x", GstCategory.Food, P(59000), 500, P(2950), P(1500), P(1475));
        Assert.Throws<InvalidOperationException>(() => InvoiceRules.BuildLines([bad], false));
    }

    [Fact]
    public void Gstr1_puts_RST_invoices_in_B2CS_or_B2B_like_INV_and_lists_the_RST_series_with_its_cancelled_count()
    {
        var folio = new Gstr1Invoice("INV/2627/0000001", InvoiceDocType.TaxInvoice, SupplyType.B2C, false, P(426500), InvoiceRules.BuildLines(InvoiceRulesTests.Ac006(), false));
        var walkIn = new Gstr1Invoice("RST/2627/0000001", InvoiceDocType.RestaurantInvoice, SupplyType.B2C, false, P(37800), Bill(1, 36000));     // dal + 2 roti
        var company = new Gstr1Invoice("RST/2627/0000002", InvoiceDocType.RestaurantInvoice, SupplyType.B2B, false, P(88200), Bill(2, 84000));    // 2 thali, GSTIN
        var voided = new Gstr1Invoice("RST/2627/0000003", InvoiceDocType.RestaurantInvoice, SupplyType.B2C, false, P(61950), Bill(3, 59000), Cancelled: true);
        var reversal = CreditNoteRules.Plan(voided.Lines.Select(l => new CreditableLine(l, Paise.Zero, Paise.Zero, Paise.Zero, Paise.Zero)).ToList(), CreditMode.Full, null, [], false);
        var r = Gstr1Summary.Build([folio, walkIn, company, voided], [new Gstr1CreditNote("CN/2627/00000001", SupplyType.B2C, false, P(61950), reversal)]);

        // B2B 5 %: the company bill ₹840 → tax ₹42 = 2100 + 2100.
        // B2CS 5 %: rooms 2,00,000 + 2,00,000 − 50,000 (discount) = 3,50,000 + walk-in 36,000 + voided 59,000 − its credit note 59,000 = 3,86,000 paise;
        //           CGST 5,000 + 5,000 − 1,250 + 900 + 1,475 − 1,475 = 9,650; documents INV 1, RST 1, RST 3, CN 1 = 4.
        // B2CS 18 %: laundry 50,000, CGST 4,500.
        Assert.Equal([("B2B", 500, 84000L, 2100L, 2100L, 1), ("B2CS", 500, 386000L, 9650L, 9650L, 4), ("B2CS", 1800, 50000L, 4500L, 4500L, 1)],
            r.Rates.Select(x => (x.Section, x.Bps, x.Taxable.Value, x.Cgst.Value, x.Sgst.Value, x.Documents)));
        // SAC 996331 (restaurant service): 36,000 + 84,000 + 59,000 − 59,000 = 1,20,000 at 5 %.
        Assert.Equal([("996311", 500, 350000L), ("996331", 500, 120000L), ("999712", 1800, 50000L)], r.Sac.Select(x => (x.Sac, x.Bps, x.Taxable.Value)));
        // Table 13: one row per series in the order INV, RST, CRN; the voided bill's RST invoice is counted as cancelled (net issued 2).
        Assert.Equal([("INV", "INV/2627/0000001", "INV/2627/0000001", 1, 0), ("RST", "RST/2627/0000001", "RST/2627/0000003", 3, 1), ("CRN", "CN/2627/00000001", "CN/2627/00000001", 1, 0)],
            r.Documents.Select(d => (d.DocType, d.First, d.Last, d.Count, d.Cancelled)));
    }
}
