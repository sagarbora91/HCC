using System.Globalization;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Ota;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Errors;
using Hcc.Inventory.Tests.Harness;
using Hcc.Ota.Tests.Harness;

namespace Hcc.Ota.Tests.Sql;

/// <summary>Fresh BASE database per test (xunit builds one instance per test).</summary>
public abstract class OtaSqlTest : IAsyncLifetime
{
    protected OtaFixture F { get; } = new();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => F.InitializeAsync();

    public ValueTask DisposeAsync() => F.DisposeAsync();

    protected Task<int> CountAsync(string sql) => F.Db.CountAsync(sql, Ct);

    protected Task ExecAsync(string sql) => F.Db.ExecAsync(sql, Ct);

    protected async Task<string> CodeAsync(Func<Task> call) => await Outcome.CodeAsync(call);

    protected async Task<int> RoomIdAsync(string roomNo) => Convert.ToInt32(await F.Db.ScalarAsync($"SELECT room_id FROM inv.room WHERE room_no = '{roomNo}';", Ct), CultureInfo.InvariantCulture);

    protected async Task<ChannelView> ChannelAsync(string code) => (await F.Queries.ListChannelsAsync(1, F.Owner, Ct)).Single(c => c.ChannelId == F.ChannelIds[code]);

    protected async Task<long> LedgerSumAsync(string fixtureKey)
        => Convert.ToInt64(await F.Db.ScalarAsync($"SELECT ISNULL(SUM(commission_paise), 0) FROM ota.commission_entry WHERE message_id = {F.MessageIds[fixtureKey]};", Ct), CultureInfo.InvariantCulture);

    protected Task<int> LedgerRowsAsync(string fixtureKey) => CountAsync($"SELECT COUNT(*) FROM ota.commission_entry WHERE message_id = {F.MessageIds[fixtureKey]};");

    protected async Task<PullResult> PullAsync(int? channel = null) => (await F.Service.PullAsync(F.Ctx(F.Manager, "PullChannelMessages"), new PullChannelMessagesRequest(channel), Ct)).Value;

    protected async Task ConnectAsync(string code)
    {
        var c = await ChannelAsync(code);
        await F.Service.SetStatusAsync(F.Ctx(F.Manager, "SetChannelStatus"), new SetChannelStatusRequest(c.ChannelId, c.Version, ChannelActions.Connect), Ct);
    }

    protected async Task<AvailabilityResult> SuiteAvailabilityAsync(string a, string d)
        => await F.Inventory.GetAvailabilityAsync(1, F.Owner, new AvailabilityQuery(a, d, 1, 0, F.TypeIds["Suite"]), Ct);
}

[Trait("Category", "Sql")]
public sealed class OtaImportSqlTests : OtaSqlTest
{
    [Fact]
    public async Task Fixture_has_four_channels_six_pending_messages_and_three_scripted()
    {
        var channels = await F.Queries.ListChannelsAsync(1, F.Reception, Ct);
        Assert.Equal(["BookNest", "RoomRaahi", "StayHub", "TripKart"], channels.Select(c => c.Name).Order(StringComparer.Ordinal));
        Assert.Equal(6, (await F.Queries.ListMessagesAsync(1, F.Reception, MessageStatuses.Pending, 50, Ct)).Count);
        Assert.Equal(3, await CountAsync("SELECT COUNT(*) FROM ota.message WHERE status = 'Queued';"));
        Assert.Single(await F.Queries.ListMessagesAsync(1, F.Reception, MessageStatuses.Rejected, 50, Ct));
        Assert.Equal(21, (await F.Queries.PushPreviewAsync(1, F.Reception, F.ChannelIds["BOOKNEST"], Ct)).Rows.Count);
    }

