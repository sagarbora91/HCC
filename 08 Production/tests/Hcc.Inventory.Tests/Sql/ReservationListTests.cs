using System.Globalization;
using Hcc.Application.Reservations;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Errors;
using Hcc.Inventory.Tests.Harness;

namespace Hcc.Inventory.Tests.Sql;

/// <summary>
/// W4-1: GET /reservations (front-desk list) through <see cref="ReservationService.ListAsync"/> on the BASE fixture — filters (view, date range,
/// status incl. Expired, text), paging, the stay-side facts (guest, registration, stay id/version, room segments), validation and permission.
/// Rooms/dates are private to this class (201–204 in March 2027).
/// </summary>
[Trait("Category", "Sql")]
public sealed class ReservationListTests(GoldenDatabaseFixture f) : IClassFixture<GoldenDatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private P2Services S => f.Services;

    private async Task<ReservationResult> BookAsync(string room, string a, string d, string guest, bool hold = false)
    {
        var q = (await S.Inventory.QuoteStayAsync(P2Services.Ctx(FakeAccessPolicy.Reception, "QuoteStay"), new QuoteStayRequest(a, d, 2, 0, f.RoomIds[room]), Ct)).Value;
        return (await S.Booking.CreateAsync(P2Services.Ctx(FakeAccessPolicy.Reception, "CreateReservation"),
            new CreateReservationRequest(q.QuoteId, q.Version, new GuestInput(guest, "+91 90000 12345"), f.RoomIds[room], hold), Ct)).Value;
    }

    private Task<ReservationList> ListAsync(string? view = null, string? from = null, string? to = null, string? status = null, string? text = null, int page = 1, int size = 100, int principal = FakeAccessPolicy.Reception)
        => S.Booking.ListAsync(1, principal, view, from, to, status, text, page, size, Ct);

    [Fact]
    public async Task List_filters_pages_and_carries_the_stay_side_facts()
    {
        var confirmed = await BookAsync("201", "2027-03-02", "2027-03-04", "Lista Confirmed");
        var held = await BookAsync("202", "2027-03-03", "2027-03-05", "Lista Held", hold: true);
        var inHouse = await BookAsync("203", "2027-03-02", "2027-03-06", "Lista Resident");
        var cancelled = await BookAsync("204", "2027-03-10", "2027-03-11", "Lista Cancelled");
        await S.Booking.CancelAsync(P2Services.Ctx(FakeAccessPolicy.Reception, "CancelReservation"), new CancelReservationRequest(cancelled.ReservationId, cancelled.Version, "Changed plans"), Ct);

        // Registration + stay straight in SQL (the Stays module owns these tables; its commands are tested in Hcc.Stays.Tests).
        var inHouseRoom = f.RoomIds["203"];
        var otherRoom = f.RoomIds["205"];
        var ids = string.Join(',', new[] { confirmed.ReservationId, held.ReservationId, inHouse.ReservationId, cancelled.ReservationId });
        await f.Db.ExecAsync($"""
            DECLARE @g BIGINT, @f BIGINT, @s BIGINT;
            INSERT gst.guest(property_id, full_name, phone, nationality, verified, origin, created_utc) VALUES (1, N'Lista Resident', '+91 90000 12345', N'Indian', 1, 'manual', '2027-03-02T09:00:00');
            SET @g = SCOPE_IDENTITY();
            INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by) VALUES ({inHouse.ReservationId}, @g, '2027-03-02T09:00:00', 3);
            INSERT bill.folio(reservation_id) VALUES ({inHouse.ReservationId});
            SET @f = SCOPE_IDENTITY();
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by)
              VALUES (1, {inHouse.ReservationId}, @g, @f, 'InHouse', '2027-03-02', '2027-03-06', '2027-03-02', '2027-03-02T09:00:00', 3);
            SET @s = SCOPE_IDENTITY();
            INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date) VALUES (@s, 1, {otherRoom}, '2027-03-02', '2027-03-03');
            INSERT gst.stay_segment(stay_id, seq, room_id, from_date) VALUES (@s, 2, {inHouseRoom}, '2027-03-03');
            UPDATE inv.reservation SET status = 'InHouse' WHERE reservation_id = {inHouse.ReservationId};
            """, Ct);

        var all = await ListAsync(from: "2027-03-01", to: "2027-03-31");
        var mine = all.Items.Where(x => x.GuestName.StartsWith("Lista", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, mine.Count);
        Assert.Equal("2026-10-24", all.BusinessDate);
        Assert.Equal(new[] { "Lista Cancelled", "Lista Confirmed", "Lista Held", "Lista Resident" }, mine.Select(x => x.GuestName).Order().ToArray());

        var r = mine.Single(x => x.GuestName == "Lista Resident");
        Assert.Equal((ReservationStatuses.InHouse, true, "203"), (r.Status, r.Registered, r.RoomNo));
        Assert.NotNull(r.GuestId);
        Assert.NotNull(r.StayId);
        Assert.False(string.IsNullOrEmpty(r.StayVersion));
        Assert.Equal("InHouse", r.StayStatus);
        Assert.Equal("2027-03-02", r.CheckedInDate);
        Assert.Equal([("205", "2027-03-02", "2027-03-03"), ("203", "2027-03-03", null)], r.Segments.Select(x => (x.RoomNo, x.From, x.To)));

        var c = mine.Single(x => x.GuestName == "Lista Confirmed");
        Assert.Equal((ReservationStatuses.Confirmed, false, null, null), (c.Status, c.Registered, c.GuestId, c.StayId));
        Assert.Empty(c.Segments);
        Assert.Equal((await S.Booking.GetAsync(1, 3, confirmed.ReservationId, Ct)).Version, c.Version);
        Assert.Equal("Changed plans", mine.Single(x => x.GuestName == "Lista Cancelled").CancelReason);
        Assert.NotNull(mine.Single(x => x.GuestName == "Lista Held").HoldUntil);

        // Views and filters.
        Assert.Contains(r.ReservationId, (await ListAsync(ReservationViews.InHouse)).Items.Select(x => x.ReservationId));
        Assert.DoesNotContain(c.ReservationId, (await ListAsync(ReservationViews.InHouse)).Items.Select(x => x.ReservationId));
        Assert.DoesNotContain(cancelled.ReservationId, (await ListAsync(ReservationViews.Active)).Items.Select(x => x.ReservationId));
        Assert.Equal(["Lista Held"], (await ListAsync(status: "Held", from: "2027-03-01", to: "2027-03-31")).Items.Select(x => x.GuestName));
        Assert.Equal(["Lista Cancelled"], (await ListAsync(status: "Cancelled", text: "Lista")).Items.Select(x => x.GuestName));
        Assert.Equal(["Lista Resident"], (await ListAsync(text: "resident", from: "2027-03-01", to: "2027-03-31")).Items.Select(x => x.GuestName));
        Assert.Equal(["Lista Held"], (await ListAsync(text: "202", from: "2027-03-01", to: "2027-03-31")).Items.Select(x => x.GuestName));
        Assert.Empty((await ListAsync(text: "100%_[x]")).Items);
        Assert.DoesNotContain(r.ReservationId, (await ListAsync(from: "2027-03-06", to: "2027-03-31")).Items.Select(x => x.ReservationId));
        Assert.Contains(r.ReservationId, (await ListAsync(from: "2027-03-05", to: "2027-03-31")).Items.Select(x => x.ReservationId));

        // Paging: 4 matches, 3 per page → 2 pages, newest arrival first and stable.
        var p1 = await ListAsync(from: "2027-03-01", to: "2027-03-31", text: "Lista", size: 3);
        var p2 = await ListAsync(from: "2027-03-01", to: "2027-03-31", text: "Lista", size: 3, page: 2);
        Assert.Equal((4, 3, 1), (p1.Total, p1.Items.Count, p2.Items.Count));
        Assert.Equal("Lista Cancelled", p1.Items[0].GuestName);
        Assert.Empty(p1.Items.Select(x => x.ReservationId).Intersect(p2.Items.Select(x => x.ReservationId)));

        // A lapsed hold reads Expired (and filters as Expired) without a sweep.
        var start = S.Clock.Now;
        try
        {
            S.Clock.Now = start.AddMinutes(180);
            Assert.Equal(ReservationStatuses.Expired, (await ListAsync(status: "Expired", text: "Lista Held")).Items.Single().Status);
            Assert.Empty((await ListAsync(status: "Held", text: "Lista Held")).Items);
        }
        finally
        {
            S.Clock.Now = start;
        }
    }

    [Fact]
    public async Task List_rejects_bad_input_and_needs_the_view_permission()
    {
        foreach (var bad in new Func<Task>[]
        {
            () => ListAsync(view: "everything"), () => ListAsync(status: "Gone"), () => ListAsync(from: "2027-03-05", to: "2027-03-05"),
            () => ListAsync(from: "05/03/2027"), () => ListAsync(page: 0), () => ListAsync(size: 501),
        })
            Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(bad)).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => ListAsync(principal: FakeAccessPolicy.Housekeeper))).Code);
        Assert.Contains(("PERM-RES-001"), f.Services.Access.Checks.Where(x => x.Principal == FakeAccessPolicy.Housekeeper).Select(x => x.Permission));
        _ = CultureInfo.InvariantCulture;
    }
}
