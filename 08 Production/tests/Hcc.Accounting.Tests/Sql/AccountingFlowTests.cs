using System.Data;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Billing;
using Hcc.Infrastructure.SqlServer.Accounting;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>
/// Billing / cashier commands → outbox → accounting consumer → vouchers that balance and reconcile with bill.charge / pay.receipt;
/// then lock (closed day only, no pending events), snapshot drift, unlock with reason and version. Real P1 policy and grants.
/// </summary>
[Trait("Category", "Sql")]
public sealed class AccountingFlowTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static readonly DateOnly D = AccountingDb.Day;
    private static readonly DateOnly Y = AccountingDb.Day.AddDays(-1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    [Fact]
    public async Task Folio_day_becomes_balanced_vouchers_that_reconcile_then_lock_drift_and_unlock()
    {
        // ── a day of folio activity (P4 commands, real grants) ──
        var f = await db.NewFolioAsync(ratePaise: 200_000, taxBps: 1000);
        // P6 late-write barrier: a night posts only on its own business date, so yesterday's is posted before the date moves to D.
        await db.ScalarAsync("UPDATE inv.property SET business_date = @d WHERE property_id = @p;", ("@d", SqlDbType.Date, Y.ToDateTime(TimeOnly.MinValue)), ("@p", SqlDbType.Int, db.PropertyId));
        await db.Billing.PostRoomNightAsync(db.As("OWN"), Key(), new PostRoomNightRequest(f, Y), Ct);
        await db.ScalarAsync("UPDATE inv.property SET business_date = @d WHERE property_id = @p;", ("@d", SqlDbType.Date, D.ToDateTime(TimeOnly.MinValue)), ("@p", SqlDbType.Int, db.PropertyId));
        await db.Billing.PostRoomNightAsync(db.As("OWN"), Key(), new PostRoomNightRequest(f, D), Ct);
        var laundry = (await db.Billing.PostChargeAsync(db.As("OWN"), Key(), new PostChargeRequest(f, "Laundry", 50_000), Ct)).Value;
        await db.Billing.RequestAdjustmentAsync(db.As("OWN"), Key(), new RequestAdjustmentRequest("discount", f, null, 20_000, "Goodwill", await db.FolioVersionAsync(f)), Ct);
        var upi = (await db.Billing.RecordReceiptAsync(db.As("OWN"), Key(), new RecordReceiptRequest(f, "payment", "UPI", 300_000, "DEMO-UPI", null), Ct)).Value;
        var corr = (await db.Billing.RequestAdjustmentAsync(db.As("OWN"), Key(), new RequestAdjustmentRequest("correction", f, laundry.ChargeId, 50_000, "Posted twice", await db.FolioVersionAsync(f)), Ct)).Value;
        await db.Billing.DecideApprovalAsync(db.As("MGR"), Key(), new DecideApprovalRequest(corr.ApprovalId!.Value, true, null, corr.ApprovalVersion!), Ct);
        var refund = (await db.Billing.RequestAdjustmentAsync(db.As("MGR"), Key(), new RequestAdjustmentRequest("refund", f, upi.ReceiptId, 5_000, "Overcharge", await db.FolioVersionAsync(f)), Ct)).Value;
        var decided = (await db.Billing.DecideApprovalAsync(db.As("OWN"), Key(), new DecideApprovalRequest(refund.ApprovalId!.Value, true, null, refund.ApprovalVersion!), Ct)).Value;
        await db.Billing.ExecuteAdjustmentAsync(db.As("OWN"), Key(), new ExecuteAdjustmentRequest(refund.ApprovalId.Value, decided.Version, null, "DEMO-REFUND", null), Ct);

        // ── cash: shift, cash payment, count ₹50 short, accepted by the Owner (no P4 event → swept from pay.shift) ──
        var till = (await db.Cashier.CreateTillAsync(db.As("OWN"), Key(), new CreateTillRequest("FD1", "Front desk"), Ct)).Value;
        var shift = (await db.Cashier.OpenShiftAsync(db.As("CSH"), Key(), new OpenShiftRequest(till.TillId, 500_000), Ct)).Value;
        await db.Billing.RecordReceiptAsync(db.As("CSH"), Key(), new RecordReceiptRequest(f, "payment", "Cash", 100_000, null, till.TillId), Ct);
        var counted = (await db.Cashier.SubmitShiftCountAsync(db.As("CSH"), Key(), new SubmitShiftCountRequest(shift.ShiftId, 595_000, null, null, "Short by Rs 50", shift.Version), Ct)).Value;
        var apr = await db.LongAsync("SELECT approval_id FROM pay.shift WHERE shift_id = @s;", ("@s", SqlDbType.BigInt, shift.ShiftId));
        var aprVersion = Convert.ToBase64String((byte[])(await db.ScalarAsync("SELECT row_ver FROM bill.approval WHERE approval_id = @a;", ("@a", SqlDbType.BigInt, apr)))!);
        Assert.Equal("Submitted", counted.Status);
        await db.Billing.DecideApprovalAsync(db.As("OWN"), Key(), new DecideApprovalRequest(apr, true, null, aprVersion), Ct);

        // Before the consumer runs, the day reports pending events (blocks locking).
        var before = await db.Queries.DayAsync(db.As("ACC"), D, Ct);
        Assert.Contains(before.Exceptions, x => x.Kind == "pending");

        Assert.True(await db.ProjectAsync() >= 10);
        // Vouchers took CGST/SGST from agent C's per-charge GST records (room nights, laundry, discount, reversal), not a recomputation.
        Assert.Equal(5, await db.LongAsync("SELECT COUNT(*) FROM tax.charge_gst g JOIN bill.charge c ON c.charge_id = g.charge_id WHERE c.folio_id = @f;", ("@f", SqlDbType.BigInt, f)));
        Assert.Equal(0, await db.ProjectAsync()); // exactly once per event

        var day = await db.Queries.DayAsync(db.As("ACC"), D, Ct);
        Assert.Equal("Open", day.Status);
        Assert.True(day.Provisional);
        Assert.True(day.Balanced);
        Assert.All(day.Vouchers, v => Assert.True(v.Balanced, v.Number));
        Assert.Equal(0, day.Errors);
        Assert.All(day.Reconciliation, c => Assert.True(c.Ok, $"{c.Id}: report {c.ReportPaise} ledger {c.LedgerPaise}"));
        Assert.Equal(8, day.VoucherCount); // night, laundry, discount, correction, UPI, cash, refund, cash short
        Assert.Equal(["SAL/20261024/001", "SAL/20261024/002", "SAL/20261024/003", "SAL/20261024/004", "RCT/20261024/001", "RCT/20261024/002", "PMT/20261024/001", "JRN/20261024/001"],
            day.Vouchers.Select(v => v.Number));

        // Stored tax split into CGST / SGST (10 % fixed source today: ₹500 laundry → ₹50 tax → ₹25 + ₹25).
        var service = day.Vouchers.Single(v => v.Sub == "service");
        Assert.Equal([("1100", 55_000L, 0L), ("4100", 0L, 50_000L), ("2110", 0L, 2_500L), ("2120", 0L, 2_500L)], service.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        var reversal = day.Vouchers.Single(v => v.Sub == "correction");
        Assert.Equal([("1100", 0L, 55_000L), ("4910", 50_000L, 0L), ("2110", 2_500L, 0L), ("2120", 2_500L, 0L)], reversal.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        Assert.Equal([("1100", 5_000L, 0L), ("1010", 0L, 5_000L)], day.Vouchers.Single(v => v.Sub == "refund").Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        Assert.Equal([("5900", 5_000L, 0L), ("1000", 0L, 5_000L)], day.Vouchers.Single(v => v.SourceKind == "closing").Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)));
        Assert.Equal(1, (await db.Queries.DayAsync(db.As("ACC"), Y, Ct)).VoucherCount);

        // Trial balance agrees (day and cumulative).
        var tb = await db.Queries.TrialBalanceAsync(db.As("MGR"), D, cumulative: true, Ct);
        Assert.True(tb.Balanced);
        Assert.Equal(0, tb.Rows.Sum(r => r.BalancePaise));

        // ── lock rules ──
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Accounting.LockAsync(db.As("ACC"), Key(), new LockPeriodRequest(D), Ct)); // day open
        await Fails(HccErrorCode.FORBIDDEN, () => db.Accounting.LockAsync(db.As("CSH"), Key(), new LockPeriodRequest(D), Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Accounting.LockAsync(db.As("REC"), Key(), new LockPeriodRequest(D), Ct));
        await db.SetBusinessDateAsync(D.AddDays(1)); // P6 day close stand-in
        var key = Key();
        var locked = await db.Accounting.LockAsync(db.As("ACC"), key, new LockPeriodRequest(D), Ct);
        Assert.False(locked.Replayed);
        Assert.Equal(8, locked.Value.VoucherCount);
        Assert.Equal(locked.Value.DebitPaise, locked.Value.CreditPaise);
        var replay = await db.Accounting.LockAsync(db.As("ACC"), key, new LockPeriodRequest(D), Ct);
        Assert.True(replay.Replayed);
        Assert.Equal(locked.Value.LockId, replay.Value.LockId);
        var again = await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Accounting.LockAsync(db.As("MGR"), Key(), new LockPeriodRequest(D), Ct));
        Assert.Contains("already locked", again.Message, StringComparison.Ordinal);
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ACC_PERIOD_LOCKED';"));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'AccountingPeriodLocked';"));

        var lockedDay = await db.Queries.DayAsync(db.As("OWN"), D, Ct);
        Assert.Equal("Locked", lockedDay.Status);
        Assert.False(lockedDay.Provisional);
        Assert.Equal(day.Vouchers.Select(v => v.Number + v.SourceKey), lockedDay.Vouchers.Select(v => v.Number + v.SourceKey));

        // ── a late source for the locked date: snapshot kept, drift warning ──
        await db.PublishAsync(AccountingEvents.ExpensePosted, 901, new ExpensePostedEvent(901, D, "Laundry", "Cash", 12_000, "Demo Linen", null));
        await db.ProjectAsync();
        var drift = await db.Queries.DayAsync(db.As("OWN"), D, Ct);
        Assert.Equal(8, drift.VoucherCount);
        Assert.Contains(drift.Exceptions, x => x.Kind == "drift" && x.Severity == "warning");

        // Mapping change after lock does not touch the snapshot.
        await db.Accounting.SetMappingAsync(db.As("OWN"), Key(), new SetLedgerMappingRequest("line:service", "4000"), Ct);
        Assert.Contains((await db.Queries.DayAsync(db.As("OWN"), D, Ct)).Vouchers.Single(v => v.Sub == "service").Entries, e => e.Account == "4100");
        Assert.Contains((await db.Queries.DayAsync(db.As("OWN"), D, Ct)).Exceptions, x => x.Kind == "drift");
        await db.Accounting.ResetMappingAsync(db.As("OWN"), Key(), new ResetLedgerMappingRequest("line:service"), Ct);

        // ── unlock: Owner only, reason, version ──
        await Fails(HccErrorCode.FORBIDDEN, () => db.Accounting.UnlockAsync(db.As("ACC"), Key(), new UnlockPeriodRequest(D, "Fix laundry", locked.Value.Version), Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Accounting.UnlockAsync(db.As("MGR"), Key(), new UnlockPeriodRequest(D, "Fix laundry", locked.Value.Version), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Accounting.UnlockAsync(db.As("OWN"), Key(), new UnlockPeriodRequest(D, " ", locked.Value.Version), Ct));
        await Fails(HccErrorCode.STALE_VERSION, () => db.Accounting.UnlockAsync(db.As("OWN"), Key(), new UnlockPeriodRequest(D, "Fix laundry", "AAAAAAAAAAA="), Ct));
        var unlocked = (await db.Accounting.UnlockAsync(db.As("OWN"), Key(), new UnlockPeriodRequest(D, "Late laundry expense", locked.Value.Version), Ct)).Value;
        Assert.Equal("Closed", unlocked.Status);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Accounting.UnlockAsync(db.As("OWN"), Key(), new UnlockPeriodRequest(D, "again", locked.Value.Version), Ct));
        var live = await db.Queries.DayAsync(db.As("OWN"), D, Ct);
        Assert.Equal("Closed", live.Status);
        Assert.Equal(9, live.VoucherCount);
        Assert.Equal(["lock", "unlock"], (await db.Queries.LockHistoryAsync(db.As("OWN"), Ct)).Select(h => h.Action));

        // Relock: a new lock row, history kept.
        var relock = (await db.Accounting.LockAsync(db.As("MGR"), Key(), new LockPeriodRequest(D), Ct)).Value;
        Assert.NotEqual(locked.Value.LockId, relock.LockId);
        Assert.Equal(9, relock.VoucherCount);
        Assert.Equal(2, await db.LongAsync("SELECT COUNT(*) FROM acc.period_lock WHERE business_date = '2026-10-24';"));
    }

}

/// <summary>Consumer idempotency: one source per operational record, redelivery ignored, sources immutable.</summary>
[Trait("Category", "Sql")]
public sealed class AccountingConsumerTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    [Fact]
    public async Task Consumer_is_exactly_once_per_record_and_redelivery_is_ignored()
    {
        var f = await db.NewFolioAsync();
        var charge = (await db.Billing.PostChargeAsync(db.As("OWN"), Key(), new PostChargeRequest(f, "Minibar", 30_000), Ct)).Value;
        await db.ProjectAsync();
        var key = AccountingEventMapperKey(charge.ChargeId);
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM acc.source WHERE source_key = @k;", ("@k", SqlDbType.VarChar, key)));
        // Same record re-sent by the publisher (new outbox row): still one source.
        await db.PublishAsync(AccountingEvents.ChargePosted, f, new { folioId = f, chargeId = charge.ChargeId, kind = "service", serviceDate = charge.ServiceDate, amountPaise = 30_000, taxPaise = charge.TaxPaise });
        Assert.Equal(1, await db.ProjectAsync());
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM acc.source WHERE source_key = @k;", ("@k", SqlDbType.VarChar, key)));
        // Redelivery of an already processed outbox row does nothing.
        var outboxId = await db.LongAsync("SELECT MIN(outbox_id) FROM plat.outbox WHERE event_type = 'ChargePosted';");
        var evt = new OutboxEvent(outboxId, AccountingEvents.ChargePosted, f, "{}", DateTime.UtcNow);
        Assert.False(await db.Platform.Outbox.DeliverAsync(AccountingVoucherProjector.ConsumerName, evt, db.Projector.ApplyAsync, Ct));
        // Sources are append-only.
        await Assert.ThrowsAnyAsync<Exception>(() => db.ScalarAsync("DELETE acc.source_entry WHERE source_id = (SELECT MIN(source_id) FROM acc.source);"));
    }

    private static string AccountingEventMapperKey(long chargeId) => Hcc.Application.Accounting.AccountingEventMapper.ChargeKey(chargeId);
}
