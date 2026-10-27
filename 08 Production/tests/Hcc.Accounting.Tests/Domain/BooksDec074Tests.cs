using System.Text.Json;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Domain;

/// <summary>
/// DEC-074 (wave B3, audit A03b-02, A03b-03, A04-01 voucher, A04-06/07): group receipts, attrition and settlement, staff advances and
/// full-and-final settlements become vouchers. Every figure is worked out by hand in the comments; every voucher balances.
/// </summary>
public sealed class BooksDec074Tests
{
    private static readonly DateOnly D = new(2026, 10, 24);
    private static readonly IReadOnlyDictionary<string, string> Map = ChartOfAccounts.Effective(null);

    private static Voucher One(AccountingSource s) => VoucherBuilder.Build(s.BusinessDate, [s], Map).Single();

    /// <summary>Account → (debit, credit) summed (two keys may map to one ledger, e.g. employer PF + ESIC → 5110).</summary>
    private static Dictionary<string, (long Dr, long Cr)> ByAccount(Voucher v)
        => v.Entries.GroupBy(e => e.Account).ToDictionary(g => g.Key, g => (g.Sum(e => e.DebitPaise), g.Sum(e => e.CreditPaise)));

    // ── Groups (A03b-02) ──

    [Fact]
    public void Group_attrition_is_a_sales_voucher_on_the_company_receivable_with_the_stored_gst_split()
    {
        // ₹9,000 block, 50 % retention, 2 unpicked rooms × 3 nights: each night 2 × ₹4,500 = ₹9,000 net, 18 % (the ₹9,000 slab, DEC-074) = ₹1,620
        // → CGST ₹810 + SGST ₹810. Three lines: net ₹27,000, GST ₹4,860 (CGST ₹2,430 + SGST ₹2,430); the receivable carries ₹31,860.
        var e = new GroupAttritionPostedEvent(12, 7, [101, 102, 103], 2_700_000, 486_000, 243_000, 243_000, D);
        var v = One(AccountingEventMapper.GroupAttrition(e));
        Assert.Equal((VoucherType.Sales, "SAL/20261024/001", "group-attrition:7", "line", "group-attrition"), (v.Type, v.Number, v.SourceKey, v.SourceKind, v.Sub));
        Assert.True(v.Balanced);
        Assert.Empty(v.Unmapped);
        Assert.Equal([("1110", 3_186_000L, 0L), ("4300", 0L, 2_700_000L), // ACC-1: attrition → 4300
            ("2110", 0L, 243_000L), ("2120", 0L, 243_000L)],
            v.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        Assert.Contains("3 line(s) · group 12 · attrition 7", v.Narration, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => AccountingEventMapper.GroupAttrition(e with { CgstPaise = 243_001 }));   // never recomputed, never accepted inconsistent
    }

    [Fact]
    public void Group_receipts_book_payment_to_the_receivable_deposit_to_advance_deposits_and_a_refund_against_what_it_refunds()
    {
        // Payment ₹16,060 UPI → Receipt: Dr 1010 UPI clearing · Cr 1110 Company receivables.
        var pay = One(AccountingEventMapper.GroupReceipt(new GroupReceiptRecordedEvent(12, 31, "payment", "UPI", 1_606_000, D), null));
        Assert.Equal((VoucherType.Receipt, "group-payment", "group-receipt:31"), (pay.Type, pay.Sub, pay.SourceKey));
        Assert.Equal([("1010", 1_606_000L, 0L), ("1110", 0L, 1_606_000L)], pay.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));

        // Deposit ₹20,000 by bank transfer → Receipt: Dr 1030 Bank · Cr 2000 Advance deposits (held until settlement).
        var dep = One(AccountingEventMapper.GroupReceipt(new GroupReceiptRecordedEvent(12, 32, "deposit", "Bank transfer", 2_000_000, D), null));
        Assert.Equal((VoucherType.Receipt, "group-deposit"), (dep.Type, dep.Sub));
        Assert.Equal([("1030", 2_000_000L, 0L), ("2000", 0L, 2_000_000L)], dep.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));

