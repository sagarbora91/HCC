using System.Data;
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Payroll;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Accounting;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Payroll.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Payroll.Tests.Sql;

/// <summary>
/// W3-b: Payroll lock / unlock / re-lock through Accounting's real outbox consumer, with the BASE payroll golden (August 2026, 12 staff,
/// net ₹2,12,101.66). Lock → pay-run voucher; unlock → exact reversal (nets to zero, booked once); re-lock → a fresh voucher; a bank file
/// issued before the unlock keeps its payment voucher and the reversal says so.
/// </summary>
[Trait("Category", "Sql")]
public sealed class PayRunAccountingTests(PayrollDb db) : IClassFixture<PayrollDb>
{
    private const long AugustNet = 21_210_166;            // ₹2,12,101.66
    private const string NetPayable = "2300";
    private const string Bank = "1030";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AccountingVoucherProjector Projector => new(db.Platform.Connections, db.Platform.Outbox, new StoredChargeGstReader(), db.Clock);

    private async Task<TrialBalanceView> TrialBalanceAsync(Hotel h) =>
        await new AccountingQueries(new SqlAccountingStore(db.Platform.Connections), db.Policy).TrialBalanceAsync(new Caller(h.PropertyId, h.U("owner")), new DateOnly(2026, 10, 24), true, Ct);

    private static long Balance(TrialBalanceView tb, string account) => tb.Rows.SingleOrDefault(r => r.Account == account)?.BalancePaise ?? 0;

    private async Task<List<(string Key, string Sub, string Narration, List<SourceEntry> Entries)>> SourcesAsync(Hotel h)
    {
        var list = new List<(string, string, string, List<SourceEntry>)>();
        await using var conn = await db.Platform.Connections.OpenAsync(Ct);
        await using var cmd = new SqlCommand("SELECT s.source_id, s.source_key, s.sub, s.narration FROM acc.source s WHERE s.property_id = @p ORDER BY s.source_id;", conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = h.PropertyId;
        var ids = new List<(long, string, string, string)>();
        await using (var rd = await cmd.ExecuteReaderAsync(Ct))
            while (await rd.ReadAsync(Ct)) ids.Add((rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3)));
        foreach (var (id, key, sub, narration) in ids)
        {
            var entries = new List<SourceEntry>();
            await using var e = new SqlCommand("SELECT map_key, debit_paise, credit_paise FROM acc.source_entry WHERE source_id = @s ORDER BY line_no;", conn);
            e.Parameters.Add("@s", SqlDbType.BigInt).Value = id;
            await using var rd = await e.ExecuteReaderAsync(Ct);
            while (await rd.ReadAsync(Ct)) entries.Add(new SourceEntry(rd.GetString(0), rd.GetInt64(1), rd.GetInt64(2)));
            list.Add((key, sub, narration, entries));
        }

        return list;
    }

    private static async Task<PayRunResult> PrepareAugustAsync(Hotel h)
        => (await h.Payroll.PreparePayRunAsync(h.As("accountant", PayrollService.OpPreparePayRun), new PreparePayRunRequest("2026-08"), Ct)).Value;

    private static async Task<PayRunResult> LockAsync(Hotel h, PayRunResult run)
        => (await h.Payroll.LockPayRunAsync(h.As("owner", PayrollService.OpLockPayRun), new LockPayRunRequest(run.RunId, run.Version), Ct)).Value;

    private static async Task<PayRunResult> UnlockAsync(Hotel h, PayRunResult run, bool confirmBankFile = false)
        => (await h.Payroll.UnlockPayRunAsync(h.As("owner", PayrollService.OpUnlockPayRun), new UnlockPayRunRequest(run.RunId, run.Version, "Correction of attendance", confirmBankFile), Ct)).Value;

    [Fact]
    public void Reversal_is_the_exact_negative_of_the_pay_run_voucher()
    {
        var posted = AccountingEventMapper.PayRun(new PayrollPostedEvent(7, "2026-08", new DateOnly(2026, 9, 1), 25_000_000, 1_200_000, 100_000, 1_200_000, 50_000, 20_000,
            100_000, 2_419_834, AccountNet()));
        var reversal = AccountingEventMapper.PayRunReversal(new PayRunUnlockedEvent(7, "2026-08", 1, 0, new DateOnly(2026, 10, 24)), new DateOnly(2026, 10, 24), posted.SourceKey, posted.Entries, false);
        Assert.Equal(("payroll:7", "payroll-unlock:7/lock1", VoucherType.Journal), (posted.SourceKey, reversal.SourceKey, reversal.Type));
        Assert.Equal(posted.Entries.Select(e => (e.MapKey, e.CreditPaise, e.DebitPaise)), reversal.Entries.Select(e => (e.MapKey, e.DebitPaise, e.CreditPaise)));
        Assert.Equal(posted.Debit, reversal.Credit);
        Assert.DoesNotContain("BANK FILE", reversal.Narration, StringComparison.Ordinal);
        Assert.Equal("payroll:7/lock2", AccountingEventMapper.PayRunKey(7, 2));
        Assert.Contains("BANK FILE ALREADY ISSUED", AccountingEventMapper.PayRunReversal(new PayRunUnlockedEvent(7, "2026-08", 1, 1), new DateOnly(2026, 10, 24), posted.SourceKey, posted.Entries, true).Narration, StringComparison.Ordinal);

        static long AccountNet() => 25_000_000 - 1_200_000 - 50_000 - 20_000 - 100_000 - 2_419_834;
    }

    [Fact]
    public async Task Golden_august_lock_voucher_unlock_reversal_nets_to_zero_once_and_relock_posts_a_fresh_voucher()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        var aug = await PrepareAugustAsync(h);
        Assert.Equal(AugustNet, aug.NetPaise);

