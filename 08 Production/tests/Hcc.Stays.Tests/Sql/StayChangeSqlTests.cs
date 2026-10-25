using System.Globalization;
using Hcc.Application.Housekeeping;
using Hcc.Contracts.Housekeeping;
using Hcc.Contracts.Stays;
using Hcc.Stays.Tests.Harness;

namespace Hcc.Stays.Tests.Sql;

/// <summary>
/// W3-7: room move, extend, shorten, early departure over SQL with the real access policy and the real allocator. BASE (24 Oct 2026):
/// 101 Ananya Rao (Classic, 23 to 26, 4,000 rupees agreed, night 23 posted). Tests check that the maintained type-night counts equal
/// a recomputation from the room nights.
/// </summary>
[Trait("Category", "Sql")]
public class StayChangeSqlTests : IAsyncLifetime
{
    private StayChangeWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await StayChangeWorld.CreateAsync(ct: Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private async Task<string> VerAsync(string booking) => await _w.Seed.StayVersionAsync(_w.Stay(booking), Ct);

    private async Task<string> Q(string sql) => (await _w.Db.StringAsync(sql, Ct))!;

    private long Res(string booking) => _w.Seed.Reservations[booking];

    private async Task<MoveRoomResult> MoveAsync(int who, string booking, string toRoom, string key, string? rate = null, long? newRate = null, string reason = "Guest asked for a quieter room")
        => (await _w.Changes.MoveRoomAsync(_w.Ctx(who, key), new MoveRoomRequest(_w.Stay(booking), await VerAsync(booking), _w.Room(toRoom), reason, rate, newRate), Ct)).Value;

    [Fact]
    public async Task Move_same_type_reallocates_from_the_business_date_dirties_the_old_room_and_creates_one_clean()
    {
        var before = await VerAsync("HCC-1041");
        var r = await MoveAsync(_w.Reception, "HCC-1041", "105", "mv-1");
        Assert.Equal(("101", "105", "Dirty", "2026-10-24"), (r.FromRoomNo, r.ToRoomNo, r.FromRoomHk, r.BusinessDate));
        Assert.Equal(StayChangeWorld.Rate, r.NightlyRatePaise); // agreed tariff preserved
        Assert.Equal(0, r.RateLossPaise);
        Assert.NotEqual(before, r.StayVersion);
        Assert.Equal("101@2026-10-23 105@2026-10-24 105@2026-10-25", await _w.NightsOfAsync("HCC-1041", Ct));
        Assert.Equal("Dirty", await Q($"SELECT hk_status FROM inv.room WHERE room_id = {_w.Room("101")};"));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM ops.hk_task WHERE room_id = {_w.Room("101")} AND kind = 'move' AND stay_id = {_w.Stay("HCC-1041")} AND status = 'ToDo';", Ct));
        Assert.Equal($"hk:move:{_w.Stay("HCC-1041")}:1", await Q($"SELECT causal_key FROM ops.hk_task WHERE task_id = {r.HousekeepingTaskId};"));
        Assert.Equal("105", await Q($"SELECT room_no FROM gst.stay_segment s JOIN inv.room r ON r.room_id = s.room_id WHERE s.stay_id = {_w.Stay("HCC-1041")} AND s.to_date IS NULL;"));
        Assert.Equal("2026-10-24", await Q($"SELECT CONVERT(CHAR(10), to_date, 23) FROM gst.stay_segment WHERE stay_id = {_w.Stay("HCC-1041")} AND seq = 1;"));
        Assert.Equal(_w.Room("105").ToString(CultureInfo.InvariantCulture), await Q($"SELECT CAST(room_id AS varchar(10)) FROM inv.reservation WHERE reservation_id = {Res("HCC-1041")};"));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'StayRoomMoved' AND aggregate_id = {_w.Stay("HCC-1041")};", Ct));
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'MOVE_ROOM';", Ct));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.stay_change WHERE stay_id = {_w.Stay("HCC-1041")} AND kind = 'move' AND rate_decision = 'keep' AND rate_loss_paise = 0;", Ct));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task Move_to_another_type_shifts_the_type_counts_and_keeps_the_agreed_rate_and_price_snapshot()
    {
        await MoveAsync(_w.Manager, "HCC-1041", "106", "mv-suite"); // Classic to Suite
        Assert.Equal("101@2026-10-23 106@2026-10-24 106@2026-10-25", await _w.NightsOfAsync("HCC-1041", Ct));
        Assert.Equal("Suite", await Q($"SELECT t.code FROM inv.reservation r JOIN inv.room_type t ON t.room_type_id = r.room_type_id WHERE r.reservation_id = {Res("HCC-1041")};"));
        Assert.Equal(3, await _w.Db.CountAsync($"SELECT COUNT(*) FROM inv.reservation_night WHERE reservation_id = {Res("HCC-1041")} AND rate_paise = {StayChangeWorld.Rate};", Ct));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task Move_replay_with_the_same_key_returns_the_stored_result_and_a_second_move_back_creates_its_own_clean()
    {
        var req = new MoveRoomRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), _w.Room("105"), "Noise from the lift", null, null);
        var first = await _w.Changes.MoveRoomAsync(_w.Ctx(_w.Reception, "mv-same"), req, Ct);
        var again = await _w.Changes.MoveRoomAsync(_w.Ctx(_w.Reception, "mv-same"), req, Ct);
        Assert.True(again.Replayed && again.Value == first.Value);
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM ops.hk_task WHERE kind = 'move';", Ct));
        // Housekeeping cleans 101 (Ready again), then the guest moves back the same day: a third segment, a second clean (for 105).
        await _w.Db.ExecAsync($"UPDATE inv.room SET hk_status = 'Ready' WHERE room_id = {_w.Room("101")};", Ct);
        await MoveAsync(_w.Reception, "HCC-1041", "101", "mv-back");
        Assert.Equal("101@2026-10-23 101@2026-10-24 101@2026-10-25", await _w.NightsOfAsync("HCC-1041", Ct));
        Assert.Equal(2, await _w.Db.CountAsync("SELECT COUNT(*) FROM ops.hk_task WHERE kind = 'move';", Ct));
        Assert.Equal(3, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.stay_segment WHERE stay_id = {_w.Stay("HCC-1041")};", Ct));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task Move_refusals_leave_everything_untouched()
    {
        var snapshot = await _w.NightsOfAsync("HCC-1041", Ct);
        async Task<string> Code(int who, string room, string key, string reason = "Guest request") =>
            await Expect.CodeAsync(async () => await MoveAsync(who, "HCC-1041", room, key, reason: reason));
        Assert.Equal("NOT_AVAILABLE", await Code(_w.Reception, "108", "r1"));      // dirty
        Assert.Equal("NOT_AVAILABLE", await Code(_w.Reception, "102", "r2"));      // occupied by another in-house guest
        Assert.Equal("NOT_AVAILABLE", await Code(_w.Reception, "308", "r3"));      // blocked out of order
        Assert.Equal("VALIDATION", await Code(_w.Reception, "101", "r5"));         // same room
        Assert.Equal("VALIDATION", await Code(_w.Reception, "105", "r6", reason: " "));   // reason required
        Assert.Equal("FORBIDDEN", await Code(_w.Housekeeper, "105", "r7"));
        var stale = await Expect.CodeAsync(async () => await _w.Changes.MoveRoomAsync(_w.Ctx(_w.Reception, "r8"),
            new MoveRoomRequest(_w.Stay("HCC-1041"), await _w.Seed.StayVersionAsync(_w.Stay("HCC-1042"), Ct), _w.Room("105"), "stale", null, null), Ct));
        Assert.Equal("STALE_VERSION", stale);
        Assert.Equal(snapshot, await _w.NightsOfAsync("HCC-1041", Ct));
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM ops.hk_task WHERE kind = 'move';", Ct));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task Move_onto_a_room_held_by_an_arrival_is_refused_by_the_allocator()
    {
        // 105 is vacant and Ready now, but another reservation holds it for the night of 25 Oct.
        await _w.Seed.ReservationAsync("105", "2026-10-25", "2026-10-26", "Arrival Tomorrow", ct: Ct);
        Assert.Equal("NOT_AVAILABLE", await Expect.CodeAsync(async () => await MoveAsync(_w.Reception, "HCC-1041", "105", "held")));
        Assert.Equal("101@2026-10-23 101@2026-10-24 101@2026-10-25", await _w.NightsOfAsync("HCC-1041", Ct));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task Move_with_a_lower_rate_needs_approval_rights_and_reprices_only_the_unposted_nights()
    {
        // Reception cannot give revenue up; the Manager can within the limit (1,000 rupees), beyond it APPROVAL_REQUIRED; the Owner can.
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await MoveAsync(_w.Reception, "HCC-1041", "105", "d1", "change", 395_000)));
        Assert.Equal("APPROVAL_REQUIRED", await Expect.CodeAsync(async () => await MoveAsync(_w.Manager, "HCC-1041", "105", "d2", "change", 300_000)));
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM ops.hk_task WHERE kind = 'move';", Ct));
        var ok = await MoveAsync(_w.Manager, "HCC-1041", "105", "d3", "change", 395_000); // 2 nights x 50 rupees = 100 rupees given up
        Assert.Equal((395_000L, 10_000L), (ok.NightlyRatePaise, ok.RateLossPaise));
        Assert.Equal("400000,395000,395000", await Q($"SELECT STRING_AGG(CAST(rate_paise AS varchar(12)), ',') WITHIN GROUP (ORDER BY night) FROM inv.reservation_night WHERE reservation_id = {Res("HCC-1041")};"));
        Assert.Equal("395000", await Q($"SELECT CAST(nightly_rate_paise AS varchar(12)) FROM inv.reservation WHERE reservation_id = {Res("HCC-1041")};"));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.stay_change WHERE stay_id = {_w.Stay("HCC-1041")} AND rate_decision = 'change' AND old_rate_paise = 400000 AND new_rate_paise = 395000 AND rate_loss_paise = 10000;", Ct));
        var up = await MoveAsync(_w.Reception, "HCC-1042", "107", "d4", "change", 450_000); // a raise needs no approval
        Assert.Equal(0, up.RateLossPaise);
        var owner = await MoveAsync(_w.Owner, "HCC-1043", "203", "d5", "change", 300_000);
        Assert.Equal(200_000, owner.RateLossPaise);
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task Extend_adds_the_nights_through_the_allocator_prices_them_at_the_agreed_rate_and_is_refused_when_taken()
    {
        var res = Res("HCC-1041");
        await _w.Seed.ReservationAsync("101", "2026-10-27", "2026-10-28", "Next Guest", ct: Ct); // another guest holds 101 on the night of 27 Oct
        Assert.Equal("NOT_AVAILABLE", await Expect.CodeAsync(async () => await _w.Changes.ExtendStayAsync(_w.Ctx(_w.Reception, "x-taken"),
            new ExtendStayRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), "2026-10-28"), Ct)));
        Assert.Equal("2026-10-26", await Q($"SELECT CONVERT(CHAR(10), departure, 23) FROM gst.stay WHERE stay_id = {_w.Stay("HCC-1041")};")); // rolled back
        Assert.Equal(3, await _w.Db.CountAsync($"SELECT COUNT(*) FROM inv.room_night WHERE reservation_id = {res};", Ct));

        var r = (await _w.Changes.ExtendStayAsync(_w.Ctx(_w.Reception, "x-1"), new ExtendStayRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), "2026-10-27"), Ct)).Value;
        Assert.Equal(("2026-10-26", "2026-10-27", 1, StayChangeWorld.Rate, 0L), (r.OldDeparture, r.NewDeparture, r.NightsChanged, r.NightlyRatePaise, r.RateLossPaise));
        Assert.Equal("101@2026-10-23 101@2026-10-24 101@2026-10-25 101@2026-10-26", await _w.NightsOfAsync("HCC-1041", Ct));
        Assert.Equal("2026-10-27", await Q($"SELECT CONVERT(CHAR(10), departure, 23) FROM inv.reservation WHERE reservation_id = {res};"));
        Assert.Equal("400000/20000", await Q($"SELECT CONCAT(rate_paise, '/', tax_paise) FROM inv.reservation_night WHERE reservation_id = {res} AND night = '2026-10-26';"));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'StayExtended' AND aggregate_id = {_w.Stay("HCC-1041")};", Ct));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task Extend_validation_and_rate_change_for_the_extended_and_unposted_nights()
    {
        async Task<string> Code(string dep, string key) => await Expect.CodeAsync(async () => await _w.Changes.ExtendStayAsync(_w.Ctx(_w.Reception, key),
            new ExtendStayRequest(_w.Stay("HCC-1042"), await VerAsync("HCC-1042"), dep), Ct));
        Assert.Equal("VALIDATION", await Code("2026-10-26", "e1"));   // not later
        Assert.Equal("VALIDATION", await Code("2026-12-01", "e2"));   // more than 30 nights
        Assert.Equal("VALIDATION", await Code("26/10", "e3"));
        var r = (await _w.Changes.ExtendStayAsync(_w.Ctx(_w.Owner, "e4"),
            new ExtendStayRequest(_w.Stay("HCC-1042"), await VerAsync("HCC-1042"), "2026-10-27", "Corporate rate for the extra day", "change", 380_000), Ct)).Value;
        Assert.Equal(3 * 20_000L, r.RateLossPaise); // 3 unposted nights (24, 25, 26) x 200 rupees
        Assert.Equal("400000,380000,380000,380000", await Q($"SELECT STRING_AGG(CAST(rate_paise AS varchar(12)), ',') WITHIN GROUP (ORDER BY night) FROM inv.reservation_night WHERE reservation_id = {Res("HCC-1042")};"));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task Shorten_releases_the_nights_and_is_refused_for_posted_nights_or_not_after_tonight()
    {
        async Task<string> Code(string dep, string key) => await Expect.CodeAsync(async () => await _w.Changes.ShortenStayAsync(_w.Ctx(_w.Reception, key),
            new ShortenStayRequest(_w.Stay("HCC-1042"), await VerAsync("HCC-1042"), dep, "Guest called to shorten"), Ct));
        Assert.Equal("VALIDATION", await Code("2026-10-24", "s1"));   // the guest keeps tonight
        Assert.Equal("VALIDATION", await Code("2026-10-27", "s2"));   // not before the current departure
        await _w.Seed.PostLinesAsync(await _w.FolioAsync("HCC-1042", Ct), [new GoldenLine("room", "2026-10-25", StayChangeWorld.Rate, 500)], Ct); // night of 25 Oct already posted
        Assert.Equal("INVALID_TRANSITION", await Code("2026-10-25", "s3"));
        Assert.Equal(3, await _w.Db.CountAsync($"SELECT COUNT(*) FROM inv.room_night WHERE reservation_id = {Res("HCC-1042")};", Ct));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task Shorten_by_one_night_releases_it_for_sale_and_keeps_the_audit_trail()
    {
        // 104 (Deluxe) extended first to 27 Oct so that a night 26 can be released afterwards.
        await _w.Changes.ExtendStayAsync(_w.Ctx(_w.Reception, "pre"), new ExtendStayRequest(_w.Stay("HCC-1043"), await VerAsync("HCC-1043"), "2026-10-27"), Ct);
        var r = (await _w.Changes.ShortenStayAsync(_w.Ctx(_w.Reception, "s5"), new ShortenStayRequest(_w.Stay("HCC-1043"), await VerAsync("HCC-1043"), "2026-10-26", "Back to the booked date"), Ct)).Value;
        Assert.Equal((1, "2026-10-26"), (r.NightsChanged, r.NewDeparture));
        Assert.Equal("104@2026-10-23 104@2026-10-24 104@2026-10-25", await _w.NightsOfAsync("HCC-1043", Ct));
        Assert.Equal(3, await _w.Db.CountAsync($"SELECT COUNT(*) FROM inv.reservation_night WHERE reservation_id = {Res("HCC-1043")};", Ct));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'StayShortened' AND aggregate_id = {_w.Stay("HCC-1043")};", Ct));
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'SHORTEN_STAY';", Ct));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
        await _w.Seed.ReservationAsync("104", "2026-10-26", "2026-10-27", "Walk In", ct: Ct); // the released night is free in room inventory (no ROOM_TAKEN)
    }

    [Fact]
    public async Task Shorten_after_a_move_keeps_the_history_in_both_rooms()
    {
        await _w.Changes.ExtendStayAsync(_w.Ctx(_w.Reception, "pre"), new ExtendStayRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), "2026-10-27"), Ct);
        await MoveAsync(_w.Reception, "HCC-1041", "105", "m");
        await _w.Changes.ShortenStayAsync(_w.Ctx(_w.Reception, "sh"), new ShortenStayRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), "2026-10-25", "Leaving early"), Ct);
        Assert.Equal("101@2026-10-23 105@2026-10-24", await _w.NightsOfAsync("HCC-1041", Ct));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task Early_departure_releases_the_remaining_nights_charges_no_fee_and_checkout_follows()
    {
        var folio = await _w.FolioAsync("HCC-1041", Ct);
        var req = new EarlyDepartureRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), "Family emergency");
        var r = (await _w.Changes.EarlyDepartureAsync(_w.Ctx(_w.Reception, "ed-1"), req, Ct)).Value;
        Assert.Equal(("2026-10-26", "2026-10-24", 2, 0L), (r.OldDeparture, r.NewDeparture, r.NightsChanged, r.RateLossPaise));
        Assert.Equal("101@2026-10-23", await _w.NightsOfAsync("HCC-1041", Ct));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM inv.reservation_night WHERE reservation_id = {Res("HCC-1041")};", Ct));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM bill.charge WHERE folio_id = {folio};", Ct)); // no fee, nothing else charged
        Assert.True((await _w.Changes.EarlyDepartureAsync(_w.Ctx(_w.Reception, "ed-1"), req, Ct)).Replayed);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(async () => await _w.Changes.EarlyDepartureAsync(_w.Ctx(_w.Reception, "ed-2"),
            new EarlyDepartureRequest(_w.Stay("HCC-1041"), r.StayVersion, "again"), Ct))); // nothing left to release
        Assert.Equal(0, await _w.CountDriftAsync(Ct));

        // The released nights are sellable, and CheckoutStay settles the one night that was stayed (4,000 rupees + 200 GST).
        await _w.Seed.ReservationAsync("101", "2026-10-24", "2026-10-25", "Late Arrival", ct: Ct);
        await _w.Seed.ReceiptAsync(folio, "payment", "Cash", StayChangeWorld.Rate + StayChangeWorld.Rate / 20, Ct);
        var co = await _w.W.Stays.CheckoutStayAsync(_w.Ctx(_w.Reception, "co-ed"), new CheckoutStayRequest(_w.Stay("HCC-1041"), r.StayVersion), Ct);
        Assert.Equal("Dirty", co.Value.RoomHk);
    }

    [Fact]
    public async Task Early_departure_for_a_guest_who_arrived_today_is_refused()
    {
        // 103 Ishaan Bose arrives 24 Oct: check him in directly (seed) and ask to leave the same day.
        var res = Res("HCC-1049");
        await _w.Db.ExecAsync($"UPDATE inv.reservation SET room_id = {_w.Room("103")}, room_type_id = (SELECT room_type_id FROM inv.room WHERE room_id = {_w.Room("103")}), nightly_rate_paise = 400000, status = 'InHouse' WHERE reservation_id = {res};", Ct);
        var guest = await _w.Db.ScalarAsync($"INSERT gst.guest(property_id, full_name, phone, verified, origin, created_utc) OUTPUT INSERTED.guest_id VALUES ({_w.W.PropertyId}, N'Ishaan Bose', '+91 98200 00000', 1, 'manual', '2026-10-24');", Ct);
        var stay = await _w.Db.ScalarAsync($"""
            INSERT bill.folio(reservation_id) VALUES ({res});
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by)
            OUTPUT INSERTED.stay_id VALUES ({_w.W.PropertyId}, {res}, {guest}, SCOPE_IDENTITY(), 'InHouse', '2026-10-24', '2026-10-26', '2026-10-24', '2026-10-24T08:00:00', {_w.Reception});
            """, Ct);
        var stayId = Convert.ToInt64(stay, CultureInfo.InvariantCulture);
        await _w.Db.ExecAsync($"INSERT gst.stay_segment(stay_id, seq, room_id, from_date) VALUES ({stayId}, 1, {_w.Room("103")}, '2026-10-24');", Ct);
        var ver = Convert.ToBase64String((byte[])(await _w.Db.ScalarAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {stayId};", Ct))!);
        Assert.Equal("VALIDATION", await Expect.CodeAsync(async () => await _w.Changes.EarlyDepartureAsync(_w.Ctx(_w.Reception, "ed-today"), new EarlyDepartureRequest(stayId, ver, "Changed plans"), Ct)));
        Assert.Equal(2, await _w.Db.CountAsync($"SELECT COUNT(*) FROM inv.room_night WHERE reservation_id = {res};", Ct));
    }

    [Fact]
    public async Task Housekeeping_roles_cannot_change_stays()
    {
        foreach (var who in new[] { _w.Housekeeper, _w.Supervisor })
        {
            Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await _w.Changes.EarlyDepartureAsync(_w.Ctx(who, "f" + who.ToString(CultureInfo.InvariantCulture)),
                new EarlyDepartureRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), "nope"), Ct)));
        }

        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.stay WHERE stay_id = {_w.Stay("HCC-1041")} AND departure = '2026-10-26';", Ct));
    }

    [Fact]
    public async Task Changes_are_refused_on_a_closed_business_day()
    {
        var run = await _w.Db.ScalarAsync($"""
            INSERT cls.close_run(property_id, business_date, run_ref, last_run_ref, status, current_step, attempts, started_by, started_utc, last_attempt_by, last_attempt_utc)
            OUTPUT INSERTED.run_id VALUES ({_w.W.PropertyId}, '2026-10-24', 'r', 'r', 'Closed', 'close', 1, {_w.Owner}, '2026-10-24T20:00:00', {_w.Owner}, '2026-10-24T20:00:00');
            """, Ct);
        await _w.Db.ExecAsync($"INSERT cls.business_day(property_id, business_date, run_id, closed_by, closed_utc) VALUES ({_w.W.PropertyId}, '2026-10-24', {run}, {_w.Owner}, '2026-10-24T20:00:00');", Ct);
        Assert.Equal("DAY_CLOSED", await Expect.CodeAsync(async () => await MoveAsync(_w.Owner, "HCC-1041", "105", "closed")));
        Assert.Equal("DAY_CLOSED", await Expect.CodeAsync(async () => await _w.Changes.ExtendStayAsync(_w.Ctx(_w.Owner, "closed2"),
            new ExtendStayRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), "2026-10-27"), Ct)));
        Assert.Equal("DAY_CLOSED", await Expect.CodeAsync(async () => await _w.Stayover.GenerateAsync(_w.Ctx(_w.Supervisor, "closed3"), new GenerateStayoverTasksRequest(), Ct)));
        Assert.Equal("101@2026-10-23 101@2026-10-24 101@2026-10-25", await _w.NightsOfAsync("HCC-1041", Ct));
    }

    [Fact]
    public async Task Stayover_tasks_are_one_per_occupied_room_per_business_date_and_follow_moves()
    {
        var r = (await _w.Stayover.GenerateAsync(_w.Ctx(_w.Supervisor, "so-1"), new GenerateStayoverTasksRequest(), Ct)).Value;
        Assert.Equal(("2026-10-24", 8, 0, 8), (r.BusinessDate, r.Created, r.AlreadyExisted, r.OccupiedRooms)); // the eight in-house rooms of the BASE fixture
        var again = (await _w.Stayover.GenerateAsync(_w.Ctx(_w.Supervisor, "so-2"), new GenerateStayoverTasksRequest(), Ct)).Value;
        Assert.Equal((0, 8), (again.Created, again.AlreadyExisted));
        Assert.Equal(8, await _w.Db.CountAsync("SELECT COUNT(*) FROM ops.hk_task WHERE kind = 'stayover' AND status = 'ToDo' AND assignee_id IS NULL AND business_date = '2026-10-24';", Ct));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM ops.hk_task WHERE causal_key = 'hk:stayover:{_w.Room("101")}:20261024';", Ct));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await _w.Stayover.GenerateAsync(_w.Ctx(_w.Housekeeper, "so-3"), new GenerateStayoverTasksRequest(), Ct)));

        // Next business day: a guest moved into 105 is cleaned there; one new task per occupied room.
        await MoveAsync(_w.Reception, "HCC-1041", "105", "so-move");
        await _w.Db.ExecAsync($"UPDATE inv.property SET business_date = '2026-10-25' WHERE property_id = {_w.W.PropertyId};", Ct);
        var next = (await _w.Stayover.GenerateAsync(_w.Ctx(_w.Supervisor, "so-4"), new GenerateStayoverTasksRequest(), Ct)).Value;
        Assert.Equal(("2026-10-25", 8, 8), (next.BusinessDate, next.Created, next.OccupiedRooms));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM ops.hk_task WHERE causal_key = 'hk:stayover:{_w.Room("105")}:20261025';", Ct));
        Assert.Equal(0, await _w.Db.CountAsync($"SELECT COUNT(*) FROM ops.hk_task WHERE causal_key = 'hk:stayover:{_w.Room("101")}:20261025';", Ct));
        Assert.Equal(16, await _w.Db.CountAsync("SELECT COUNT(*) FROM ops.hk_task WHERE kind = 'stayover';", Ct));
    }

    [Fact]
    public async Task Stayover_projector_opens_the_days_tasks_from_BusinessDateOpened_once_and_ignores_a_stale_event()
    {
        var p = new Hcc.Infrastructure.SqlServer.Housekeeping.StayoverTaskProjector(_w.Db.Platform.Outbox);
        var pid = _w.W.PropertyId;
        await _w.Db.ExecAsync($"INSERT plat.outbox(event_type, aggregate_id, payload, property_id) VALUES ('BusinessDateOpened', 1, N'{{\"propertyId\":{pid},\"closedDate\":\"2026-10-23\",\"businessDate\":\"2026-10-24\"}}', {pid});", Ct);
        await _w.Db.ExecAsync($"INSERT plat.outbox(event_type, aggregate_id, payload, property_id) VALUES ('BusinessDateOpened', 2, N'{{\"propertyId\":{pid},\"closedDate\":\"2026-10-22\",\"businessDate\":\"2026-10-23\"}}', {pid});", Ct);
        Assert.Equal(2, await p.RunOnceAsync(ct: Ct));
        Assert.Equal(0, await p.RunOnceAsync(ct: Ct));
        Assert.Equal(8, await _w.Db.CountAsync("SELECT COUNT(*) FROM ops.hk_task WHERE kind = 'stayover';", Ct)); // the stale 23 Oct event created nothing
        Assert.Equal(0, (await _w.Stayover.GenerateAsync(_w.Ctx(_w.Supervisor, "so-p"), new GenerateStayoverTasksRequest(), Ct)).Value.Created); // manual run afterwards: no-op
    }

    [Fact]
    public async Task Stayover_task_flows_through_the_housekeeping_machine_without_changing_room_readiness()
    {
        await _w.Stayover.GenerateAsync(_w.Ctx(_w.Supervisor, "so-m"), new GenerateStayoverTasksRequest(), Ct);
        var task = Convert.ToInt64(await _w.Db.ScalarAsync($"SELECT task_id FROM ops.hk_task WHERE causal_key = 'hk:stayover:{_w.Room("101")}:20261024';", Ct), CultureInfo.InvariantCulture);
        async Task<TransitionHousekeepingTaskResult> T(int who, string action, string key, int? assignee = null, int? index = null) =>
            (await _w.Hk.TransitionAsync(_w.Ctx(who, key), new TransitionHousekeepingTaskRequest(task, await _w.Seed.TaskVersionAsync(task, Ct), action, assignee, index), Ct)).Value;
        await T(_w.Supervisor, "assign", "a", assignee: _w.Housekeeper);
        await T(_w.Housekeeper, "start", "b");
        for (var i = 0; i < 3; i++) await T(_w.Housekeeper, "check", "c" + i.ToString(CultureInfo.InvariantCulture), index: i);
        var done = await T(_w.Housekeeper, "complete", "d");
        Assert.Equal("Done", done.Task.Status);
        Assert.Equal("Ready", await Q($"SELECT hk_status FROM inv.room WHERE room_id = {_w.Room("101")};"));
    }
}