        // Refund ₹800 of that deposit → Payment: Dr 2000 · Cr 1030. Refund ₹500 of a payment → Payment: Dr 1110 · Cr 1010.
        var refDep = One(AccountingEventMapper.GroupReceipt(new GroupReceiptRecordedEvent(12, 33, "refund", "Bank transfer", 80_000, D, "deposit"), "deposit"));
        Assert.Equal((VoucherType.Payment, "group-refund"), (refDep.Type, refDep.Sub));
        Assert.Equal([("2000", 80_000L, 0L), ("1030", 0L, 80_000L)], refDep.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        Assert.Contains("refund of deposit", refDep.Narration, StringComparison.Ordinal);
        var refPay = One(AccountingEventMapper.GroupReceipt(new GroupReceiptRecordedEvent(12, 34, "refund", "UPI", 50_000, D), "payment"));
        Assert.Equal([("1110", 50_000L, 0L), ("1010", 0L, 50_000L)], refPay.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        Assert.All([pay, dep, refDep, refPay], x => Assert.True(x.Balanced));
    }

    [Fact]
    public void Group_settlement_applies_the_deposits_held_to_the_receivable_and_the_group_nets_to_zero()
    {
        // Charges: routed shares ₹4,200 (transferred: Dr 1110 · Cr 1100) + attrition ₹31,860 = ₹36,060. Deposit ₹20,000, payment ₹16,060.
        // Settlement: Dr 2000 ₹20,000 · Cr 1110 ₹20,000 → 1110 = 4,200 + 31,860 − 16,060 − 20,000 = 0; 2000 = −20,000 + 20,000 = 0.
        var settle = One(AccountingEventMapper.GroupSettlement(new GroupSettledEvent(12, 3_606_000, 3_606_000, 2_000_000, D), 2_000_000, D));
        Assert.Equal((VoucherType.Journal, "group-settlement", "group-settle:12"), (settle.Type, settle.Sub, settle.SourceKey));
        Assert.Equal([("2000", 2_000_000L, 0L), ("1110", 0L, 2_000_000L)], settle.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));

        var sources = new List<AccountingSource>
        {
            AccountingEventMapper.Receipt(5, 900, "company-credit", "Company credit", D, 420_000),
            AccountingEventMapper.GroupAttrition(new GroupAttritionPostedEvent(12, 7, [101, 102, 103], 2_700_000, 486_000, 243_000, 243_000, D)),
            AccountingEventMapper.GroupReceipt(new GroupReceiptRecordedEvent(12, 32, "deposit", "Bank transfer", 2_000_000, D), null),
            AccountingEventMapper.GroupReceipt(new GroupReceiptRecordedEvent(12, 31, "payment", "UPI", 1_606_000, D), null),
            AccountingEventMapper.GroupSettlement(new GroupSettledEvent(12, 3_606_000, 3_606_000, 2_000_000, D), 2_000_000, D),
        };
        var tb = VoucherBuilder.TrialBalance(VoucherBuilder.Build(D, sources, Map)).ToDictionary(r => r.Account, r => r.BalancePaise);
        Assert.Equal((0L, 0L), (tb["1110"], tb["2000"]));
        Assert.Equal((-420_000L, -2_700_000L, 1_606_000L, 2_000_000L), (tb["1100"], tb["4300"], tb["1010"], tb["1030"]));
        Assert.True(AccountingEventMapper.GroupSettlement(new GroupSettledEvent(12, 420_000, 420_000), 0, D).IsZero);   // no deposit held → no voucher
    }

    // ── Staff advances (A03b-03 / A04-07) ──

    [Fact]
    public void Advance_given_is_a_payment_debiting_staff_advances_so_recoveries_never_drive_1200_negative()
    {
        // ₹3,000 advance (bank transfer), then two pay runs recover ₹1,000 each: 1200 = 3,000 − 1,000 − 1,000 = ₹1,000 debit (an asset), never a credit.
        var adv = One(AccountingEventMapper.Advance(new AdvanceGivenEvent(41, 5, 300_000, 100_000, new DateOnly(2026, 8, 10))));
        Assert.Equal((VoucherType.Payment, "payroll", "advance", "advance:41"), (adv.Type, adv.SourceKind, adv.Sub, adv.SourceKey));
        Assert.Equal([("1200", 300_000L, 0L), ("1030", 0L, 300_000L)], adv.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));   // W2 payload: no funding → bank
        Assert.Equal("1000", One(AccountingEventMapper.Advance(new AdvanceGivenEvent(42, 5, 100, 0, D, "Cash"))).Entries[1].Account);

