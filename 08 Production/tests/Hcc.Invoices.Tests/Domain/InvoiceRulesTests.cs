using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Invoices;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Invoices.Tests.Domain;

/// <summary>Pure invoice / credit-note rules with the prototype figures (no database).</summary>
public class InvoiceRulesTests
{
    public const string HotelGstin = "27AAAAA0000A1Z2";   // fictional, valid checksum (the prototype placeholder 27AAAAA0000A1Z5 is not)

    private static Paise P(long v) => new(v);

    private static SourceLine Line(long id, string kind, GstCategory cat, long amount, int bps, string label = "x") =>
        new(id, kind, new DateOnly(2026, 10, 22), label, cat, P(amount), bps, MoneyMath.TaxOf(P(amount), bps), null, null);

    /// <summary>Prototype AC-006 (stream-c.mjs): rooms 2 × ₹2,000 at 5 %, laundry ₹500 at 18 %, ₹500 discount at the room rate → ₹4,265.</summary>
    public static IReadOnlyList<SourceLine> Ac006() =>
    [
        Line(1, "room", GstCategory.Room, 200000, 500, "Room night · 2026-10-22"),
        Line(2, "room", GstCategory.Room, 200000, 500, "Room night · 2026-10-23"),
        Line(3, "service", GstCategory.Laundry, 50000, 1800, "Laundry service"),
        Line(4, "discount", GstCategory.Room, -50000, 500, "Discount · Service delay goodwill"),
    ];

    [Fact]
    public void Golden_AC006_invoice_totals_split_and_words()
    {
        var lines = InvoiceRules.BuildLines(Ac006(), interState: false);
        var t = InvoiceRules.Totals(lines);
        Assert.Equal((400000L, 13250L, 13250L, 0L, 426500L), (t.Taxable.Value, t.Cgst.Value, t.Sgst.Value, t.Igst.Value, t.Total.Value));
        Assert.Equal("Rupees Four Thousand Two Hundred Sixty Five Only", InvoiceRules.Words(t.Total));
        Assert.Equal(["996311", "996311", "999712", "996311"], lines.Select(l => l.Sac));
        Assert.Equal(InvoiceDocType.TaxInvoice, InvoiceRules.DocTypeFor(lines));
        Assert.Equal((-1250L, -1250L), (lines[3].Cgst.Value, lines[3].Sgst.Value));
    }

    [Fact]
    public void Inter_state_puts_the_whole_line_tax_in_IGST_and_odd_paisa_goes_to_CGST_intra_state()
    {
        var t = InvoiceRules.Totals(InvoiceRules.BuildLines(Ac006(), interState: true));
        Assert.Equal((0L, 0L, 26500L, 426500L), (t.Cgst.Value, t.Sgst.Value, t.Igst.Value, t.Total.Value));
        var odd = InvoiceRules.BuildLines([Line(9, "service", GstCategory.Food, 1010, 500)], false)[0]; // 50.5 → 51 paise tax
        Assert.Equal((51L, 26L, 25L), (odd.Tax.Value, odd.Cgst.Value, odd.Sgst.Value));
    }

    [Fact]
    public void Stored_tax_that_disagrees_with_its_rate_is_a_data_fault_and_zero_rated_is_a_bill_of_supply()
    {
        var bad = new SourceLine(1, "room", new DateOnly(2026, 10, 22), "x", GstCategory.Room, P(200000), 500, P(9999), null, null);
        Assert.Throws<InvalidOperationException>(() => InvoiceRules.BuildLines([bad], false));
        Assert.Equal(InvoiceDocType.BillOfSupply, InvoiceRules.DocTypeFor(InvoiceRules.BuildLines([Line(1, "service", GstCategory.Other, 10000, 0)], false)));
    }

