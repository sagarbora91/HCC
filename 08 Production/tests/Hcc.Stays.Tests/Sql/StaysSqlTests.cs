using Hcc.Application.Housekeeping;
using Hcc.Contracts;
using Hcc.Contracts.Housekeeping;
using Hcc.Contracts.Stays;
using Hcc.Infrastructure.SqlServer.Housekeeping;
using Hcc.Stays.Tests.Harness;

namespace Hcc.Stays.Tests.Sql;

/// <summary>RegisterGuest / CheckIn / CheckoutStay through SQL, against the prototype's golden journey and cases.</summary>
[Trait("Category", "Sql")]
public class StaysSqlTests : IAsyncLifetime
{
    private static readonly StaysGolden G = StaysGolden.Load();
    private P3World _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await P3World.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static readonly GuestDraftDto Full = new("Sonal Kulkarni", "+91 98200 05555", "sonal@example.test", "5 Demo Lane, Latur (fictional)", "Indian",
        "Passport (demo)", "K555", Party: [new GuestPartyDto("Om Kulkarni", "Child", "Son")]);

    private async Task<string> RoomHkAsync(string roomNo) => (await _w.Db.StringAsync($"SELECT hk_status FROM inv.room WHERE room_id = {_w.Seed.Rooms[roomNo]};", Ct))!;

    private async Task<(string RoomHk, string Status, string? Booking, string? Task, int Tasks)> SnapAsync(long resId, long? stayId)
    {
        var board = await _w.Board.BoardAsync(_w.PropertyId, Users.Supervisor, 1, 100, Ct);
        var row = board.Rows.Single(r => r.RoomNo == "105");
        var res = await _w.Db.StringAsync($"SELECT status FROM inv.reservation WHERE reservation_id = {resId};", Ct);
        var task = stayId is null ? null : await _w.Db.StringAsync($"SELECT status FROM ops.hk_task WHERE stay_id = {stayId};", Ct);
        var tasks = stayId is null ? 0 : await _w.Db.CountAsync($"SELECT COUNT(*) FROM ops.hk_task WHERE stay_id = {stayId};", Ct);
        return (row.Hk, row.Status, res, task, tasks);
    }

    private static (string, string, string?, string?, int) Golden(string step)
    {
        var s = G.Journey.Steps.Single(x => x.Step == step);
        static string? Res(string? p) => p switch { "In house" => "InHouse", _ => p };
        static string? Task(string? p) => p switch { "To do" => "ToDo", "In progress" => "InProgress", _ => p };
        return (s.RoomHk, s.RoomStatus, Res(s.BookingStatus), Task(s.TaskStatus), s.TasksForBooking);
    }

