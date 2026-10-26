using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Payroll;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Payroll;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Payroll.Tests.Harness;

namespace Hcc.Payroll.Tests.Sql;

/// <summary>
/// DEC-074 (wave B3, A03b-03, A04-07, A04-01 voucher, A04-06): staff advances and full-and-final settlements through Accounting's real
/// outbox consumer, on the BASE payroll golden (Ravi HR-S05: ₹3,000 advance on 10 Aug 2026, ₹1,000 a month). Ledger 1200 Staff advances
/// follows the advance register and is never a credit; a full and final recovers only the part of the advance it covers.
/// </summary>
[Trait("Category", "Sql")]
public sealed class AdvanceFnfAccountingTests(PayrollDb db) : IClassFixture<PayrollDb>
{
    private const long Advance = 300_000;   // ₹3,000
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AccountingVoucherProjector Projector => new(db.Platform.Connections, db.Platform.Outbox, new StoredChargeGstReader(), db.Clock);

    private AccountingQueries Books => new(new SqlAccountingStore(db.Platform.Connections), db.Policy);

    private async Task<Dictionary<string, long>> BalancesAsync(Hotel h, DateOnly asOf)
    {
        var tb = await Books.TrialBalanceAsync(new Caller(h.PropertyId, h.U("owner")), asOf, true, Ct);
        Assert.True(tb.Balanced);
        return tb.Rows.ToDictionary(r => r.Account, r => r.BalancePaise);
    }

    private static long B(Dictionary<string, long> tb, string account) => tb.GetValueOrDefault(account);

    private async Task<long> RegisterBalanceAsync(Hotel h, long staffId)
        => await db.LongAsync("SELECT ISNULL(SUM(amount_paise - recovered_paise), 0) FROM hr.advance WHERE staff_id = @s;", ("@s", staffId));

    /// <summary>The golden advance given through the command on its golden date (publishes AdvanceGiven), then back to <paramref name="backTo"/>.</summary>
    private static async Task<AdvanceResult> GiveGoldenAdvanceAsync(Hotel h, string backTo)
    {
        await h.SetBusinessDateAsync("2026-08-10");
        var r = (await h.Payroll.GiveAdvanceAsync(h.As("accountant", PayrollService.OpGiveAdvance), new GiveAdvanceRequest(h.S("HR-S05"), Advance, 100_000, "Festival advance (fictional)"), Ct)).Value;
        await h.SetBusinessDateAsync(backTo);
        return r;
    }

    [Fact]
    public async Task Golden_advance_is_booked_when_given_and_two_pay_runs_recover_it_without_1200_ever_going_negative()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync(advances: false);
        var ravi = h.S("HR-S05");
        var adv = await GiveGoldenAdvanceAsync(h, "2026-10-24");
        await Projector.RunOnceAsync(ct: Ct);
        var asOf = new DateOnly(2026, 10, 24);

