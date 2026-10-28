using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;

namespace Hcc.Accounting.Tests.Domain;

/// <summary>
/// ACC-2 · lane acc2-purchase — the purchase rules (02 Planning/agents/ACC2-POSTING-CONTRACT.md §3; rulebook C1, C3–C5, C8–C10; DEC-143, DEC-145)
/// as the prototype twin posts them (tests/stream-accounting-purchase.mjs): Purchase vouchers on a credit method, IGST to 1330, TDS held back,
/// the RCM second source, vendor payments / advances / voids, returns, the mapping keys, and the shared purchase rules.
/// </summary>
public sealed class PurchaseRulesTests
{
    private static readonly DateOnly D = new(2026, 10, 24);
    private static readonly IReadOnlyDictionary<string, string> Map = ChartOfAccounts.Effective(null);

    private static List<(string Account, long Debit, long Credit)> Lines(Voucher v) => v.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).ToList();

    [Fact]
    public void A_bill_on_vendor_credit_is_a_Purchase_voucher_numbered_PUR_and_a_bill_paid_at_once_stays_a_Payment()
    {
        var credit = AccountingEventMapper.Expense(new ExpensePostedEvent(5, D, "Supplies", "Vendor credit", 40_000, "Demo Traders", null), new PartyContext(Vendor: "V-DEMO"));
        var paid = AccountingEventMapper.Expense(new ExpensePostedEvent(6, D, "Supplies", "UPI", 10_000, "Demo Traders", null));
        var vs = VoucherBuilder.Build(D, [credit, paid], Map);
        var pur = vs.Single(v => v.SourceKey == "expense:5");
        Assert.Equal((VoucherType.Purchase, "PUR/20261024/001"), (pur.Type, pur.Number));
        Assert.Equal(new[] { ("5000", 40_000L, 0L), ("2200/V-DEMO", 0L, 40_000L) }, Lines(pur));
        Assert.Equal(VoucherType.Payment, vs.Single(v => v.SourceKey == "expense:6").Type);
        Assert.Equal("PMT/20261024/001", vs[0].Number); // Payment before Purchase, as the prototype orders the types
        // An event that says the funding is a credit method (an Owner-added credit method) is a Purchase too; Credit = false wins over the name.
        Assert.Equal(VoucherType.Purchase, AccountingEventMapper.Expense(new ExpensePostedEvent(7, D, "Supplies", "Supplier account", 1, "X", null, Credit: true)).Type);
        Assert.Equal(VoucherType.Payment, AccountingEventMapper.Expense(new ExpensePostedEvent(8, D, "Supplies", "Vendor credit", 1, "X", null, Credit: false)).Type);
    }

    [Fact]
    public void An_inter_state_bill_puts_the_whole_ITC_on_1330_and_TDS_held_back_credits_2150_with_the_vendor_net()
    {
        var inter = AccountingEventMapper.Expense(new ExpensePostedEvent(9, D, "Supplies", "Vendor credit", 59_000, "Kaveri", null, 9_000, "allowed", GstKind: "inter"));
        Assert.Equal(new[] { ("5000", 50_000L, 0L), ("1330", 9_000L, 0L), ("2200", 0L, 59_000L) }, Lines(VoucherBuilder.Build(D, [inter], Map).Single()));
        var intra = AccountingEventMapper.Expense(new ExpensePostedEvent(10, D, "Supplies", "Vendor credit", 118_000, "Deccan", null, 18_000, "allowed"));
        Assert.Equal(new[] { ("5000", 100_000L, 0L), ("1310", 9_000L, 0L), ("1320", 9_000L, 0L), ("2200", 0L, 118_000L) }, Lines(VoucherBuilder.Build(D, [intra], Map).Single()));
        var tds = AccountingEventMapper.Expense(new ExpensePostedEvent(11, D, "Supplies", "Vendor credit", 100_000, "Deccan", null, TdsPaise: 2_000, TdsSection: "contractor"));
        var v = VoucherBuilder.Build(D, [tds], Map).Single();
        Assert.Equal(new[] { ("5000", 100_000L, 0L), ("2150", 0L, 2_000L), ("2200", 0L, 98_000L) }, Lines(v));
        Assert.Contains("TDS contractor", v.Narration, StringComparison.Ordinal);
        // A reversal record mirrors every line (negative amounts swap the sides).
        var rev = AccountingEventMapper.Expense(new ExpensePostedEvent(12, D, "Supplies", "Vendor credit", -59_000, "Kaveri", 9, -9_000, "allowed", GstKind: "inter"));
        Assert.Equal(new[] { ("5000", 0L, 50_000L), ("1330", 0L, 9_000L), ("2200", 59_000L, 0L) }, Lines(VoucherBuilder.Build(D, [rev], Map).Single()));
    }

    [Fact]
    public void Reverse_charge_is_a_second_source_Dr_the_category_or_the_ITC_ledgers_Cr_2140_at_the_rate()
    {
        var r = AccountingEventMapper.Rcm(new ExpenseRcmPostedEvent(20, D, "Maintenance", "Shield Security", 4_000_000, 1800, false));
        Assert.Equal(("rcm:20", SourceKinds.Tax, "rcm", VoucherType.Journal), (r.SourceKey, r.Kind, r.Sub, r.Type));
        Assert.Equal(new[] { ("5020", 720_000L, 0L), ("2140", 0L, 720_000L) }, Lines(VoucherBuilder.Build(D, [r], Map).Single()));
        var itc = AccountingEventMapper.Rcm(new ExpenseRcmPostedEvent(21, D, "Maintenance", "Shield Security", 100_000, 500, true));
        Assert.Equal(new[] { ("1310", 2_500L, 0L), ("1320", 2_500L, 0L), ("2140", 0L, 5_000L) }, Lines(VoucherBuilder.Build(D, [itc], Map).Single()));
        var inter = AccountingEventMapper.Rcm(new ExpenseRcmPostedEvent(22, D, "Maintenance", "GTA", 100_000, 500, true, "inter"));
        Assert.Equal(new[] { ("1330", 5_000L, 0L), ("2140", 0L, 5_000L) }, Lines(VoucherBuilder.Build(D, [inter], Map).Single()));
        Assert.Equal((1L, -1L, 0L), (SourceRules.BpsOf(5, 1000), SourceRules.BpsOf(-5, 1000), SourceRules.BpsOf(4, 1000))); // half away from zero
    }

    [Fact]
    public void Vendor_payment_debits_the_vendor_party_and_credits_the_tender_a_later_void_reverses_on_its_date_a_same_day_void_books_nothing()
    {
        var e = new VendorPaymentEvent(30, D, "V-DECCAN", "Deccan Agro Traders", "Bank transfer", 3_500_000, 3_000_000, "NEFT-778");
        var p = AccountingEventMapper.VendorPayment(e, new PartyContext(Tender: "BANK-1", Vendor: "V-DECCAN"));
        Assert.Equal(("vendor-payment:30", SourceKinds.Vendor, "vendor-payment", VoucherType.Payment), (p.SourceKey, p.Kind, p.Sub, p.Type));
        Assert.Equal(new[] { ("2200/V-DECCAN", 3_500_000L, 0L), ("1030/BANK-1", 0L, 3_500_000L) }, Lines(VoucherBuilder.Build(D, [p], Map).Single()));
        Assert.Equal("vendor-advance", AccountingEventMapper.VendorPayment(e with { AllocatedPaise = 0 }).Sub);
        Assert.Null(AccountingEventMapper.VendorPaymentVoid(e with { VoidDate = D, VoidReason = "Wrong vendor" }));
        var later = AccountingEventMapper.VendorPaymentVoid(e with { BusinessDate = new DateOnly(2026, 10, 20), VoidDate = D, VoidReason = "Bounced" }, new PartyContext(Tender: "BANK-1", Vendor: "V-DECCAN"))!;
        Assert.Equal(("vendor-payment:30/void", "vendor-payment-void", D), (later.SourceKey, later.Sub, later.BusinessDate));
        Assert.Equal(new[] { ("2200/V-DECCAN", 0L, 3_500_000L), ("1030/BANK-1", 3_500_000L, 0L) }, Lines(VoucherBuilder.Build(D, [later], Map).Single()));
        Assert.Equal("V-DECCAN", new PartyContext(Vendor: "V-DECCAN").For(SourceRules.VendorPayableKey));
    }

    [Fact]
    public void A_return_is_a_negative_Purchase_through_the_vendor_payable_reversing_the_ITC_taken()
    {
        var r = AccountingEventMapper.ExpenseReturn(new ExpenseReturnedEvent(40, 10, D, "Supplies", "Vendor credit", "Deccan", 59_000, 9_000, "allowed"), new PartyContext(Vendor: "V-DECCAN"));
        Assert.Equal(("expense:40", "return", VoucherType.Purchase), (r.SourceKey, r.Sub, r.Type));
        Assert.Equal(new[] { ("5000", 0L, 50_000L), ("1310", 0L, 4_500L), ("1320", 0L, 4_500L), ("2200/V-DECCAN", 59_000L, 0L) }, Lines(VoucherBuilder.Build(D, [r], Map).Single()));
        var noItc = AccountingEventMapper.ExpenseReturn(new ExpenseReturnedEvent(41, 10, D, "Supplies", "Vendor credit", "Deccan", 10_000, 1_800, "disallowed"));
        Assert.Equal(new[] { ("5000", 0L, 10_000L), ("2200", 10_000L, 0L) }, Lines(VoucherBuilder.Build(D, [noItc], Map).Single()));
    }

    [Fact]
    public void The_new_mapping_keys_have_defaults_on_live_ledgers_and_the_vendor_key_group_is_valid()
    {
        Assert.Equal(("1330", "2140", "2150", "2200"), (Map["tax:gst-input-igst"], Map["tax:rcm-payable"], Map["tax:tds-payable"], Map["vendor:payable"]));
        Assert.True(ChartOfAccounts.IsValidKey("vendor:payable"));
        Assert.Equal("Vendor payments (payables)", ChartOfAccounts.KeyGroup("vendor:payable"));
        Assert.Contains(PurchaseEvents.VendorPaymentRecorded, AccountingEvents.Consumed);
        Assert.Contains(PurchaseEvents.ExpenseReturned, AccountingEvents.Consumed);
        Assert.Contains(SourceKinds.Vendor, SourceKinds.All);
    }

    [Fact]
    public void Purchase_rules_entry_stamp_allocation_ageing_returns_and_asset_numbers()
    {
        var plain = new ExpenseCategoryRow("BANQ", "Banquet supplies", true, false, null, Ledger: "5000");
        var food = plain with { Code = "FOOD", Name = "Staff food", ItcBlocked = true };
        var rcm = plain with { Code = "SEC", Name = "Security", Rcm = true, RcmBps = 1800 };
        var car = plain with { Code = "CAR", Name = "Vehicles", Capital = true, Ledger = "1540" };
        var ka = new ExpenseVendorRow(1, "V-KAVERI", "Kaveri", true, "KA");
        Assert.Equal(("inter", (int?)null, true), ExpensePurchase.Entry(plain, ka, null, null, 1_800, true, "allowed"));
        Assert.Equal(((string?)null, (int?)null, false), ExpensePurchase.Entry(plain, ka with { State = "MH" }, null, null, null, false, null));
        Assert.Equal("intra", ExpensePurchase.Entry(plain, ka, "intra", null, 1_800, true, "allowed").GstKind);
        Assert.Contains("DEC-143", Assert.Throws<HccException>(() => ExpensePurchase.Entry(food, null, null, null, 500, true, "allowed")).Message, StringComparison.Ordinal);
        Assert.Contains("DEC-143", Assert.Throws<HccException>(() => ExpensePurchase.Entry(car, null, null, null, 18_000, true, "allowed")).Message, StringComparison.Ordinal);
        Assert.Throws<HccException>(() => ExpensePurchase.Entry(rcm, null, null, null, 18_000, false, null));
        Assert.Equal(((string?)null, (int?)500, true), ExpensePurchase.Entry(rcm, null, null, 500, null, false, "allowed"));
        Assert.Equal((true, (int?)1800, false), ExpensePurchase.Stamp(rcm, null, null, false));
        Assert.Equal((true, (int?)500, false), ExpensePurchase.Stamp(rcm, null, 500, false));
        Assert.Equal((false, (int?)null, true), ExpensePurchase.Stamp(car, null, null, false));
        Assert.Equal((0L, 720_000L, false), ExpensePurchase.Lines(4_000_000, null, false, true, 1800));
        Assert.Equal((9_000L, 0L, false), ExpensePurchase.Lines(59_000, 9_000, true, false, null));

        var bills = new[] { new ExpensePurchase.OpenBill(2, new DateOnly(2026, 10, 20), 1_200_000, 0, 0), new ExpensePurchase.OpenBill(1, new DateOnly(2026, 9, 9), 3_000_000, 0, 0) };
        Assert.Equal(new[] { (1L, 3_000_000L), (2L, 500_000L) }, ExpensePurchase.Allocate(bills, 3_500_000));
        Assert.Equal(new[] { (1L, 3_000_000L), (2L, 1_200_000L) }, ExpensePurchase.Allocate(bills, 9_000_000)); // the rest is an advance
        Assert.Equal("The allocations add up to more than the payment.", ExpensePurchase.AllocationRefusal(bills, 100, [(2, 101)]));
        Assert.StartsWith("Bill 9 is not an open bill", ExpensePurchase.AllocationRefusal(bills, 100, [(9, 1)]), StringComparison.Ordinal);
        Assert.Null(ExpensePurchase.AllocationRefusal(bills, 100, [(2, 100)]));
        Assert.Equal(new[] { "0–30", "31–60", "31–60", "61–90", "90+" }, new[] { 30, 31, 60, 90, 91 }.Select(ExpensePurchase.AgeBucket).ToArray());
        Assert.Equal("61–90", ExpensePurchase.AgeBucket(61));
        Assert.Equal(1_000L, new ExpensePurchase.OpenBill(1, D, 5_000, 1_000, 3_000).Open);
        Assert.Equal(0L, new ExpensePurchase.OpenBill(1, D, 5_000, 3_000, 3_000).Open);

        Assert.StartsWith("At most 590.00 of this bill", ExpensePurchase.ReturnRefusal(118_000, 18_000, 59_000, 9_000, 60_000, 0), StringComparison.Ordinal);
        Assert.StartsWith("At most 90.00 of the bill's GST", ExpensePurchase.ReturnRefusal(118_000, 18_000, 59_000, 9_000, 10_000, 10_000), StringComparison.Ordinal);
        Assert.Null(ExpensePurchase.ReturnRefusal(118_000, 18_000, 0, 0, 59_000, 9_000));
        Assert.Equal("FA-2026-003", ExpensePurchase.NextAssetNo(D, ["FA-2026-001", "FA-2026-002", "FA-2025-009"]));
        Assert.Equal("FA-2027-001", ExpensePurchase.NextAssetNo(new DateOnly(2027, 4, 2), ["FA-2026-001"]));
        Assert.True(ExpensePurchase.IsAssetLedger("1510"));
        Assert.False(ExpensePurchase.IsAssetLedger("5000"));
        Assert.True(ExpensePurchase.SameDayVoid(D, D));
    }
}