    [Fact]
    public async Task Accept_new_creates_a_confirmed_reservation_through_the_reservation_service_at_the_hotel_tariff()
    {
        var before = await CountAsync("SELECT COUNT(*) FROM inv.reservation;");
        var r = await F.AcceptAsync("F1");
        Assert.Equal((MessageStatuses.Accepted, 108000L), (r.Status, r.CommissionPaise));
        var d = await F.ReservationAsync(r.ReservationId!.Value);
        Assert.Equal((ReservationStatuses.Confirmed, "Deluxe", "2026-10-27", "2026-10-29"), (d.Status, d.RoomType, d.Arrival, d.Departure));
        Assert.Equal((300000L, 600000L), (d.NightlyRatePaise, d.NetPaise));
        Assert.Equal("OTA · BookNest", d.Source);
        Assert.Contains("BN-88231", d.Notes, StringComparison.Ordinal);
        Assert.Equal("Kiran Joshi", d.GuestName);
        Assert.Equal(before + 1, await CountAsync("SELECT COUNT(*) FROM inv.reservation;"));
        Assert.Equal(1, await LedgerRowsAsync("F1"));
        var m = await F.MessageAsync("F1");
        Assert.Equal((MessageStatuses.Accepted, d.PublicNo, d.RoomNo), (m.Status, m.PublicNo, m.Room));
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => F.AcceptAsync("F1")));
        Assert.Equal(before + 1, await CountAsync("SELECT COUNT(*) FROM inv.reservation;"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM ota.message WHERE status = 'Importing';"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'AcceptChannelMessage';"));
    }

    [Fact]
    public async Task Accept_with_a_staff_chosen_room_refuses_the_wrong_type()
    {
        var classic = await RoomIdAsync("101");
        Assert.Equal("VALIDATION", await CodeAsync(() => F.AcceptAsync("F1", roomId: classic)));
        var r = await F.AcceptAsync("F1", roomId: await RoomIdAsync("204"));
        Assert.Equal("204", (await F.ReservationAsync(r.ReservationId!.Value)).RoomNo);
    }

    [Fact]
    public async Task Overbooking_is_prevented_and_reject_needs_a_reason()
    {
        var f3 = await F.AcceptAsync("F3");
        var f4 = await F.AcceptAsync("F4");
        Assert.Equal(["106", "306"], new[] { (await F.ReservationAsync(f3.ReservationId!.Value)).RoomNo, (await F.ReservationAsync(f4.ReservationId!.Value)).RoomNo }.Order(StringComparer.Ordinal));
        var pulled = await PullAsync();
        Assert.Equal(F.MessageIds["O1"], pulled.MessageId);
        var late = await F.MessageAsync("O1");
        Assert.Equal("BN-88275", late.ChannelRef);
        var chk = await F.Queries.CheckMessageAsync(1, F.Reception, late.MessageId, null, Ct);
        Assert.False(chk.Ok);
        Assert.Contains("No Suite room free", chk.Reason, StringComparison.Ordinal);
        var reservations = await CountAsync("SELECT COUNT(*) FROM inv.reservation;");
        Assert.Equal("NOT_AVAILABLE", await CodeAsync(() => F.AcceptAsync("O1")));
        Assert.Equal(reservations, await CountAsync("SELECT COUNT(*) FROM inv.reservation;"));
        var still = await F.MessageAsync("O1");
        Assert.Equal((MessageStatuses.Pending, late.Version), (still.Status, still.Version));
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.RejectAsync(F.Ctx(F.Reception, "RejectChannelMessage"), new RejectChannelMessageRequest(late.MessageId, late.Version, ""), Ct)));
        var rej = await F.Service.RejectAsync(F.Ctx(F.Reception, "RejectChannelMessage"), new RejectChannelMessageRequest(late.MessageId, late.Version, chk.Reason), Ct);
        Assert.Equal(MessageStatuses.Rejected, rej.Value.Status);
        Assert.Contains("No Suite", (await F.MessageAsync("O1")).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pre_checks_cover_capacity_unmapped_code_past_arrival_and_duplicates()
    {
        var f5 = await F.MessageAsync("F5");
        var chk = await F.Queries.CheckMessageAsync(1, F.Reception, f5.MessageId, null, Ct);
        Assert.Contains("exceeds Classic capacity (2)", chk.Reason, StringComparison.Ordinal);
        Assert.Equal("VALIDATION", await CodeAsync(() => F.AcceptAsync("F5")));

        var hub = F.ChannelIds["STAYHUB"];
        ChannelMessageInput Msg(string ext, string cref, string code, string arrival, string departure) =>
            new(hub, ext, MessageKinds.New, cref, "Test Guest", "+91 90000 11111", null, code, arrival, departure, 1, 0, 100000);
        var unmapped = (await F.Service.ImportMessageAsync(F.Ctx(F.Reception, "ImportChannelMessage"), new ImportChannelMessageRequest(Msg("U1", "SH-1", "SH-STE", "2026-10-25", "2026-10-26")), Ct)).Value;
        Assert.Contains("not mapped on StayHub", (await F.Queries.CheckMessageAsync(1, F.Reception, unmapped.MessageId, null, Ct)).Reason, StringComparison.Ordinal);
        var past = (await F.Service.ImportMessageAsync(F.Ctx(F.Reception, "ImportChannelMessage"), new ImportChannelMessageRequest(Msg("U2", "SH-2", "SH-STD", "2026-10-20", "2026-10-21")), Ct)).Value;
        Assert.Contains("before the business date", (await F.Queries.CheckMessageAsync(1, F.Reception, past.MessageId, null, Ct)).Reason, StringComparison.Ordinal);
        var already = await F.Queries.CheckMessageAsync(1, F.Reception, F.MessageIds["F2"], null, Ct);
        Assert.True(already.Ok);
        await F.AcceptAsync("F2");
        Assert.False((await F.Queries.CheckMessageAsync(1, F.Reception, F.MessageIds["F2"], null, Ct)).Ok);
    }

    [Fact]
    public async Task Duplicate_channel_deliveries_are_idempotent_by_message_id_and_by_booking_reference()
    {
        var bn = F.ChannelIds["BOOKNEST"];
        var input = new ChannelMessageInput(bn, "WH-1", MessageKinds.New, "BN-99001", "Neha Rao", "+91 90000 99001", null, "BN-DLX", "2026-10-28", "2026-10-29", 2, 0, 330000);
        var key = Guid.NewGuid().ToString("N");
        var first = await F.Service.ImportMessageAsync(F.Ctx(F.Reception, "ImportChannelMessage", key), new ImportChannelMessageRequest(input), Ct);
        Assert.False(first.Value.Duplicate);
        var replay = await F.Service.ImportMessageAsync(F.Ctx(F.Reception, "ImportChannelMessage", key), new ImportChannelMessageRequest(input), Ct);
        Assert.True(replay.Replayed);
        var sameExternal = await F.Service.ImportMessageAsync(F.Ctx(F.Reception, "ImportChannelMessage"), new ImportChannelMessageRequest(input), Ct);
        Assert.True(sameExternal.Value.Duplicate);
        Assert.Equal(first.Value.MessageId, sameExternal.Value.MessageId);
        var sameRef = await F.Service.ImportMessageAsync(F.Ctx(F.Reception, "ImportChannelMessage"), new ImportChannelMessageRequest(input with { ExternalId = "WH-2" }), Ct);
        Assert.True(sameRef.Value.Duplicate);
        Assert.Equal(first.Value.MessageId, sameRef.Value.MessageId);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM ota.message WHERE channel_ref = 'BN-99001';"));
        // accepted twice with the same key = one reservation
        var m = await F.MessageAsync("F1");
        var req = new AcceptChannelMessageRequest(m.MessageId, m.Version);
        var akey = Guid.NewGuid().ToString("N");
        var before = await CountAsync("SELECT COUNT(*) FROM inv.reservation;");
        var a1 = await F.Service.AcceptAsync(F.Ctx(F.Reception, "AcceptChannelMessage", akey), req, Ct);
        var a2 = await F.Service.AcceptAsync(F.Ctx(F.Reception, "AcceptChannelMessage", akey), req, Ct);
        Assert.False(a1.Replayed);
        Assert.True(a2.Replayed);
        Assert.Equal(a1.Value.ReservationId, a2.Value.ReservationId);
        Assert.Equal(before + 1, await CountAsync("SELECT COUNT(*) FROM inv.reservation;"));
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => F.Service.ImportMessageAsync(F.Ctx(F.Reception, "ImportChannelMessage"),
            new ImportChannelMessageRequest(input with { ChannelId = F.ChannelIds["TRIPKART"], ExternalId = "WH-9", ChannelRef = "TK-1", ChannelRoomCode = "TK-D1" }), Ct)));
    }

    [Fact]
    public async Task Two_users_accepting_the_same_message_at_once_create_exactly_one_reservation()
    {
        var m = await F.MessageAsync("F1");
        var req = new AcceptChannelMessageRequest(m.MessageId, m.Version);
        var before = await CountAsync("SELECT COUNT(*) FROM inv.reservation;");
        async Task<string> Try(int who) => await CodeAsync(() => F.Service.AcceptAsync(F.Ctx(who, "AcceptChannelMessage"), req, Ct));
        var results = await Task.WhenAll(Try(F.Owner), Try(F.Manager), Try(F.Reception));
        Assert.Equal(1, results.Count(r => r == "OK"));
        Assert.All(results.Where(r => r != "OK"), r => Assert.Contains(r, new[] { "INVALID_TRANSITION", "STALE_VERSION" }));
        Assert.Equal(before + 1, await CountAsync("SELECT COUNT(*) FROM inv.reservation;"));
        Assert.Equal(1, await LedgerRowsAsync("F1"));
    }

    [Fact]
    public async Task An_interrupted_import_is_claimed_by_its_user_and_resumes_without_a_second_reservation()
    {
        var room = await RoomIdAsync("103");
        await ExecAsync($"UPDATE ota.message SET status = 'Importing', decided_by = {F.Reception}, room_id = {room} WHERE message_id = {F.MessageIds["F1"]};");
        var m = await F.MessageAsync("F1");
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => F.AcceptAsync("F1", F.Manager)));
        var before = await CountAsync("SELECT COUNT(*) FROM inv.reservation;");
        var r = await F.Service.AcceptAsync(F.Ctx(F.Reception, "AcceptChannelMessage"), new AcceptChannelMessageRequest(m.MessageId, m.Version), Ct);
        Assert.Equal(MessageStatuses.Accepted, r.Value.Status);
        Assert.Equal("103", (await F.ReservationAsync(r.Value.ReservationId!.Value)).RoomNo);
        Assert.Equal(before + 1, await CountAsync("SELECT COUNT(*) FROM inv.reservation;"));
    }
}

