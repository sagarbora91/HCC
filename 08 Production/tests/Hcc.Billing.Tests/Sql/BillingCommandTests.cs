using System.Data;
using Hcc.Billing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Microsoft.Data.SqlClient;
using static Hcc.Billing.Tests.Harness.Assertions;

namespace Hcc.Billing.Tests.Sql;

/// <summary>Folio commands on SQL Server: night posting, receipts, adjustments and approvals (DEC-031, DEC-041), queries, audit.</summary>
[Trait("Category", "Sql")]
public class BillingCommandTests(BillingDb db) : IClassFixture<BillingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(long Rid, long Folio)> StayAsync(long rate = 200_000)
    {
        var (rid, folio) = await db.NewFolioAsync("InHouse", BillingDb.Day, BillingDb.Day.AddDays(3));
        db.Stays[rid] = (folio, rate, 1000);
        return (rid, folio);
    }

    // ── PostRoomNight (spike T07) ──

    [Fact]
    public async Task Room_night_posts_once_with_tax_snapshot_and_repeats_return_the_existing_charge()
    {
        var (rid, folio) = await StayAsync();
        var key = BillingDb.Key();
        var first = await db.Billing.PostRoomNightAsync(db.As(Who.Manager), key, new PostRoomNightRequest(rid, BillingDb.Day), Ct);
        Assert.True(first.Value.Created);
        Assert.Equal((200_000L, 1000, 20_000L), (first.Value.AmountPaise, first.Value.TaxBps, first.Value.TaxPaise));

        var replay = await db.Billing.PostRoomNightAsync(db.As(Who.Manager), key, new PostRoomNightRequest(rid, BillingDb.Day), Ct);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Value, replay.Value);

        var otherKey = await db.Billing.PostRoomNightAsync(db.As(Who.Owner), BillingDb.Key(), new PostRoomNightRequest(rid, BillingDb.Day), Ct);
        Assert.False(otherKey.Replayed);
        Assert.False(otherKey.Value.Created);
        Assert.Equal(first.Value.ChargeId, otherKey.Value.ChargeId);
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM bill.charge WHERE folio_id = @f AND kind = 'room'", ("@f", SqlDbType.BigInt, folio)));

        await Fails(HccErrorCode.IDEMPOTENCY_MISMATCH, () => db.Billing.PostRoomNightAsync(db.As(Who.Manager), key, new PostRoomNightRequest(rid, BillingDb.Day.AddDays(1)), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Billing.PostRoomNightAsync(db.As(Who.Manager), BillingDb.Key(), new PostRoomNightRequest(rid, BillingDb.Day.AddDays(1)), Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Billing.PostRoomNightAsync(db.As(Who.Reception), BillingDb.Key(), new PostRoomNightRequest(rid, BillingDb.Day), Ct));
    }

    [Fact]
    public async Task Parallel_night_posts_with_different_keys_create_one_charge()
    {
        var (rid, folio) = await StayAsync(250_000);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            db.Billing.PostRoomNightAsync(db.As(Who.Manager), BillingDb.Key(), new PostRoomNightRequest(rid, BillingDb.Day), Ct)));
        Assert.Single(results.Select(r => r.Value.ChargeId).Distinct());
        Assert.Equal(1, results.Count(r => r.Value.Created));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM bill.charge WHERE folio_id = @f", ("@f", SqlDbType.BigInt, folio)));
    }

    [Fact]
    public async Task Night_posting_needs_an_in_house_stay_inside_its_dates()
    {
        var (rid, folio) = await db.NewFolioAsync("Confirmed");
        db.Stays[rid] = (folio, 100_000, 1000);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Billing.PostRoomNightAsync(db.As(Who.Manager), BillingDb.Key(), new PostRoomNightRequest(rid, BillingDb.Day), Ct));
        var (rid2, _) = await StayAsync();
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Billing.PostRoomNightAsync(db.As(Who.Manager), BillingDb.Key(), new PostRoomNightRequest(rid2, BillingDb.Day.AddDays(-1)), Ct));
        await Fails(HccErrorCode.NOT_FOUND, () => db.Billing.PostRoomNightAsync(db.As(Who.Manager), BillingDb.Key(), new PostRoomNightRequest(999_999, BillingDb.Day), Ct));
    }

    // ── RecordReceipt ──

    [Fact]
    public async Task Receipts_validate_tender_shift_and_deposit_rules()
    {
        var (_, folio) = await StayAsync();
        await Fails(HccErrorCode.VALIDATION, () => db.Billing.RecordReceiptAsync(db.As(Who.Cashier), BillingDb.Key(), new RecordReceiptRequest(folio, "payment", "UPI", 1000, null, null), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.Billing.RecordReceiptAsync(db.As(Who.Cashier), BillingDb.Key(), new RecordReceiptRequest(folio, "payment", "Cash", 1000, null, null), Ct));
        await Fails(HccErrorCode.VALIDATION, () => db.PayAsync(folio, 0));
        await Fails(HccErrorCode.VALIDATION, () => db.PayAsync(folio, 10_000_000_001));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.PayAsync(folio, 1000, kind: "refund"));
        await Fails(HccErrorCode.VALIDATION, () => db.PayAsync(folio, 1000, kind: "company-credit"));

        // Cash needs an open shift on that till.
        var till = (await db.Cashier.CreateTillAsync(db.As(Who.Owner), BillingDb.Key(), new CreateTillRequest("NOSHIFT", "Spare"), Ct)).Value.TillId;
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.PayAsync(folio, 1000, "Cash", till: till));

        // Deposit return ≤ deposit held; a deposit never reduces the balance.
        var (openTill, shift) = await db.OpenTillAsync(0);
        await db.PayAsync(folio, 50_000, "Cash", "deposit", openTill);
        await Fails(HccErrorCode.VALIDATION, () => db.PayAsync(folio, 50_001, "Cash", "deposit-return", openTill));
        var cash = await db.PayAsync(folio, 50_000, "Cash", "deposit-return", openTill);
        Assert.Equal(shift.ShiftId, cash.Value.ShiftId);
        Assert.Contains(db.Policy.Calls, c => c.Permission == "PERM-PAY-002" && c.Scope?.Kind == "shift" && c.Scope.TargetId == shift.ShiftId);

        // Payments on a departed booking are refused; a cashier cannot post charges.
        var (_, departed) = await db.NewFolioAsync("Departed");
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.PayAsync(departed, 1000));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Billing.PostChargeAsync(db.As(Who.Cashier), BillingDb.Key(), new PostChargeRequest(folio, "Laundry", 100), Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.PayAsync(folio, 1000, who: Who.Housekeeper));
    }

    [Fact]
    public async Task Same_key_receipt_replays_in_parallel_create_one_receipt()
    {
        var (_, folio) = await StayAsync();
        var key = BillingDb.Key();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => db.PayAsync(folio, 123_456, key: key)));
        Assert.Single(results.Select(r => r.Value.ReceiptId).Distinct());
        Assert.Equal(5, results.Count(r => r.Replayed));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM pay.receipt WHERE folio_id = @f", ("@f", SqlDbType.BigInt, folio)));
        await Fails(HccErrorCode.IDEMPOTENCY_MISMATCH, () => db.PayAsync(folio, 999, key: key));
    }

    [Fact]
    public async Task Receipts_and_charges_are_append_only_and_every_command_audits()
    {
        var (_, folio) = await StayAsync();
        var r = await db.PayAsync(folio, 5_000);
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.ScalarAsync("UPDATE pay.receipt SET amount_paise = 1 WHERE receipt_id = @r", ("@r", SqlDbType.BigInt, r.Value.ReceiptId)));
        Assert.Equal(51002, ex.Number);
        await Assert.ThrowsAsync<SqlException>(() => db.ScalarAsync("DELETE pay.receipt WHERE receipt_id = @r", ("@r", SqlDbType.BigInt, r.Value.ReceiptId)));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'RECEIPT_RECORDED' AND target = @t", ("@t", SqlDbType.VarChar, $"folio:{folio}")));
        Assert.True(await db.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ReceiptRecorded' AND aggregate_id = @f", ("@f", SqlDbType.BigInt, folio)) >= 1);
    }

    [Fact]
    public async Task Other_property_folios_are_not_found()
    {
        var (_, foreign) = await db.NewFolioAsync(property: db.OtherPropertyId);
        await Fails(HccErrorCode.NOT_FOUND, () => db.PayAsync(foreign, 1000));
        await Fails(HccErrorCode.NOT_FOUND, () => db.Queries.GetFolioAsync(db.As(Who.Owner), foreign, Ct));
    }

    // ── Discounts (DEC-039, DEC-041) ──

    [Fact]
    public async Task Discount_within_limit_posts_directly_and_above_limit_needs_another_approver()
    {
        var (rid, folio) = await StayAsync();
        await db.Billing.PostRoomNightAsync(db.As(Who.Manager), BillingDb.Key(), new PostRoomNightRequest(rid, BillingDb.Day), Ct);
        var small = await db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(),
            new RequestAdjustmentRequest("discount", folio, null, 25_000, "Loyalty", await db.FolioVersionAsync(folio)), Ct);
        Assert.Equal("Posted", small.Value.Outcome);
        var f = await db.Queries.GetFolioAsync(db.As(Who.Owner), folio, Ct);
        Assert.Contains(f.Lines, l => l.Kind == "discount" && l.AmountPaise == -25_000 && l.TaxPaise == -2_500 && l.Label == "Discount · Loyalty");

        await Fails(HccErrorCode.VALIDATION, () => db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(),
            new RequestAdjustmentRequest("discount", folio, null, 175_001, "Too much", f.Version), Ct));
        await Fails(HccErrorCode.STALE_VERSION, () => db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(),
            new RequestAdjustmentRequest("discount", folio, null, 30_000, "Old version", small.Value.FolioVersion == f.Version ? Convert.ToBase64String(new byte[8]) : small.Value.FolioVersion), Ct));

        var big = await db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(), new RequestAdjustmentRequest("discount", folio, null, 30_000, "Noise complaint", f.Version), Ct);
        Assert.Equal("PendingApproval", big.Value.Outcome);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(),
            new RequestAdjustmentRequest("discount", folio, null, 40_000, "Second", f.Version), Ct));
        var id = big.Value.ApprovalId!.Value;
        await Fails(HccErrorCode.FORBIDDEN, () => db.Billing.DecideApprovalAsync(db.As(Who.Reception), BillingDb.Key(), new DecideApprovalRequest(id, true, null, big.Value.ApprovalVersion!), Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Billing.DecideApprovalAsync(db.As(Who.Cashier), BillingDb.Key(), new DecideApprovalRequest(id, true, null, big.Value.ApprovalVersion!), Ct));
        await Fails(HccErrorCode.STALE_VERSION, () => db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(id, true, null, Convert.ToBase64String(new byte[8])), Ct));
        var ok = await db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(id, true, null, big.Value.ApprovalVersion!), Ct);
        Assert.Equal("Executed", ok.Value.Status);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Billing.DecideApprovalAsync(db.As(Who.Owner), BillingDb.Key(), new DecideApprovalRequest(id, true, null, ok.Value.Version), Ct));
        var after = await db.Queries.GetFolioAsync(db.As(Who.Owner), folio, Ct);
        Assert.Equal(200_000 - 25_000 - 30_000, after.Totals.NetPaise);
        Assert.Equal(14_500, after.Totals.TaxPaise);
    }

    [Fact]
    public async Task Approval_is_bound_to_the_folio_version_and_expires_after_24_hours()
    {
        var (rid, folio) = await StayAsync();
        await db.Billing.PostRoomNightAsync(db.As(Who.Manager), BillingDb.Key(), new PostRoomNightRequest(rid, BillingDb.Day), Ct);
        var req = await db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(), new RequestAdjustmentRequest("discount", folio, null, 50_000, "Late check-in", await db.FolioVersionAsync(folio)), Ct);
        await db.Billing.PostChargeAsync(db.As(Who.Reception), BillingDb.Key(), new PostChargeRequest(folio, "Laundry", 10_000), Ct); // folio changes
        await Fails(HccErrorCode.STALE_VERSION, () => db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(req.Value.ApprovalId!.Value, true, null, req.Value.ApprovalVersion!), Ct));
        var rejected = await db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(req.Value.ApprovalId!.Value, false, "Folio changed", req.Value.ApprovalVersion!), Ct);
        Assert.Equal("Rejected", rejected.Value.Status);

        var again = await db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(), new RequestAdjustmentRequest("discount", folio, null, 50_000, "Late check-in", await db.FolioVersionAsync(folio)), Ct);
        db.Clock.Now = db.Clock.Now.AddHours(25);
        try
        {
            Assert.Equal("Expired", (await db.Queries.GetFolioAsync(db.As(Who.Owner), folio, Ct)).Approvals.Single(a => a.ApprovalId == again.Value.ApprovalId).Status);
            await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(again.Value.ApprovalId!.Value, true, null, again.Value.ApprovalVersion!), Ct));
            var dismissed = await db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(again.Value.ApprovalId!.Value, false, null, again.Value.ApprovalVersion!), Ct);
            Assert.Equal("Expired", dismissed.Value.Status);
        }
        finally
        {
            db.Clock.Now = db.Clock.Now.AddHours(-25);
        }
    }

    // ── Corrections ──

    [Fact]
    public async Task Correction_posts_a_linked_reversal_once()
    {
        var (_, folio) = await StayAsync();
        var charge = await db.Billing.PostChargeAsync(db.As(Who.Reception), BillingDb.Key(), new PostChargeRequest(folio, "Minibar", 33_333), Ct);
        Assert.Equal(3_333, charge.Value.TaxPaise);
        await Fails(HccErrorCode.VALIDATION, () => db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(),
            new RequestAdjustmentRequest("correction", folio, charge.Value.ChargeId, 1_000, "Wrong", charge.Value.FolioVersion), Ct));
        var req = await db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(),
            new RequestAdjustmentRequest("correction", folio, charge.Value.ChargeId, 33_333, "Posted to wrong room", charge.Value.FolioVersion), Ct);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(),
            new RequestAdjustmentRequest("correction", folio, charge.Value.ChargeId, 33_333, "Again", charge.Value.FolioVersion), Ct));
        var ok = await db.Billing.DecideApprovalAsync(db.As(Who.Owner), BillingDb.Key(), new DecideApprovalRequest(req.Value.ApprovalId!.Value, true, null, req.Value.ApprovalVersion!), Ct);
        Assert.Equal("Executed", ok.Value.Status);
        var f = await db.Queries.GetFolioAsync(db.As(Who.Owner), folio, Ct);
        Assert.Equal((0L, 0L), (f.Totals.NetPaise, f.Totals.TaxPaise));
        Assert.True(f.Lines.Single(l => l.ChargeId == charge.Value.ChargeId).Reversed);
        Assert.Equal(charge.Value.ChargeId, f.Lines.Single(l => l.Kind == "correction").ReversesChargeId);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(),
            new RequestAdjustmentRequest("correction", folio, charge.Value.ChargeId, 33_333, "Twice", f.Version), Ct));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Billing.ExecuteAdjustmentAsync(db.As(Who.Cashier), BillingDb.Key(), new ExecuteAdjustmentRequest(ok.Value.ApprovalId, ok.Value.Version, null, null, null), Ct));
    }

    // ── Refunds (DEC-031) ──

    [Fact]
    public async Task Refund_is_requested_approved_and_executed_by_different_people_within_the_remainder()
    {
        var (openTill, shift) = await db.OpenTillAsync(100_000);
        var (_, folio) = await StayAsync();
        var payment = await db.PayAsync(folio, 100_000, "Cash", till: openTill);
        var version = await db.FolioVersionAsync(folio);
        await Fails(HccErrorCode.VALIDATION, () => db.Billing.RequestAdjustmentAsync(db.As(Who.Cashier), BillingDb.Key(), new RequestAdjustmentRequest("refund", folio, payment.Value.ReceiptId, 100_001, "Overcharged", version), Ct));
        var deposit = await db.PayAsync(folio, 5_000, "UPI", "deposit");
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Billing.RequestAdjustmentAsync(db.As(Who.Cashier), BillingDb.Key(), new RequestAdjustmentRequest("refund", folio, deposit.Value.ReceiptId, 100, "Deposit", deposit.Value.FolioVersion), Ct));
        version = deposit.Value.FolioVersion;

        var req = await db.Billing.RequestAdjustmentAsync(db.As(Who.Cashier), BillingDb.Key(), new RequestAdjustmentRequest("refund", folio, payment.Value.ReceiptId, 60_000, "Early departure", version), Ct);
        await Fails(HccErrorCode.VALIDATION, () => db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(), new RequestAdjustmentRequest("refund", folio, payment.Value.ReceiptId, 40_001, "Outstanding counts", version), Ct));
        var id = req.Value.ApprovalId!.Value;
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Billing.ExecuteAdjustmentAsync(db.As(Who.Cashier2), BillingDb.Key(), new ExecuteAdjustmentRequest(id, req.Value.ApprovalVersion!, null, null, openTill), Ct));
        var approved = await db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(id, true, null, req.Value.ApprovalVersion!), Ct);
        Assert.Equal("Approved", approved.Value.Status);
        Assert.Null(approved.Value.ResultId);

        await Fails(HccErrorCode.FORBIDDEN, () => db.Billing.ExecuteAdjustmentAsync(db.As(Who.Cashier), BillingDb.Key(), new ExecuteAdjustmentRequest(id, approved.Value.Version, null, null, openTill), Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Billing.ExecuteAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(), new ExecuteAdjustmentRequest(id, approved.Value.Version, null, null, openTill), Ct));
        var done = await db.Billing.ExecuteAdjustmentAsync(db.As(Who.Cashier2), BillingDb.Key(), new ExecuteAdjustmentRequest(id, approved.Value.Version, null, null, openTill), Ct);
        Assert.Equal("Executed", done.Value.Status);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Billing.ExecuteAdjustmentAsync(db.As(Who.Cashier2), BillingDb.Key(), new ExecuteAdjustmentRequest(id, done.Value.Version, null, null, openTill), Ct));

        var f = await db.Queries.GetFolioAsync(db.As(Who.Owner), folio, Ct);
        var parent = f.Receipts.Single(r => r.ReceiptId == payment.Value.ReceiptId);
        Assert.Equal((60_000L, 40_000L), (parent.RefundedPaise, parent.RefundablePaise));
        var child = f.Receipts.Single(r => r.Kind == "refund");
        Assert.Equal((payment.Value.ReceiptId, "Cash", 60_000L), (child.RefundOf!.Value, child.Tender, child.AmountPaise));
        Assert.Equal(40_000, f.Totals.PaidPaise);
        var drawer = await db.Queries.GetShiftAsync(db.As(Who.Owner), shift.ShiftId, Ct);
        Assert.Equal(100_000 + 100_000 - 60_000, drawer.Expected.ExpectedPaise);
        Assert.Equal(60_000, drawer.Expected.RefundsPaise);
    }

    [Fact]
    public async Task Parallel_refund_requests_never_exceed_the_remainder()
    {
        var (_, folio) = await StayAsync();
        var payment = await db.PayAsync(folio, 100_000);
        var version = payment.Value.FolioVersion;
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            try
            {
                await db.Billing.RequestAdjustmentAsync(db.As(i % 2 == 0 ? Who.Cashier : Who.Reception), BillingDb.Key(),
                    new RequestAdjustmentRequest("refund", folio, payment.Value.ReceiptId, 30_000, "Parallel " + i, version), Ct);
                return true;
            }
            catch (Hcc.Domain.Errors.HccException ex) when (ex.Code == HccErrorCode.VALIDATION)
            {
                return false;
            }
        }));
        Assert.Equal(3, attempts.Count(ok => ok));
        Assert.Equal(90_000, await db.LongAsync("SELECT SUM(amount_paise) FROM bill.approval WHERE folio_id = @f AND action = 'refund'", ("@f", SqlDbType.BigInt, folio)));
    }

    /// <summary>
    /// Spike C3 through the production command: two approvals that together exceed the receipt (an over-commitment that the
    /// request check normally prevents, injected here directly) executed in parallel — exactly one succeeds; total ≤ captured.
    /// </summary>
    [Fact]
    public async Task Parallel_refund_executions_are_serialized_on_the_parent_receipt()
    {
        var (_, folio) = await StayAsync();
        var payment = await db.PayAsync(folio, 100_000);
        var ids = new List<long>();
        for (var i = 0; i < 2; i++)
        {
            ids.Add(await db.LongAsync("""
                INSERT bill.approval(property_id, action, folio_id, target_kind, target_id, amount_paise, target_version, reason, status, requested_by,
                                     requested_utc, expires_utc, decided_by, decided_utc)
                OUTPUT INSERTED.approval_id
                SELECT @p, 'refund', @f, 'receipt', @r, 75000, f.row_ver, N'C3', 'Approved', @req, '2026-10-24T08:00:00', '2026-10-25T08:00:00', @mgr, '2026-10-24T08:00:00'
                FROM bill.folio f WHERE f.folio_id = @f;
                """, ("@p", SqlDbType.Int, db.PropertyId), ("@f", SqlDbType.BigInt, folio), ("@r", SqlDbType.BigInt, payment.Value.ReceiptId),
                ("@req", SqlDbType.Int, Who.Reception), ("@mgr", SqlDbType.Int, Who.Manager)));
        }

        var view = await db.Queries.GetFolioAsync(db.As(Who.Owner), folio, Ct);
        var outcomes = await Task.WhenAll(ids.Select(async (id, i) =>
        {
            try
            {
                var v = view.Approvals.Single(a => a.ApprovalId == id).Version;
                await db.Billing.ExecuteAdjustmentAsync(db.As(i == 0 ? Who.Cashier : Who.Cashier2), BillingDb.Key(), new ExecuteAdjustmentRequest(id, v, "UPI", "DEMO-REFUND", null), Ct);
                return "ok";
            }
            catch (Hcc.Domain.Errors.HccException ex)
            {
                return ex.Code.ToString();
            }
        }));
        Assert.Equal(1, outcomes.Count(o => o == "ok"));
        Assert.Contains(nameof(HccErrorCode.INVALID_TRANSITION), outcomes);
        Assert.Equal(75_000, await db.LongAsync("SELECT ISNULL(SUM(amount_paise), 0) FROM pay.receipt WHERE refund_of = @r", ("@r", SqlDbType.BigInt, payment.Value.ReceiptId)));
    }

    // ── Company credit ──

    [Fact]
    public async Task Company_credit_moves_the_exact_balance_to_a_receivable_after_approval()
    {
        var (rid, folio) = await StayAsync(1_500_000);
        var night = await db.Billing.PostRoomNightAsync(db.As(Who.Manager), BillingDb.Key(), new PostRoomNightRequest(rid, BillingDb.Day), Ct);
        await Fails(HccErrorCode.VALIDATION, () => db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(), new RequestAdjustmentRequest("credit", folio, null, 1_000_000, "Acme", night.Value.FolioVersion), Ct));
        var req = await db.Billing.RequestAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(), new RequestAdjustmentRequest("credit", folio, null, 1_650_000, "Acme Travels", night.Value.FolioVersion), Ct);
        var id = req.Value.ApprovalId!.Value;
        await Fails(HccErrorCode.APPROVAL_REQUIRED, () => db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(id, true, null, req.Value.ApprovalVersion!), Ct));
        var ok = await db.Billing.DecideApprovalAsync(db.As(Who.Owner), BillingDb.Key(), new DecideApprovalRequest(id, true, null, req.Value.ApprovalVersion!), Ct);
        Assert.Equal("Approved", ok.Value.Status);
        var done = await db.Billing.ExecuteAdjustmentAsync(db.As(Who.Reception), BillingDb.Key(), new ExecuteAdjustmentRequest(id, ok.Value.Version, null, null, null), Ct);
        Assert.Equal("Executed", done.Value.Status);
        var f = await db.Queries.GetFolioAsync(db.As(Who.Owner), folio, Ct);
        Assert.Equal((0L, 1_650_000L, 0L), (f.Totals.BalancePaise, f.Totals.CompanyCreditPaise, f.Totals.PaidPaise));
    }
}
