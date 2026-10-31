using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Payroll;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Accounting;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Payroll.Tests.Harness;

namespace Hcc.Payroll.Tests.Sql;

/// <summary>
/// ACC-2 parity (02 Planning/agents/ACC2-POSTING-CONTRACT.md §8; ACC1-DONE §7 item 3): the pay-run lock publishes the advance recovered PER
/// STAFF member (PayrollPostedEvent.AdvanceRecoveries) and the voucher credits <c>1200/STF-&lt;id&gt;</c> — the 1200 control itself stays
/// empty, so the day carries no <c>no-party</c> warning for it; the unlock reversal takes the same party back. BASE payroll golden (Ravi
/// HR-S05: ₹3,000 advance on 10 Aug 2026, ₹1,000 a month).
/// </summary>
[Trait("Category", "Sql")]
public sealed class PerStaffRecoveryTests(PayrollDb db) : IClassFixture<PayrollDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AccountingVoucherProjector Projector => new(db.Platform.Connections, db.Platform.Outbox, new StoredChargeGstReader(), db.Clock);

    private AccountingQueries Books => new(new SqlAccountingStore(db.Platform.Connections), db.Policy);

    [Fact]
    public async Task Lock_credits_the_recovery_to_the_staff_party_and_the_unlock_reverses_the_same_party()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync(advances: false);
        var ravi = h.S("HR-S05");
        await h.SetBusinessDateAsync("2026-08-10");
        await h.Payroll.GiveAdvanceAsync(h.As("accountant", PayrollService.OpGiveAdvance), new GiveAdvanceRequest(ravi, 300_000, 100_000, "Festival advance (fictional)"), Ct);
        await h.SetBusinessDateAsync("2026-10-24");
        var run = (await h.Payroll.PreparePayRunAsync(h.As("accountant", PayrollService.OpPreparePayRun), new PreparePayRunRequest("2026-08"), Ct)).Value;
        var locked = (await h.Payroll.LockPayRunAsync(h.As("owner", PayrollService.OpLockPayRun), new LockPayRunRequest(run.RunId, run.Version), Ct)).Value;
        await Projector.RunOnceAsync(ct: Ct);

        // the event names each staff member's recovery
        var payload = (string)(await db.ScalarAsync("SELECT payload FROM plat.outbox WHERE event_type = 'PayrollPosted' AND property_id = @p;", ("@p", h.PropertyId)))!;
        Assert.Contains("\"advanceRecoveries\":[{\"paise\":100000,\"staffId\":" + ravi + "}]", payload, StringComparison.Ordinal); // wire {staffId, paise} (outbox JSON keys sorted)

        var owner = new Caller(h.PropertyId, h.U("owner"));
        // coordinator ruling 2: the pay-run voucher is dated the period's last day (31 Aug); the narration carries the lock date
        var day = await Books.DayAsync(owner, new DateOnly(2026, 8, 31), Ct);
        var v = day.Vouchers.Single(x => x.SourceKey == "payroll:" + run.RunId);
        Assert.EndsWith(" · locked 2026-10-24", v.Narration, StringComparison.Ordinal);
        var staff = "1200/" + PartyLedgers.Staff(ravi);
        Assert.Equal([(staff, 0L, 100_000L)], v.Entries.Where(e => e.Account.StartsWith("1200", StringComparison.Ordinal)).Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        Assert.DoesNotContain(day.Exceptions, x => x.Kind == "no-party" && x.Id.EndsWith(":1200", StringComparison.Ordinal));
        var tb = await Books.TrialBalanceAsync(owner, new DateOnly(2026, 10, 24), true, Ct);
        Assert.Equal(200_000, tb.Rows.Single(r => r.Account == "1200").BalancePaise);   // the advance register: ₹3,000 − ₹1,000

        // unlock → the reversal debits the same staff party; the run's books net to nil on every ledger the pay run touched
        await h.Payroll.UnlockPayRunAsync(h.As("owner", PayrollService.OpUnlockPayRun), new UnlockPayRunRequest(locked.RunId, locked.Version, "Correction of attendance", false), Ct);
        await Projector.RunOnceAsync(ct: Ct);
        day = await Books.DayAsync(owner, new DateOnly(2026, 10, 24), Ct);
        var rev = day.Vouchers.Single(x => x.SourceKey == $"payroll-unlock:{run.RunId}/lock1");
        Assert.Contains(rev.Entries, e => e.Account == staff && e.DebitPaise == 100_000);
        tb = await Books.TrialBalanceAsync(owner, new DateOnly(2026, 10, 24), true, Ct);
        Assert.Equal(300_000, tb.Rows.Single(r => r.Account == "1200").BalancePaise);
    }
}