[Trait("Category", "Sql")]
public sealed class OtaModifyCancelSqlTests : OtaSqlTest
{
    [Fact]
    public async Task Modify_of_a_pending_request_updates_the_queue_and_the_later_accept_uses_the_new_dates()
    {
        var applied = await F.AcceptAsync("F6");
        Assert.Equal(MessageStatuses.Applied, applied.Status);
        var f2 = await F.MessageAsync("F2");
        Assert.Equal(("2026-10-27", MessageStatuses.Pending), (f2.Departure, f2.Status));
        var r = await F.AcceptAsync("F2");
        var d = await F.ReservationAsync(r.ReservationId!.Value);
        Assert.Equal(("2026-10-25", "2026-10-27", 200000L, 400000L), (d.Arrival, d.Departure, d.NightlyRatePaise, d.NetPaise));
        Assert.Equal(60000, r.CommissionPaise); // 15 % of 2 x 200000
    }

    [Fact]
    public async Task Modify_of_an_accepted_booking_amends_the_reservation_keeps_the_agreed_rate_and_adjusts_the_ledger()
    {
        var r = await F.AcceptAsync("F1");
        Assert.Equal(108000, await LedgerSumAsync("F1"));
        await PullAsync();
        await PullAsync();
        var mod = await F.AcceptAsync("O2");
        Assert.Equal(MessageStatuses.Applied, mod.Status);
        var d = await F.ReservationAsync(r.ReservationId!.Value);
        Assert.Equal((ReservationStatuses.Confirmed, "2026-10-27", "2026-10-30", 300000L, 900000L), (d.Status, d.Arrival, d.Departure, d.NightlyRatePaise, d.NetPaise));
        Assert.Equal(162000, await LedgerSumAsync("F1"));
        Assert.Equal(2, await LedgerRowsAsync("F1"));
        Assert.Equal(162000, mod.CommissionPaise);
        var m = await F.MessageAsync("F1");
        Assert.Equal("2026-10-30", m.Departure);
        Assert.Equal(1062000, m.AmountPaise);
    }

