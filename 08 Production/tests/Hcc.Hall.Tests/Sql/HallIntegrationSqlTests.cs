using Hcc.Application.Hall;
using Hcc.Application.Inventory;
using Hcc.Application.Payments;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Inventory;
using Hcc.Domain.Errors;

namespace Hcc.Hall.Tests.Sql;

/// <summary>F6-int: the room block on accept (a group block in the same transaction, refused when the rooms are not free or group blocks are off),
/// the cash receipt limit per hall event, and the menu / T&amp;C template reads (money stripped for housekeeping, DEC-061).</summary>
[Trait("Category", "Sql")]
public sealed class HallIntegrationSqlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Accept_blocks_rooms_in_the_same_transaction_and_refuses_when_rooms_are_not_free()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (sagar, lawn, _) = await HallBillingSqlTests.VenuesAsync(h);
        var c = h.Get<HallBillingCommands>();
        var type = Convert.ToInt32(await h.ScalarAsync($"SELECT TOP 1 room_type_id FROM inv.room_type WHERE property_id = {h.PropertyId} ORDER BY room_type_id;", Ct), System.Globalization.CultureInfo.InvariantCulture);
        var rooms = Convert.ToInt32(await h.ScalarAsync($"SELECT COUNT(*) FROM inv.room WHERE property_id = {h.PropertyId} AND room_type_id = {type};", Ct), System.Globalization.CultureInfo.InvariantCulture);
        var q1 = await HallBillingSqlTests.QuoteAsync(h, sagar, "2027-03-10");

        // group blocks switched off (DEC-080): refused, nothing stored
        var off = await Assert.ThrowsAsync<HccException>(() => c.AcceptAsync(h.Ctx(h.Manager, HallBillingCommands.OpAccept), new AcceptQuotationRequest(q1, RoomBlock: new HallRoomBlockIn([new GroupBlockAllotment(type, 1, 300_000)])), Ct));
        Assert.Equal(HccErrorCode.NOT_AVAILABLE, off.Code);
        Assert.Equal(0L, await h.LongAsync("SELECT COUNT(*) FROM hall.event", Ct));

        h.Get<GroupBlockOptions>().Enabled = true;
        var tooMany = await Assert.ThrowsAsync<HccException>(() => c.AcceptAsync(h.Ctx(h.Manager, HallBillingCommands.OpAccept), new AcceptQuotationRequest(q1, RoomBlock: new HallRoomBlockIn([new GroupBlockAllotment(type, rooms + 1, 300_000)])), Ct));
        Assert.Equal(HccErrorCode.NOT_AVAILABLE, tooMany.Code);
        Assert.Equal(0L, await h.LongAsync("SELECT COUNT(*) FROM hall.event", Ct)); // the whole accept rolled back

        var ev = (await c.AcceptAsync(h.Ctx(h.Manager, HallBillingCommands.OpAccept), new AcceptQuotationRequest(q1, RoomBlock: new HallRoomBlockIn([new GroupBlockAllotment(type, 1, 300_000)])), Ct)).Value;
        var block = await h.LongAsync($"SELECT block_id FROM hall.event WHERE event_id = {ev.EventId}", Ct);
        Assert.Equal($"HEV-{ev.EventId}", (string)(await h.ScalarAsync($"SELECT code FROM inv.group_block WHERE group_block_id = {block}", Ct))!);
        Assert.Equal("2027-03-11", ((DateTime)(await h.ScalarAsync($"SELECT departure FROM inv.group_block WHERE group_block_id = {block}", Ct))!).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        _ = lawn;

        // cancel releases the block (no booking, no held rooms)
        await c.CancelAsync(h.Ctx(h.Reception, HallBillingCommands.OpCancel), new CancelHallEventRequest(ev.EventId, "Date changed"), Ct);
        Assert.NotEqual("Open", (string)(await h.ScalarAsync($"SELECT status FROM inv.group_block WHERE group_block_id = {block}", Ct))!);
    }

    [Fact]
    public async Task Hall_cash_receipts_stop_at_the_cash_limit_per_event()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        var (sagar, _, _) = await HallBillingSqlTests.VenuesAsync(h);
        var till = Convert.ToInt32(await h.ScalarAsync($"INSERT pay.till(property_id, code, name, created_by, created_utc) OUTPUT INSERTED.till_id VALUES ({h.PropertyId}, 'HALL-T2', N'Hall test till', {h.Owner}, SYSUTCDATETIME());", Ct),
            System.Globalization.CultureInfo.InvariantCulture);
        await h.Get<CashierService>().OpenShiftAsync(h.As(h.Cashier), Hotel.Key(), new OpenShiftRequest(till, 100_000), Ct);
        var c = h.Get<HallBillingCommands>();
        var quote = await HallBillingSqlTests.QuoteAsync(h, sagar, "2027-02-12", pax: 150);
        var ev = (await c.AcceptAsync(h.Ctx(h.Reception, HallBillingCommands.OpAccept), new AcceptQuotationRequest(quote, Advance: new HallTenderIn(15_000_000, "Cash", TillId: till)), Ct)).Value;
        var over = await Assert.ThrowsAsync<HccException>(() => c.ReceiveAdvanceAsync(h.Ctx(h.Cashier, HallBillingCommands.OpAdvance), new HallAdvanceRequest(ev.EventId, 5_000_000, "Cash", TillId: till), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, over.Code); // ₹1,50,000 + ₹50,000 reaches the ₹2,00,000 limit (CASH_LIMIT)
        await c.ReceiveAdvanceAsync(h.Ctx(h.Cashier, HallBillingCommands.OpAdvance), new HallAdvanceRequest(ev.EventId, 5_000_000, "UPI", "UPI-LIM", TillId: till), Ct); // any other tender passes
        Assert.Equal(15_000_000L, await h.LongAsync("SELECT SUM(amount_paise) FROM pay.cash_movement WHERE source_type = 'hall'", Ct));
    }

    [Fact]
    public async Task Menus_and_templates_are_read_from_the_server_with_prices_stripped_for_housekeeping()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        await h.ScalarAsync($"""
            INSERT hall.menu_package(property_id, code, name, per_plate_paise) VALUES ({h.PropertyId}, 'MP-T1', N'Test veg (fictional)', 65000);
            INSERT hall.menu_package_item(menu_id, seq, pos_item_code) VALUES (SCOPE_IDENTITY(), 1, 'PANEER');
            SELECT 1;
            """, Ct);
        var hk = Convert.ToInt32(await h.ScalarAsync($"""
            INSERT plat.app_user(property_id, login_name, display_name, created_utc) VALUES ({h.PropertyId}, 'hk.int', N'hk.int', SYSUTCDATETIME());
            DECLARE @u INT = SCOPE_IDENTITY();
            INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, 'HKP', DATEADD(day, -30, SYSUTCDATETIME()), SYSUTCDATETIME());
            SELECT @u;
            """, Ct), System.Globalization.CultureInfo.InvariantCulture);
        var q = h.Get<HallQueries>();
        var own = (await q.MenusAsync(h.PropertyId, h.Owner, Ct)).Single(m => m.Code == "MP-T1");
        Assert.Equal(65000L, own.PerPlatePaise);
        Assert.Equal(["PANEER"], own.Items);
        Assert.Null((await q.MenusAsync(h.PropertyId, hk, Ct)).Single(m => m.Code == "MP-T1").PerPlatePaise);
        await h.Get<HallQuoteCommands>().SaveTemplateAsync(h.Ctx(h.Owner, HallQuoteCommands.OpTemplate), new SaveTncTemplateRequest("WEDDING", [new TncClauseIn("Advance", "Half on acceptance.")]), Ct);
        var t = (await q.TncTemplatesAsync(h.PropertyId, h.Reception, Ct)).Single(x => x.EventType == "WEDDING");
        Assert.Equal("Advance", t.Versions[^1].Clauses[0].Heading);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => q.TncTemplatesAsync(h.PropertyId, hk, Ct))).Code);
    }
}