        static PayrollPostedEvent Run(long id, DateOnly d) => new(id, "2026-08", d, 1_317_338, 83_504, 39_664, 77_081, 9_153, 20_000, 100_000, 0, 1_111_104);
        var balance = 0L;
        foreach (var s in new[] { AccountingEventMapper.Advance(new AdvanceGivenEvent(41, 5, 300_000, 100_000, new DateOnly(2026, 8, 10))),
                     AccountingEventMapper.PayRun(Run(1, new DateOnly(2026, 9, 1))), AccountingEventMapper.PayRun(Run(2, new DateOnly(2026, 10, 1))) })
        {
            var v = One(s);
            Assert.True(v.Balanced);
            balance += v.Entries.Where(x => x.Account == "1200").Sum(x => x.DebitPaise - x.CreditPaise);
            Assert.True(balance >= 0, $"1200 went negative after {v.SourceKey}");
        }

        Assert.Equal(100_000, balance);
    }

    // ── Full and final (A04-01 voucher, A04-06) ──

    /// <summary>
    /// Ravi (golden HR-S05), exit 31 Aug 2026, August salary in the settlement: gross ₹13,173.38 (earned 12,846.77 + OT 326.61), PF ₹770.81
    /// (employer 835.04), ESIC ₹91.53 (employer 396.64), PT ₹200; advance outstanding ₹3,000.
    /// </summary>
    private static FnfSettledEvent Ravi(long deductions, long advanceRecovered, long net, long leftOpen)
        => new(9, 5, "2026-08", 1_317_338, 106_234 + advanceRecovered + deductions, net, advanceRecovered, new DateOnly(2026, 8, 31), 1_317_338, 0, 77_081, 9_153, 20_000,
            83_504, 39_664, deductions, advanceRecovered, leftOpen, "Bank transfer");

    [Fact]
    public void Fnf_books_the_salary_journal_and_the_net_pay_payment_mirroring_the_pay_run()
    {
        // Full recovery: 13,173.38 − 1,062.34 statutory = 12,111.04 covers the ₹3,000 advance → net 9,111.04.
        // Journal: Dr 5100 13,173.38 · Dr 5110 835.04 + 396.64 = 1,231.68 | Cr 2310 770.81 + 835.04 = 1,605.85 · Cr 2320 91.53 + 396.64 = 488.17 ·
        // Cr 2330 200 · Cr 1200 3,000 · Cr 2300 9,111.04 → both sides 14,405.06. Payment: Dr 2300 9,111.04 · Cr 1030.
        var list = AccountingEventMapper.Fnf(Ravi(0, 300_000, 911_104, 0), new DateOnly(2026, 8, 31));
        Assert.Equal(2, list.Count);
        var j = One(list[0]);
        Assert.Equal((VoucherType.Journal, "fnf", "fnf:9"), (j.Type, j.Sub, j.SourceKey));
        Assert.True(j.Balanced);
        Assert.Equal(1_440_506, j.Debit);
        var a = ByAccount(j);
        Assert.Equal((1_317_338L, 0L), a["5100"]);
        Assert.Equal((123_168L, 0L), a["5110"]);
        Assert.Equal((0L, 160_585L), a["2310"]);
        Assert.Equal((0L, 48_817L), a["2320"]);
        Assert.Equal((0L, 20_000L), a["2330"]);
        Assert.Equal((0L, 300_000L), a["1200"]);
        Assert.Equal((0L, 911_104L), a["2300"]);
        var paid = One(list[1]);
        Assert.Equal((VoucherType.Payment, "fnf-payment", "fnf-paid:9"), (paid.Type, paid.Sub, paid.SourceKey));
        Assert.Equal([("2300", 911_104L, 0L), ("1030", 0L, 911_104L)], paid.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
    }

    [Fact]
    public void Fnf_partial_and_negative_settlements_credit_1200_only_with_the_part_recovered()
    {
        // ₹11,000 deduction: covered 12,111.04 − 11,000 = 1,111.04 of the ₹3,000 advance → net 0, ₹1,888.96 left open. Journal only (nothing to pay):
        // Dr 5100 13,173.38 · Dr 5110 1,231.68 | Cr 2310 1,605.85 · Cr 2320 488.17 · Cr 2330 200 · Cr 1200 1,111.04 · Cr 2340 11,000 → 14,405.06 each.
        var part = AccountingEventMapper.Fnf(Ravi(1_100_000, 111_104, 0, 188_896), new DateOnly(2026, 8, 31));
        var j = One(Assert.Single(part));
        Assert.True(j.Balanced);
        Assert.Equal(1_440_506, j.Credit);
        Assert.Equal((0L, 111_104L), ByAccount(j)["1200"]);
        Assert.Equal((0L, 1_100_000L), ByAccount(j)["2340"]);
        Assert.False(ByAccount(j).ContainsKey("2300"));
        Assert.Contains("advance 1888.96 left open", j.Narration, StringComparison.Ordinal);

        // ₹13,000 deduction: covered 12,111.04 − 13,000 = −888.96 → no advance recovered, net −888.96 recoverable: Dr 1200 888.96 (what the leaver owes).
        // Dr 13,173.38 + 1,231.68 + 888.96 = 15,294.02 = Cr 1,605.85 + 488.17 + 200 + 13,000.
        var neg = One(Assert.Single(AccountingEventMapper.Fnf(Ravi(1_300_000, 0, -88_896, 300_000), new DateOnly(2026, 8, 31))));
        Assert.True(neg.Balanced);
        Assert.Equal(1_529_402, neg.Debit);
        Assert.Equal((88_896L, 0L), ByAccount(neg)["1200"]);
        Assert.Contains("recoverable from the leaver 888.96", neg.Narration, StringComparison.Ordinal);
    }

    [Fact]
    public void Events_published_before_DEC_074_still_map_and_balance()
    {
        // W2/W3 payload shapes (anonymous objects): no business date, no split, no refund kind, no deposits held.
        var o = CanonicalJson.SerializerOptions;
        var fnf = JsonSerializer.Deserialize<FnfSettledEvent>(
            """{"advanceWrittenOffPaise":300000,"earningsPaise":1317338,"fnfId":9,"month":"2026-08","netPaise":911104,"recoveriesPaise":406234,"staffId":5}""", o)!;
        Assert.Null(fnf.BusinessDate);
        var list = AccountingEventMapper.Fnf(fnf, D);
        var j = One(list[0]);
        // Unsplit: Dr 5100 13,173.38 | Cr 1200 3,000 · Cr 2340 1,062.34 (statutory not split) · Cr 2300 9,111.04.
        Assert.True(j.Balanced);
        Assert.Equal([("5100", 1_317_338L, 0L), ("1200", 0L, 300_000L), ("2340", 0L, 106_234L), ("2300", 0L, 911_104L)], j.Entries.Select(x => (x.Account, x.DebitPaise, x.CreditPaise)));
        Assert.Contains("published before DEC-074", j.Narration, StringComparison.Ordinal);
        Assert.Equal(911_104, One(list[1]).Credit);

        var adv = JsonSerializer.Deserialize<AdvanceGivenEvent>("""{"advanceId":4,"amountPaise":300000,"date":"2026-08-10","installmentPaise":100000,"staffId":5}""", o)!;
        Assert.Equal(new DateOnly(2026, 8, 10), One(AccountingEventMapper.Advance(adv)).Date);
        var rec = JsonSerializer.Deserialize<GroupReceiptRecordedEvent>("""{"amountPaise":80000,"businessDate":"2026-10-24","entryId":3,"groupId":1,"kind":"refund","tender":"UPI"}""", o)!;
        Assert.Null(rec.RefundOfKind);
        var settled = JsonSerializer.Deserialize<GroupSettledEvent>("""{"chargesPaise":420000,"groupId":1,"receivedPaise":420000}""", o)!;
        Assert.Equal((null, null), (settled.DepositsHeldPaise, settled.BusinessDate));
        var attr = JsonSerializer.Deserialize<GroupAttritionPostedEvent>(
            """{"attritionId":2,"businessDate":"2026-10-24","cgstPaise":13125,"entryIds":[1,2,3],"groupId":1,"netPaise":525000,"sgstPaise":13125,"taxPaise":26250}""", o)!;
        Assert.Equal(551_250, One(AccountingEventMapper.GroupAttrition(attr)).Debit);
    }

    // ── Reconciliation, trial balance and Tally ──

    [Fact]
    public void Reconciliation_tally_xml_and_trial_balance_carry_the_new_vouchers()
    {
        var sources = new List<AccountingSource>
        {
            AccountingEventMapper.GroupAttrition(new GroupAttritionPostedEvent(12, 7, [101, 102, 103], 2_700_000, 486_000, 243_000, 243_000, D)),
            AccountingEventMapper.GroupReceipt(new GroupReceiptRecordedEvent(12, 32, "deposit", "Bank transfer", 2_000_000, D), null),
            AccountingEventMapper.GroupReceipt(new GroupReceiptRecordedEvent(12, 33, "refund", "Bank transfer", 80_000, D, "deposit"), "deposit"),
            AccountingEventMapper.Advance(new AdvanceGivenEvent(41, 5, 300_000, 100_000, D)),
        };
        sources.AddRange(AccountingEventMapper.Fnf(Ravi(0, 300_000, 911_104, 0), D));
        var vs = VoucherBuilder.Build(D, sources, Map);
        Assert.Equal(["SAL/20261024/001", "RCT/20261024/001", "PMT/20261024/001", "PMT/20261024/002", "PMT/20261024/003", "JRN/20261024/001"], vs.Select(v => v.Number));
        var (dr, cr) = VoucherBuilder.Totals(vs);
        Assert.Equal(dr, cr);
        Assert.Empty(VoucherBuilder.Exceptions(vs, vs, false, DayStatus.Closed));

        // Attrition ₹27,000 net + ₹4,860 GST; master receipts in − out = 20,000 − 800 = ₹19,200.
        var checks = Reconciliation.Check(vs, Map, new OperationalFigures(0, 0, 0, 0, 0, 0, 0, 0, 2_700_000, 486_000, 1_920_000)).ToDictionary(c => c.Id);
        Assert.True(checks["group-attrition"].Ok);
        Assert.True(checks["tax"].Ok);
        Assert.Equal(486_000, checks["tax"].LedgerPaise);
        Assert.True(checks["group-receipts"].Ok);
        Assert.Equal(1_920_000, checks["group-receipts"].LedgerPaise);
        Assert.False(Reconciliation.Check(vs, Map, new OperationalFigures(0, 0, 0, 0, 0, 0, 0, 0, 2_700_000, 486_000, 2_000_000)).Single(c => c.Id == "group-receipts").Ok);

        var xml = VoucherExport.TallyXml(vs, "HCC Demo Residency", "DEMO");
        Assert.Contains("VCHTYPE=\"Sales\" ACTION=\"Create\"", xml, StringComparison.Ordinal);
        Assert.Contains("<LEDGERNAME>Company receivables (city ledger)</LEDGERNAME><ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><AMOUNT>-31860.00</AMOUNT>", xml, StringComparison.Ordinal);
        Assert.Contains("<LEDGERNAME>Staff advances</LEDGERNAME><ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><AMOUNT>-3000.00</AMOUNT>", xml, StringComparison.Ordinal);
        Assert.Contains("<VOUCHERTYPENAME>Journal</VOUCHERTYPENAME><VOUCHERNUMBER>JRN/20261024/001</VOUCHERNUMBER><NARRATION>Full and final 2026-08", xml, StringComparison.Ordinal);
        Assert.All(new[] { "line:attrition", "receipt:group-deposit", "payroll:fnf-additions", "payroll:fnf-deductions", "payroll:fnf-recoverable" },
            k => Assert.True(ChartOfAccounts.IsValidKey(k) && ChartOfAccounts.Resolve(Map, k) is not null, k));
    }
}