    [Fact]
    public async Task Cancel_of_an_accepted_booking_cancels_the_reservation_and_reverses_the_commission()
    {
        var r = await F.AcceptAsync("F4");
        Assert.Equal(1, (await SuiteAvailabilityAsync("2026-10-25", "2026-10-26")).Types.Single().AvailableRooms);
        var o1 = await PullAsync();
        var o2 = await PullAsync();
        var none = await PullAsync();
        Assert.Equal((F.MessageIds["O1"], F.MessageIds["O2"], (long?)null), (o1.MessageId, o2.MessageId, none.MessageId)); // TripKart is in Error: its cancel stays queued
        Assert.Contains("no new messages", none.Summary, StringComparison.Ordinal);
        var trip = await ChannelAsync("TRIPKART");
        Assert.Equal("FORBIDDEN", await CodeAsync(() => F.Service.SetStatusAsync(F.Ctx(F.Reception, "SetChannelStatus"), new SetChannelStatusRequest(trip.ChannelId, trip.Version, "connect"), Ct)));
        await ConnectAsync("TRIPKART");
        var cancel = await PullAsync();
        Assert.Equal(F.MessageIds["O3"], cancel.MessageId);
        var applied = await F.AcceptAsync("O3");
        Assert.Equal((MessageStatuses.Applied, 0L), (applied.Status, applied.CommissionPaise));
        var d = await F.ReservationAsync(r.ReservationId!.Value);
        Assert.Equal(ReservationStatuses.Cancelled, d.Status);
        Assert.Contains("TK-30017", d.CancelReason, StringComparison.Ordinal);
        Assert.Equal(0, await LedgerSumAsync("F4"));
        Assert.Equal(2, await LedgerRowsAsync("F4"));
        Assert.Equal(2, (await SuiteAvailabilityAsync("2026-10-25", "2026-10-26")).Types.Single().AvailableRooms);
    }

