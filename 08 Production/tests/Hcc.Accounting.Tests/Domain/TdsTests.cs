using Hcc.Application.Accounting;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Accounting.Tests.Domain;

/// <summary>
/// ACC-2 · lane acc2-tds (contract §4 A9, §5; rulebook C9 / A9 / T7a / T7b; DEC-146): the dated TDS table (same seed as the prototype's TDS_SEED),
/// TDS on a bill with the period threshold (TdsCalculator.ForBill — the purchase lane's entry point), the company payment net of TDS (Dr 1360),
/// the deductee report allocation and the remittance outstanding. Figures mirror 03 Prototype/tests/stream-accounting-tds.mjs.
/// </summary>
public sealed class TdsTests
{
    private static readonly DateOnly D = new(2026, 10, 24);

    private static long R(long rupees) => rupees * 100;

    private static readonly TdsVendor Contractor = new("Shiv Civil Works (fictional)", true, "contractor");

    [Fact]
    public void Seed_rows_thresholds_rates_and_ca_note()
    {
        Assert.Equal(["rent", "contractor", "professional", "commission"], TdsRates.Sections.Select(s => s.Id));
        Assert.All(TdsRates.Sections, s =>
        {
            Assert.Equal("CA to confirm", s.Note);
            Assert.StartsWith("s.393", s.Section, StringComparison.Ordinal);
            Assert.Contains("LAW S-13", s.Source, StringComparison.Ordinal);
            foreach (var f in new[] { "threshold", "bpsIndividual", "bpsOther" }) Assert.True(TdsRates.IsKey($"tds.{s.Id}.{f}"), s.Id + f);
        });
        Assert.Equal(15, TdsRates.Keys.Count);
        long V(string k) => TdsRates.ValueOn(k, D);
        Assert.Equal((R(50_000), 1000L, 200L), (V("tds.rent.threshold"), V("tds.rent.bpsOther"), V("tds.rent.bpsPlant")));
        Assert.Equal((R(30_000), R(1_00_000), 100L, 200L), (V("tds.contractor.threshold"), V("tds.contractor.thresholdYear"), V("tds.contractor.bpsIndividual"), V("tds.contractor.bpsOther")));
        Assert.Equal((R(50_000), 1000L, 200L), (V("tds.professional.threshold"), V("tds.professional.bpsOther"), V("tds.professional.bpsTechnical")));
        Assert.Equal((R(20_000), 200L), (V("tds.commission.threshold"), V("tds.commission.bpsOther")));
        Assert.Equal(1000L, TdsRates.ValueOn("tds.rent.bpsOther", new DateOnly(2025, 6, 1))); // the first version covers earlier dates
        Assert.Equal([1000, 200, 200, 200, 200, 0, 0],
            new[] { "rent", "rent:plant", "contractor", "professional:technical", "commission", "rent:x", "tour" }.Select(c => TdsRates.RateBps(c, D, TdsColumn.Other)));
        Assert.Equal(100, TdsRates.RateBps("contractor", D, TdsColumn.Individual));
        Assert.Equal(("1360", "2150"), (ChartOfAccounts.DefaultMapping[TdsCalculator.ReceivableKey], ChartOfAccounts.DefaultMapping[TdsCalculator.PayableKey]));
    }

    [Fact]
    public void Owner_overrides_are_dated_and_validated()
    {
        IReadOnlyList<TdsRateVersion> o = [new("tds.contractor.bpsOther", new DateOnly(2026, 10, 25), 150), new("tds.contractor.bpsOther", new DateOnly(2026, 11, 1), 175)];
        Assert.Equal((200L, 150L, 175L), (TdsRates.ValueOn("tds.contractor.bpsOther", D, o), TdsRates.ValueOn("tds.contractor.bpsOther", D.AddDays(1), o),
            TdsRates.ValueOn("tds.contractor.bpsOther", new DateOnly(2027, 1, 1), o)));
        Assert.Throws<HccException>(() => TdsRates.ValidateValue("tds.contractor.bpsOther", 4000));
        Assert.Throws<HccException>(() => TdsRates.ValidateValue("tds.nope", 1));
        TdsRates.ValidateValue("tds.rent.threshold", R(60_000));
    }

