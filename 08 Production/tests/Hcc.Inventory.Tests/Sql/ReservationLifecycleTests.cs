using System.Globalization;
using Hcc.Application.Inventory;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Errors;
using Hcc.Inventory.Tests.Harness;

namespace Hcc.Inventory.Tests.Sql;

/// <summary>
/// Reservation lifecycle on the BASE fixture: holds (expiry, confirm), cancel / no-show per the status map, amend (atomic,
/// original untouched on failure, keep/requote), stay-restriction override (DEC-053), idempotency, group pickup with explicit
/// block context, block release/auto-release, room blocks, permissions, audit/outbox. Each test uses its own rooms/dates.
/// </summary>
[Trait("Category", "Sql")]
public sealed class ReservationLifecycleTests(GoldenDatabaseFixture f) : IClassFixture<GoldenDatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private P2Services S => f.Services;

    private async Task<QuoteResult> QuoteAsync(string room, string a, string d, int adults = 1, int principal = FakeAccessPolicy.Reception)
        => (await S.Inventory.QuoteStayAsync(P2Services.Ctx(principal, "QuoteStay"), new QuoteStayRequest(a, d, adults, 0, f.RoomIds[room]), Ct)).Value;

    private async Task<ReservationResult> BookAsync(string room, string a, string d, bool hold = false, int adults = 1, int principal = FakeAccessPolicy.Reception,
        long? block = null, string? overrideReason = null)
    {
        var q = await QuoteAsync(room, a, d, adults, principal);
        return (await S.Booking.CreateAsync(P2Services.Ctx(principal, "CreateReservation"),
            new CreateReservationRequest(q.QuoteId, q.Version, new GuestInput("Test Guest", "+91 90000 12345"), f.RoomIds[room], hold, overrideReason, block), Ct)).Value;
    }

    private Task<ReservationDetails> DetailsAsync(long id) => S.Booking.GetAsync(1, FakeAccessPolicy.Reception, id, Ct);

    private async Task<long> IdOfAsync(string publicNo)
        => Convert.ToInt64(await f.Db.ScalarAsync($"SELECT reservation_id FROM inv.reservation WHERE public_no = '{publicNo}';", Ct), CultureInfo.InvariantCulture);

    private async Task<bool> FreeAsync(string room, string a, string d, long? block = null)
        => (await S.Inventory.GetAvailabilityAsync(1, FakeAccessPolicy.Reception, new AvailabilityQuery(a, d, 1, 0, null, null, block), Ct)).Rooms.Single(r => r.RoomNo == room).Available;

    [Fact]
    public async Task Hold_is_held_for_120_minutes_then_confirmed_and_stale_versions_are_rejected()
    {
        var h = await BookAsync("205", "2026-11-02", "2026-11-03", hold: true);
        Assert.Equal(ReservationStatuses.Held, h.Status);
        Assert.Equal("2026-10-24T04:30:00.000Z", h.HoldUntil);  // 08:00 IST + 120 min = 10:00 IST
        Assert.False(await FreeAsync("205", "2026-11-02", "2026-11-03"));
        Assert.Equal("Held", (await S.Inventory.GetAvailabilityAsync(1, 3, new AvailabilityQuery("2026-11-02", "2026-11-03", 1), Ct)).Rooms.Single(r => r.RoomNo == "205").Reasons[0]);

        var bad = await Assert.ThrowsAsync<HccException>(() => S.Booking.ConfirmHoldAsync(P2Services.Ctx(3, "ConfirmHold"), new ConfirmHoldRequest(h.ReservationId, Convert.ToBase64String(new byte[8])), Ct));
        Assert.Equal(HccErrorCode.STALE_VERSION, bad.Code);
        var ok = (await S.Booking.ConfirmHoldAsync(P2Services.Ctx(3, "ConfirmHold"), new ConfirmHoldRequest(h.ReservationId, h.Version), Ct)).Value;
        Assert.Equal(ReservationStatuses.Confirmed, ok.Status);
        Assert.NotEqual(h.Version, ok.Version);
        var d = await DetailsAsync(h.ReservationId);
        Assert.Null(d.HoldUntil);
        var again = await Assert.ThrowsAsync<HccException>(() => S.Booking.ConfirmHoldAsync(P2Services.Ctx(3, "ConfirmHold"), new ConfirmHoldRequest(h.ReservationId, ok.Version), Ct));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, again.Code);
    }

    [Fact]
    public async Task Lapsed_holds_free_the_room_at_once_and_are_swept_to_Expired_by_the_next_command()
    {
        var start = S.Clock.Now;
        try
        {
            var h = await BookAsync("208", "2026-11-12", "2026-11-13", hold: true);
            S.Clock.Now = start.AddMinutes(120);
            Assert.True(await FreeAsync("208", "2026-11-12", "2026-11-13"), "a lapsed hold never blocks availability");
            Assert.Equal(ReservationStatuses.Expired, (await DetailsAsync(h.ReservationId)).Status);
            Assert.True(await FreeAsync("106", "2026-10-24", "2026-10-25"), "BASE hold HCC-1090 lapsed at 09:30 IST");

            var e = await Assert.ThrowsAsync<HccException>(() => S.Booking.ConfirmHoldAsync(P2Services.Ctx(3, "ConfirmHold"), new ConfirmHoldRequest(h.ReservationId, h.Version), Ct));
            Assert.Equal(HccErrorCode.INVALID_TRANSITION, e.Code);
            Assert.Contains("expired at 10:00", e.Message, StringComparison.Ordinal);
            Assert.Equal("Expired", (string)(await f.Db.ScalarAsync($"SELECT status FROM inv.reservation WHERE reservation_id = {h.ReservationId};", Ct))!);
            Assert.Equal(0, await f.Db.CountAsync($"SELECT COUNT(*) FROM inv.room_night WHERE reservation_id = {h.ReservationId};", Ct));
            Assert.Equal("Expired", (string)(await f.Db.ScalarAsync("SELECT status FROM inv.reservation WHERE public_no = 'HCC-1090';", Ct))!);
            Assert.True(await f.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'HoldExpired';", Ct) >= 2);

            var walkIn = await BookAsync("106", "2026-10-24", "2026-10-25");
            Assert.Equal(ReservationStatuses.Confirmed, walkIn.Status);
            Assert.Equal(0, await S.DriftAsync());
        }
        finally
        {
            S.Clock.Now = start;
        }
    }

    [Fact]
    public async Task Cancel_needs_settled_money_then_releases_the_room_nights()
    {
        var r = await BookAsync("108", "2026-10-28", "2026-10-30");
        await f.Db.ExecAsync($"""
            INSERT bill.folio(reservation_id) VALUES ({r.ReservationId});
            INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) SELECT folio_id, 'payment', 'UPI', 100000, '2026-10-24' FROM bill.folio WHERE reservation_id = {r.ReservationId};
            """, Ct);
        var e = await Assert.ThrowsAsync<HccException>(() => S.Booking.CancelAsync(P2Services.Ctx(3, "CancelReservation"), new CancelReservationRequest(r.ReservationId, r.Version, "Guest changed plans"), Ct));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, e.Code);
        Assert.Contains("Refund the advance", e.Message, StringComparison.Ordinal);

        await f.Db.ExecAsync($"""
            INSERT pay.receipt(folio_id, kind, method, amount_paise, refund_of, business_date)
            SELECT r.folio_id, 'refund', 'UPI', 100000, r.receipt_id, '2026-10-24' FROM pay.receipt r JOIN bill.folio f ON f.folio_id = r.folio_id WHERE f.reservation_id = {r.ReservationId};
            """, Ct);
        var c = (await S.Booking.CancelAsync(P2Services.Ctx(3, "CancelReservation"), new CancelReservationRequest(r.ReservationId, r.Version, "Guest changed plans"), Ct)).Value;
        Assert.Equal(ReservationStatuses.Cancelled, c.Status);
        var d = await DetailsAsync(r.ReservationId);
        Assert.Equal("Guest changed plans", d.CancelReason);
        Assert.Equal(0, await f.Db.CountAsync($"SELECT COUNT(*) FROM inv.room_night WHERE reservation_id = {r.ReservationId};", Ct));
        Assert.True(await FreeAsync("108", "2026-10-28", "2026-10-30"));
        var twice = await Assert.ThrowsAsync<HccException>(() => S.Booking.CancelAsync(P2Services.Ctx(3, "CancelReservation"), new CancelReservationRequest(r.ReservationId, c.Version), Ct));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, twice.Code);
        Assert.Equal(1, await f.Db.CountAsync($"SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ReservationCancelled' AND aggregate_id = {r.ReservationId};", Ct));
        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task No_show_only_for_confirmed_stays_on_or_after_arrival()
    {
        var future = await BookAsync("305", "2026-10-27", "2026-10-28");
        var e = await Assert.ThrowsAsync<HccException>(() => S.Booking.MarkNoShowAsync(P2Services.Ctx(3, "MarkNoShow"), new CancelReservationRequest(future.ReservationId, future.Version), Ct));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, e.Code);
        Assert.Contains("on or after the arrival", e.Message, StringComparison.Ordinal);

        var hold = await BookAsync("305", "2026-11-20", "2026-11-21", hold: true);
        var h = await Assert.ThrowsAsync<HccException>(() => S.Booking.MarkNoShowAsync(P2Services.Ctx(3, "MarkNoShow"), new CancelReservationRequest(hold.ReservationId, hold.Version), Ct));
        Assert.Contains("A hold cannot be marked no-show", h.Message, StringComparison.Ordinal);

        var id = await IdOfAsync("HCC-1050");
        var x = await DetailsAsync(id);
        var ns = (await S.Booking.MarkNoShowAsync(P2Services.Ctx(3, "MarkNoShow"), new CancelReservationRequest(id, x.Version, "Did not arrive"), Ct)).Value;
        Assert.Equal(ReservationStatuses.NoShow, ns.Status);
        Assert.True(await FreeAsync("207", "2026-10-24", "2026-10-26"));

        var inHouse = await IdOfAsync("HCC-1041");
        var ih = await Assert.ThrowsAsync<HccException>(async () => await S.Booking.CancelAsync(P2Services.Ctx(3, "CancelReservation"),
            new CancelReservationRequest(inHouse, (await DetailsAsync(inHouse)).Version), Ct));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, ih.Code);
    }

    [Fact]
    public async Task Amend_is_atomic_keeps_the_agreed_rate_and_requotes_on_request()
    {
        var id = await IdOfAsync("HCC-1049");
        var before = await DetailsAsync(id);
        var nightsBefore = await f.Db.CountAsync($"SELECT COUNT(*) FROM inv.room_night WHERE reservation_id = {id} AND room_id = {f.RoomIds["103"]};", Ct);
        Assert.Equal(2, nightsBefore);

        var conflict = await Assert.ThrowsAsync<HccException>(() => S.Booking.AmendAsync(P2Services.Ctx(3, "AmendReservation"),
            new AmendReservationRequest(id, before.Version, "2026-10-24", "2026-10-27", f.RoomIds["101"]), Ct));
        Assert.Equal(HccErrorCode.NOT_AVAILABLE, conflict.Code);
        var unchanged = await DetailsAsync(id);
        Assert.Equal((before.Version, before.RoomNo, before.Arrival, before.Departure), (unchanged.Version, unchanged.RoomNo, unchanged.Arrival, unchanged.Departure));
        Assert.Equal(2, await f.Db.CountAsync($"SELECT COUNT(*) FROM inv.room_night WHERE reservation_id = {id} AND room_id = {f.RoomIds["103"]};", Ct));

        Assert.Equal("STALE_VERSION", await Outcome.CodeAsync(() => S.Booking.AmendAsync(P2Services.Ctx(3, "AmendReservation"),
            new AmendReservationRequest(id, Convert.ToBase64String(new byte[8]), "2026-10-25", "2026-10-26", f.RoomIds["105"]), Ct)));
        Assert.Equal("VALIDATION", await Outcome.CodeAsync(() => S.Booking.AmendAsync(P2Services.Ctx(3, "AmendReservation"),
            new AmendReservationRequest(id, before.Version, "2026-10-23", "2026-10-26", f.RoomIds["105"]), Ct)));
        Assert.Equal("VALIDATION", await Outcome.CodeAsync(() => S.Booking.AmendAsync(P2Services.Ctx(3, "AmendReservation"),
            new AmendReservationRequest(id, before.Version, before.Arrival, before.Departure, before.RoomId), Ct)));
        var inHouse = await IdOfAsync("HCC-1041");
        Assert.Equal("INVALID_TRANSITION", await Outcome.CodeAsync(async () => await S.Booking.AmendAsync(P2Services.Ctx(3, "AmendReservation"),
            new AmendReservationRequest(inHouse, (await DetailsAsync(inHouse)).Version, "2026-10-24", "2026-10-26", f.RoomIds["105"]), Ct)));

        var moved = (await S.Booking.AmendAsync(P2Services.Ctx(3, "AmendReservation"),
            new AmendReservationRequest(id, before.Version, "2026-10-25", "2026-10-28", f.RoomIds["105"]), Ct)).Value;
        var after = await DetailsAsync(id);
        Assert.Equal(("105", "2026-10-25", "2026-10-28", before.NightlyRatePaise), (after.RoomNo, after.Arrival, after.Departure, after.NightlyRatePaise));
        Assert.Equal(3, after.Nights.Count);
        Assert.All(after.Nights, n => Assert.Equal(before.NightlyRatePaise, n.RatePaise));
        Assert.True(await FreeAsync("103", "2026-10-24", "2026-10-26"));
        Assert.Equal(3, await f.Db.CountAsync($"SELECT COUNT(*) FROM inv.room_night WHERE reservation_id = {id} AND room_id = {f.RoomIds["105"]};", Ct));

        // Requote: tariff for Classic raised from 2026-10-26; the new quote's nights replace the agreed rate.
        await f.Db.ExecAsync("INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise) SELECT room_type_id, '2026-10-26', 210000 FROM inv.room_type WHERE code = 'Classic';", Ct);
        try
        {
            var q = await QuoteAsync("105", "2026-10-26", "2026-10-29");
            Assert.All(q.Nights, n => Assert.Equal(210000, n.RatePaise));
            var rq = (await S.Booking.AmendAsync(P2Services.Ctx(3, "AmendReservation"),
                new AmendReservationRequest(id, moved.Version, "2026-10-26", "2026-10-29", f.RoomIds["105"], PriceDecisions.Requote, q.QuoteId, q.Version), Ct)).Value;
            var rd = await DetailsAsync(id);
            Assert.Equal((210000L, 693000L), (rd.NightlyRatePaise!.Value, rd.TotalPaise));
            Assert.Equal("VALIDATION", await Outcome.CodeAsync(() => S.Booking.AmendAsync(P2Services.Ctx(3, "AmendReservation"),
                new AmendReservationRequest(id, rq.Version, "2026-10-26", "2026-10-30", f.RoomIds["105"], PriceDecisions.Requote, q.QuoteId, q.Version), Ct)));
        }
        finally
        {
            await f.Db.ExecAsync("DELETE inv.room_type_tariff WHERE effective_from = '2026-10-26';", Ct);
        }

        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task Restricted_amend_is_rejected_with_violations_and_an_approver_may_override_with_a_reason()
    {
        var r = await BookAsync("105", "2026-11-12", "2026-11-13");
        var e = await Assert.ThrowsAsync<HccException>(() => S.Booking.AmendAsync(P2Services.Ctx(3, "AmendReservation"),
            new AmendReservationRequest(r.ReservationId, r.Version, "2026-11-07", "2026-11-08", f.RoomIds["105"]), Ct));
        Assert.Equal(HccErrorCode.RESTRICTED, e.Code);
        var v = e.Details!["violations"];
        Assert.Equal("MIN_STAY", v[0].GetProperty("code").GetString());
        Assert.Equal("YLDR-1", v[0].GetProperty("restriction").GetString());
        Assert.Equal("2026-11-12", (await DetailsAsync(r.ReservationId)).Arrival);

        Assert.Equal("FORBIDDEN", await Outcome.CodeAsync(() => S.Booking.AmendAsync(P2Services.Ctx(3, "AmendReservation"),
            new AmendReservationRequest(r.ReservationId, r.Version, "2026-11-07", "2026-11-08", f.RoomIds["105"], RestrictionOverride: "Regular guest"), Ct)));
        Assert.Equal("VALIDATION", await Outcome.CodeAsync(() => S.Booking.AmendAsync(P2Services.Ctx(2, "AmendReservation"),
            new AmendReservationRequest(r.ReservationId, r.Version, "2026-11-07", "2026-11-08", f.RoomIds["105"], RestrictionOverride: "x"), Ct)));
        await S.Booking.AmendAsync(P2Services.Ctx(2, "AmendReservation"),
            new AmendReservationRequest(r.ReservationId, r.Version, "2026-11-07", "2026-11-08", f.RoomIds["105"], RestrictionOverride: "Regular corporate guest"), Ct);
        var d = await DetailsAsync(r.ReservationId);
        Assert.Equal("2026-11-07", d.Arrival);
        Assert.Contains("restriction overridden (MIN_STAY YLDR-1): Regular corporate guest", d.Notes, StringComparison.Ordinal);
        Assert.Contains(S.Access.Checks, c => c.Principal == 2 && c.Permission == InventoryPermissions.ApproveOverride);
        Assert.Equal(1, await f.Db.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'AmendReservation' AND target = (SELECT public_no FROM inv.reservation WHERE reservation_id = {r.ReservationId}) AND detail LIKE '%restriction overridden%';", Ct));
    }

    [Fact]
    public async Task Same_key_replays_the_original_booking_and_a_different_payload_is_a_mismatch()
    {
        var q = await QuoteAsync("305", "2026-12-01", "2026-12-02");
        var req = new CreateReservationRequest(q.QuoteId, q.Version, new GuestInput("Replay Guest"), f.RoomIds["305"]);
        var first = await S.Booking.CreateAsync(P2Services.Ctx(3, "CreateReservation", "book:once"), req, Ct);
        var second = await S.Booking.CreateAsync(P2Services.Ctx(3, "CreateReservation", "book:once"), req, Ct);
        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(1, await f.Db.CountAsync("SELECT COUNT(*) FROM inv.reservation WHERE guest_name = N'Replay Guest';", Ct));
        Assert.Equal("IDEMPOTENCY_MISMATCH", await Outcome.CodeAsync(() => S.Booking.CreateAsync(P2Services.Ctx(3, "CreateReservation", "book:once"), req with { Hold = true }, Ct)));
        Assert.Equal("VALIDATION", await Outcome.CodeAsync(() => S.Booking.CreateAsync(P2Services.Ctx(3, "CreateReservation"), req, Ct)));  // quote already used
    }

    [Fact]
    public async Task Group_pickup_needs_explicit_block_context_and_returns_the_room_to_the_block_on_cancel()
    {
        var gb4 = Convert.ToInt64(await f.Db.ScalarAsync("SELECT group_block_id FROM inv.group_block WHERE code = 'GB-4';", Ct), CultureInfo.InvariantCulture);
        Assert.Equal("NOT_AVAILABLE", await Outcome.CodeAsync(() => BookAsync("307", "2026-10-25", "2026-10-28", adults: 2)));

        var pick = await BookAsync("307", "2026-10-25", "2026-10-28", adults: 2, block: gb4);
        var d = await DetailsAsync(pick.ReservationId);
        Assert.Equal(("Group · GB-4", 250000L, gb4), (d.Source, d.NightlyRatePaise!.Value, d.GroupBlockId!.Value));
        Assert.Equal(1, await f.Db.CountAsync($"SELECT picked FROM inv.group_block_type WHERE group_block_id = {gb4};", Ct));
        var avail = await S.Inventory.GetAvailabilityAsync(1, 3, new AvailabilityQuery("2026-10-25", "2026-10-28", 1, 0, f.TypeIds["Deluxe"]), Ct);
        Assert.Equal(["303", "304"], avail.Rooms.Where(r => r.HeldForBlock == "GB-4").Select(r => r.RoomNo).Order(StringComparer.Ordinal));
        Assert.Equal("NOT_AVAILABLE", await Outcome.CodeAsync(() => BookAsync("304", "2026-10-25", "2026-10-26")));

        var later = (await S.Inventory.CreateGroupBlockAsync(P2Services.Ctx(3, "CreateGroupBlock"), new CreateGroupBlockRequest("GB-P1", "Later group", "2026-12-26",
            "2026-12-28", "2026-12-20", [new GroupBlockAllotment(f.TypeIds["Deluxe"], 1)]), Ct)).Value;
        Assert.Equal("VALIDATION", await Outcome.CodeAsync(() => BookAsync("303", "2026-10-25", "2026-10-28", block: later.GroupBlockId)));  // outside that block's window

        await S.Booking.CancelAsync(P2Services.Ctx(3, "CancelReservation"), new CancelReservationRequest(pick.ReservationId, pick.Version), Ct);
        Assert.Equal(0, await f.Db.CountAsync($"SELECT picked FROM inv.group_block_type WHERE group_block_id = {gb4};", Ct));
        Assert.Equal(3, await f.Db.CountAsync($"SELECT held FROM inv.type_night_count WHERE room_type_id = {f.TypeIds["Deluxe"]} AND night = '2026-10-26';", Ct));
        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task Releasing_a_block_frees_its_rooms_and_blocks_past_cutoff_are_released_by_the_sweep()
    {
        var gb2 = await f.Db.ScalarAsync("SELECT CONCAT(group_block_id, '|', CONVERT(varchar(20), CAST(row_ver AS varbinary(8)), 2)) FROM inv.group_block WHERE code = 'GB-2';", Ct);
        var parts = ((string)gb2!).Split('|');
        var id = long.Parse(parts[0], CultureInfo.InvariantCulture);
        var ver = Convert.ToBase64String(Convert.FromHexString(parts[1]));
        Assert.False(await FreeAsync("305", "2026-10-30", "2026-11-01"));
        Assert.Equal("STALE_VERSION", await Outcome.CodeAsync(() => S.Inventory.ReleaseGroupBlockAsync(P2Services.Ctx(3, "ReleaseGroupBlock"), new ReleaseGroupBlockRequest(id, Convert.ToBase64String(new byte[8])), Ct)));
        var rel = (await S.Inventory.ReleaseGroupBlockAsync(P2Services.Ctx(3, "ReleaseGroupBlock"), new ReleaseGroupBlockRequest(id, ver), Ct)).Value;
        Assert.Equal("Released", rel.Status);
        Assert.True(await FreeAsync("305", "2026-10-30", "2026-11-01"));
        Assert.Equal("INVALID_TRANSITION", await Outcome.CodeAsync(() => S.Inventory.ReleaseGroupBlockAsync(P2Services.Ctx(3, "ReleaseGroupBlock"), new ReleaseGroupBlockRequest(id, rel.Version), Ct)));

        // GB-3 cut-off is 2026-10-24: still open today, auto-released once the business date passes it.
        Assert.False(await FreeAsync("306", "2026-11-06", "2026-11-08"));
        await f.Db.ExecAsync("UPDATE inv.property SET business_date = '2026-10-25';", Ct);
        try
        {
            Assert.True(await FreeAsync("306", "2026-11-06", "2026-11-08"), "a block past its cut-off reads Released before the sweep");
            await S.Store.SweepAsync(1, 3, S.Clock.Now.UtcDateTime, Ct);
            Assert.Equal("Released", (string)(await f.Db.ScalarAsync("SELECT status FROM inv.group_block WHERE code = 'GB-3';", Ct))!);
            Assert.Equal(1, await f.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'GroupBlockAutoRelease' AND target = 'GB-3';", Ct));
        }
        finally
        {
            await f.Db.ExecAsync("UPDATE inv.property SET business_date = '2026-10-24';", Ct);
        }

        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task New_group_blocks_are_capacity_checked_against_the_type_counts()
    {
        var suite = f.TypeIds["Suite"];
        var b = (await S.Inventory.CreateGroupBlockAsync(P2Services.Ctx(3, "CreateGroupBlock"), new CreateGroupBlockRequest("GB-T1", "Test suites", "2026-11-20", "2026-11-22",
            "2026-11-15", [new GroupBlockAllotment(suite, 2, 400000)]), Ct)).Value;
        Assert.Equal("Open", b.Status);
        Assert.Equal(2, await f.Db.CountAsync($"SELECT held FROM inv.type_night_count WHERE room_type_id = {suite} AND night = '2026-11-21';", Ct));
        var avail = await S.Inventory.GetAvailabilityAsync(1, 3, new AvailabilityQuery("2026-11-20", "2026-11-22", 1, 0, suite), Ct);
        Assert.Equal(1, avail.Types.Single().AvailableRooms);
        Assert.Equal(["206", "306"], avail.Rooms.Where(r => r.HeldForBlock == "GB-T1").Select(r => r.RoomNo).Order(StringComparer.Ordinal));
        var full = await Assert.ThrowsAsync<HccException>(() => S.Inventory.CreateGroupBlockAsync(P2Services.Ctx(3, "CreateGroupBlock"),
            new CreateGroupBlockRequest("GB-T2", "Too many", "2026-11-21", "2026-11-23", "2026-11-15", [new GroupBlockAllotment(suite, 2)]), Ct));
        Assert.Equal(HccErrorCode.NOT_AVAILABLE, full.Code);
        Assert.Equal(0, await f.Db.CountAsync("SELECT COUNT(*) FROM inv.group_block WHERE code = 'GB-T2';", Ct));
        Assert.Equal("VALIDATION", await Outcome.CodeAsync(() => S.Inventory.CreateGroupBlockAsync(P2Services.Ctx(3, "CreateGroupBlock"),
            new CreateGroupBlockRequest("GB-T1", "Duplicate", "2026-12-20", "2026-12-22", "2026-12-15", [new GroupBlockAllotment(suite, 1)]), Ct)));
        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task Room_blocks_reduce_sellable_and_cannot_cover_booked_nights()
    {
        var classic = f.TypeIds["Classic"];
        await BookAsync("205", "2026-12-10", "2026-12-12");  // materializes the Classic count rows for those nights
        var before = await f.Db.CountAsync($"SELECT sellable FROM inv.type_night_count WHERE room_type_id = {classic} AND night = '2026-12-10';", Ct);
        long blockId = 0;
        await S.Runner.RunAsync(P2Services.Ctx(1, "TestRoomBlock"), new { room = 208 }, async (tx, ct) =>
        {
            blockId = await S.RoomBlocks.PlaceAsync(tx, 1, f.RoomIds["208"], new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 20), "AC repair (fictional)", "Maintenance", 77, ct);
            await tx.AuditAsync("TestRoomBlock", "208", "block", ct);
            return new Done(true);
        }, Ct);
        Assert.Equal(before - 1, await f.Db.CountAsync($"SELECT sellable FROM inv.type_night_count WHERE room_type_id = {classic} AND night = '2026-12-10';", Ct));
        Assert.Equal("Blocked", (await S.Inventory.GetAvailabilityAsync(1, 3, new AvailabilityQuery("2026-12-10", "2026-12-11", 1), Ct)).Rooms.Single(r => r.RoomNo == "208").Reasons[0]);
        Assert.Equal("NOT_AVAILABLE", await Outcome.CodeAsync(() => S.Runner.RunAsync(P2Services.Ctx(1, "TestRoomBlock"), new { room = 205 }, async (tx, ct) =>
        {
            await S.RoomBlocks.PlaceAsync(tx, 1, f.RoomIds["205"], new DateOnly(2026, 12, 11), null, "Leak (fictional)", "Maintenance", 78, ct);
            await tx.AuditAsync("TestRoomBlock", "205", "block", ct);
            return new Done(true);
        }, Ct)));
        Assert.Equal(0, await S.DriftAsync());

        await S.Runner.RunAsync(P2Services.Ctx(1, "TestRoomUnblock"), new { blockId }, async (tx, ct) =>
        {
            await S.RoomBlocks.RemoveAsync(tx, 1, blockId, ct);
            await tx.AuditAsync("TestRoomUnblock", "208", "unblock", ct);
            return new Done(true);
        }, Ct);
        Assert.Equal(before, await f.Db.CountAsync($"SELECT sellable FROM inv.type_night_count WHERE room_type_id = {classic} AND night = '2026-12-10';", Ct));
        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task Every_handler_checks_its_permission_first()
    {
        Assert.Equal("FORBIDDEN", await Outcome.CodeAsync(() => S.Inventory.GetAvailabilityAsync(1, FakeAccessPolicy.Housekeeper, new AvailabilityQuery("2026-12-01", "2026-12-02", 1), Ct)));
        Assert.Equal("FORBIDDEN", await Outcome.CodeAsync(() => S.Inventory.QuoteStayAsync(P2Services.Ctx(FakeAccessPolicy.Housekeeper, "QuoteStay"), new QuoteStayRequest("2026-12-01", "2026-12-02", 1, 0, f.RoomIds["105"]), Ct)));
        Assert.Equal("FORBIDDEN", await Outcome.CodeAsync(() => S.Booking.CreateAsync(P2Services.Ctx(FakeAccessPolicy.Housekeeper, "CreateReservation"), new CreateReservationRequest(1, "AAAAAAAAAAA=", new GuestInput("X Y"), 1), Ct)));
        Assert.Equal("FORBIDDEN", await Outcome.CodeAsync(() => S.Booking.AmendAsync(P2Services.Ctx(FakeAccessPolicy.Housekeeper, "AmendReservation"), new AmendReservationRequest(1, "AAAAAAAAAAA=", "2026-12-01", "2026-12-02", 1), Ct)));
        Assert.Equal("FORBIDDEN", await Outcome.CodeAsync(() => S.Booking.CancelAsync(P2Services.Ctx(FakeAccessPolicy.Housekeeper, "CancelReservation"), new CancelReservationRequest(1, "AAAAAAAAAAA="), Ct)));
        Assert.Equal("FORBIDDEN", await Outcome.CodeAsync(() => S.Booking.GetAsync(1, FakeAccessPolicy.Housekeeper, 1, Ct)));
        var perms = S.Access.Checks.Where(c => c.Principal == FakeAccessPolicy.Housekeeper).Select(c => c.Permission).ToList();
        Assert.Equal(["PERM-RES-001", "PERM-RES-002", "PERM-RES-002", "PERM-RES-003", "PERM-RES-004", "PERM-RES-001"], perms);
        Assert.Equal("NOT_FOUND", await Outcome.CodeAsync(() => S.Booking.GetAsync(1, FakeAccessPolicy.Reception, 999999, Ct)));
    }

    [Fact]
    public async Task Created_reservations_carry_nights_audit_and_outbox_in_one_commit()
    {
        var r = await BookAsync("105", "2026-12-20", "2026-12-23", adults: 2);
        var d = await DetailsAsync(r.ReservationId);
        Assert.StartsWith("HCC-", d.PublicNo, StringComparison.Ordinal);
        Assert.Equal((3, 600000L, 60000L, 660000L), (d.Nights.Count, d.NetPaise, d.TaxPaise, d.TotalPaise));
        Assert.Equal("+91 90000 12345", d.Phone);
        Assert.Equal(1, await f.Db.CountAsync($"SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ReservationCreated' AND aggregate_id = {r.ReservationId};", Ct));
        Assert.Equal(1, await f.Db.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'CreateReservation' AND target = '{d.PublicNo}';", Ct));
        Assert.Equal(r.ReservationId, Convert.ToInt64(await f.Db.ScalarAsync($"SELECT used_by_reservation_id FROM bill.quote WHERE quote_id = (SELECT quote_id FROM inv.reservation WHERE reservation_id = {r.ReservationId});", Ct), CultureInfo.InvariantCulture));
        Assert.Equal("VALIDATION", await Outcome.CodeAsync(() => S.Booking.CreateAsync(P2Services.Ctx(3, "CreateReservation"),
            new CreateReservationRequest(1, "AAAAAAAAAAA=", new GuestInput("A"), f.RoomIds["105"]), Ct)));  // name too short (checked before the quote)
    }
}