    [Fact]
    public async Task Cancel_of_a_pending_request_withdraws_it()
    {
        await ConnectAsync("TRIPKART");
        await PullAsync();
        await PullAsync();
        var third = await PullAsync();
        Assert.Equal(F.MessageIds["O3"], third.MessageId);
        var before = await CountAsync("SELECT COUNT(*) FROM inv.reservation;");
        await F.AcceptAsync("O3");
        var f4 = await F.MessageAsync("F4");
        Assert.Equal(MessageStatuses.Rejected, f4.Status);
        Assert.Contains("before acceptance", f4.Reason, StringComparison.Ordinal);
        Assert.Equal(before, await CountAsync("SELECT COUNT(*) FROM inv.reservation;"));
    }

    [Fact]
    public async Task A_message_that_cannot_be_applied_leaves_the_message_and_the_reservation_untouched()
    {
        var r = await F.AcceptAsync("F1");
        var mod = await F.MessageAsync("F1");
        // party change on a Modify is refused (the reservation service cannot change the party): front-desk job
        await PullAsync();
        await PullAsync();
        await ExecAsync($"UPDATE ota.message SET adults = 3 WHERE message_id = {F.MessageIds["O2"]};");
        var o2 = await F.MessageAsync("O2");
        Assert.False((await F.Queries.CheckMessageAsync(1, F.Reception, o2.MessageId, null, Ct)).Ok);
        Assert.Equal("VALIDATION", await CodeAsync(() => F.AcceptAsync("O2")));
        var after = await F.MessageAsync("O2");
        Assert.Equal(MessageStatuses.Pending, after.Status);
        Assert.Equal(("2026-10-29", 1), ((await F.ReservationAsync(r.ReservationId!.Value)).Departure, await LedgerRowsAsync("F1")));
        Assert.Equal(MessageStatuses.Accepted, mod.Status);
    }
}

[Trait("Category", "Sql")]
public sealed class OtaChannelSqlTests : OtaSqlTest
{
    [Fact]
    public async Task Push_preview_reproduces_the_prototype_figures_and_follows_inventory_and_stop_sell()
    {
        var p = await F.Queries.PushPreviewAsync(1, F.Reception, F.ChannelIds["BOOKNEST"], Ct);
        Assert.Equal(21, p.Rows.Count);
        Assert.Empty(p.Unmapped);
        var suite0 = p.Rows.Single(r => r.RoomType == "Suite" && r.Night == "2026-10-25");
        Assert.Equal((2, 495000L, "BN-STE"), (suite0.Avail, suite0.RatePaise, suite0.ChannelCode)); // 450000 + 10 % markup
        await F.AcceptAsync("F3");
        Assert.Equal(1, (await F.Queries.PushPreviewAsync(1, F.Reception, F.ChannelIds["BOOKNEST"], Ct)).Rows.Single(r => r.RoomType == "Suite" && r.Night == "2026-10-25").Avail);
        var sh = await F.Queries.PushPreviewAsync(1, F.Reception, F.ChannelIds["STAYHUB"], Ct);
        Assert.Equal(["Suite"], sh.Unmapped);
        Assert.Equal(14, sh.Rows.Count);
        Assert.Equal(7, sh.Rows.Count(r => r.Closed && r.Why == "Stop-sell"));
        Assert.Equal(324000, sh.Rows.First(r => r.RoomType == "Deluxe").RatePaise); // 300000 + 8 %
        // a dated tariff (what a yield publish writes) flows into the push
        await ExecAsync($"INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise) VALUES ({F.TypeIds["Suite"]}, '2026-10-26', 500000);");
        var again = await F.Queries.PushPreviewAsync(1, F.Reception, F.ChannelIds["BOOKNEST"], Ct);
        Assert.Equal((495000L, 550000L), (again.Rows.Single(r => r.RoomType == "Suite" && r.Night == "2026-10-25").RatePaise, again.Rows.Single(r => r.RoomType == "Suite" && r.Night == "2026-10-26").RatePaise));
    }