    [Fact]
    public void For_bill_excludes_gst_tests_the_single_bill_and_credits_the_vendor_net()
    {
        // ₹59,000 with ₹9,000 GST → taxable ₹50,000 > ₹30,000 single → 2 % = ₹1,000; vendor credited ₹58,000.
        var r = TdsCalculator.ForBill(new TdsBill(R(59_000), R(9_000), D), Contractor, []);
        Assert.Equal((R(1_000), "contractor", R(50_000), R(50_000), 200), (r.Paise, r.Section, r.TaxablePaise, r.BasePaise, r.Bps));
        Assert.Equal(R(58_000), R(59_000) - r.Paise);
        Assert.Equal(0, TdsCalculator.ForBill(new TdsBill(R(20_000), 0, D), Contractor, []).Paise);
        Assert.Equal(30_001, TdsCalculator.ForBill(new TdsBill(3_000_050, 0, D), Contractor, [], new TdsSettings(true, TdsColumn.Individual)).Paise); // half away from zero
        Assert.Equal(0, TdsCalculator.ForBill(new TdsBill(R(59_000), 0, D), Contractor, [], new TdsSettings(false, TdsColumn.Other)).Paise);
        Assert.Equal(0, TdsCalculator.ForBill(new TdsBill(R(59_000), 0, D), Contractor with { Tds = false }, []).Paise);
        Assert.Equal(0, TdsCalculator.ForBill(new TdsBill(R(59_000), 0, D), Contractor with { Section = null }, []).Paise);
        Assert.Equal(0, TdsCalculator.ForBill(new TdsBill(R(59_000), 0, D), null, []).Paise);
    }

    [Fact]
    public void Threshold_per_financial_year_carries_the_earlier_untaxed_bills_once()
    {
        TdsPriorBill B(string d, long rupees, long tds = 0) => new(DateOnly.Parse(d, System.Globalization.CultureInfo.InvariantCulture), R(rupees), 0, tds);
        List<TdsPriorBill> prior = [B("2026-03-31", 90_000), B("2026-05-10", 25_000), B("2026-07-10", 28_000), B("2026-09-10", 29_000)];
        var cross = TdsCalculator.ForBill(new TdsBill(R(20_000), 0, D), Contractor, prior);
        Assert.Equal((R(2_040), R(1_02_000)), (cross.Paise, cross.BasePaise)); // 2 % of (₹20,000 + ₹82,000); FY 2025-26 bill ignored
        Assert.Contains("earlier bills without TDS", cross.Reason, StringComparison.Ordinal);
        prior.Add(B("2026-10-24", 20_000, cross.Paise));
        Assert.Equal(R(100), TdsCalculator.ForBill(new TdsBill(R(5_000), 0, D), Contractor, prior).Paise); // after the crossing: this bill only

        var landlord = new TdsVendor("Godown Owner (fictional)", true, "rent");
        Assert.Equal(0, TdsCalculator.ForBill(new TdsBill(R(50_000), 0, D), landlord, []).Paise);
        Assert.Equal(R(6_000), TdsCalculator.ForBill(new TdsBill(R(30_000), 0, D), landlord, [B("2026-10-02", 30_000)]).Paise);  // ₹60,000 in October
        Assert.Equal(0, TdsCalculator.ForBill(new TdsBill(R(30_000), 0, new DateOnly(2026, 11, 2)), landlord, [B("2026-10-02", 30_000)]).Paise); // November starts again
        Assert.Equal(R(1_200), TdsCalculator.ForBill(new TdsBill(R(60_000), 0, D), landlord with { Section = "rent:plant" }, []).Paise);
        var ca = new TdsVendor("Audit Partners (fictional)", true, "professional");
        Assert.Equal(0, TdsCalculator.ForBill(new TdsBill(R(11_800), R(1_800), D), ca, [B("2026-06-01", 40_000)]).Paise);
        Assert.Equal(500_010, TdsCalculator.ForBill(new TdsBill(R(10_001), 0, D), ca, [B("2026-06-01", 40_000)]).Paise);
    }

