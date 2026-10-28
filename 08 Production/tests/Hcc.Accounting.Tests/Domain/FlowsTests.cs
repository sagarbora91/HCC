using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Ota;

namespace Hcc.Accounting.Tests.Domain;

/// <summary>
/// ACC-2 lane acc2-flows (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4, §7, §8; rulebook A6a, A13, A17b, B9, B10): the deposit applied at check-out,
/// the OTA credit and the OTA payout from the statement, the Udhaar payment reversed, the cash guards and the new receipt kinds. Mirrors the
/// prototype's tests/stream-accounting-flows.mjs (same entries, limits and messages).
/// </summary>
public sealed class FlowsTests
{
    private static readonly DateOnly D = new(2026, 10, 24);
    private static readonly IReadOnlyDictionary<string, string> Map = ChartOfAccounts.Effective(null);

    private static List<(string, long, long)> E(Voucher v) => v.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)).ToList();

    [Fact]
    public void Default_mapping_names_the_ota_receivable_commission_and_tcs_ledgers()
    {
        Assert.Equal("1115", ChartOfAccounts.DefaultMapping["receipt:ota"]);
        Assert.Equal("5070", ChartOfAccounts.DefaultMapping["receipt:ota-commission"]);
        Assert.Equal("1340", ChartOfAccounts.DefaultMapping["tax:tcs"]);
        Assert.True(ChartOfAccounts.IsValidKey("receipt:ota-commission"));
        Assert.True(ChartOfAccounts.IsValidKey("tax:tcs"));
    }

    [Fact]
    public void Deposit_applied_is_a_journal_dr_advance_deposits_cr_guest_ledger()
    {
        var s = AccountingEventMapper.Receipt(9, 77, "deposit-apply", "Deposit applied", D, 100_000, new PartyContext(Tender: null));
        Assert.Equal(("receipt", "deposit-apply", VoucherType.Journal), (s.Kind, s.Sub, s.Type));
        var v = Assert.Single(VoucherBuilder.Build(D, [s], Map));
        Assert.Equal([("2000", 100_000L, 0L), ("1100", 0L, 100_000L)], E(v));
        Assert.StartsWith("Deposit applied at check-out · folio 9", v.Narration, StringComparison.Ordinal);
    }

    [Fact]
    public void Ota_credit_moves_the_folio_balance_to_the_channel_party()
    {
        var s = AccountingEventMapper.Receipt(9, 78, "ota-credit", "OTA credit", D, 420_000, new PartyContext(Channel: "BOOKNEST"));
        Assert.Equal(("receipt", "ota-credit", VoucherType.Journal), (s.Kind, s.Sub, s.Type));
        var v = Assert.Single(VoucherBuilder.Build(D, [s], Map));
        Assert.Equal([("1115/BOOKNEST", 420_000L, 0L), ("1100", 0L, 420_000L)], E(v));
        // Without the channel the control takes it (no-party warning, never blocking).
        var bare = Assert.Single(VoucherBuilder.Build(D, [SourceRules.FolioReceipt("receipt:79", "ota-credit", "OTA credit", D, 1_000, "x")], Map));
        Assert.Equal("1115", bare.Entries[0].Account);
    }

    [Fact]
    public void Ota_payout_splits_bank_commission_with_gst_and_tcs_against_the_gross()
    {
        var e = new OtaPayoutRecordedEvent(5, 1, "BOOKNEST", "BookNest", D, "BN-STMT-OCT-01", 420_000, 10_000, 1_800, 500, 407_700, "BANK-1", false, 1);
        var s = AccountingEventMapper.OtaPayout(e);
        Assert.Equal(("ota-payout:5", "ota", "ota-payout", VoucherType.Receipt), (s.SourceKey, s.Kind, s.Sub, s.Type));
        var v = Assert.Single(VoucherBuilder.Build(D, [s], Map));
        Assert.Equal([("1030/BANK-1", 407_700L, 0L), ("5070", 11_800L, 0L), ("1340", 500L, 0L), ("1115/BOOKNEST", 0L, 420_000L)], E(v));
        Assert.Equal(v.Entries.Sum(x => x.DebitPaise), v.Entries.Sum(x => x.CreditPaise));
        Assert.Equal("OTA payout · BookNest · statement BN-STMT-OCT-01 · 1 booking(s) · payout 5", v.Narration);
    }

    [Fact]
    public void Ota_payout_with_itc_allowed_takes_the_commission_gst_to_input_cgst_and_sgst()
    {
        var e = new OtaPayoutRecordedEvent(6, 1, "BOOKNEST", "BookNest", D, "BN-2", 420_000, 10_000, 1_900, 500, 407_600, "BANK-1", true, 0);
        var v = Assert.Single(VoucherBuilder.Build(D, [AccountingEventMapper.OtaPayout(e)], Map));
        Assert.Equal([("1030/BANK-1", 407_600L, 0L), ("5070", 10_000L, 0L), ("1310", 950L, 0L), ("1320", 950L, 0L), ("1340", 500L, 0L), ("1115/BOOKNEST", 0L, 420_000L)], E(v));
    }

    [Fact]
    public void Udhaar_payment_reversed_is_the_negative_receipt_on_the_same_parties()
    {
        var pay = AccountingEventMapper.UdhaarPayment(new UdhaarPaymentRecordedEvent(3, 30, D, "Cash", 200_000, "Reversal Test"), new PartyContext(Tender: "TILL", Udhaar: "UD-9800000002"));
        var back = AccountingEventMapper.UdhaarPaymentReversed(new UdhaarPaymentReversedEvent(3, 31, 30, D, "Cash", 200_000, "Reversal Test", "Wrong customer"),
            new PartyContext(Tender: "TILL", Udhaar: "UD-9800000002"));
        Assert.Equal(("udhaar-payment:31", "udhaar-payment", VoucherType.Receipt), (back.SourceKey, back.Sub, back.Type));
        var vs = VoucherBuilder.Build(D, [pay, back], Map);
        Assert.Equal([("1000/TILL", 200_000L, 0L), ("1120/UD-9800000002", 0L, 200_000L)], E(vs[0]));
        Assert.Equal([("1000/TILL", 0L, 200_000L), ("1120/UD-9800000002", 200_000L, 0L)], E(vs[1]));
        Assert.Contains("Udhaar payment reversed (A17b) · 30 · Reversal Test · Cash · Wrong customer · payment 31", vs[1].Narration, StringComparison.Ordinal);
    }

    [Fact]
    public void Folio_totals_treat_deposit_apply_and_ota_credit_like_the_prototype()
    {
        var lines = new[] { new FolioLine(new Paise(400_000), 500) };   // ₹4,000 + 5 % = ₹4,200
        var t = FolioTotals.Compute(lines, [new FolioReceipt(ReceiptKind.Deposit, new Paise(100_000)), new FolioReceipt(ReceiptKind.DepositApply, new Paise(100_000))]);
        Assert.Equal((0L, 320_000L, 100_000L), (t.Deposit.Value, t.Balance.Value, t.Paid.Value)); // coordinator ruling: deposit-apply counts as paid
        var op = FolioTotals.Compute(lines, [new FolioReceipt(ReceiptKind.DepositApply, new Paise(100_000), FromOpening: true)]);
        Assert.Equal((0L, 320_000L, 100_000L), (op.Deposit.Value, op.Balance.Value, op.Paid.Value));               // an opening-balance advance: no folio deposit to leave
        var st = Hcc.Domain.Billing.FolioStatement.Compute([], [new Hcc.Domain.Billing.PostedReceipt(1, ReceiptKind.Deposit, "Cash", new Paise(5_000), D, null, ""),
            new Hcc.Domain.Billing.PostedReceipt(2, ReceiptKind.DepositApply, FolioTotals.OpeningAdvanceTender, new Paise(2_000), D, null, "")]);
        Assert.Equal((5_000L, 2_000L), (st.Deposit.Value, st.Paid.Value));
        var o = FolioTotals.Compute(lines, [new FolioReceipt(ReceiptKind.OtaCredit, new Paise(420_000))]);
        Assert.Equal((0L, 0L), (o.Balance.Value, o.Deposit.Value));
        Assert.Equal(ReceiptKind.DepositApply, FolioTotals.ParseKind("deposit-apply"));
        Assert.Equal(ReceiptKind.OtaCredit, FolioTotals.ParseKind("ota-credit"));
        Assert.Equal("deposit-apply", FolioTotals.KindCode(ReceiptKind.DepositApply));
        Assert.Equal("ota-credit", FolioTotals.KindCode(ReceiptKind.OtaCredit));
    }

    [Fact]
    public void Cash_receipt_at_the_limit_is_refused_with_cash_limit()
    {
        Assert.Equal((20_000_000L, 1_000_000L, 3_500_000L), (CashGuardLimits.Seed.ReceiptLimitPaise, CashGuardLimits.Seed.PaymentLimitPaise, CashGuardLimits.Seed.TransportLimitPaise));
        var ok = CashGuard.Receipt(0, 15_000_000, scoped: true, 4_999_900, 20_000_000, "Flows Test", D);
        Assert.False(ok.Refused);
        var no = CashGuard.Receipt(0, 15_000_000, scoped: true, 5_000_000, 20_000_000, "Flows Test (booking 9)", D);
        Assert.True(no.Refused);
        Assert.Equal(20_000_000, no.TotalPaise);
        Assert.Equal("Cash of ₹2,00,000 from Flows Test (booking 9) for this booking / transaction reaches the ₹2,00,000 cash-receipt limit — take it by UPI, card or bank transfer "
                     + "(Income-tax Act 2025 s.186, old s.269ST; VERIFY section numbers — CA to confirm (rulebook OP-10)).", no.Message);
        var day = CashGuard.Receipt(20_000_000, 0, scoped: false, 1, 20_000_000, "Ramesh", D);
        Assert.True(day.Refused);
        Assert.Contains("on 2026-10-24 reaches", day.Message, StringComparison.Ordinal);
        var ex = CashGuard.Refusal(no);
        Assert.Equal(HccErrorCode.VALIDATION, ex.Code);
        Assert.Equal(CashGuard.Reason, ex.Details!["reason"].GetString());
        Assert.Equal(20_000_000, ex.Details["limitPaise"].GetInt64());
    }

    [Fact]
    public void Cash_payment_above_the_limit_is_allowed_with_the_deduction_lost_warning()
    {
        Assert.False(CashGuard.Payment(600_000, 400_000, 1_000_000, "Morning Supplies", D).DeductionLost);
        var w = CashGuard.Payment(600_000, 500_000, 1_000_000, "Morning Supplies", D);
        Assert.True(w.DeductionLost);
        Assert.False(w.Refused);
        Assert.Equal("Cash paid to Morning Supplies on 2026-10-24 totals ₹11,000, more than ₹10,000: the income-tax deduction is lost (old s.40A(3); "
                     + "VERIFY section numbers — CA to confirm (rulebook OP-10)) — pay by bank transfer or UPI to keep it. Marked “deduction lost”.", w.Message);
        Assert.False(CashGuard.Payment(0, 3_500_000, 3_500_000, "Transporter", D).DeductionLost);
        Assert.True(CashGuard.Payment(0, 3_500_100, 3_500_000, "Transporter", D).DeductionLost);
    }

    [Theory]
    [InlineData(20_000_000L, "₹2,00,000")]
    [InlineData(1_100_000L, "₹11,000")]
    [InlineData(123_450L, "₹1,234.50")]
    [InlineData(1_000_000_000L, "₹1,00,00,000")]
    [InlineData(5L, "₹0.05")]
    public void Rupees_use_indian_grouping_as_the_prototype(long paise, string text) => Assert.Equal(text, CashGuard.Rs(paise));

    [Fact]
    public void Ota_payout_statement_rules_match_the_prototype()
    {
        Assert.Null(OtaPayoutRules.Problem("BN-1", 420_000, 10_000, 1_800, 500, 407_700));
        Assert.Equal("Enter the OTA statement reference.", OtaPayoutRules.Problem(" ", 1, 0, 0, 0, 1));
        Assert.Equal("Keep the statement reference to 40 characters.", OtaPayoutRules.Problem(new string('X', 41), 1, 0, 0, 0, 1));
        Assert.Equal("TCS: enter an amount of ₹0 or more.", OtaPayoutRules.Problem("BN-1", 1, 0, 0, -1, 1));
        Assert.Equal("Enter the gross amount and the net amount received (both above ₹0).", OtaPayoutRules.Problem("BN-1", 0, 0, 0, 0, 0));
        Assert.StartsWith("Gross ₹4,200 must equal net ₹4,200 + commission ₹100", OtaPayoutRules.Problem("BN-1", 420_000, 10_000, 1_800, 500, 420_000), StringComparison.Ordinal);
        Assert.Equal("The gross ₹4,300 is more than BookNest's open receivable ₹4,200 (OTA credits at check-out less payouts).", OtaPayoutRules.OverReceivable("BookNest", 430_000, 420_000));
    }

    [Fact]
    public void Cash_guard_settings_are_three_dated_owner_money_settings()
    {
        var defs = CashGuardSettings.Provider.Definitions;
        Assert.Equal([CashGuard.ReceiptLimitKey, CashGuard.PaymentLimitKey, CashGuard.TransportLimitKey], defs.Select(d => d.Key).ToArray());
        Assert.All(defs, d => Assert.True(d.Money == true && d.Dated == true));
        Assert.All(defs, d => Assert.Contains("VERIFY section numbers", d.Help, StringComparison.Ordinal));
        Assert.Equal(Hcc.Domain.Configuration.ConfigGroupIds.Money, CashGuardSettings.Provider.Group);
    }
}