    [Fact]
    public async Task Golden_journey_register_checkin_checkout_clean_inspect_matches_the_prototype_step_by_step()
    {
        var j = G.Journey;
        var res = await _w.Seed.ReservationAsync(j.Room, j.Arrival, j.Departure, "Journey Test", ct: Ct);
        Assert.Equal(Golden("book"), await SnapAsync(res, null));

        var reg = await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "reg-1"), new RegisterGuestRequest(res, await _w.Seed.ReservationVersionAsync(res, Ct), Full), Ct);
        Assert.True(reg.Value.Verified);
        Assert.Equal(Golden("register"), await SnapAsync(res, null));

        var ci = await _w.Stays.CheckInAsync(_w.Ctx(Users.Reception, "ci-1"), new CheckInRequest(res, await _w.Seed.ReservationVersionAsync(res, Ct)), Ct);
        Assert.Equal(("105", "2026-10-24"), (ci.Value.RoomNo, ci.Value.BusinessDate));
        Assert.Equal(Golden("checkin"), await SnapAsync(res, ci.Value.StayId));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM bill.folio WHERE reservation_id = {res};", Ct));

        // Billing (P4 stand-in): the prototype's posted nights; unpaid checkout is refused exactly as in the prototype.
        await _w.Seed.PostLinesAsync(ci.Value.FolioId, j.Folio.Lines, Ct);
        var stayVer = await _w.Seed.StayVersionAsync(ci.Value.StayId, Ct);
        Assert.Equal(j.Expected.UnpaidCheckout, await Expect.CodeAsync(() => _w.Stays.CheckoutStayAsync(_w.Ctx(Users.Reception, "co-unpaid"), new CheckoutStayRequest(ci.Value.StayId, stayVer), Ct)));
        foreach (var r in j.Folio.Receipts) await _w.Seed.ReceiptAsync(ci.Value.FolioId, r.Kind, r.Method, r.AmountPaise, Ct);

        var key = "checkout:" + ci.Value.StayId;
        var req = new CheckoutStayRequest(ci.Value.StayId, stayVer);
        var co = await _w.Stays.CheckoutStayAsync(_w.Ctx(Users.Reception, key), req, Ct);
        Assert.Equal("Dirty", co.Value.RoomHk);
        var again = await _w.Stays.CheckoutStayAsync(_w.Ctx(Users.Reception, key), req, Ct);
        Assert.Equal(j.Expected.ReplaySame, again.Replayed && again.Value == co.Value);
        Assert.Equal(j.Expected.SecondCheckoutWithoutKey, await Expect.CodeAsync(() => _w.Stays.CheckoutStayAsync(_w.Ctx(Users.Reception, "co-other"), req, Ct)));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'StayDeparted' AND aggregate_id = {ci.Value.StayId};", Ct));

        Assert.Equal(1, await _w.Projector.RunOnceAsync(ct: Ct));
        Assert.Equal(0, await _w.Projector.RunOnceAsync(ct: Ct));
        Assert.Equal(Golden("checkout"), await SnapAsync(res, ci.Value.StayId));
        var taskId = Convert.ToInt64(await _w.Db.ScalarAsync($"SELECT task_id FROM ops.hk_task WHERE stay_id = {ci.Value.StayId};", Ct), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"hk:checkout:{ci.Value.StayId}", await _w.Db.StringAsync($"SELECT causal_key FROM ops.hk_task WHERE task_id = {taskId};", Ct));

        // Production creates the task unassigned; the supervisor assigns the prototype's assignee.
        var hk = Users.Of(j.TaskAssignee);
        async Task<TransitionHousekeepingTaskResult> T(int who, string action, string k, int? assignee = null, int? index = null, string? reason = null) =>
            (await _w.Housekeeping.TransitionAsync(_w.Ctx(who, k), new TransitionHousekeepingTaskRequest(taskId, await _w.Seed.TaskVersionAsync(taskId, Ct), action, assignee, index, reason), Ct)).Value;
        await T(Users.Supervisor, "assign", "t-assign", assignee: hk);
        Assert.Equal(j.Expected.HkStartOther, await Expect.CodeAsync(() => T(Users.Housekeeper2, "start", "t-other")));
        await T(hk, "start", "t-start");
        Assert.Equal(Golden("start"), await SnapAsync(res, ci.Value.StayId));
        Assert.Equal(j.Expected.CompleteEarly, await Expect.CodeAsync(() => T(hk, "complete", "t-early")));
        for (var i = 0; i < 3; i++) await T(hk, "check", "t-check-" + i, index: i);
        await T(hk, "complete", "t-complete");
        Assert.Equal(Golden("complete"), await SnapAsync(res, ci.Value.StayId));
        Assert.Equal(j.Expected.HkInspect, await Expect.CodeAsync(() => T(hk, "inspect", "t-hk-inspect")));
        Assert.Equal(j.Expected.RejectNoReason, await Expect.CodeAsync(() => T(Users.Supervisor, "reject", "t-reject-empty")));
        var rej = await T(Users.Supervisor, "reject", "t-reject", reason: "Mirror streaks in bathroom");
        Assert.Equal([false, false, false], rej.Task.Checklist);
        Assert.Equal(Golden("reject"), await SnapAsync(res, ci.Value.StayId));
        for (var i = 0; i < 3; i++) await T(hk, "check", "t-check2-" + i, index: i);
        await T(hk, "complete", "t-complete2");
        Assert.Equal(Golden("complete-again"), await SnapAsync(res, ci.Value.StayId));
        var done = await T(Users.Supervisor, "inspect", "t-inspect");
        Assert.Equal("Ready", done.RoomHk);
        Assert.Equal(Golden("inspect"), await SnapAsync(res, ci.Value.StayId));
        Assert.NotNull(done.Task.StartedUtc);
        Assert.NotNull(done.Task.CompletedUtc);
    }

    [Fact]
    public async Task RegisterGuest_validates_saves_and_never_writes_ID_details_or_contact_data_to_the_audit()
    {
        var res = await _w.Seed.ReservationAsync("106", "2026-10-24", "2026-10-25", "Walk In Guest", ct: Ct);
        var ver = await _w.Seed.ReservationVersionAsync(res, Ct);
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "r0"), new RegisterGuestRequest(res, ver, Full with { IdLast4 = "" }), Ct));
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "r1"), new RegisterGuestRequest(res, ver), Ct)); // no auto demo profile
        await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Housekeeper, "r2"), new RegisterGuestRequest(res, ver, Full), Ct));
        await Expect.ErrorAsync(HccErrorCode.STALE_VERSION, () => _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "r3"), new RegisterGuestRequest(res, "AAAAAAAAAAA=", Full), Ct));
        Assert.Equal(0, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.registration WHERE reservation_id = {res};", Ct));

        var ok = await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "r4"), new RegisterGuestRequest(res, ver, Full), Ct);
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.guest_party WHERE guest_id = {ok.Value.GuestId};", Ct));
        // Updating the linked profile needs its version; a stale one is rejected.
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "r5"), new RegisterGuestRequest(res, ver, Full with { Address = "6 Demo Lane" }), Ct));
        var upd = await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "r6"), new RegisterGuestRequest(res, ver, Full with { Address = "6 Demo Lane" }, null, ok.Value.GuestVersion), Ct);
        Assert.Equal(ok.Value.GuestId, upd.Value.GuestId);
        await Expect.ErrorAsync(HccErrorCode.STALE_VERSION, () => _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "r7"), new RegisterGuestRequest(res, ver, Full, null, ok.Value.GuestVersion), Ct));

        var leaks = await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail LIKE '%K555%' OR detail LIKE '%Passport%' OR detail LIKE '%98200%' OR detail LIKE '%Sonal%' OR detail LIKE '%Demo Lane%';", Ct);
        Assert.Equal(0, leaks);
        Assert.Equal(2, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'REGISTER';", Ct));
    }

    [Fact]
    public async Task Guest_read_masks_ID_and_contact_by_permission_like_the_prototype()
    {
        var res = await _w.Seed.ReservationAsync("106", "2026-10-24", "2026-10-25", "Walk In Guest", ct: Ct);
        var reg = await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "r"), new RegisterGuestRequest(res, await _w.Seed.ReservationVersionAsync(res, Ct), Full), Ct);
        var rec = await _w.StayQueries.GetGuestAsync(_w.PropertyId, Users.Reception, reg.Value.GuestId, Ct);
        Assert.Equal(("K555", "Passport (demo)", Full.Phone, false), (rec.IdLast4, rec.IdType, rec.Phone, rec.Masked));
        var csh = await _w.StayQueries.GetGuestAsync(_w.PropertyId, Users.Cashier, reg.Value.GuestId, Ct);
        Assert.Equal(("Restricted", "Restricted", "••••••5555", true), (csh.IdLast4, csh.IdType, csh.Phone, csh.Masked));
        Assert.NotEqual(Full.Email, csh.Email);
        await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => _w.StayQueries.GetGuestAsync(_w.PropertyId, Users.Housekeeper, reg.Value.GuestId, Ct));
        await Expect.ErrorAsync(HccErrorCode.NOT_FOUND, () => _w.StayQueries.GetGuestAsync(_w.PropertyId, Users.Reception, 999_999, Ct));
    }

    [Fact]
    public async Task CheckIn_refuses_dirty_blocked_unregistered_out_of_dates_and_stale_requests_and_changes_nothing()
    {
        // Prototype S2: arrival in dirty 108 → NOT_AVAILABLE.
        var dirty = await _w.Seed.ReservationAsync(G.Cases.DirtyRoomCheckin.Room, "2026-10-24", "2026-10-25", "Test Guest", ct: Ct);
        await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "rd"), new RegisterGuestRequest(dirty, await _w.Seed.ReservationVersionAsync(dirty, Ct), Full), Ct);
        Assert.Equal(G.Cases.DirtyRoomCheckin.Code, await Expect.CodeAsync(async () => await _w.Stays.CheckInAsync(_w.Ctx(Users.Reception, "cd"), new CheckInRequest(dirty, await _w.Seed.ReservationVersionAsync(dirty, Ct)), Ct)));

        // Blocked for a later night of the stay → NOT_AVAILABLE (whole interval checked, prototype available()).
        var blocked = await _w.Seed.ReservationAsync("106", "2026-10-24", "2026-10-27", "Block Guest", ct: Ct);
        await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "rb"), new RegisterGuestRequest(blocked, await _w.Seed.ReservationVersionAsync(blocked, Ct), Full), Ct);
        await _w.Db.ExecAsync($"INSERT ops.room_block(property_id, room_id, from_date, to_date, reason, source, created_by, created_utc) VALUES ({_w.PropertyId}, {_w.Seed.Rooms["106"]}, '2026-10-26', '2026-10-28', N'Paint', 'manual', 5, '2026-10-24');", Ct);
        Assert.Equal("NOT_AVAILABLE", await Expect.CodeAsync(async () => await _w.Stays.CheckInAsync(_w.Ctx(Users.Reception, "cb"), new CheckInRequest(blocked, await _w.Seed.ReservationVersionAsync(blocked, Ct)), Ct)));

        var unreg = await _w.Seed.ReservationAsync("107", "2026-10-24", "2026-10-25", "Unregistered", ct: Ct);
        Assert.Equal("VALIDATION", await Expect.CodeAsync(async () => await _w.Stays.CheckInAsync(_w.Ctx(Users.Reception, "cu"), new CheckInRequest(unreg, await _w.Seed.ReservationVersionAsync(unreg, Ct)), Ct)));
        Assert.Equal("STALE_VERSION", await Expect.CodeAsync(() => _w.Stays.CheckInAsync(_w.Ctx(Users.Reception, "cs"), new CheckInRequest(unreg, "AAAAAAAAAAA="), Ct)));
        var future = await _w.Seed.ReservationAsync("107", "2026-10-26", "2026-10-27", "Future", ct: Ct);
        await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "rf"), new RegisterGuestRequest(future, await _w.Seed.ReservationVersionAsync(future, Ct), Full), Ct);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(async () => await _w.Stays.CheckInAsync(_w.Ctx(Users.Reception, "cf"), new CheckInRequest(future, await _w.Seed.ReservationVersionAsync(future, Ct)), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await _w.Stays.CheckInAsync(_w.Ctx(Users.Cashier, "cc"), new CheckInRequest(future, await _w.Seed.ReservationVersionAsync(future, Ct)), Ct)));
        // An in-house reservation cannot check in again.
        var inHouse = _w.Seed.Reservations["HCC-1041"];
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(async () => await _w.Stays.CheckInAsync(_w.Ctx(Users.Reception, "ci"), new CheckInRequest(inHouse, await _w.Seed.ReservationVersionAsync(inHouse, Ct)), Ct)));

        Assert.Equal(8, await _w.Db.CountAsync("SELECT COUNT(*) FROM gst.stay;", Ct));
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'CHECKIN';", Ct));
        Assert.Equal("Confirmed", await _w.Db.StringAsync($"SELECT status FROM inv.reservation WHERE reservation_id = {dirty};", Ct));
    }

    [Fact]
    public async Task Seeded_arrival_checks_in_and_occupies_the_room_on_the_board()
    {
        var res = _w.Seed.Reservations[G.Base.Expected.Arrivals[0]]; // HCC-1049 → room 103, not yet registered (as in the prototype)
        Assert.Equal("VALIDATION", await Expect.CodeAsync(async () => await _w.Stays.CheckInAsync(_w.Ctx(Users.Reception, "a0"), new CheckInRequest(res, await _w.Seed.ReservationVersionAsync(res, Ct)), Ct)));
        await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "r1049"), new RegisterGuestRequest(res, await _w.Seed.ReservationVersionAsync(res, Ct), Full with { Name = "Ishaan Bose" }), Ct);
        var ci = await _w.Stays.CheckInAsync(_w.Ctx(Users.Reception, "a1"), new CheckInRequest(res, await _w.Seed.ReservationVersionAsync(res, Ct)), Ct);
        var row = (await _w.Board.BoardAsync(_w.PropertyId, Users.Manager, 1, 100, Ct)).Rows.Single(r => r.RoomId == ci.Value.RoomId);
        Assert.Equal("Occupied", row.Status);
        Assert.Equal("Ishaan Bose", row.Current!.Name);
        Assert.Null(row.Next);
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.stay_segment WHERE stay_id = {ci.Value.StayId} AND to_date IS NULL AND from_date = '2026-10-24';", Ct));
    }

    [Fact]
    public async Task Golden_unpaid_departure_and_deposit_held_block_checkout()
    {
        // Prototype S3: HCC-1042 owes ₹200 → INVALID_TRANSITION. Same shape here: one posted night short of payment.
        var resId = _w.Seed.Reservations[G.Cases.UnpaidDeparture.Booking];
        var stay = _w.Seed.Stays[G.Cases.UnpaidDeparture.Booking];
        var folio = await _w.Seed.FolioOfAsync(resId, Ct);
        await _w.Seed.PostLinesAsync(folio, [new GoldenLine("room", "2026-10-23", 200000, 1000), new GoldenLine("room", "2026-10-24", 200000, 1000), new GoldenLine("room", "2026-10-25", 200000, 1000)], Ct);
        await _w.Seed.ReceiptAsync(folio, "payment", "UPI", 660000 - G.Cases.UnpaidDeparture.BalancePaise, Ct);
        var req = new CheckoutStayRequest(stay, await _w.Seed.StayVersionAsync(stay, Ct));
        Assert.Equal(G.Cases.UnpaidDeparture.Code, await Expect.CodeAsync(() => _w.Stays.CheckoutStayAsync(_w.Ctx(Users.Reception, "u1"), req, Ct)));
        await _w.Seed.ReceiptAsync(folio, "payment", "Cash", G.Cases.UnpaidDeparture.BalancePaise, Ct);
        await _w.Seed.ReceiptAsync(folio, "deposit", "Cash", 50000, Ct);
        var e = await Expect.ErrorAsync(HccErrorCode.INVALID_TRANSITION, () => _w.Stays.CheckoutStayAsync(_w.Ctx(Users.Reception, "u2"), req, Ct));
        Assert.Contains("deposit", e.Message, StringComparison.Ordinal);
        await _w.Seed.ReceiptAsync(folio, "deposit-return", "Cash", 50000, Ct);
        // An unknown company-credit approval is refused by billing (the real IStayFolioPort since the Wave 1 integration).
        await Expect.ErrorAsync(HccErrorCode.NOT_FOUND, () => _w.Stays.CheckoutStayAsync(_w.Ctx(Users.Reception, "u3"), req with { CompanyCreditApprovalId = 1 }, Ct));
        var ok = await _w.Stays.CheckoutStayAsync(_w.Ctx(Users.Reception, "u4"), req, Ct);
        Assert.Equal("Dirty", ok.Value.RoomHk);
        Assert.Equal(0, await _w.Db.CountAsync($"SELECT COUNT(*) FROM gst.stay_segment WHERE stay_id = {stay} AND to_date IS NULL;", Ct));
        Assert.Equal("Departed", await _w.Db.StringAsync($"SELECT status FROM inv.reservation WHERE reservation_id = {resId};", Ct));
    }

    [Fact]
    public async Task A_replayed_key_is_re_authorized_so_a_disabled_user_is_denied()
    {
        var res = await _w.Seed.ReservationAsync("106", "2026-10-24", "2026-10-25", "Walk In Guest", ct: Ct);
        var req = new RegisterGuestRequest(res, await _w.Seed.ReservationVersionAsync(res, Ct), Full);
        await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "k"), req, Ct);
        Assert.True((await _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "k"), req, Ct)).Replayed);
        _w.Policy.Disable(Users.Reception);
        await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => _w.Stays.RegisterGuestAsync(_w.Ctx(Users.Reception, "k"), req, Ct));
        Assert.Contains(_w.Policy.Calls, c => c.Perm == "PERM-GST-002");
    }

    [Fact]
    public async Task Projector_is_the_frozen_outbox_consumer_with_its_own_consumer_name()
    {
        Assert.Equal("housekeeping-tasks", HousekeepingTaskProjector.ConsumerName);
        Assert.Equal(0, await _w.Projector.RunOnceAsync(ct: Ct));
        await Task.CompletedTask;
    }
}