        // Given: Payment Dr 1200 ₹3,000 · Cr 1030 Bank ₹3,000 (dated 10 Aug).
        var day = await Books.DayAsync(new Caller(h.PropertyId, h.U("owner")), new DateOnly(2026, 8, 10), Ct);
        var v = Assert.Single(day.Vouchers);
        Assert.Equal(("PMT/20260810/001", "Payment", "advance", "advance:" + adv.AdvanceId), (v.Number, v.Type, v.Sub, v.SourceKey));
        Assert.Equal([("1200", Advance, 0L), ("1030", 0L, Advance)], v.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        var tb = await BalancesAsync(h, asOf);
        Assert.Equal((Advance, -Advance), (B(tb, "1200"), B(tb, "1030")));
        Assert.Equal(Advance, await RegisterBalanceAsync(h, ravi));

        // August then September locked: each recovers ₹1,000 (golden lines) → 1200 = ₹2,000, then ₹1,000 — always the register balance, never a credit.
        foreach (var (month, left) in new[] { ("2026-08", 200_000L), ("2026-09", 100_000L) })
        {
            var run = (await h.Payroll.PreparePayRunAsync(h.As("accountant", PayrollService.OpPreparePayRun), new PreparePayRunRequest(month), Ct)).Value;
            await h.Payroll.LockPayRunAsync(h.As("owner", PayrollService.OpLockPayRun), new LockPayRunRequest(run.RunId, run.Version), Ct);
            await Projector.RunOnceAsync(ct: Ct);
            tb = await BalancesAsync(h, asOf);
            Assert.Equal(left, await RegisterBalanceAsync(h, ravi));
            Assert.Equal(left, B(tb, "1200"));
            Assert.True(B(tb, "1200") >= 0, $"1200 is a credit after {month}");
        }

        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'AdvanceGiven' AND property_id = @p;", ("@p", h.PropertyId)));
        Assert.Contains("\"funding\":\"Bank transfer\"", (string)(await db.ScalarAsync("SELECT payload FROM plat.outbox WHERE event_type = 'AdvanceGiven' AND property_id = @p;", ("@p", h.PropertyId)))!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Ravi leaves on 31 Aug 2026 with August's salary in the settlement (no August run locked): gross ₹13,173.38 (earned 12,846.77 + OT
    /// 326.61), PF ₹770.81 (employer 835.04), ESIC ₹91.53 (employer 396.64), PT ₹200 → statutory ₹1,062.34; before the advance the settlement
    /// is worth 13,173.38 − 1,062.34 − deductions.
    /// </summary>
    private async Task<(Hotel H, long Ravi, FnfResult Fnf)> LeaverAsync(long deductionPaise)
    {
        var h = await db.NewHotelAsync("2026-08-31");
        await h.SeedGoldenAsync(advances: false);
        var ravi = h.S("HR-S05");
        await GiveGoldenAdvanceAsync(h, "2026-08-31");
        await h.Payroll.SetStaffActiveAsync(h.As("manager", PayrollService.OpSetStaffActive), new SetStaffActiveRequest(ravi, await h.StaffVersionAsync(ravi), false), Ct);
        IReadOnlyList<FnfAdjustmentDto> adj = deductionPaise > 0 ? [new FnfAdjustmentDto("Damage recovery (fictional)", "ded", deductionPaise)] : [];
        var draft = (await h.Payroll.PrepareFnfAsync(h.As("accountant", PayrollService.OpPrepareFnf), new PrepareFnfRequest(ravi, adj), Ct)).Value;
        var settled = (await h.Payroll.ConfirmFnfAsync(h.As("owner", PayrollService.OpConfirmFnf), new DecideFnfRequest(draft.FnfId, draft.Version), Ct)).Value;
        await Projector.RunOnceAsync(ct: Ct);
        return (h, ravi, settled);
    }

    [Fact]
    public async Task Fnf_with_full_recovery_books_the_salary_journal_and_the_payment_and_clears_1200()
    {
        // 12,111.04 covers the ₹3,000 advance: net 9,111.04 paid. Journal: Dr 5100 13,173.38 · Dr 5110 1,231.68 | Cr 2310 1,605.85 · Cr 2320 488.17 ·
        // Cr 2330 200 · Cr 1200 3,000 · Cr 2300 9,111.04 (14,405.06 each side). Payment: Dr 2300 · Cr 1030 9,111.04.
        var (h, ravi, f) = await LeaverAsync(0);
        Assert.Equal((911_104L, 1_317_338L, 406_234L), (f.NetPaise, f.EarningsPaise, f.RecoveriesPaise));
        var day = await Books.DayAsync(new Caller(h.PropertyId, h.U("owner")), new DateOnly(2026, 8, 31), Ct);
        var j = day.Vouchers.Single(v => v.SourceKey == "fnf:" + f.FnfId);
        Assert.Equal(("Journal", "fnf", 1_440_506L, true), (j.Type, j.Sub, j.DebitPaise, j.Balanced));
        var paid = day.Vouchers.Single(v => v.SourceKey == "fnf-paid:" + f.FnfId);
        Assert.Equal(("Payment", 911_104L), (paid.Type, paid.CreditPaise));
        Assert.Equal([("2300", 911_104L, 0L), ("1030", 0L, 911_104L)], paid.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));

        var tb = await BalancesAsync(h, new DateOnly(2026, 8, 31));
        Assert.Equal((0L, 0L, -1_211_104L), (B(tb, "1200"), B(tb, "2300"), B(tb, "1030")));   // bank: advance 3,000 + net pay 9,111.04
        Assert.Equal((1_317_338L, 123_168L, -160_585L, -48_817L, -20_000L), (B(tb, "5100"), B(tb, "5110"), B(tb, "2310"), B(tb, "2320"), B(tb, "2330")));
        Assert.Equal(0, await RegisterBalanceAsync(h, ravi));

        var tally = await Books.TallyAsync(new Caller(h.PropertyId, h.U("owner")), new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 31), Ct);
        Assert.Equal((3, true), (tally.VoucherCount, tally.DebitPaise == tally.CreditPaise));
        Assert.Contains("<NARRATION>Salary advance given · Bank transfer", tally.Xml, StringComparison.Ordinal);
        Assert.Contains("<VOUCHERTYPENAME>Journal</VOUCHERTYPENAME>", tally.Xml, StringComparison.Ordinal);
        Assert.Contains("<LEDGERNAME>Staff advances</LEDGERNAME><ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE><AMOUNT>3000.00</AMOUNT>", tally.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fnf_with_partial_recovery_records_only_the_covered_part_and_the_rest_stays_an_open_advance()
    {
        // ₹11,000 damage: 12,111.04 − 11,000 = 1,111.04 covers part of the ₹3,000 → net 0, ₹1,888.96 still owed. Before DEC-074 this was
        // net −1,888.96 with the whole advance marked recovered (A04-06). Journal only: Cr 1200 1,111.04 · Cr 2340 11,000; no payment.
        var (h, ravi, f) = await LeaverAsync(1_100_000);
        Assert.Equal((0L, 106_234L + 111_104L + 1_100_000L), (f.NetPaise, f.RecoveriesPaise));
        Assert.Equal(188_896, await RegisterBalanceAsync(h, ravi));
        Assert.Equal(111_104, await db.LongAsync("SELECT SUM(amount_paise) FROM hr.advance_recovery WHERE fnf_id = @f;", ("@f", f.FnfId)));
        Assert.Contains("advance ₹1,111.04 recovered, ₹1,888.96 left open", (string)(await db.ScalarAsync("SELECT detail FROM plat.audit_event WHERE action = 'HR_FNF_SETTLED' AND target = @t;", ("@t", "hr.fnf:" + f.FnfId)))!,
            StringComparison.Ordinal);
        var day = await Books.DayAsync(new Caller(h.PropertyId, h.U("owner")), new DateOnly(2026, 8, 31), Ct);
        var j = Assert.Single(day.Vouchers);
        Assert.Equal(("fnf", true), (j.Sub, j.Balanced));
        Assert.Contains("advance 1888.96 left open", j.Narration, StringComparison.Ordinal);
        var tb = await BalancesAsync(h, new DateOnly(2026, 8, 31));
        Assert.Equal((188_896L, 0L, -1_100_000L, -Advance), (B(tb, "1200"), B(tb, "2300"), B(tb, "2340"), B(tb, "1030")));   // 1200 = the register balance

        // A negative settlement (₹13,000 damage, 888.96 beyond the settlement): no advance recovered, ₹3,000 stays open, the ₹888.96 the leaver owes
        // is Dr 1200 (recoverable): 1200 = 3,000 + 888.96.
        var (h2, ravi2, neg) = await LeaverAsync(1_300_000);
        Assert.Equal(-88_896, neg.NetPaise);
        Assert.Equal(Advance, await RegisterBalanceAsync(h2, ravi2));
        Assert.Equal(0, await db.LongAsync("SELECT COUNT(*) FROM hr.advance_recovery WHERE fnf_id = @f;", ("@f", neg.FnfId)));
        var tb2 = await BalancesAsync(h2, new DateOnly(2026, 8, 31));
        Assert.Equal((Advance + 88_896, -1_300_000L), (B(tb2, "1200"), B(tb2, "2340")));
    }
}