    [Fact]
    public void A9_rulebook_figure_deccan_agro_60000_taxable_gives_6000_tds()
    {
        Assert.Equal((R(6_000), 1000, R(50_000)), TdsCalculator.CompanyExpected(R(60_000), new DateOnly(2026, 4, 30)));
        Assert.Equal(0, TdsCalculator.CompanyExpected(R(40_000), new DateOnly(2026, 4, 10)).Paise);
        Assert.Equal(R(60_000), TdsCalculator.Taxable(R(63_000), R(3_000)));
        Assert.Null(TdsCalculator.ReceivableProblem(R(57_000), R(6_000)));
        Assert.NotNull(TdsCalculator.ReceivableProblem(R(57_000), R(20_000)));
        Assert.NotNull(TdsCalculator.ReceivableProblem(R(57_000), -1));
    }

    [Fact]
    public void Company_payment_net_of_tds_books_dr_tender_dr_1360_cr_1110_company_gross()
    {
        var e = new CompanyPaymentRecordedEvent(7, 31, new DateOnly(2026, 4, 30), "Bank transfer", R(57_000), "NEFT-DEMO-0430", "Deccan Agro (fictional)", R(6_000));
        var source = AccountingEventMapper.CompanyPayment(e);
        Assert.Equal(("company-payment:31", "receipt", "company-payment", VoucherType.Receipt), (source.SourceKey, source.Kind, source.Sub, source.Type));
        var v = Assert.Single(VoucherBuilder.Build(e.BusinessDate, [source], ChartOfAccounts.Effective(null)));
        Assert.Equal([("1030", R(57_000), 0L), ("1360", R(6_000), 0L), ("1110/CMP-7", 0L, R(63_000))], v.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        Assert.EndsWith("· TDS ₹6000.00 deducted", v.Narration, StringComparison.Ordinal);
        Assert.Empty(v.Unmapped);
        // Without TDS (older payloads too): the ACC-1 two-line voucher.
        var old = VoucherBuilder.Build(e.BusinessDate, [AccountingEventMapper.CompanyPayment(e with { TdsPaise = 0 })], ChartOfAccounts.Effective(null)).Single();
        Assert.Equal([("1030", R(57_000), 0L), ("1110/CMP-7", 0L, R(57_000))], old.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
    }

    [Fact]
    public void Deductee_report_allocates_remittances_per_section_and_month_and_outstanding_guards_the_remit()
    {
        var oct = new DateOnly(2026, 10, 5);
        List<TdsBillRow> bills =
        [
            new(1, oct, "Shiv Civil Works (fictional)", "AAAPS1234C", "contractor", R(50_000), R(1_000)),
            new(2, new DateOnly(2026, 9, 5), "Shiv Civil Works (fictional)", "AAAPS1234C", "contractor", R(40_000), R(800)),
            new(3, new DateOnly(2026, 10, 1), "Godown Owner (fictional)", "AAAPS1234C", "rent", R(60_000), R(6_000)),
        ];
        var lines = TdsReports.Deductees(bills, [new TdsRemitRow("rent", "2026-10", R(6_000))], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal([("Godown Owner (fictional)", "rent", 1, R(60_000), R(6_000), R(6_000), 0L), ("Shiv Civil Works (fictional)", "contractor", 1, R(50_000), R(1_000), 0L, R(1_000))],
            lines.Select(l => (l.Vendor, l.Section, l.Bills, l.TaxablePaise, l.TdsPaise, l.RemittedPaise, l.PendingPaise)));
        var fy = TdsReports.Deductees(bills, [], new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)).Single(l => l.Section == "contractor");
        Assert.Equal((2, R(90_000), R(1_800)), (fy.Bills, fy.TaxablePaise, fy.TdsPaise));
        List<TdsRemitRecord> remits = [new(1, "2026-10", "contractor", R(400), "Bank transfer", "CIN-1", D, null)];
        Assert.Equal(R(600), TdsService.Outstanding(bills, remits, "contractor", "2026-10"));
        Assert.Equal(0, TdsService.Outstanding(bills, remits, "contractor", "2026-08"));
        Assert.Equal("TDS remittance · contractor (s.393(1), old 194C) · 2026-10 · challan CIN-1", TdsService.Narration("contractor", "2026-10", "CIN-1"));
        Assert.Equal(("FY 2026-27", "2026-04", new DateOnly(2026, 10, 31)), (TdsService.Period("2026-27", D).Label, TdsService.Period("2026-04", D).Label, TdsService.Period("2026-10", D).To));
        Assert.Equal("FY 2026-27", TdsService.Period(null, D).Label);
    }
}