    [Fact]
    public async Task Stop_sell_closes_the_type_push_is_logged_and_acknowledged_by_the_outbox_dispatcher()
    {
        await F.AcceptAsync("F3");
        Assert.True(await F.Projector.RunOnceAsync(ct: Ct) >= 1);
        Assert.True((await ChannelAsync("BOOKNEST")).PushDue); // the booking made the pushed availability stale
        await F.Service.SetStopSellAsync(F.Ctx(F.Reception, "SetChannelStopSell"), new SetChannelStopSellRequest(null, F.TypeIds["Suite"], true), Ct);
        var preview = await F.Queries.PushPreviewAsync(1, F.Reception, F.ChannelIds["BOOKNEST"], Ct);
        Assert.True(preview.Rows.Where(r => r.RoomType == "Suite").All(r => r.Closed));
        var key = Guid.NewGuid().ToString("N");
        var push = await F.Service.PushRatesAsync(F.Ctx(F.Reception, "PushChannelRates", key), new PushChannelRatesRequest(F.ChannelIds["BOOKNEST"]), Ct);
        Assert.Equal((21, 7, "Queued"), (push.Value.Rows, push.Value.Closed, push.Value.Status));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM ota.push_row WHERE push_id = {push.Value.PushId} AND closed = 1 AND why = 'Stop-sell' AND room_type_id = {F.TypeIds["Suite"]} AND channel_code = 'BN-STE' AND night = '2026-10-25' AND rate_paise = 495000"));
        Assert.True((await F.Service.PushRatesAsync(F.Ctx(F.Reception, "PushChannelRates", key), new PushChannelRatesRequest(F.ChannelIds["BOOKNEST"]), Ct)).Replayed);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM ota.push;"));
        var queued = (await F.Queries.PushLogAsync(1, F.Reception, F.ChannelIds["BOOKNEST"], 10, Ct)).Single();
        Assert.Equal(("Queued", null), (queued.Status, queued.AckRef));
        await F.Projector.RunOnceAsync(ct: Ct);
        var done = (await F.Queries.PushLogAsync(1, F.Reception, F.ChannelIds["BOOKNEST"], 10, Ct)).Single();
        Assert.Equal(("Delivered", "SIM-" + push.Value.PushId.ToString(CultureInfo.InvariantCulture)), (done.Status, done.AckRef));
        Assert.False((await ChannelAsync("BOOKNEST")).PushDue);
        Assert.NotNull((await ChannelAsync("BOOKNEST")).LastSync);
        Assert.Equal(0, await F.Projector.RunOnceAsync(ct: Ct)); // exactly-once per consumer
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => F.Service.PushRatesAsync(F.Ctx(F.Reception, "PushChannelRates"), new PushChannelRatesRequest(F.ChannelIds["TRIPKART"]), Ct)));
        Assert.Equal("FORBIDDEN", await CodeAsync(() => F.Service.PushRatesAsync(F.Ctx(F.Accountant, "PushChannelRates"), new PushChannelRatesRequest(F.ChannelIds["BOOKNEST"]), Ct)));
        Assert.Equal("FORBIDDEN", await CodeAsync(() => F.Service.PushRatesAsync(F.Ctx(F.Cashier, "PushChannelRates"), new PushChannelRatesRequest(F.ChannelIds["BOOKNEST"]), Ct)));
    }

    [Fact]
    public async Task Channel_setup_validates_terms_and_codes_and_is_a_manager_job()
    {
        var rr = await ChannelAsync("ROOMRAAHI");
        SaveChannelRequest Save(int com, int mk, params (string Type, string Code)[] map) =>
            new(rr.ChannelId, rr.Version, "ROOMRAAHI", "RoomRaahi", com, mk, [.. map.Select(m => new ChannelRoomMapping(F.TypeIds[m.Type], m.Code))]);
        Assert.Equal("FORBIDDEN", await CodeAsync(() => F.Service.SaveChannelAsync(F.Ctx(F.Reception, "SaveChannel"), Save(1500, 0), Ct)));
        Assert.Equal("FORBIDDEN", await CodeAsync(() => F.Service.SaveChannelAsync(F.Ctx(F.Accountant, "SaveChannel"), Save(1500, 0), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SaveChannelAsync(F.Ctx(F.Manager, "SaveChannel"), Save(3500, 0), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SaveChannelAsync(F.Ctx(F.Manager, "SaveChannel"), Save(1500, 2600), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SaveChannelAsync(F.Ctx(F.Manager, "SaveChannel"), Save(1500, 0, ("Classic", "X1"), ("Deluxe", "x1")), Ct)));
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SaveChannelAsync(F.Ctx(F.Manager, "SaveChannel"), Save(1500, 0, ("Classic", "bad code!")), Ct)));
        var ok = await F.Service.SaveChannelAsync(F.Ctx(F.Manager, "SaveChannel"), Save(1250, 0, ("Classic", "rr-std"), ("Deluxe", "")), Ct);
        var saved = await ChannelAsync("ROOMRAAHI");
        Assert.Equal((1250, 0, ok.Value.Version), (saved.CommissionBps, saved.MarkupBps, saved.Version));
        var map = Assert.Single(saved.Mapping);
        Assert.Equal(("Classic", "RR-STD"), (map.RoomType, map.ChannelCode));
        Assert.Equal("STALE_VERSION", await CodeAsync(() => F.Service.SaveChannelAsync(F.Ctx(F.Manager, "SaveChannel"), Save(1000, 0), Ct)));

        var created = await F.Service.SaveChannelAsync(F.Ctx(F.Owner, "SaveChannel"),
            new SaveChannelRequest(null, null, "newota", "NewOTA", 900, 100, [new ChannelRoomMapping(F.TypeIds["Suite"], "ns-1")]), Ct);
        Assert.Equal("Disconnected", created.Value.Status);
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SaveChannelAsync(F.Ctx(F.Owner, "SaveChannel"), new SaveChannelRequest(null, null, "NEWOTA", "Again", 900, 100, []), Ct)));
        var connect = await F.Service.SetStatusAsync(F.Ctx(F.Owner, "SetChannelStatus"), new SetChannelStatusRequest(created.Value.ChannelId, created.Value.Version, "connect"), Ct);
        Assert.Equal("Connected", connect.Value.Status);
        Assert.Equal("INVALID_TRANSITION", await CodeAsync(() => F.Service.SetStatusAsync(F.Ctx(F.Owner, "SetChannelStatus"), new SetChannelStatusRequest(created.Value.ChannelId, connect.Value.Version, "connect"), Ct)));
        var paused = await F.Service.SetStatusAsync(F.Ctx(F.Owner, "SetChannelStatus"), new SetChannelStatusRequest(created.Value.ChannelId, connect.Value.Version, "pause"), Ct);
        Assert.Equal("Paused", paused.Value.Status);
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SetStatusAsync(F.Ctx(F.Owner, "SetChannelStatus"), new SetChannelStatusRequest(created.Value.ChannelId, paused.Value.Version, "explode"), Ct)));
    }

    [Fact]
    public async Task Stop_sell_needs_a_mapped_room_type_and_operate_permission()
    {
        Assert.Equal("VALIDATION", await CodeAsync(() => F.Service.SetStopSellAsync(F.Ctx(F.Reception, "SetChannelStopSell"), new SetChannelStopSellRequest(F.ChannelIds["STAYHUB"], F.TypeIds["Suite"], true), Ct)));
        Assert.Equal("FORBIDDEN", await CodeAsync(() => F.Service.SetStopSellAsync(F.Ctx(F.Accountant, "SetChannelStopSell"), new SetChannelStopSellRequest(F.ChannelIds["BOOKNEST"], F.TypeIds["Suite"], true), Ct)));
        var r = await F.Service.SetStopSellAsync(F.Ctx(F.Reception, "SetChannelStopSell"), new SetChannelStopSellRequest(F.ChannelIds["BOOKNEST"], F.TypeIds["Classic"], true), Ct);
        Assert.Equal((await ChannelAsync("BOOKNEST")).Version, r.Value.Version);
        Assert.True((await ChannelAsync("BOOKNEST")).Mapping.Single(m => m.RoomType == "Classic").StopSell);
    }
}

