using System.Collections.Concurrent;
using System.Data;
using Hcc.Accounting.Tests.Harness;
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Accounting;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Compliance;
using Hcc.Infrastructure.SqlServer.Expenses;
using Microsoft.Data.SqlClient;
using static Hcc.Accounting.Tests.Harness.Assertions;

namespace Hcc.Accounting.Tests.Sql;

/// <summary>Records what the lane asks acc2-manual's manual-voucher API to save (stands in until the integrator wires ManualVoucherService).</summary>
public sealed class FakeGstManualVouchers : IGstManualVouchers
{
    public ConcurrentQueue<(Caller Caller, GstManualVoucherDraft Draft)> Saved { get; } = new();

    public int Pending { get; set; }

    public Task<GstManualVoucherRef> SaveAsync(ICommandTransaction tx, Caller caller, GstManualVoucherDraft draft, CancellationToken ct)
    {
        Saved.Enqueue((caller, draft));
        return Task.FromResult(new GstManualVoucherRef("MV-" + Saved.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), "pending"));
    }

    public Task<int> PendingAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct) => Task.FromResult(Pending);
}

/// <summary>
/// ACC-2 acc2-gst on SQL (migrations 4246–4249) with the real P1 policy: the September month of the rulebook §8.6 shape (output 14,500 + 14,500,
/// TCS 100 → challan 28,900), PostGstSetoff (PERM-ACC-013, idempotent per month, the journal on the business date), the payments through the
/// manual-voucher port, the remittance's compliance card, the month lock (PERM-ACC-012: request / lock / unlock, every day locked, the expense
/// month locked) and GST on booking advances (switch off by default; on: deposit, return, apply capped by the GST held).
/// </summary>
[Trait("Category", "Sql")]
public sealed class GstMonthSqlTests(AccountingDb db) : IClassFixture<AccountingDb>
{
    private static readonly DateOnly D = AccountingDb.Day;            // 2026-10-24, the business date
    private static readonly DateOnly Sep10 = new(2026, 9, 10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Key() => AccountingDb.Key();

    private static long R(long rupees) => rupees * 100;

    private async Task InsertAsync(AccountingSource s)
    {
        await using var conn = await db.Platform.Connections.OpenAsync(Ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(Ct);
        await AccountingVoucherProjector.InsertSourceAsync(conn, tx, db.PropertyId, s, "test", null, DateTime.UtcNow, Ct);
        await tx.CommitAsync(Ct);
    }

    private GstMonthService Service(SqlAccessPolicy policy, IGstManualVouchers? manual) => new(db.Platform.Commands, db.Store, new SqlGstMonthStore(db.Platform.Connections), policy, db.Clock,
        manual, new ExpenseMonthLockAdapter(new SqlExpenseStore(db.Platform.Connections), new SqlExpenseRegisterStore(db.Platform.Connections)),
        new ComplianceCardHook(new SqlComplianceStore(db.Platform.Connections)));

    [Fact]
    public async Task Month_setoff_payments_remittance_card_month_lock_and_advance_gst()
    {
        var policy = new SqlAccessPolicy(db.Platform.Connections, db.Clock, [new SqlShiftScopeResolver(db.Platform.Connections)]);
        var fake = new FakeGstManualVouchers();
        var svc = Service(policy, fake);

        // ── September: room nights 5,80,000 at 5 % (output 14,500 + 14,500) and a Goibibo payout with TCS ₹100 (kind ota) ──
        await InsertAsync(SourceRules.FolioLine("test:sep-rooms", "room", Sep10, R(5_80_000), R(14_500), R(14_500), "September room nights (test)"));
        await InsertAsync(new AccountingSource("test:sep-ota", SourceKinds.Ota, "ota-payout", new DateOnly(2026, 9, 29), VoucherType.Receipt, "Goibibo payout (test)",
            [new SourceEntry("method:Bank transfer", R(17_360), 0), new SourceEntry("tax:gst-cash", R(100), 0), new SourceEntry("folio:guest", 0, R(17_460))]));

        var view = await svc.MonthAsync(db.As("ACC"), "2026-09", Ct);
        Assert.Equal((0L, R(14_500), R(14_500)), (view.Output.IgstPaise, view.Output.CgstPaise, view.Output.SgstPaise));
        Assert.Equal((R(100), R(100), R(29_000), R(28_900)), (view.TcsPaise, view.CashBalancePaise, view.PayablePaise, view.ChallanPaise));
        Assert.Equal("Post the GST set-off for 2026-09 first (rulebook E1).", view.LockBlocker);
        Assert.Null(view.Posted);
        await Fails(HccErrorCode.FORBIDDEN, () => svc.MonthAsync(db.As("CSH"), "2026-09", Ct));

        // ── PostGstSetoff: PERM-ACC-013 (Owner, Accountant); the month must be over; idempotent per month ──
        foreach (var role in new[] { "MGR", "REC", "CSH", "TAD" })
            await Fails(HccErrorCode.FORBIDDEN, () => svc.SetoffAsync(db.As(role), Key(), new PostGstSetoffRequest("2026-09"), Ct));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.SetoffAsync(db.As("ACC"), Key(), new PostGstSetoffRequest("2026-10"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.SetoffAsync(db.As("ACC"), Key(), new PostGstSetoffRequest("2026-9"), Ct));
        var key = Key();
        var posted = (await svc.SetoffAsync(db.As("ACC"), key, new PostGstSetoffRequest("2026-09"), Ct)).Value;
        Assert.Equal(("2026-09", D, "gst-setoff:2026-09@2026-10-24", R(29_000), R(28_900), false), (posted.Month, posted.PostedOn, posted.SourceKey, posted.PayablePaise, posted.ChallanPaise, posted.Unchanged));
        Assert.True((await svc.SetoffAsync(db.As("ACC"), key, new PostGstSetoffRequest("2026-09"), Ct)).Replayed);
        Assert.True((await svc.SetoffAsync(db.As("OWN"), Key(), new PostGstSetoffRequest("2026-09"), Ct)).Value.Unchanged);
        var day = await AccountingDays.EvaluateAsync(db.Store, null, db.PropertyId, D, Ct);
        var journal = day.Live.Single(v => v.Sub == "gst-setoff");
        Assert.Equal((VoucherType.Journal, "tax"), (journal.Type, journal.SourceKind));
        Assert.Equal([("2110", R(14_500), 0L), ("2120", R(14_500), 0L), ("1340", 0L, R(29_000))], journal.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).ToArray());
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM acc.gst_setoff WHERE property_id = @p AND superseded_utc IS NULL;", ("@p", SqlDbType.Int, db.PropertyId)));
        var after = await svc.MonthAsync(db.As("OWN"), "2026-09", Ct);
        Assert.Equal(("gst-setoff:2026-09@2026-10-24", R(28_900)), (after.Posted!.SourceKey, after.Posted.ChallanPaise));
        Assert.Null(after.LockBlocker);

        // ── RecordGstPayment: through the manual-voucher port (template gst-payment), dated after the month; unwired → not configured ──
        await Fails(HccErrorCode.VALIDATION, () => svc.PayAsync(db.As("ACC"), Key(), new RecordGstPaymentRequest("2026-09", R(28_900), "Cash", "CIN-DEMO-1020", new DateOnly(2026, 9, 30), null), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.PayAsync(db.As("ACC"), Key(), new RecordGstPaymentRequest("2026-09", R(28_900), "Cash", "", null, null), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.PayAsync(db.As("ACC"), Key(), new RecordGstPaymentRequest("2026-09", R(100), "Cash", "CIN-1", null, R(200)), Ct));
        var pay = (await svc.PayAsync(db.As("ACC"), Key(), new RecordGstPaymentRequest("2026-09", R(28_900), "Cash", "CIN-DEMO-1020", new DateOnly(2026, 10, 20), R(400)), Ct)).Value;
        Assert.Equal(("gst-payment", "MV-1", "pending"), (pay.Template, pay.VoucherId, pay.VoucherStatus));
        var draft = fake.Saved.Single().Draft;
        Assert.Equal((new DateOnly(2026, 10, 20), "Payment", "CIN-DEMO-1020", "gst-payment"), (draft.Date, draft.Type, draft.Reference, draft.Template));
        Assert.Equal([("1340", R(28_900), 0L), ("1000", 0L, R(28_900)), ("2140", R(400), 0L), ("1340", 0L, R(400))], draft.Lines.ToArray());
        var unwired = Service(policy, null);
        var nc = await Fails(HccErrorCode.UNAVAILABLE, () => unwired.PayAsync(db.As("OWN"), Key(), new RecordGstPaymentRequest("2026-09", R(100), "Cash", "CIN-X1", null, null), Ct));
        Assert.Contains("not wired", nc.Message, StringComparison.Ordinal);

        // ── RecordStatutoryRemittance: Dr 2310 / 2320 / 2330 · Cr tender; the compliance card (no firm profile → not marked; with one → filed) ──
        var pf = (await svc.RemitAsync(db.As("ACC"), Key(), new RecordStatutoryRemittanceRequest("pf", "2026-09", R(36_000), "Cash", "EPF-TRRN-1015", new DateOnly(2026, 10, 15)), Ct)).Value;
        Assert.Equal(("statutory-remittance", (string?)null), (pf.Template, pf.ComplianceCard));
        Assert.Contains("set the firm profile first", pf.ComplianceNote, StringComparison.Ordinal);
        Assert.Equal("2310", fake.Saved.Last().Draft.Lines[0].Account);
        await Fails(HccErrorCode.VALIDATION, () => svc.RemitAsync(db.As("ACC"), Key(), new RecordStatutoryRemittanceRequest("tds", "2026-09", 1, "Cash", "ABC", null), Ct));
        await db.ScalarAsync("""
            INSERT cmp.profile(property_id, firm_name, entity, gst_type, has_employees, qrmp) VALUES (@p, N'HCC Demo Residency (fictional)', 'partnership', 'regular', 1, 0);
            """, ("@p", SqlDbType.Int, db.PropertyId));
        var esic = (await svc.RemitAsync(db.As("ACC"), Key(), new RecordStatutoryRemittanceRequest("esic", "2026-09", R(8_000), "Cash", "ESIC-CH-1015", new DateOnly(2026, 10, 15)), Ct)).Value;
        Assert.Equal("oc10", esic.ComplianceCard);
        Assert.Equal("2320", fake.Saved.Last().Draft.Lines[0].Account);
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM cmp.record WHERE property_id = @p AND fy_start = 2026 AND item_id = 'oc10' AND done = 1 AND evidence_ref = N'ESIC-CH-1015';",
            ("@p", SqlDbType.Int, db.PropertyId)));
        var again = (await svc.RemitAsync(db.As("OWN"), Key(), new RecordStatutoryRemittanceRequest("esic", "2026-09", R(10), "Cash", "ESIC-CH-AGAIN", new DateOnly(2026, 10, 16)), Ct)).Value;
        Assert.Contains("already filed", again.ComplianceNote, StringComparison.Ordinal);

        // ── Month lock: Accountant requests; a pending manual voucher blocks; Owner locks every day + the expense month; unlock with a reason ──
        foreach (var role in new[] { "MGR", "CSH", "REC" })
            await Fails(HccErrorCode.FORBIDDEN, () => svc.LockMonthAsync(db.As(role), Key(), new LockAccountingMonthRequest("2026-09"), Ct));
        // The Accountant holds PERM-ACC-012 request-only ('R'): the policy answers APPROVAL_REQUIRED — the Accountant requests, the Owner locks.
        await Fails(HccErrorCode.APPROVAL_REQUIRED, () => svc.LockMonthAsync(db.As("ACC"), Key(), new LockAccountingMonthRequest("2026-09"), Ct));
        fake.Pending = 1;
        var waits = await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.LockMonthAsync(db.As("OWN"), Key(), new LockAccountingMonthRequest("2026-09"), Ct));
        Assert.Contains("1 manual voucher is pending approval in 2026-09", waits.Message, StringComparison.Ordinal);
        fake.Pending = 0;
        Assert.Equal("Requested", (await svc.RequestMonthLockAsync(db.As("ACC"), Key(), new LockAccountingMonthRequest("2026-09"), Ct)).Value.Status);
        Assert.True((await svc.MonthAsync(db.As("OWN"), "2026-09", Ct)).LockRequested);
        var locked = (await svc.LockMonthAsync(db.As("OWN"), Key(), new LockAccountingMonthRequest("2026-09"), Ct)).Value;
        Assert.Equal(("Locked", 30, "gst-setoff:2026-09@2026-10-24", true), (locked.Status, locked.DaysLocked, locked.SetoffKey, locked.ExpenseMonthLocked));
        Assert.Equal(30, await db.LongAsync("SELECT COUNT(*) FROM acc.period_lock WHERE property_id = @p AND business_date BETWEEN '2026-09-01' AND '2026-09-30' AND unlocked_utc IS NULL;",
            ("@p", SqlDbType.Int, db.PropertyId)));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM ops.expense_month_lock WHERE property_id = @p AND lock_month = '2026-09' AND unlocked_utc IS NULL;", ("@p", SqlDbType.Int, db.PropertyId)));
        var lockedView = await svc.MonthAsync(db.As("ACC"), "2026-09", Ct);
        Assert.Equal((true, false), (lockedView.Locked, lockedView.LockRequested));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.LockMonthAsync(db.As("OWN"), Key(), new LockAccountingMonthRequest("2026-09"), Ct));
        var refused = await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.SetoffAsync(db.As("ACC"), Key(), new PostGstSetoffRequest("2026-09"), Ct));
        Assert.Contains("is locked", refused.Message, StringComparison.Ordinal);
        await Fails(HccErrorCode.APPROVAL_REQUIRED, () => svc.UnlockMonthAsync(db.As("ACC"), Key(), new UnlockAccountingMonthRequest("2026-09", "Late bill"), Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => svc.UnlockMonthAsync(db.As("MGR"), Key(), new UnlockAccountingMonthRequest("2026-09", "Late bill"), Ct));
        await Fails(HccErrorCode.VALIDATION, () => svc.UnlockMonthAsync(db.As("OWN"), Key(), new UnlockAccountingMonthRequest("2026-09", "x"), Ct));
        var un = (await svc.UnlockMonthAsync(db.As("OWN"), Key(), new UnlockAccountingMonthRequest("2026-09", "Late vendor bill"), Ct)).Value;
        Assert.Equal(("Unlocked", true), (un.Status, un.ExpenseMonthLocked));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM ops.expense_month_lock WHERE property_id = @p AND lock_month = '2026-09' AND unlocked_utc IS NOT NULL;", ("@p", SqlDbType.Int, db.PropertyId)));
        Assert.Equal(30, await db.LongAsync("SELECT COUNT(*) FROM acc.period_lock WHERE property_id = @p AND business_date BETWEEN '2026-09-01' AND '2026-09-30' AND unlocked_utc IS NULL;",
            ("@p", SqlDbType.Int, db.PropertyId)));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.UnlockMonthAsync(db.As("OWN"), Key(), new UnlockAccountingMonthRequest("2026-09", "Again"), Ct));
        Assert.Equal(0, (await svc.LockMonthAsync(db.As("OWN"), Key(), new LockAccountingMonthRequest("2026-09"), Ct)).Value.DaysLocked);
        var history = await svc.LocksAsync(db.As("ACC"), Ct);
        Assert.Equal([("2026-09", true), ("2026-09", false)], history.Locks.Select(l => (l.Month, l.UnlockedUtc is not null)).ToArray());
        Assert.Equal("Late vendor bill", history.Locks[0].UnlockReason);
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ACC_MONTH_UNLOCKED';"));