        // Lock → one pay-run voucher (Cr salaries payable = the golden net).
        var locked = await LockAsync(h, aug);
        await Projector.RunOnceAsync(ct: Ct);
        var s1 = await SourcesAsync(h);
        var posted = Assert.Single(s1);
        Assert.Equal(("payroll:" + aug.RunId, "payrun"), (posted.Key, posted.Sub));
        Assert.Equal(AugustNet, posted.Entries.Single(e => e.MapKey == "payroll:net-payable").CreditPaise);
        var tb1 = await TrialBalanceAsync(h);
        Assert.True(tb1.Balanced);
        Assert.Equal(-AugustNet, Balance(tb1, NetPayable));

        // Unlock (no bank file) → the exact reversal; every ledger back to zero.
        var unlocked = await UnlockAsync(h, locked);
        await Projector.RunOnceAsync(ct: Ct);
        var s2 = await SourcesAsync(h);
        Assert.Equal(2, s2.Count);
        var rev = s2[1];
        Assert.Equal(($"payroll-unlock:{aug.RunId}/lock1", "payrun-reversal"), (rev.Key, rev.Sub));
        Assert.Equal(posted.Entries.Select(e => (e.MapKey, e.CreditPaise, e.DebitPaise)), rev.Entries.Select(e => (e.MapKey, e.DebitPaise, e.CreditPaise)));
        Assert.DoesNotContain("BANK FILE", rev.Narration, StringComparison.Ordinal);
        var tb2 = await TrialBalanceAsync(h);
        Assert.True(tb2.Balanced);
        Assert.All(tb2.Rows, r => Assert.Equal(0, r.BalancePaise));

        // Consumed exactly once: the consumer again, and the same event delivered again, add nothing.
        Assert.Equal(0, await Projector.RunOnceAsync(ct: Ct));
        await RedeliverAsync(AccountingEvents.PayRunUnlocked, aug.RunId);
        Assert.Equal(2, (await SourcesAsync(h)).Count);

        // Re-lock (lock 2) → a fresh voucher with its own key; the books show the payroll once.
        await LockAsync(h, unlocked);
        await Projector.RunOnceAsync(ct: Ct);
        var s3 = await SourcesAsync(h);
        Assert.Equal(3, s3.Count);
        Assert.Equal(($"payroll:{aug.RunId}/lock2", "payrun"), (s3[2].Key, s3[2].Sub));
        Assert.Equal(posted.Entries, s3[2].Entries);
        var tb3 = await TrialBalanceAsync(h);
        Assert.True(tb3.Balanced);
        Assert.Equal(-AugustNet, Balance(tb3, NetPayable));
        Assert.Equal(tb1.Rows.Select(r => (r.Account, r.BalancePaise)), tb3.Rows.Where(r => r.BalancePaise != 0).Select(r => (r.Account, r.BalancePaise)));
    }

    [Fact]
    public async Task Bank_file_issued_before_unlock_keeps_the_payment_voucher_and_flags_the_reversal()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        var locked = await LockAsync(h, await PrepareAugustAsync(h));
        await h.Payroll.IssueBankFileAsync(h.As("accountant", PayrollService.OpIssueBankFile), new IssueBankFileRequest(locked.RunId), Ct);
        await Projector.RunOnceAsync(ct: Ct);
        var tbPaid = await TrialBalanceAsync(h);
        Assert.Equal((0L, -AugustNet), (Balance(tbPaid, NetPayable), Balance(tbPaid, Bank)));

        var unlocked = await UnlockAsync(h, locked, confirmBankFile: true);
        await Projector.RunOnceAsync(ct: Ct);
        var sources = await SourcesAsync(h);
        Assert.Equal(["payrun", "payment", "payrun-reversal"], sources.Select(s => s.Sub));   // payment kept, not reversed
        Assert.Contains("BANK FILE ALREADY ISSUED", sources[2].Narration, StringComparison.Ordinal);
        var tb = await TrialBalanceAsync(h);
        Assert.True(tb.Balanced);
        Assert.Equal((AugustNet, -AugustNet), (Balance(tb, NetPayable), Balance(tb, Bank)));   // paid out, payable reversed: staff owe it back until re-lock

        await LockAsync(h, unlocked);
        await Projector.RunOnceAsync(ct: Ct);
        var tbRelocked = await TrialBalanceAsync(h);
        Assert.True(tbRelocked.Balanced);
        Assert.Equal((0L, -AugustNet), (Balance(tbRelocked, NetPayable), Balance(tbRelocked, Bank)));   // one payroll, one payment
    }

    /// <summary>Delivers the stored outbox event to the consumer's handler a second time (as a redelivery would).</summary>
    private async Task RedeliverAsync(string eventType, long aggregateId)
    {
        await using var conn = await db.Platform.Connections.OpenAsync(Ct);
        OutboxEvent evt;
        await using (var q = new SqlCommand("SELECT TOP (1) outbox_id, payload, created_utc FROM plat.outbox WHERE event_type = @e AND aggregate_id = @a ORDER BY outbox_id DESC;", conn))
        {
            q.Parameters.Add("@e", SqlDbType.VarChar, 60).Value = eventType;
            q.Parameters.Add("@a", SqlDbType.BigInt).Value = aggregateId;
            await using var rd = await q.ExecuteReaderAsync(Ct);
            Assert.True(await rd.ReadAsync(Ct));
            evt = new OutboxEvent(rd.GetInt64(0), eventType, aggregateId, rd.GetString(1), rd.GetDateTime(2));
        }

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(Ct);
        await Projector.ApplyAsync(evt, conn, tx, Ct);
        await tx.CommitAsync(Ct);
    }
}
