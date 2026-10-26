using Hcc.Contracts;
using Hcc.Contracts.Pos;
using Hcc.Domain.Errors;
using Hcc.Pos.Tests.Harness;
using static Hcc.Pos.Tests.Harness.Assertions;

namespace Hcc.Pos.Tests.Sql;

/// <summary>
/// Role matrix v2 in the POS (DEC-079, migration 3700): the Restaurant role operates checks only (D1), day sales are PERM-POS-004, settling is
/// PERM-POS-005, and a Manager's menu / price change is a request the Owner decides (D5, migration 3701).
/// </summary>
public class PosRbacV2Tests(PosDb db) : IClassFixture<PosDb>
{
    [Fact]
    public async Task Restaurant_role_opens_checks_adds_items_and_sends_kots()
    {
        var o = await db.OpenTableAsync("T1", Who.Waiter);
        await db.AddAsync(o.OrderId, "PANEER", 1, Who.Waiter);                                 // Rs 380
        var c = await db.AddAsync(o.OrderId, "ROTI", 2, Who.Waiter);                           // + 2 x Rs 40 = Rs 460 net
        Assert.Equal(46_000, c.NetPaise);
        var kot = await db.SendAsync(o.OrderId, Who.Waiter);
        await db.Pos.AdvanceKotAsync(db.As(Who.Waiter), PosDb.Key(), new AdvanceKotRequest(o.OrderId, kot.KotId, "Ready"));
        // The waiter sees the check, the menu and the kitchen board (PERM-POS-001) …
        Assert.Equal(46_000, (await db.Queries.GetCheckAsync(db.As(Who.Waiter), o.OrderId)).NetPaise);
        Assert.NotEmpty(await db.Queries.MenuAsync(db.As(Who.Waiter), db.Restaurant));
        Assert.Contains(await db.Queries.KitchenAsync(db.As(Who.Waiter)), k => k.OrderId == o.OrderId);
        Assert.Contains(await db.Queries.OpenChecksAsync(db.As(Who.Waiter)), x => x.OrderId == o.OrderId);
    }

