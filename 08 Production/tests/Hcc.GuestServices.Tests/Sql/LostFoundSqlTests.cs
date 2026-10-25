using Hcc.Contracts.GuestServices;
using Hcc.GuestServices.Tests.Harness;

namespace Hcc.GuestServices.Tests.Sql;

[Trait("Category", "Sql")]
public class LostFoundSqlTests : IAsyncLifetime
{
    private GsWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await GsWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private async Task<LostItemResult> LogAsync(int who, string key, string description, string category, string storage, string? room = null, string where = "Lobby sofa", int? foundBy = null) =>
        (await _w.Commands.LogItemAsync(_w.Ctx(who, key), new LogLostItemRequest(description, category, where, storage, room, foundBy), Ct)).Value;

    private Task AgeAsync(long itemId, string foundDate) => _w.Db.ExecAsync($"UPDATE ops.lost_item SET found_date = '{foundDate}' WHERE item_id = {itemId};", Ct);

    [Fact]
    public async Task Valuables_go_to_the_safe_a_housekeeper_may_log_and_the_last_stay_in_the_room_is_linked()
    {
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => LogAsync(Who.Housekeeper, "a1", "Watch", "Jewellery", "Housekeeping store", "101")));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => LogAsync(Who.Accountant, "a2", "Pen", "Other", "Housekeeping store")));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => LogAsync(Who.Cashier, "a3", "Pen", "Other", "Housekeeping store")));
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => LogAsync(Who.Housekeeper, "a4", "Pen", "Other", "Housekeeping store", "999")));
        var i = await LogAsync(Who.Housekeeper, "a5", "Steel wrist watch", "Jewellery", "Front office safe", "101", "Under the bed", foundBy: Who.Manager);
        Assert.Equal(("Stored", true, new DateOnly(2027, 4, 22)), (i.Status, i.Valuable, i.DisposeOn));   // 2026-10-24 + 180 days
        Assert.Equal(Who.Housekeeper, await _w.Db.LongAsync($"SELECT found_by FROM ops.lost_item WHERE item_id = {i.ItemId};", Ct));   // only front office may name the finder
        Assert.Equal(_w.Stays["HCC-1041"], await _w.Db.LongAsync($"SELECT stay_id FROM ops.lost_item WHERE item_id = {i.ItemId};", Ct));
        var front = await LogAsync(Who.Reception, "a6", "Reading glasses", "Personal care", "Back office cupboard", foundBy: Who.Housekeeper2);
        Assert.Equal(Who.Housekeeper2, await _w.Db.LongAsync($"SELECT found_by FROM ops.lost_item WHERE item_id = {front.ItemId};", Ct));
        Assert.Equal(new DateOnly(2027, 1, 22), front.DisposeOn);   // 90 days
    }

    [Fact]
    public async Task Claim_needs_front_office_a_phone_and_an_id_note_without_the_full_number_then_handover()
    {
        var i = await LogAsync(Who.Housekeeper, "c1", "Steel wrist watch", "Jewellery", "Front office safe", "101");
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.ClaimItemAsync(_w.Ctx(Who.Housekeeper, "c2"), new ClaimLostItemRequest(i.ItemId, "Ananya Rao", "9800000111", "Passport (fictional)", "ends 1234", "Steel strap", i.Version), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.ClaimItemAsync(_w.Ctx(Who.Reception, "c3"), new ClaimLostItemRequest(i.ItemId, "Ananya Rao", "9800000111", "Passport (fictional)", "123456789012", "Steel strap", i.Version), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.ClaimItemAsync(_w.Ctx(Who.Reception, "c4"), new ClaimLostItemRequest(i.ItemId, "Ananya Rao", "12345", "Passport (fictional)", "ends 1234", "Steel strap", i.Version), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.ClaimItemAsync(_w.Ctx(Who.Reception, "c5"), new ClaimLostItemRequest(i.ItemId, "Ananya Rao", "9800000111", "Library card", "ends 1234", "Steel strap", i.Version), Ct)));
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Commands.HandOverItemAsync(_w.Ctx(Who.Reception, "c6"), new HandOverLostItemRequest(i.ItemId, null, null, i.Version), Ct)));

        var claimed = (await _w.Commands.ClaimItemAsync(_w.Ctx(Who.Reception, "c7"), new ClaimLostItemRequest(i.ItemId, "Ananya Rao", "98000 00111", "Passport (fictional)", "Photo matched · ends 1234", "Steel strap, scratched glass", i.Version), Ct)).Value;
        Assert.Equal("Claimed", claimed.Status);
        Assert.Equal("9800000111", await _w.Db.StringAsync($"SELECT claim_phone FROM ops.lost_item WHERE item_id = {i.ItemId};", Ct));
        var done = (await _w.Commands.HandOverItemAsync(_w.Ctx(Who.Reception, "c8"), new HandOverLostItemRequest(i.ItemId, null, "Collected in person", claimed.Version), Ct)).Value;
        Assert.Equal("HandedOver", done.Status);
        Assert.Equal("Ananya Rao", await _w.Db.StringAsync($"SELECT handover_to FROM ops.lost_item WHERE item_id = {i.ItemId};", Ct));

        // Phone and ID note stay out of audit, history and the outbox (DEC-068).
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail LIKE '%9800000111%' OR detail LIKE '%1234%';", Ct));
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM ops.guest_service_log WHERE what LIKE '%9800000111%' OR what LIKE '%1234%';", Ct));
        Assert.Equal(0, await _w.Db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE payload LIKE '%9800000111%';", Ct));
        // The database also refuses a full ID number in the note.
        await Assert.ThrowsAnyAsync<Exception>(() => _w.Db.ExecAsync($"UPDATE ops.lost_item SET claim_id_note = N'123456789012' WHERE item_id = {i.ItemId};", Ct));
    }

    [Fact]
    public async Task Disposal_only_after_retention_valuables_need_approve_and_are_never_destroyed()
    {
        var scarf = await LogAsync(Who.Reception, "d1", "Blue cotton scarf", "Clothing", "Housekeeping store");
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Commands.DisposeItemAsync(_w.Ctx(Who.Owner, "d2"), new DisposeLostItemRequest(scarf.ItemId, "Donated", null, scarf.Version), Ct)));
        await AgeAsync(scarf.ItemId, "2026-06-30");
        var v = (await _w.Queries.ItemsAsync(_w.PropertyId, Who.Reception, ct: Ct)).Items.Single(x => x.ItemId == scarf.ItemId);
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.DisposeItemAsync(_w.Ctx(Who.Reception, "d3"), new DisposeLostItemRequest(scarf.ItemId, "Sold", null, v.Version), Ct)));
        Assert.True(v.Disposable);
        Assert.Equal("Disposed", (await _w.Commands.DisposeItemAsync(_w.Ctx(Who.Reception, "d4"), new DisposeLostItemRequest(scarf.ItemId, "Donated", "City shelter", v.Version), Ct)).Value.Status);

        var watch = await LogAsync(Who.Reception, "d5", "Gold-coloured earring", "Jewellery", "Front office safe");
        await AgeAsync(watch.ItemId, "2026-01-01");
        var wv = (await _w.Queries.ItemsAsync(_w.PropertyId, Who.Reception, ct: Ct)).Items.Single(x => x.ItemId == watch.ItemId).Version;
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.DisposeItemAsync(_w.Ctx(Who.Reception, "d6"), new DisposeLostItemRequest(watch.ItemId, "Donated", null, wv), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.DisposeItemAsync(_w.Ctx(Who.Manager, "d7"), new DisposeLostItemRequest(watch.ItemId, "Destroyed", null, wv), Ct)));
        Assert.Equal("Disposed", (await _w.Commands.DisposeItemAsync(_w.Ctx(Who.Manager, "d8"), new DisposeLostItemRequest(watch.ItemId, "Handed to police", null, wv), Ct)).Value.Status);

        var sum = await _w.Queries.LostFoundSummaryAsync(_w.PropertyId, Who.Manager, Ct);
        Assert.Equal((0, 0, 0, 2, 0, 0), (sum.Stored, sum.Claimed, sum.HandedOver, sum.Disposed, sum.DueDisposal, sum.Valuables));
    }

    [Fact]
    public async Task Retention_settings_move_the_disposal_date_and_housekeeping_sees_only_own_items_without_claims()
    {
        var scarf = await LogAsync(Who.Reception, "e1", "Blue cotton scarf", "Clothing", "Housekeeping store");
        var mine = await LogAsync(Who.Housekeeper, "e2", "Umbrella", "Other", "Back office cupboard");
        await AgeAsync(scarf.ItemId, "2026-06-30");
        await _w.Commands.SaveSettingsAsync(_w.Ctx(Who.Manager, "e3"), new SaveGuestServiceSettingsRequest(null, 30, 60, null), Ct);
        var all = await _w.Queries.ItemsAsync(_w.PropertyId, Who.Reception, ct: Ct);
        Assert.Equal(new DateOnly(2026, 7, 30), all.Items.Single(i => i.ItemId == scarf.ItemId).DisposeOn);   // prototype: 30-day retention
        var own = await _w.Queries.ItemsAsync(_w.PropertyId, Who.Housekeeper, ct: Ct);
        Assert.True(own.OwnOnly);
        Assert.Equal(mine.ItemId, Assert.Single(own.Items).ItemId);
        Assert.Null(own.Items[0].Claim);
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Queries.ItemsAsync(_w.PropertyId, Who.Cashier, ct: Ct)));
    }
}