[Trait("Category", "Sql")]
public sealed class OtaCommissionSqlTests : OtaSqlTest
{
    [Fact]
    public async Task Commission_report_reproduces_the_prototype_figures_and_drops_cancelled_bookings()
    {
        await F.AcceptAsync("F1");
        var f2 = await F.AcceptAsync("F2");
        var rep = await F.Queries.CommissionReportAsync(1, F.Accountant, "2026-10-01", "2026-12-31", Ct);
        var bn = rep.ByChannel.Single(c => c.Channel == "BookNest");
        var sh = rep.ByChannel.Single(c => c.Channel == "StayHub");
        Assert.Equal((1, 2, 600000L, 108000L, 492000L), (bn.Bookings, bn.Nights, bn.BasePaise, bn.CommissionPaise, bn.NetPaise));
        Assert.Equal((200000L, 30000L), (sh.BasePaise, sh.CommissionPaise));
        Assert.Equal(138000, rep.Total.CommissionPaise);
        Assert.Equal(2, rep.Rows.Count);
        await F.Booking.CancelAsync(F.Ctx(F.Owner, "CancelReservation"), new CancelReservationRequest(f2.ReservationId!.Value,
            (await F.ReservationAsync(f2.ReservationId.Value)).Version, "Test"), Ct);
        Assert.Equal(108000, (await F.Queries.CommissionReportAsync(1, F.Manager, "2026-10-01", "2026-12-31", Ct)).Total.CommissionPaise);
        Assert.Equal("FORBIDDEN", await CodeAsync(() => F.Queries.CommissionReportAsync(1, F.Reception, null, null, Ct)));
        Assert.Equal("FORBIDDEN", await CodeAsync(() => F.Queries.ReconciliationAsync(1, F.Cashier, null, null, null, Ct)));
    }