    [Fact]
    public async Task Restaurant_role_cannot_settle_decide_a_void_or_see_day_sales()
    {
        var (till, _) = await db.OpenTillAsync(0, "RST1");
        var (stay, _, _) = await db.NewInHouseAsync("411");
        var o = await db.OpenTableAsync("T2", Who.Waiter);
        await db.AddAsync(o.OrderId, "THALI", 2, Who.Waiter);                                  // 2 x Rs 420 = Rs 840 line
        await db.SendAsync(o.OrderId, Who.Waiter);
        // D1: no settle at the outlet (cash or card) and no charge to a room folio.
        await Fails(HccErrorCode.FORBIDDEN, () => db.SettleOutletAsync(o.OrderId, "Cash", till, who: Who.Waiter));
        await Fails(HccErrorCode.FORBIDDEN, () => db.SettleOutletAsync(o.OrderId, "Card", who: Who.Waiter));
        await Fails(HccErrorCode.FORBIDDEN, () => db.SettleFolioAsync(o.OrderId, stay, Who.Waiter));
        // D1: no day sales summary and no closed-bills list (PERM-POS-004 / POS-005).
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.DaySummaryAsync(db.As(Who.Waiter), null));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.ClosedChecksAsync(db.As(Who.Waiter), null));
        // A void above the Rs 500 limit becomes a request (R cell); the waiter can never decide it, approving or rejecting.
        var line = (await db.Queries.GetCheckAsync(db.As(Who.Owner), o.OrderId)).Lines[0].LineId;
        var req = (await db.Pos.VoidItemAsync(db.As(Who.Waiter), PosDb.Key(), new VoidCheckItemRequest(o.OrderId, line, "Guest changed order", null))).Value;
        Assert.Equal("PendingApproval", req.Outcome);
        await Fails(HccErrorCode.FORBIDDEN, () => db.Pos.DecideVoidAsync(db.As(Who.Waiter), PosDb.Key(), new DecideVoidRequest(req.VoidRequestId!.Value, true, null)));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Pos.DecideVoidAsync(db.As(Who.Waiter), PosDb.Key(), new DecideVoidRequest(req.VoidRequestId!.Value, false, "No")));
        // Rs 840 is within the Manager's Rs 1,000 void limit (DEC-063 G1), so the Manager approves it.
        Assert.Equal("Executed", (await db.Pos.DecideVoidAsync(db.As(Who.Manager), PosDb.Key(), new DecideVoidRequest(req.VoidRequestId!.Value, true, "ok"))).Value.Status);
        // The cashier settles what the waiter served: a second THALI line keeps the check settleable.
        await db.AddAsync(o.OrderId, "THALI", 1);
        await db.SendAsync(o.OrderId);
        var bill = (await db.SettleOutletAsync(o.OrderId, "Card")).Value;
        Assert.Equal(42_000, bill.NetPaise);                                                   // Rs 420 net (the voided Rs 840 line is gone)
    }

    [Fact]
    public async Task Day_sales_are_money_the_cashier_sees_them_reception_and_restaurant_do_not()
    {
        Assert.NotNull(await db.Queries.DaySummaryAsync(db.As(Who.Owner), null));
        Assert.NotNull(await db.Queries.DaySummaryAsync(db.As(Who.Manager), null));
        Assert.NotNull(await db.Queries.DaySummaryAsync(db.As(Who.Cashier), null));          // PERM-POS-004 own shift (shared till, DEC-052)
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.DaySummaryAsync(db.As(Who.Reception), null));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.DaySummaryAsync(db.As(Who.Waiter), null));
        // Reception settles at the desk (PERM-POS-005 own shift), so it still reprints the day's bills.
        Assert.NotNull(await db.Queries.ClosedChecksAsync(db.As(Who.Reception), null));
    }

    [Fact]
    public async Task Manager_price_change_becomes_a_request_the_Owner_decides()
    {
        var paneer = (await db.Queries.MenuAsync(db.As(Who.Owner), db.Restaurant)).Single(m => m.Code == "PANEER");
        Assert.Equal(38_000, paneer.PricePaise);                                               // Rs 380 on the menu
        // D5: the Manager asks for Rs 420; nothing changes until the Owner decides.
        var asked = (await db.Pos.SaveMenuItemAsync(db.As(Who.Manager), PosDb.Key(), new SaveMenuItemRequest(paneer.ItemId, "PANEER", paneer.Name, paneer.Category, 42_000,
            paneer.Veg, paneer.OutletIds, paneer.Version, "Dairy cost up"))).Value;
        Assert.Equal("requested", asked.Status);
        Assert.NotNull(asked.RequestId);
        Assert.Equal(38_000, (await db.Queries.MenuAsync(db.As(Who.Owner), db.Restaurant)).Single(m => m.Code == "PANEER").PricePaise);
        var pending = Assert.Single(await db.Queries.PendingMenuRequestsAsync(db.As(Who.Owner)), r => r.RequestId == asked.RequestId);
        Assert.Equal((42_000L, 38_000L, Who.Manager), (pending.PricePaise, pending.PriceBeforePaise!.Value, pending.RequestedBy));
        Assert.Contains(await db.Queries.PendingMenuRequestsAsync(db.As(Who.Manager)), r => r.RequestId == asked.RequestId);
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.PendingMenuRequestsAsync(db.As(Who.Cashier)));
        // One pending change per item; only the Owner decides (the Manager holds R, not A).
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.SaveMenuItemAsync(db.As(Who.Manager), PosDb.Key(), new SaveMenuItemRequest(paneer.ItemId, "PANEER", paneer.Name,
            paneer.Category, 45_000, paneer.Veg, paneer.OutletIds, paneer.Version)));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Pos.DecideMenuChangeAsync(db.As(Who.Manager), PosDb.Key(), new DecideMenuChangeRequest(asked.RequestId!.Value, true, null)));
        var done = (await db.Pos.DecideMenuChangeAsync(db.As(Who.Owner), PosDb.Key(), new DecideMenuChangeRequest(asked.RequestId!.Value, true, "Agreed"))).Value;
        Assert.Equal(("Executed", paneer.ItemId), (done.Status, done.ItemId!.Value));
        Assert.Equal(42_000, (await db.Queries.MenuAsync(db.As(Who.Owner), db.Restaurant)).Single(m => m.Code == "PANEER").PricePaise);
        Assert.DoesNotContain(await db.Queries.PendingMenuRequestsAsync(db.As(Who.Owner)), r => r.RequestId == asked.RequestId);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.DecideMenuChangeAsync(db.As(Who.Owner), PosDb.Key(), new DecideMenuChangeRequest(asked.RequestId!.Value, true, null)));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'POS_MENU_REQUESTED' AND target = @t;",
            ("@t", System.Data.SqlDbType.VarChar, "menu-request:" + asked.RequestId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    [Fact]
    public async Task A_rejected_or_stale_menu_request_leaves_the_menu_and_a_new_item_waits_for_the_Owner()
    {
        var tea = (await db.Queries.MenuAsync(db.As(Who.Owner), db.Restaurant)).Single(m => m.Code == "TEA");
        var asked = (await db.Pos.SaveMenuItemAsync(db.As(Who.Manager), PosDb.Key(), new SaveMenuItemRequest(tea.ItemId, "TEA", tea.Name, tea.Category, tea.PricePaise + 1_000,
            tea.Veg, tea.OutletIds, tea.Version))).Value;
        var rejected = (await db.Pos.DecideMenuChangeAsync(db.As(Who.Owner), PosDb.Key(), new DecideMenuChangeRequest(asked.RequestId!.Value, false, "Not now"))).Value;
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal(tea.PricePaise, (await db.Queries.MenuAsync(db.As(Who.Owner), db.Restaurant)).Single(m => m.Code == "TEA").PricePaise);
        // Stale: the Owner changes the item after the Manager asked, so approving the old request fails (reject it and ask again).
        var again = (await db.Pos.SaveMenuItemAsync(db.As(Who.Manager), PosDb.Key(), new SaveMenuItemRequest(tea.ItemId, "TEA", tea.Name, tea.Category, tea.PricePaise + 2_000,
            tea.Veg, tea.OutletIds, tea.Version))).Value;
        await db.Pos.SaveMenuItemAsync(db.As(Who.Owner), PosDb.Key(), new SaveMenuItemRequest(tea.ItemId, "TEA", tea.Name, tea.Category, tea.PricePaise + 500, tea.Veg, tea.OutletIds, tea.Version));
        await Fails(HccErrorCode.STALE_VERSION, () => db.Pos.DecideMenuChangeAsync(db.As(Who.Owner), PosDb.Key(), new DecideMenuChangeRequest(again.RequestId!.Value, true, null)));
        Assert.Equal(tea.PricePaise + 500, (await db.Queries.MenuAsync(db.As(Who.Owner), db.Restaurant)).Single(m => m.Code == "TEA").PricePaise);
        // A new item asked by the Manager exists only after the Owner approves it.
        var newItem = (await db.Pos.SaveMenuItemAsync(db.As(Who.Manager), PosDb.Key(), new SaveMenuItemRequest(null, "KULFI", "Kesar kulfi", "Desserts", 15_000, true, [db.Restaurant], null))).Value;
        Assert.Equal(("requested", 0), (newItem.Status, newItem.ItemId));
        Assert.DoesNotContain(await db.Queries.MenuAsync(db.As(Who.Owner), db.Restaurant), m => m.Code == "KULFI");
        var made = (await db.Pos.DecideMenuChangeAsync(db.As(Who.Owner), PosDb.Key(), new DecideMenuChangeRequest(newItem.RequestId!.Value, true, null))).Value;
        Assert.Contains(await db.Queries.MenuAsync(db.As(Who.Owner), db.Restaurant), m => m.Code == "KULFI" && m.ItemId == made.ItemId && m.PricePaise == 15_000);
    }
}