        // ── GST on booking advances: off by default (nothing); Owner switches it on (PERM-CFG-003); deposit / return / apply capped ──
        var off = await db.NewFolioAsync();
        await db.ScalarAsync("UPDATE inv.reservation SET nightly_rate_paise = 200000 WHERE reservation_id = (SELECT reservation_id FROM bill.folio WHERE folio_id = @f);", ("@f", SqlDbType.BigInt, off));
        await db.PublishAsync(AccountingEvents.ReceiptRecorded, off, new { folioId = off, receiptId = 9001L, kind = "deposit", tender = "Cash", amountPaise = R(10_500), businessDate = D });
        await db.ProjectAsync();
        Assert.Equal(0, await db.LongAsync("SELECT COUNT(*) FROM acc.source WHERE property_id = @p AND source_kind = 'tax' AND sub LIKE 'advance-gst%';", ("@p", SqlDbType.Int, db.PropertyId)));
        await Fails(HccErrorCode.FORBIDDEN, () => svc.SetGstOnAdvancesAsync(db.As("ACC"), Key(), new SetGstOnAdvancesRequest(true), Ct));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => svc.SetGstOnAdvancesAsync(db.As("OWN"), Key(), new SetGstOnAdvancesRequest(false), Ct));
        Assert.True((await svc.SetGstOnAdvancesAsync(db.As("OWN"), Key(), new SetGstOnAdvancesRequest(true), Ct)).Value.On);
        Assert.True((await svc.LocksAsync(db.As("OWN"), Ct)).GstOnAdvances);
        var f = await db.NewFolioAsync();
        await db.ScalarAsync("UPDATE inv.reservation SET nightly_rate_paise = 200000 WHERE reservation_id = (SELECT reservation_id FROM bill.folio WHERE folio_id = @f);", ("@f", SqlDbType.BigInt, f));
        await db.PublishAsync(AccountingEvents.ReceiptRecorded, f, new { folioId = f, receiptId = 9101L, kind = "deposit", tender = "Cash", amountPaise = R(10_500), businessDate = D });
        await db.PublishAsync(AccountingEvents.ReceiptRecorded, f, new { folioId = f, receiptId = 9102L, kind = "deposit-return", tender = "Cash", amountPaise = R(5_250), businessDate = D });
        await db.PublishAsync(AccountingEvents.ReceiptRecorded, f, new { folioId = f, receiptId = 9103L, kind = "deposit-apply", tender = "Advance", amountPaise = R(50_000), businessDate = D });
        await db.ProjectAsync();
        var live = (await AccountingDays.EvaluateAsync(db.Store, null, db.PropertyId, D, Ct)).Live;
        (string, long, long)[] Of(long receipt) => live.Single(v => v.SourceKey == AccountingVoucherProjector.AdvanceGstKey(f, receipt)).Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).ToArray();
        Assert.Equal([("1350", R(500), 0L), ("2110", 0L, R(250)), ("2120", 0L, R(250))], Of(9101));
        Assert.Equal([("2110", R(125), 0L), ("2120", R(125), 0L), ("1350", 0L, R(250))], Of(9102));
        Assert.Equal([("2110", R(125), 0L), ("2120", R(125), 0L), ("1350", 0L, R(250))], Of(9103)); // capped at the ₹250 still held
        Assert.Equal("advance-gst-reversal", live.Single(v => v.SourceKey == AccountingVoucherProjector.AdvanceGstKey(f, 9103)).Sub);
        Assert.DoesNotContain(live, v => v.SourceKey == AccountingVoucherProjector.AdvanceGstKey(off, 9001));
    }
}