    [Fact]
    public async Task Reconciliation_flags_a_hotel_side_cancellation_the_channel_never_sent()
    {
        await F.AcceptAsync("F1");
        var f2 = await F.AcceptAsync("F2");
        var clean = await F.Queries.ReconciliationAsync(1, F.Accountant, "2026-10-01", "2026-12-31", null, Ct);
        Assert.Equal((2, 2, 138000L, 138000L, 0L), (clean.Bookings, clean.Matched, clean.LedgerCommissionPaise, clean.ExpectedCommissionPaise, clean.CommissionVariancePaise));
        var row1 = clean.Rows.Single(r => r.ChannelRef == "BN-88231");
        Assert.Equal((708000L, 600000L, 108000L), (row1.ChannelAmountPaise, row1.HotelBasePaise, row1.CommissionVariancePaise + row1.LedgerCommissionPaise));
        await F.Booking.CancelAsync(F.Ctx(F.Owner, "CancelReservation"), new CancelReservationRequest(f2.ReservationId!.Value,
            (await F.ReservationAsync(f2.ReservationId.Value)).Version, "Guest called the hotel"), Ct);
        var rec = await F.Queries.ReconciliationAsync(1, F.Accountant, "2026-10-01", "2026-12-31", null, Ct);
        Assert.Equal((2, 1, 138000L, 108000L, 30000L), (rec.Bookings, rec.Matched, rec.LedgerCommissionPaise, rec.ExpectedCommissionPaise, rec.CommissionVariancePaise));
        var bad = rec.Rows.Single(r => r.ChannelRef == "SH-55102");
        Assert.Equal(("Cancelled", 30000L, 0L), (bad.ReservationStatus, bad.LedgerCommissionPaise, bad.ExpectedCommissionPaise));
        Assert.Contains(bad.Flags, x => x.Contains("never cancelled", StringComparison.Ordinal));
        var only = await F.Queries.ReconciliationAsync(1, F.Accountant, "2026-10-01", "2026-12-31", F.ChannelIds["BOOKNEST"], Ct);
        Assert.Equal(1, only.Bookings);
    }

    [Fact]
    public async Task The_commission_ledger_is_append_only_and_keeps_the_rate_of_the_day_it_accrued()
    {
        await F.AcceptAsync("F1");
        await Assert.ThrowsAnyAsync<Exception>(() => F.Db.ExecAsync("UPDATE ota.commission_entry SET commission_paise = 1;", Ct));
        await Assert.ThrowsAnyAsync<Exception>(() => F.Db.ExecAsync("DELETE ota.commission_entry;", Ct));
        // the channel's rate changes later: the booking keeps 18 %
        var c = await ChannelAsync("BOOKNEST");
        await F.Service.SaveChannelAsync(F.Ctx(F.Manager, "SaveChannel"), new SaveChannelRequest(c.ChannelId, c.Version, "BOOKNEST", "BookNest", 2500, 1000,
            [.. c.Mapping.Select(m => new ChannelRoomMapping(m.RoomTypeId, m.ChannelCode))]), Ct);
        var rep = await F.Queries.CommissionReportAsync(1, F.Owner, "2026-10-01", "2026-12-31", Ct);
        Assert.Equal((1800, 108000L), (rep.Rows.Single().CommissionBps, rep.Rows.Single().CommissionPaise));
        Assert.Equal(108000, await LedgerSumAsync("F1"));
    }
}
