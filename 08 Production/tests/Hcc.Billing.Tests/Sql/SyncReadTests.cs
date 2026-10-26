using Hcc.Billing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using static Hcc.Billing.Tests.Harness.Assertions;

namespace Hcc.Billing.Tests.Sql;

/// <summary>W4-3 reads for the production web sync layer: folio by reservation (+ stay ref) and the cash desk (tills, active / last shift).</summary>
[Trait("Category", "Sql")]
public class SyncReadTests(BillingDb db) : IClassFixture<BillingDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Folio_by_reservation_returns_the_folio_view_or_null_before_check_in_and_hides_other_properties()
    {
        var bare = await db.LongAsync("""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status)
            OUTPUT INSERTED.reservation_id VALUES (@p, 'SYNC-NOFOLIO', N'Fictional guest', '2026-10-25', '2026-10-27', 'Confirmed');
            """, ("@p", System.Data.SqlDbType.Int, db.PropertyId));
        var none = await db.Queries.GetReservationFolioAsync(db.As(Who.Reception), bare, Ct);
        Assert.Equal((bare, (long?)null, (string?)null), (none.ReservationId, none.StayId, none.StayVersion));
        Assert.Null(none.Folio);

        var (rid, fid) = await db.NewFolioAsync();
        await db.Billing.PostChargeAsync(db.As(Who.Reception), BillingDb.Key(), new PostChargeRequest(fid, "Laundry", 10_000, "laundry"), Ct);
        await db.PayAsync(fid, 5_000);
        var view = await db.Queries.GetReservationFolioAsync(db.As(Who.Cashier), rid, Ct);
        Assert.Equal(fid, view.Folio!.FolioId);
        Assert.Null(view.StayId);
        var direct = await db.Queries.GetFolioAsync(db.As(Who.Owner), fid, Ct);
        Assert.Equal(direct.Version, view.Folio.Version);
        Assert.Equal(direct.Totals, view.Folio.Totals);
        Assert.Equal(10_000, view.Folio.Lines.Single().AmountPaise);
        Assert.Equal(5_000, view.Folio.Receipts.Single().AmountPaise);

        var (otherRid, _) = await db.NewFolioAsync(property: db.OtherPropertyId);
        await Fails(HccErrorCode.NOT_FOUND, () => db.Queries.GetReservationFolioAsync(db.As(Who.Owner), otherRid, Ct));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.GetReservationFolioAsync(db.As(Who.Housekeeper), rid, Ct));
    }

    [Fact]
    public async Task Cashier_folio_list_shows_in_house_folios_with_balance_and_todays_arrivals_without_the_reservations_read()
    {
        // D5: the Cashier holds PERM-FOL-001 but not PERM-RES-001 (DEC-063), so its folio screen reads GET /cashier/folios.
        var (rid, fid) = await db.NewFolioAsync();
        await db.Billing.PostChargeAsync(db.As(Who.Reception), BillingDb.Key(), new PostChargeRequest(fid, "Laundry", 10_000, "laundry"), Ct);
        await db.PayAsync(fid, 4_000);
        var arrival = await db.LongAsync("""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status)
            OUTPUT INSERTED.reservation_id VALUES (@p, 'DESK-ARR', N'Fictional arrival', '2026-10-24', '2026-10-26', 'Confirmed');
            """, ("@p", System.Data.SqlDbType.Int, db.PropertyId));
        var future = await db.LongAsync("""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status)
            OUTPUT INSERTED.reservation_id VALUES (@p, 'DESK-FUT', N'Fictional future', '2026-11-02', '2026-11-04', 'Confirmed');
            """, ("@p", System.Data.SqlDbType.Int, db.PropertyId));
        var (cancelled, _) = await db.NewFolioAsync(status: "Cancelled");
        var (other, _) = await db.NewFolioAsync(property: db.OtherPropertyId);

        var list = await db.Queries.GetDeskFoliosAsync(db.As(Who.Cashier), Ct);
        Assert.Equal(BillingDb.Day, list.BusinessDate);
        var ids = list.Items.Select(i => i.ReservationId).ToHashSet();
        Assert.Contains(rid, ids);
        Assert.Contains(arrival, ids);
        Assert.DoesNotContain(future, ids);
        Assert.DoesNotContain(cancelled, ids);
        Assert.DoesNotContain(other, ids);
        var inHouse = list.Items.Single(i => i.ReservationId == rid);
        var direct = await db.Queries.GetFolioAsync(db.As(Who.Owner), fid, Ct);
        Assert.Equal(("InHouse", (long?)fid, direct.Version, direct.Totals.BalancePaise), (inHouse.Status, inHouse.FolioId, inHouse.FolioVersion, inHouse.BalancePaise));
        Assert.True(inHouse.BalancePaise > 0);
        var arr = list.Items.Single(i => i.ReservationId == arrival);
        Assert.Equal(("Confirmed", (long?)null, 0L, "Fictional arrival"), (arr.Status, arr.FolioId, arr.BalancePaise, arr.GuestName));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.GetDeskFoliosAsync(db.As(Who.Housekeeper), Ct));
        Assert.Contains(db.Policy.Calls, c => c.Principal == Who.Cashier && c.Permission == "PERM-FOL-001" && c.Scope is null);
    }

    [Fact]
    public async Task Cash_desk_shows_the_active_shift_with_expected_cash_the_pending_difference_and_then_the_last_closed_shift()
    {
        var (till, shift) = await db.OpenTillAsync(100_000, "DESK1");
        var (_, folio) = await db.NewFolioAsync();
        await db.PayAsync(folio, 50_000, "Cash", till: till);

        var open = (await db.Queries.GetCashDeskAsync(db.As(Who.Cashier), Ct)).Tills.Single(t => t.Till.TillId == till);
        Assert.Equal(("Open", 100_000L, 150_000L), (open.Active!.Shift.Status, open.Active.Shift.OpeningFloatPaise, open.Expected!.ExpectedPaise));
        Assert.Equal(50_000, open.Expected.PaymentsPaise);
        Assert.Null(open.LastClosed);
        Assert.Null(open.Active.Approval);

        var submitted = (await db.Cashier.SubmitShiftCountAsync(db.As(Who.Cashier), BillingDb.Key(),
            new SubmitShiftCountRequest(shift.ShiftId, 149_000, null, null, "Change given twice", open.Active.Shift.Version), Ct)).Value;
        var pending = (await db.Queries.GetCashDeskAsync(db.As(Who.Reception), Ct)).Tills.Single(t => t.Till.TillId == till);
        Assert.Equal(("Submitted", "Change given twice", (int?)Who.Cashier), (pending.Active!.Shift.Status, pending.Active.DifferenceReason, pending.Active.SubmittedBy));
        Assert.Equal(("cash-difference", "Pending", 1_000L, submitted.ApprovalId!.Value), (pending.Active.Approval!.Action, pending.Active.Approval.Status,
            pending.Active.Approval.AmountPaise, pending.Active.Approval.ApprovalId));

        await db.Billing.DecideApprovalAsync(db.As(Who.Manager), BillingDb.Key(), new DecideApprovalRequest(submitted.ApprovalId.Value, true, null, pending.Active.Approval.Version), Ct);
        var closed = (await db.Queries.GetCashDeskAsync(db.As(Who.Owner), Ct)).Tills.Single(t => t.Till.TillId == till);
        Assert.Null(closed.Active);
        Assert.Null(closed.Expected);
        Assert.Equal(("Closed", 150_000L, 149_000L, (int?)Who.Manager), (closed.LastClosed!.Shift.Status, closed.LastClosed.Shift.ExpectedPaise!.Value,
            closed.LastClosed.Shift.CountedPaise!.Value, closed.LastClosed.ClosedBy));
        Assert.Equal("Approved", closed.LastClosed.Approval!.Status);
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.GetCashDeskAsync(db.As(Who.Housekeeper), Ct));
    }
}
