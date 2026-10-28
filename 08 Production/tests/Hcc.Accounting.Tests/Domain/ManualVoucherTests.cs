using Hcc.Application.Accounting;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Accounting.Tests.Domain;

/// <summary>
/// ACC-2 manual vouchers (contract §2; rulebook §1 principle 2, B5–B7, B11, A18): every guard with the prototype's message and order
/// (tests/stream-accounting-manual.mjs), the source rule (ledger keys + parties), the reversal, the loan plan. Pure, no database.
/// </summary>
public sealed class ManualVoucherTests
{
    private static readonly DateOnly D = new(2026, 10, 24);
    private const string Bank = "1030/BANK-1";
    private const string Till = "1000/TILL";

    private static long R(long rupees) => rupees * 100;

    private static readonly IReadOnlySet<string> Parties = new HashSet<string>(StringComparer.Ordinal) { Bank, Till, "1110/CMP-1", "2400/LN-HDFC", "1200/STF-7" };

    private static ManualLineRequest L(string account, long debit, long credit) => new(account, debit, credit);

    private static IReadOnlyList<ManualLineRequest> Charges => [L("5080", R(590), 0), L(Bank, 0, R(590))];

    private static IReadOnlyList<string> P(string? type, IReadOnlyList<ManualLineRequest>? lines, DateOnly? date = null, string? narration = "Guard test",
        string? template = null, string? reference = null, Func<DateOnly, bool>? locked = null, EffectiveChart? chart = null)
        => ManualVoucherRules.Problems(type, template, narration, reference, date ?? D, lines, D, locked ?? (_ => false), chart ?? ChartOfAccounts.Builtin, Parties);

    [Fact]
    public void A_bank_charge_payment_passes_every_guard()
        => Assert.Empty(P("Payment", Charges, template: "bank-charges", reference: "STMT-OCT"));