    [Theory]
    [InlineData("27AAPFU0939F1ZV", true)]
    [InlineData("29AAGCB7383J1Z4", true)]
    [InlineData(HotelGstin, true)]
    [InlineData("27AAAAA0000A1Z5", false)]   // checksum
    [InlineData("99AAAAA0000A1Z5", false)]   // state code
    [InlineData("27AAAAA0000A1X2", false)]   // 14th character must be Z
    [InlineData("27aapfu0939f1zv", false)]   // shape is upper case (Validate upper-cases input)
    public void Gstin_format_state_and_checksum(string gstin, bool valid)
    {
        Assert.Equal(valid, Gstin.IsValid(gstin));
        if (!valid && gstin != "27aapfu0939f1zv") Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => Gstin.Validate(gstin, "recipient")).Code);
        Assert.Equal("27AAPFU0939F1ZV", Gstin.Validate(" 27aapfu0939f1zv ", "recipient"));
    }

    [Fact]
    public void Numbering_is_16_characters_per_financial_year()
    {
        Assert.Equal("2627", InvoiceNumbering.FyCode(new DateOnly(2026, 10, 24)));
        Assert.Equal("2526", InvoiceNumbering.FyCode(new DateOnly(2026, 3, 31)));
        Assert.Equal("2627", InvoiceNumbering.FyCode(new DateOnly(2026, 4, 1)));
        Assert.Equal("INV/2627/0000001", InvoiceNumbering.Format("INV", "2627", 1));
        Assert.Equal("CN/2627/00000012", InvoiceNumbering.Format("CN", "2627", 12));
        Assert.Equal("HOTEL1/2627/0042", InvoiceNumbering.Format("HOTEL1", "2627", 42));
        Assert.All(new[] { InvoiceNumbering.Format("INV", "2627", 9_999_999), InvoiceNumbering.Format("CN", "2627", 1) }, n => Assert.Equal(16, n.Length));
        Assert.Throws<HccException>(() => InvoiceNumbering.Format("INV", "2627", 10_000_000));
        Assert.Throws<HccException>(() => InvoiceNumbering.ValidatePrefix("DEMO-INV")); // 8 chars: allowed by Master configuration, too long for 16
        Assert.Equal("DEMO", InvoiceNumbering.ValidatePrefix("demo"));
    }

    [Fact]
    public void Recipient_rules_B2B_needs_valid_GSTIN_and_name_B2C_large_needs_name_and_address()
    {
        var b2b = InvoiceRules.Recipient("B2B", "Demo Industries Pvt Ltd (fictional)", "29AAGCB7383J1Z4", null, null, "27", P(426500));
        Assert.Equal((SupplyType.B2B, "27", false), (b2b.Type, b2b.PlaceOfSupply, b2b.InterState)); // hotel supply: place of supply = hotel state
        Assert.True(InvoiceRules.Recipient("B2B", "X", "29AAGCB7383J1Z4", null, "29", "27", P(1)).InterState);
        Assert.Throws<HccException>(() => InvoiceRules.Recipient("B2B", null, "29AAGCB7383J1Z4", null, null, "27", P(1)));
        Assert.Throws<HccException>(() => InvoiceRules.Recipient("B2B", "X", "29AAGCB7383J1Z5", null, null, "27", P(1)));
        Assert.Throws<HccException>(() => InvoiceRules.Recipient("B2C", "X", "29AAGCB7383J1Z4", null, null, "27", P(1)));
        Assert.Throws<HccException>(() => InvoiceRules.Recipient("B2C", "Guest", null, null, null, "27", Paise.FromRupees(50_000)));
        Assert.NotNull(InvoiceRules.Recipient("B2C", "Guest", null, "Latur", null, "27", Paise.FromRupees(50_000)));
        Assert.Throws<HccException>(() => InvoiceRules.Recipient("B2C", null, null, null, "28", "27", P(1))); // 28 is not a state code
    }

    private static IReadOnlyList<CreditableLine> Fresh(IEnumerable<InvoiceLine> lines) => lines.Select(l => new CreditableLine(l, Paise.Zero, Paise.Zero, Paise.Zero, Paise.Zero)).ToList();

    /// <summary>The invoice lines with every credit-note line issued so far added up per invoice line (as <c>CreditedAsync</c> reads them).</summary>
    private static IReadOnlyList<CreditableLine> After(IEnumerable<InvoiceLine> lines, params IEnumerable<CreditLine>[] notes) => lines.Select(l =>
    {
        var c = notes.SelectMany(n => n).Where(x => x.InvoiceLineNo == l.LineNo).ToList();
        Paise Sum(Func<CreditLine, Paise> f) => c.Aggregate(Paise.Zero, (a, x) => a + f(x));
        return new CreditableLine(l, Sum(x => x.Taxable), Sum(x => x.Tax), Sum(x => x.Cgst), Sum(x => x.Sgst));
    }).ToList();

    [Fact]
    public void Golden_credit_note_100_rupees_on_a_room_only_stay_is_105_rupees()
    {
        // stream-c.mjs: "credit note ₹100 + 5 % room-rate GST = −₹105".
        var inv = InvoiceRules.BuildLines([Line(1, "room", GstCategory.Room, 200000, 500), Line(2, "room", GstCategory.Room, 200000, 500)], false);
        var byLines = CreditNoteRules.Plan(Fresh(inv), CreditMode.Lines, null, [new RequestedCreditLine(1, P(10000))], false);
        Assert.Equal(10500L, CreditNoteRules.Totals(byLines).Total.Value);
        var byAmount = CreditNoteRules.Plan(Fresh(inv), CreditMode.Amount, P(10000), null, false);
        Assert.Equal((10000L, 500L, 10500L), (CreditNoteRules.Totals(byAmount).Taxable.Value, CreditNoteRules.Totals(byAmount).Tax.Value, CreditNoteRules.Totals(byAmount).Total.Value));
        Assert.Equal([5000L, 5000L], byAmount.Select(l => l.Taxable.Value));
        Assert.Equal((250L, 125L, 125L), (byAmount[0].Tax.Value, byAmount[0].Cgst.Value, byAmount[0].Sgst.Value));
    }

    [Fact]
    public void Amount_mode_spreads_in_proportion_with_largest_remainder_and_reverses_tax_at_each_rate()
    {
        var inv = InvoiceRules.BuildLines(Ac006(), false);
        var plan = CreditNoteRules.Plan(Fresh(inv), CreditMode.Amount, P(10000), null, false);
        // Weights 2,00,000 / 2,00,000 / 50,000 (the discount line is not spread over): 4444.44 / 4444.44 / 1111.11 → 4445 / 4444 / 1111.
        Assert.Equal([(1, 4445L, 222L), (2, 4444L, 222L), (3, 1111L, 200L)], plan.Select(l => (l.InvoiceLineNo, l.Taxable.Value, l.Tax.Value)));
        Assert.Equal(10644L, CreditNoteRules.Totals(plan).Total.Value);
    }

    [Fact]
    public void Full_credit_reverses_the_invoice_exactly_including_the_discount_line()
    {
        var inv = InvoiceRules.BuildLines(Ac006(), false);
        var plan = CreditNoteRules.Plan(Fresh(inv), CreditMode.Full, null, null, false);
        var t = CreditNoteRules.Totals(plan);
        Assert.Equal((400000L, 13250L, 13250L, 426500L), (t.Taxable.Value, t.Cgst.Value, t.Sgst.Value, t.Total.Value));
        Assert.Equal(-50000L, plan[3].Taxable.Value);
    }

    [Fact]
    public void Credit_limits_never_exceed_what_remains()
    {
        var inv = InvoiceRules.BuildLines(Ac006(), false);
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => CreditNoteRules.Plan(Fresh(inv), CreditMode.Amount, P(400001), null, false)).Code);
        Assert.NotEmpty(CreditNoteRules.Plan(Fresh(inv), CreditMode.Amount, P(400000), null, false));
        Assert.Throws<HccException>(() => CreditNoteRules.Plan(Fresh(inv), CreditMode.Lines, null, [new RequestedCreditLine(3, P(50001))], false));
        Assert.Throws<HccException>(() => CreditNoteRules.Plan(Fresh(inv), CreditMode.Lines, null, [new RequestedCreditLine(4, P(100))], false)); // discount line
        Assert.Throws<HccException>(() => CreditNoteRules.Plan(Fresh(inv), CreditMode.Lines, null, [new RequestedCreditLine(1, P(100)), new RequestedCreditLine(1, P(100))], false));
        Assert.Throws<HccException>(() => CreditNoteRules.Plan(Fresh(inv), CreditMode.Lines, null, [new RequestedCreditLine(7, P(100))], false));
        // Rooms + laundry lines total 4,50,000 but the invoice's net is 4,00,000: naming lines cannot credit more than the net.
        Assert.Throws<HccException>(() => CreditNoteRules.Plan(Fresh(inv), CreditMode.Lines, null,
            [new RequestedCreditLine(1, P(200000)), new RequestedCreditLine(2, P(200000)), new RequestedCreditLine(3, P(50000))], false));

        // After a ₹4,000 credit only the full mode (which also reverses the discount) has anything left: nothing net.
        var credited = inv.Select(l => l.LineNo switch
        {
            1 => new CreditableLine(l, P(200000), P(10000), P(5000), P(5000)),
            2 => new CreditableLine(l, P(200000), P(10000), P(5000), P(5000)),
            _ => new CreditableLine(l, Paise.Zero, Paise.Zero, Paise.Zero, Paise.Zero),
        }).ToList();
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => CreditNoteRules.Plan(credited, CreditMode.Amount, P(1), null, false)).Code);
    }

    [Fact]
    public void Rounding_never_credits_more_tax_than_was_charged()
    {
        // ₹0.10 at 5 % = 0.5 → 1 paisa. Crediting 5 paise (0.25 → 0) and then the remaining 5 paise credits exactly the 1 paisa left
        // (A03a-04: a whole remainder carries the tax still on the line, as pieces that each follow the per-line rule: −5 at 0 + 10 at 1).
        var inv = InvoiceRules.BuildLines([Line(1, "service", GstCategory.Food, 10, 500)], false);
        var first = CreditNoteRules.Plan(Fresh(inv), CreditMode.Lines, null, [new RequestedCreditLine(1, P(5))], false);
        Assert.Equal(0L, first[0].Tax.Value); // 0.25 → 0
        var second = CreditNoteRules.Plan(After(inv, first), CreditMode.Full, null, null, false);
        Assert.Equal((5L, 1L), (CreditNoteRules.Totals(second).Taxable.Value, CreditNoteRules.Totals(second).Tax.Value));
        Assert.All(second, l => Assert.Equal(MoneyMath.TaxOf(l.Taxable, 500), l.Tax));
        var inv2 = InvoiceRules.BuildLines([Line(1, "service", GstCategory.Food, 40, 500)], false); // tax 2
        var part = CreditNoteRules.Plan(Fresh(inv2), CreditMode.Lines, null, [new RequestedCreditLine(1, P(10))], false);   // 0.5 → 1
        var ok = CreditNoteRules.Plan(After(inv2, part), CreditMode.Lines, null, [new RequestedCreditLine(1, P(10))], false); // 0.5 → 1, total 2
        Assert.Equal(1L, ok[0].Tax.Value);
        // 10 more paise (not the whole remainder) → 1 more paisa > 2 charged: refused.
        Assert.Throws<HccException>(() => CreditNoteRules.Plan(After(inv2, part, ok), CreditMode.Lines, null, [new RequestedCreditLine(1, P(10))], false));
        // The remaining 20 paise carry no tax: the full mode credits them at 0 (8 + 6 + 6 paise, each 0 by the per-line rule), never refused.
        var rest = CreditNoteRules.Plan(After(inv2, part, ok), CreditMode.Full, null, null, false);
        Assert.Equal((20L, 0L), (CreditNoteRules.Totals(rest).Taxable.Value, CreditNoteRules.Totals(rest).Tax.Value));
    }

    [Fact]
    public void A03a_04_full_credit_after_a_half_paisa_partial_credits_exactly_the_rest_of_the_invoice()
    {
        // Probe V1_creditnote_halfpaisa.mjs: a ₹1,000 room line at 5 % (tax ₹50.00 = CGST 25.00 + SGST 25.00). A partial credit of ₹333.30 carries
        // 1666.5 → 1667 paise of tax. The rest, ₹666.70, would carry 3333.5 → 3334 on its own (5001 in all: refused before the fix).
        var inv = InvoiceRules.BuildLines([Line(1, "room", GstCategory.Room, 100000, 500)], false);
        Assert.Equal((5000L, 2500L, 2500L), (inv[0].Tax.Value, inv[0].Cgst.Value, inv[0].Sgst.Value));
        var partial = CreditNoteRules.Plan(Fresh(inv), CreditMode.Lines, null, [new RequestedCreditLine(1, P(33330))], false);
        Assert.Equal((33330L, 1667L, 834L, 833L), (partial[0].Taxable.Value, partial[0].Tax.Value, partial[0].Cgst.Value, partial[0].Sgst.Value));

        foreach (var (mode, amount, lines) in new (CreditMode, Paise?, IReadOnlyList<RequestedCreditLine>?)[]
                 { (CreditMode.Full, null, null), (CreditMode.Amount, P(66670), null), (CreditMode.Lines, null, [new RequestedCreditLine(1, P(66670))]) })
        {
            var rest = CreditNoteRules.Plan(After(inv, partial), mode, amount, lines, false);
            // 666.69 at 3333 (3333.45) + 0.01 at 0: each line follows the per-line rule; together exactly the ₹666.70 and ₹33.33 left.
            Assert.Equal([(66669L, 3333L), (1L, 0L)], rest.Select(l => (l.Taxable.Value, l.Tax.Value)));
            var t = CreditNoteRules.Totals(rest);
            Assert.Equal((66670L, 1666L, 1667L, 70003L), (t.Taxable.Value, t.Cgst.Value, t.Sgst.Value, t.Total.Value));
            var all = After(inv, partial, rest).Single();
            Assert.Equal((100000L, 5000L, 2500L, 2500L), (all.CreditedTaxable.Value, all.CreditedTax.Value, all.CreditedCgst.Value, all.CreditedSgst.Value));
            Assert.Equal(105000L, (CreditNoteRules.Totals(partial).Total + t.Total).Value); // the invoice's ₹1,050.00 exactly
            Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => CreditNoteRules.Plan(After(inv, partial, rest), CreditMode.Full, null, null, false)).Code);
        }

        // The audit paper's figures: ₹2,000 at 5 % (₹100), ₹100.10 credited (500.5 → 501), then the line's remaining ₹1,899.90 by naming it.
        var inv2 = InvoiceRules.BuildLines([Line(1, "room", GstCategory.Room, 200000, 500)], false);
        var p2 = CreditNoteRules.Plan(Fresh(inv2), CreditMode.Lines, null, [new RequestedCreditLine(1, P(10010))], false);
        Assert.Equal(501L, p2[0].Tax.Value);
        var r2 = CreditNoteRules.Plan(After(inv2, p2), CreditMode.Lines, null, [new RequestedCreditLine(1, P(189990))], false);
        Assert.Equal((189990L, 9499L), (CreditNoteRules.Totals(r2).Taxable.Value, CreditNoteRules.Totals(r2).Tax.Value));
        var end = After(inv2, p2, r2)[0];
        Assert.Equal((10000L, 5000L, 5000L), (end.CreditedTax.Value, end.CreditedCgst.Value, end.CreditedSgst.Value));
    }

    [Fact]
    public void A03a_04_any_run_of_partial_credits_then_full_ends_at_exactly_the_invoice_tax_CGST_and_SGST()
    {
        var rnd = new Random(20261026);
        for (var run = 0; run < 400; run++)
        {
            var src = Enumerable.Range(1, rnd.Next(1, 4)).Select(i =>
            {
                var (cat, bps) = rnd.Next(3) switch { 0 => (GstCategory.Room, 500), 1 => (GstCategory.Room, 1800), _ => (GstCategory.Laundry, 1800) };
                return Line(i, "room", cat, rnd.Next(1, 300_000), bps);
            }).ToList();
            var inter = run % 10 == 9;
            var inv = InvoiceRules.BuildLines(src, false);
            if (inter) inv = inv.Select(l => l with { Cgst = Paise.Zero, Sgst = Paise.Zero, Igst = l.Tax }).ToList();
            var notes = new List<IReadOnlyList<CreditLine>>();
            for (var k = rnd.Next(0, 6); k > 0; k--)
            {
                var now = After(inv, [.. notes]);
                try
                {
                    notes.Add(rnd.Next(2) == 0
                        ? CreditNoteRules.Plan(now, CreditMode.Amount, P(rnd.Next(1, (int)Math.Max(2, CreditNoteRules.RemainingTaxable(now).Value / 3))), null, inter)
                        : CreditNoteRules.Plan(now, CreditMode.Lines, null, [new RequestedCreditLine(1, P(rnd.Next(1, 40)))], inter));
                }
                catch (HccException)
                {
                    // A partial credit that would over-credit tax, or more than is left: refused; the run goes on.
                }
            }

            if (CreditNoteRules.RemainingTaxable(After(inv, [.. notes])) > Paise.Zero)
                notes.Add(CreditNoteRules.Plan(After(inv, [.. notes]), CreditMode.Full, null, null, inter));
            foreach (var c in After(inv, [.. notes]))
                Assert.Equal((c.Line.Taxable, c.Line.Tax, c.Line.Cgst, c.Line.Sgst), (c.CreditedTaxable, c.CreditedTax, c.CreditedCgst, c.CreditedSgst));
            Assert.All(notes.SelectMany(n => n), l =>
            {
                Assert.Equal(MoneyMath.TaxOf(l.Taxable, l.Bps), l.Tax);
                Assert.Equal(l.Tax, l.Cgst + l.Sgst + l.Igst);
                Assert.True(Math.Abs((l.Cgst - l.Sgst).Value) <= 1);
            });
        }
    }

    [Fact]
    public void Gstr1_summary_by_section_rate_and_sac_with_credit_notes()
    {
        var lines = InvoiceRules.BuildLines(Ac006(), false);
        var b2c = new Gstr1Invoice("INV/2627/0000001", InvoiceDocType.TaxInvoice, SupplyType.B2C, false, P(426500), lines);
        var b2b = new Gstr1Invoice("INV/2627/0000002", InvoiceDocType.TaxInvoice, SupplyType.B2B, false, P(420000),
            InvoiceRules.BuildLines([Line(1, "room", GstCategory.Room, 200000, 500), Line(2, "room", GstCategory.Room, 200000, 500)], false));
        var cnLines = CreditNoteRules.Plan(Fresh(b2b.Lines), CreditMode.Lines, null, [new RequestedCreditLine(1, P(10000))], false);
        var cnB2c = CreditNoteRules.Plan(Fresh(lines), CreditMode.Lines, null, [new RequestedCreditLine(3, P(10000))], false);
        var r = Gstr1Summary.Build([b2c, b2b],
            [new Gstr1CreditNote("CN/2627/00000001", SupplyType.B2B, false, P(420000), cnLines), new Gstr1CreditNote("CN/2627/00000002", SupplyType.B2C, false, P(426500), cnB2c)]);
        Assert.Equal([("B2B", 500, 400000L, 1), ("B2CS", 500, 350000L, 1), ("B2CS", 1800, 40000L, 2), ("CDNR", 500, 10000L, 1)],
            r.Rates.Select(x => (x.Section, x.Bps, x.Taxable.Value, x.Documents)));
        Assert.Equal([("996311", 500, 740000L), ("999712", 1800, 40000L)], r.Sac.Select(x => (x.Sac, x.Bps, x.Taxable.Value)));
        Assert.Equal([("INV", "INV/2627/0000001", "INV/2627/0000002", 2), ("CRN", "CN/2627/00000001", "CN/2627/00000002", 2)],
            r.Documents.Select(d => (d.DocType, d.First, d.Last, d.Count)));
        Assert.Equal("B2CL", Gstr1Summary.SectionOf(SupplyType.B2C, true, Paise.FromRupees(100_001)));
        Assert.Equal("B2CS", Gstr1Summary.SectionOf(SupplyType.B2C, true, Paise.FromRupees(100_000)));
    }
}