    [Fact]
    public void Header_guards_in_order_with_the_prototype_messages()
    {
        Assert.Equal([ManualVoucherRules.MsgType, ManualVoucherRules.MsgNarration, ManualVoucherRules.MsgLines],
            ManualVoucherRules.Problems("X", null, "", null, D, [], D, _ => false, ChartOfAccounts.Builtin, Parties));
        Assert.Equal([ManualVoucherRules.MsgTemplate], P("Payment", Charges, template: "gift"));
        Assert.Equal([ManualVoucherRules.MsgReference], P("Payment", Charges, reference: new string('R', 61)));
        Assert.Equal([ManualVoucherRules.MsgDate], ManualVoucherRules.Problems("Payment", null, "Bank charges", null, null, Charges, D, _ => false, ChartOfAccounts.Builtin, Parties));
        Assert.Equal([ManualVoucherRules.MsgFuture], P("Payment", Charges, date: D.AddDays(1)));
        var locked = P("Payment", Charges, locked: d => d == D);
        Assert.Equal(["2026-10-24 is locked — unlock the day first (the Owner reopens a locked day with a reason, DEC-077)."], locked);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, ManualVoucherRules.Refusal(locked[0]).Code);
        Assert.Equal(HccErrorCode.VALIDATION, ManualVoucherRules.Refusal(ManualVoucherRules.MsgBalance).Code);
        Assert.Equal([ManualVoucherRules.MsgLines], P("Payment", [L("5080", 100, 0)]));
        Assert.Equal("Enter 2 to 100 lines.", ManualVoucherRules.MsgLines);
    }

    [Fact]
    public void Line_guards_shape_one_side_size_never_control_unknown_and_balance()
    {
        var ok = L(Bank, 0, R(590));
        Assert.Equal([ManualVoucherRules.MsgLineAccount(1)], P("Journal", [L("50x", 1, 0), ok]));
        Assert.Equal([ManualVoucherRules.MsgLineAmount(1), ManualVoucherRules.MsgLineAmount(2)], P("Journal", [L("5080", 5, 5), L(Bank, 0, 0)]));
        Assert.Equal([ManualVoucherRules.MsgLineAmount(1)], P("Journal", [L("5080", -5, 0), ok]));
        Assert.Equal([ManualVoucherRules.MsgLineLarge(1), ManualVoucherRules.MsgLineLarge(2)], P("Journal", [L("5080", 20_000_000_000, 0), L(Bank, 0, 20_000_000_000)]));
        Assert.Equal(["Line 1: 9999 is never posted by hand (9999 Suspense, 1400 Opening-balance difference)."], P("Journal", [L("9999", R(1), 0), ok]));
        Assert.Equal([ManualVoucherRules.MsgLineNever(1, "1400")], P("Journal", [L("1400", R(1), 0), ok]));
        foreach (var c in new[] { "1000", "1030", "1110", "1115", "1120", "1200", "2200", "2400" })
            Assert.Equal(["Line 1: " + c + " is a control ledger — post to one of its parties (" + c + "/<party>)."], P("Journal", [L(c, R(1), 0), ok]));
        Assert.Equal([ManualVoucherRules.MsgLineUnknown(1, "5081")], P("Journal", [L("5081", R(1), 0), ok]));
        Assert.Equal([ManualVoucherRules.MsgLineUnknown(1, "1110/CMP-404")], P("Journal", [L("1110/CMP-404", R(1), 0), ok]));
        Assert.Equal([ManualVoucherRules.MsgLineUnknown(1, "5080/X")], P("Journal", [L("5080/X", R(1), 0), ok]));
        Assert.Equal([ManualVoucherRules.MsgLineUnknown(1, "2400/LN-NONE")], P("Journal", [L("2400/LN-NONE", R(1), 0), ok]));
        Assert.Equal([ManualVoucherRules.MsgBalance], P("Journal", [L("5080", R(590), 0), L(Bank, 0, R(500))]));
        // A retired ledger is refused; the built-in chart accepts it.
        var retired = ChartOfAccounts.Merge([new StoredLedger("5080", "Bank & card charges", "indirect-expenses", LedgerGst.None, null, null, false)], null);
        Assert.Equal([ManualVoucherRules.MsgLineUnknown(1, "5080")], P("Payment", Charges, chart: retired));
    }

    [Fact]
    public void Contra_only_cash_and_bank_and_payment_receipt_one_cash_or_bank_side()
    {
        Assert.Empty(P("Contra", [L(Bank, R(5000), 0), L(Till, 0, R(5000))]));
        Assert.Empty(P("Contra", [L(Bank, R(5000), 0), L("1020", 0, R(5000))]));
        Assert.Empty(P("Contra", [L("1005", R(500), 0), L(Till, 0, R(500))]));
        Assert.Equal([ManualVoucherRules.MsgContra], P("Contra", [L(Bank, R(4900), 0), L("5080", R(100), 0), L("1020", 0, R(5000))]));
        Assert.Equal([ManualVoucherRules.MsgPayment], P("Payment", [L("5080", R(1), 0), L("3100", 0, R(1))]));
        Assert.Equal([ManualVoucherRules.MsgPayment], P("Payment", [L(Bank, R(1), 0), L(Till, 0, R(1))]));
        Assert.Empty(P("Receipt", [L(Bank, R(1), 0), L("4960", 0, R(1))]));
        Assert.Equal([ManualVoucherRules.MsgReceipt], P("Receipt", [L("5080", R(1), 0), L("4960", 0, R(1))]));
        Assert.Equal([ManualVoucherRules.MsgReceipt], P("Receipt", [L(Bank, R(1), 0), L(Till, 0, R(1))]));
        Assert.Empty(P("Journal", [L(Bank, R(1), 0), L(Till, 0, R(1))]));
        // Loan received / EMI post to the loan party (B11).
        Assert.Empty(P("Receipt", [L(Bank, R(5_00_000), 0), L("2400/LN-HDFC", 0, R(5_00_000))]));
        Assert.Empty(P("Payment", [L("2400/LN-HDFC", R(15_000), 0), L("5090", R(5_000), 0), L(Bank, 0, R(20_000))]));
    }

    [Fact]
    public void Bad_debt_is_the_template_or_any_debit_on_5300()
    {
        Assert.True(ManualVoucherRules.IsBadDebt("bad-debt", []));
        Assert.True(ManualVoucherRules.IsBadDebt("journal", [L("5300", R(1000), 0), L("1110/CMP-1", 0, R(1000))]));
        Assert.False(ManualVoucherRules.IsBadDebt("bank-charges", Charges));
        Assert.Equal("Only the Owner writes off a bad debt (rulebook A18).", ManualVoucherRules.MsgBadDebt);
    }

    [Fact]
    public void An_approved_voucher_is_one_manual_source_with_ledger_keys_and_parties_numbered_with_the_day()
    {
        var e = new ManualVoucherApprovedEvent(41, D, "Payment", "Bank charges October", "STMT-OCT", "bank-charges",
            [new ManualLinePayload("5080", R(590), 0), new ManualLinePayload(Bank, 0, R(590))]);
        var s = AccountingEventMapper.ManualVoucher(e);
        Assert.Equal(("manual:41", SourceKinds.Manual, "bank-charges", VoucherType.Payment), (s.SourceKey, s.Kind, s.Sub, s.Type));
        Assert.Equal("Bank charges October · STMT-OCT · manual 41", s.Narration);
        Assert.Equal([new SourceEntry("ledger:5080", R(590), 0, null), new SourceEntry("ledger:1030", 0, R(590), "BANK-1")], s.Entries);
        var contra = AccountingEventMapper.ManualVoucher(new ManualVoucherApprovedEvent(42, D, "Contra", "Cash banked", null, null,
            [new ManualLinePayload(Bank, R(5000), 0), new ManualLinePayload(Till, 0, R(5000))], VoidOf: 7));
        Assert.Equal(("journal", "Cash banked · reversal of 7"), (contra.Sub, contra.Narration));
        var vs = VoucherBuilder.Build(D, [s, contra], ChartOfAccounts.DefaultMapping);
        Assert.Equal(["PMT/20261024/001", "CTR/20261024/001"], vs.Select(v => v.Number));
        Assert.Equal([new VoucherEntry("5080", R(590), 0), new VoucherEntry(Bank, 0, R(590))], vs[0].Entries);
        Assert.Empty(vs[0].Unmapped);
        // A ledger retired after approval posts to Suspense and raises the unmapped exception (never silently lost).
        var retired = ChartOfAccounts.Merge([new StoredLedger("5080", "Bank & card charges", "indirect-expenses", LedgerGst.None, null, null, false)], null);
        var r = VoucherBuilder.Build(D, [s], ChartOfAccounts.DefaultMapping, retired)[0];
        Assert.Equal((ChartOfAccounts.Suspense, "ledger:5080"), (r.Entries[0].Account, r.Unmapped.Single()));
        // A mapping override never captures a manual line (no mapping key).
        Assert.Equal("5080", ChartOfAccounts.Resolve(new Dictionary<string, string> { ["ledger:5080"] = "5000" }, "ledger:5080"));
        Assert.Null(ChartOfAccounts.Resolve(ChartOfAccounts.DefaultMapping, "ledger:9999"));
        Assert.False(ChartOfAccounts.IsValidKey("ledger:5080"));
    }

    [Fact]
    public void A_reversal_swaps_the_lines_and_normalise_trims()
    {
        IReadOnlyList<ManualLine> lines = [new("5080", R(590), 0, "Oct"), new(Bank, 0, R(590))];
        Assert.Equal([new ManualLine("5080", 0, R(590), "Oct"), new ManualLine(Bank, R(590), 0)], ManualVoucherRules.Swap(lines));
        Assert.Equal([new ManualLine("5080", 5, 0, "x"), new ManualLine(Bank, 0, 5)],
            ManualVoucherRules.Normalise([new(" 5080 ", 5, 0, " x "), new(Bank, 0, 5, "  ")]));
    }

    [Fact]
    public void Loan_plan_makes_2400_code_rows_with_the_kind_and_refuses_duplicates()
    {
        // Coordinator ruling (28 Oct): AddLoan {code, name, lender, accountLast4, kind} → 2400/<CODE>, kind = secured | unsecured.
        var (row, refusal) = ManualVoucherRules.LoanPlan(new AddLoanRequest("ln-hdfc", "HDFC term loan", "HDFC Bank (fictional)", "7788", null, "secured"), ChartOfAccounts.Builtin, []);
        Assert.Null(refusal);
        Assert.Equal(("LN-HDFC", "2400/LN-HDFC", "7788", "secured"), (row!.PartyId, row.Account, row.AccountLast4, row.Kind));
        Assert.Equal("unsecured", ManualVoucherRules.LoanPlan(new AddLoanRequest("LN-FAMILY", "Loan from a relative", "A relative (fictional)", null, null, "unsecured"),
            ChartOfAccounts.Builtin, []).Row!.Kind);
        Assert.Equal(HccErrorCode.VALIDATION, ManualVoucherRules.LoanPlan(new AddLoanRequest("LN-X", "Odd loan", "HDFC", null, null, "overdraft"), ChartOfAccounts.Builtin, []).Refusal!.Code);
        var byLender = ManualVoucherRules.LoanPlan(new AddLoanRequest(null, "Car loan", "Axis Bank (fictional)", null, null), ChartOfAccounts.Builtin, []).Row!;
        Assert.Equal("LN-AXIS-BANK-FICTIONAL", byLender.PartyId);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, ManualVoucherRules.LoanPlan(new AddLoanRequest("LN-HDFC", "Another", "HDFC", null, null), ChartOfAccounts.Builtin, [row]).Refusal!.Code);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, ManualVoucherRules.LoanPlan(new AddLoanRequest("X", "Bank & card charges", "HDFC", null, null), ChartOfAccounts.Builtin, []).Refusal!.Code);
        Assert.Contains("4 digits", ManualVoucherRules.LoanPlan(new AddLoanRequest("X", "Some loan", "HDFC", "12", null), ChartOfAccounts.Builtin, []).Refusal!.Message, StringComparison.Ordinal);
        Assert.Equal(HccErrorCode.VALIDATION, ManualVoucherRules.LoanPlan(new AddLoanRequest("X", "ab", "HDFC", null, null), ChartOfAccounts.Builtin, []).Refusal!.Code);
    }
}
