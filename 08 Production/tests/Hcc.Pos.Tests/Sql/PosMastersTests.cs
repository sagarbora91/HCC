using System.Data;
using System.Globalization;
using Hcc.Application.Pos;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Contracts.Pos;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Pos;
using Hcc.Pos.Tests.Harness;
using static Hcc.Pos.Tests.Harness.Assertions;

namespace Hcc.Pos.Tests.Sql;

/// <summary>
/// MA-4 POS masters on SQL Server (migration 2840, PosMastersService): outlets, tables and counters, menu categories, menu items with dated
/// prices, GST profile / class, KOT by station, sold out today; PERM-MST-001 (Owner, Tech admin) and PERM-MST-002 (Owner) for money fields.
/// Each test builds its own outlet / items (shared class database), so the order of the tests does not matter.
/// </summary>
public class PosMastersTests(PosDb db) : IClassFixture<PosDb>
{
    private const int TechAdmin = 8;   // masters integration: Wave B seeds the Restaurant waiter as id 7 (old: 7)
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> Seeded = [];

    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    private static string K() => PosDb.Key();

    private static string U(string p) => p + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    /// <summary>The service over the class database, plus a Tech admin principal (id 8, role TAD) once per database.</summary>
    private async Task<PosMastersService> SvcAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (Seeded.Add(db.Name))
            {
                var uid = Convert.ToInt32(await db.ScalarAsync("INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (@p, 'techadmin', N'techadmin', '2026-01-01');",
                    ("@p", SqlDbType.Int, db.PropertyId)), CultureInfo.InvariantCulture);
                if (uid != TechAdmin) throw new InvalidOperationException($"Tech admin got user id {uid}.");
                await db.ScalarAsync($"INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES ({uid}, 'TAD', '2026-01-01', '2026-01-01');");
            }
        }
        finally
        {
            Gate.Release();
        }

        var platform = new SqlPlatform(db.Options);
        var store = new SqlPosStore(platform.Connections);
        return new PosMastersService(platform.Commands, db.Policy, store, store, db.Clock);
    }

    private async Task<OutletView> OutletAsync(int id) => (await db.Queries.OutletsAsync(db.As(Who.Owner))).Single(o => o.OutletId == id);

    private async Task<MenuItemView> ItemAsync(int id) => (await db.Queries.MenuAsync(db.As(Who.Owner), null)).Single(m => m.ItemId == id);

    private async Task<int> NewOutletAsync(PosMastersService s, string type = "bar", string? profile = null, string? station = null, int who = Who.Owner)
        => (await s.SaveOutletAsync(db.As(who), K(), new SaveOutletRequest(null, U("O"), U("Outlet "), type, "17:00", "23:30", profile, station))).Value.Id;

    private async Task<int> NewTableAsync(PosMastersService s, int outletId, string no, int who = Who.Owner)
        => (await s.SaveOutletTableAsync(db.As(who), K(), new SaveOutletTableRequest(null, outletId, U("T"), U("Table "), no, 4, "Garden"))).Value.Id;

    private async Task<int> NewItemAsync(PosMastersService s, int outletId, long paise, string gst = "food", string? station = null, int who = Who.Owner, string? from = "2026-10-24")
    {
        var cat = (await db.Queries.MenuCategoriesAsync(db.As(Who.Owner), 1, 200)).Items.First(c => c.Active).CategoryId;
        IReadOnlyList<MenuPriceView> prices = from is null ? [] : [new MenuPriceView(from, paise)];
        return (await s.SaveMenuItemAsync(db.As(who), K(), new SaveMenuItemRequest(null, U("I"), U("Item "), "", 0, true, [outletId], null, cat, null, gst, station, null, prices))).Value.ItemId;
    }

    private async Task<long> SentCheckAsync(int outletId, string table, params int[] items)
    {
        var o = (await db.Pos.OpenCheckAsync(db.As(Who.Cashier), K(), new OpenCheckRequest(outletId, table, null, 2, null))).Value;
        foreach (var i in items) await db.Pos.AddItemAsync(db.As(Who.Cashier), K(), new AddCheckItemRequest(o.OrderId, i, 1, null));
        await db.Pos.SendKotAsync(db.As(Who.Cashier), K(), new SendKotRequest(o.OrderId, null));
        return o.OrderId;
    }

    [Fact]
    public async Task Migration_and_the_w26_setup_fill_the_masters_types_tables_categories_and_dated_prices()
    {
        await SvcAsync();
        var rest = await OutletAsync(db.Restaurant);
        Assert.Equal(("restaurant", "hotel", true), (rest.Type, rest.GstProfile, rest.Active));
        Assert.Equal(40, rest.TableRows!.Count);
        Assert.Equal(("REST-T1", "Restaurant T1", "T1", 10), (rest.TableRows[0].Code, rest.TableRows[0].Name, rest.TableRows[0].TableNo, rest.TableRows[0].SortOrder));
        Assert.Equal("room-service", (await OutletAsync(db.RoomService)).Type);
        var cats = (await db.Queries.MenuCategoriesAsync(db.As(Who.Owner), 1, 200)).Items;
        Assert.Contains(cats, c => c.Code == "BREADS-RICE" && c.Name == "Breads & rice" && c.Items >= 2);
        var tea = await ItemAsync(db.Menu["TEA"]);
        Assert.Equal((6000L, "BEVERAGES", "food"), (tea.PricePaise, tea.CategoryCode, tea.GstClass));
        Assert.Equal([new MenuPriceView("2017-07-01", 6000)], tea.Prices);
        Assert.False((await ItemAsync(db.Menu["BIRYANI"])).Available);
    }

    [Fact]
    public async Task Save_outlet_by_tech_admin_with_timings_and_station_history_and_audit_manager_is_refused()
    {
        var s = await SvcAsync();
        var code = U("BAR");
        var r = (await s.SaveOutletAsync(db.As(TechAdmin), K(), new SaveOutletRequest(null, code, "Rooftop " + code, "bar", "17:00", "23:30", null, "Bar"))).Value;
        var o = await OutletAsync(r.Id);
        Assert.Equal(("table", "bar", "17:00", "23:30", "Bar", "hotel"), (o.Kind, o.Type, o.Opens, o.Closes, o.KotStation, o.GstProfile));
        Assert.Equal(o.Version, r.Version);
        Assert.True(await db.CountAsync("SELECT COUNT(*) FROM mst.master_history WHERE master = 'outlets' AND row_id = @r", ("@r", SqlDbType.BigInt, (long)r.Id)) >= 6);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail = @d", ("@d", SqlDbType.NVarChar, $"Master Outlets: added {code} · Rooftop {code}")));
        await Fails(HccErrorCode.FORBIDDEN, () => s.SaveOutletAsync(db.As(Who.Manager), K(), new SaveOutletRequest(null, U("X"), U("X "), "bar")));
        await Fails(HccErrorCode.FORBIDDEN, () => s.SaveOutletAsync(db.As(Who.Cashier), K(), new SaveOutletRequest(null, U("X"), U("X "), "bar")));
        // Rename: one history row, the audit line "Master Outlets: name … → …".
        var ren = (await s.SaveOutletAsync(db.As(TechAdmin), K(), new SaveOutletRequest(r.Id, code, "Terrace " + code, "bar", "17:00", "23:30", null, "Bar", null, r.Version))).Value;
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail = @d", ("@d", SqlDbType.NVarChar, $"Master Outlets: name Rooftop {code} → Terrace {code}")));
        await Fails(HccErrorCode.STALE_VERSION, () => s.SaveOutletAsync(db.As(TechAdmin), K(), new SaveOutletRequest(r.Id, code, "Again " + code, "bar", null, null, null, null, null, r.Version)));
        Assert.NotEqual(r.Version, ren.Version);
    }

    [Fact]
    public async Task Outlet_gst_profile_is_owner_only_and_a_fixed_specified_profile_bills_at_18_percent()
    {
        var s = await SvcAsync();
        await Fails(HccErrorCode.FORBIDDEN, () => s.SaveOutletAsync(db.As(TechAdmin), K(), new SaveOutletRequest(null, U("C"), U("Cafe "), "restaurant", null, null, "specified")));
        var outlet = await NewOutletAsync(s, "restaurant", "specified");
        await NewTableAsync(s, outlet, "C1");
        var item = await NewItemAsync(s, outlet, 28_000);
        var id = await SentCheckAsync(outlet, "C1", item);
        Assert.Equal(1800, (await db.Queries.GetCheckAsync(db.As(Who.Owner), id)).TaxBps);
        var bill = (await db.SettleOutletAsync(id, "UPI")).Value;
        Assert.Equal((1800, 5040L, 33040L), (bill.TaxBps, bill.TaxPaise, bill.TotalPaise));
        // The hotel's own status stays at 5 % for the restaurant (golden figures unchanged).
        var rest = await db.SentTableAsync("T30");
        Assert.Equal(500, (await db.Queries.GetCheckAsync(db.As(Who.Owner), rest)).TaxBps);
    }

    [Fact]
    public async Task Retire_outlet_refused_with_open_checks_then_retired_outlet_takes_no_check_and_cannot_be_deleted()
    {
        var s = await SvcAsync();
        var outlet = await NewOutletAsync(s);
        await NewTableAsync(s, outlet, "B1");
        var item = await NewItemAsync(s, outlet, 15_000);
        var id = await SentCheckAsync(outlet, "B1", item);
        var v = (await OutletAsync(outlet)).Version!;
        var inUse = await Fails(HccErrorCode.INVALID_TRANSITION, () => s.RetireAsync(db.As(Who.Owner), K(), "RetireOutlet", PosMasterTable.Outlet, outlet, v, new OutletRefRequest(outlet, v)));
        Assert.Equal(MasterReasons.InUse, Reason(inUse));
        await db.Pos.VoidBillAsync(db.As(Who.Cashier), K(), new VoidBillRequest(id, "Walked out", null));
        var ret = (await s.RetireAsync(db.As(TechAdmin), K(), "RetireOutlet", PosMasterTable.Outlet, outlet, v, new OutletRefRequest(outlet, v))).Value;
        var o = await OutletAsync(outlet);
        Assert.Equal((false, "2026-10-24"), (o.Active, o.RetiredOn));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.OpenCheckAsync(db.As(Who.Cashier), K(), new OpenCheckRequest(outlet, "B1", null, 1, null)));
        var del = await Fails(HccErrorCode.INVALID_TRANSITION, () => s.DeleteAsync(db.As(Who.Owner), K(), "DeleteOutlet", PosMasterTable.Outlet, outlet, ret.Version, new OutletRefRequest(outlet, ret.Version!)));
        Assert.Equal(MasterReasons.InUse, Reason(del));
        var back = (await s.ReactivateAsync(db.As(Who.Owner), K(), "ReactivateOutlet", PosMasterTable.Outlet, outlet, ret.Version, new OutletRefRequest(outlet, ret.Version!))).Value;
        Assert.True((await OutletAsync(outlet)).Active);
        Assert.Equal("reactivated", back.Action);
    }

    [Fact]
    public async Task Outlet_rules_duplicate_code_code_fixed_once_used_and_no_table_room_switch_once_used()
    {
        var s = await SvcAsync();
        Assert.Equal(MasterReasons.CodeTaken, Reason(await Fails(HccErrorCode.VALIDATION, () => s.SaveOutletAsync(db.As(Who.Owner), K(), new SaveOutletRequest(null, "rest", U("R "), "restaurant")))));
        var rest = await OutletAsync(db.Restaurant);
        Assert.Equal(MasterReasons.InUse, Reason(await Fails(HccErrorCode.INVALID_TRANSITION, () =>
            s.SaveOutletAsync(db.As(Who.Owner), K(), new SaveOutletRequest(db.Restaurant, "REST2", rest.Name, "restaurant", null, null, null, null, null, rest.Version)))));
        var sw = await Fails(HccErrorCode.VALIDATION, () => s.SaveOutletAsync(db.As(Who.Owner), K(), new SaveOutletRequest(db.Restaurant, "REST", rest.Name, "room-service", null, null, null, null, null, rest.Version)));
        Assert.Equal(("type", MasterReasons.Invalid), (sw.Details!["field"].GetString(), Reason(sw)));
        Assert.Equal(MasterReasons.Invalid, Reason(await Fails(HccErrorCode.VALIDATION, () => s.SaveOutletAsync(db.As(Who.Owner), K(), new SaveOutletRequest(null, U("Q"), U("Q "), "bar", "7am")))));
        // A fresh banquet outlet switches to room service and, never used, is deleted.
        var bq = await NewOutletAsync(s, "banquet");
        var v = (await OutletAsync(bq)).Version;
        var sw2 = (await s.SaveOutletAsync(db.As(Who.Owner), K(), new SaveOutletRequest(bq, (await OutletAsync(bq)).Code, (await OutletAsync(bq)).Name, "room-service", null, null, null, null, null, v))).Value;
        Assert.Equal("room", (await OutletAsync(bq)).Kind);
        await s.DeleteAsync(db.As(Who.Owner), K(), "DeleteOutlet", PosMasterTable.Outlet, bq, sw2.Version, new OutletRefRequest(bq, sw2.Version!));
        Assert.DoesNotContain(await db.Queries.OutletsAsync(db.As(Who.Owner)), o => o.OutletId == bq);
    }

    [Fact]
    public async Task Tables_seats_and_section_open_a_check_retire_refused_while_open_number_reusable_after_retire()
    {
        var s = await SvcAsync();
        var outlet = await NewOutletAsync(s);
        var t1 = await NewTableAsync(s, outlet, "B1");
        var t2 = await NewTableAsync(s, outlet, "B2");
        var o = await OutletAsync(outlet);
        Assert.Equal(["B1", "B2"], o.Tables);
        Assert.Equal((4, "Garden"), (o.TableRows![0].Seats!.Value, o.TableRows[0].Section));
        var item = await NewItemAsync(s, outlet, 10_000);
        var open = await SentCheckAsync(outlet, "B1", item);
        var v1 = o.TableRows.Single(t => t.TableId == t1).Version;
        Assert.Equal(MasterReasons.InUse, Reason(await Fails(HccErrorCode.INVALID_TRANSITION, () =>
            s.RetireAsync(db.As(Who.Owner), K(), "RetireOutletTable", PosMasterTable.Table, t1, v1, new OutletTableRefRequest(t1, v1)))));
        // The number of a used table cannot change.
        var row = o.TableRows.Single(t => t.TableId == t1);
        Assert.Equal(MasterReasons.Invalid, Reason(await Fails(HccErrorCode.VALIDATION, () =>
            s.SaveOutletTableAsync(db.As(Who.Owner), K(), new SaveOutletTableRequest(t1, outlet, row.Code, row.Name, "B9", 4, "Garden", false, null, v1)))));
        await db.Pos.VoidBillAsync(db.As(Who.Cashier), K(), new VoidBillRequest(open, "Test", null));
        var v2 = o.TableRows.Single(t => t.TableId == t2).Version;
        await s.RetireAsync(db.As(TechAdmin), K(), "RetireOutletTable", PosMasterTable.Table, t2, v2, new OutletTableRefRequest(t2, v2));
        Assert.Equal(["B1"], (await OutletAsync(outlet)).Tables);
        await Fails(HccErrorCode.VALIDATION, () => db.Pos.OpenCheckAsync(db.As(Who.Cashier), K(), new OpenCheckRequest(outlet, "B2", null, 1, null)));
        // A retired B2 does not block a new active B2; a second active B2 is refused.
        var b2 = await NewTableAsync(s, outlet, "B2");
        Assert.Equal(MasterReasons.Invalid, Reason(await Fails(HccErrorCode.VALIDATION, () => NewTableAsync(s, outlet, "b2"))));
        Assert.Contains(b2, (await OutletAsync(outlet)).TableRows!.Where(t => t.Active).Select(t => t.TableId));
        await db.Pos.OpenCheckAsync(db.As(Who.Cashier), K(), new OpenCheckRequest(outlet, "B2", null, 1, null));
        // Room service takes no tables.
        await Fails(HccErrorCode.VALIDATION, () => NewTableAsync(s, db.RoomService, "R1"));
    }

    [Fact]
    public async Task Categories_rename_follows_on_items_reorder_in_use_cannot_be_deleted_unused_can_reads_for_tech_admin()
    {
        var s = await SvcAsync();
        var created = (await s.SaveMenuCategoryAsync(db.As(TechAdmin), K(), new SaveMenuCategoryRequest(null, U("SOUP"), U("Soups ")))).Value;
        var cats = (await db.Queries.MenuCategoriesAsync(db.As(TechAdmin), 1, 200)).Items;
        var c = cats.Single(x => x.CategoryId == created.Id);
        Assert.Equal(0, c.Items);
        var item = (await s.SaveMenuItemAsync(db.As(Who.Owner), K(), new SaveMenuItemRequest(null, U("SP"), U("Soup "), "", 0, true, [db.Restaurant], null, c.CategoryId, "SP", "food", null, null,
            [new MenuPriceView("2026-10-24", 15000)]))).Value;
        var ren = (await s.SaveMenuCategoryAsync(db.As(TechAdmin), K(), new SaveMenuCategoryRequest(c.CategoryId, c.Code, "Hot soups " + c.Code, null, c.Version))).Value;
        Assert.Equal("Hot soups " + c.Code, (await ItemAsync(item.ItemId)).Category);
        var del = await Fails(HccErrorCode.INVALID_TRANSITION, () => s.DeleteAsync(db.As(Who.Owner), K(), "DeleteMenuCategory", PosMasterTable.Category, c.CategoryId, ren.Version, new MenuCategoryRefRequest(c.CategoryId, ren.Version!)));
        Assert.Equal(MasterReasons.InUse, Reason(del));
        var empty = (await s.SaveMenuCategoryAsync(db.As(Who.Owner), K(), new SaveMenuCategoryRequest(null, U("E"), U("Empty ")))).Value;
        await s.DeleteAsync(db.As(Who.Owner), K(), "DeleteMenuCategory", PosMasterTable.Category, empty.Id, empty.Version, new MenuCategoryRefRequest(empty.Id, empty.Version!));
        var ids = (await db.Queries.MenuCategoriesAsync(db.As(Who.Owner), 1, 200)).Items.Select(x => x.CategoryId).ToList();
        Assert.DoesNotContain(empty.Id, ids);
        var reversed = Enumerable.Reverse(ids).ToList();
        await s.ReorderAsync(db.As(TechAdmin), K(), "ReorderMenuCategories", PosMasterTable.Category, reversed, new ReorderMenuCategoriesRequest(reversed));
        var after = (await db.Queries.MenuCategoriesAsync(db.As(Who.Owner), 1, 200)).Items;
        Assert.Equal(reversed, after.Select(x => x.CategoryId));
        Assert.Equal(Enumerable.Range(1, after.Count).Select(i => i * 10), after.Select(x => x.SortOrder));
        await Fails(HccErrorCode.VALIDATION, () => s.ReorderAsync(db.As(Who.Owner), K(), "ReorderMenuCategories", PosMasterTable.Category, reversed.Skip(1).ToList(), new ReorderMenuCategoriesRequest(reversed)));
        // Master configuration reads (Tech admin: PERM-MST-001 only); the housekeeper reads none.
        Assert.NotEmpty(await db.Queries.OutletsAsync(db.As(TechAdmin)));
        Assert.NotEmpty(await db.Queries.MenuAsync(db.As(TechAdmin), null));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.MenuCategoriesAsync(db.As(Who.Housekeeper), 1, 200));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.OpenChecksAsync(db.As(TechAdmin)));
    }

    [Fact]
    public async Task Dated_price_from_tomorrow_keeps_todays_price_amounts_in_effect_are_immutable_and_owner_only()
    {
        var s = await SvcAsync();
        var item = await NewItemAsync(s, db.Restaurant, 20_000);
        var m = await ItemAsync(item);
        var cat = m.CategoryId!.Value;
        SaveMenuItemRequest Body(string version, params MenuPriceView[] prices) =>
            new(item, m.Code, m.Name, "", 0, true, [db.Restaurant], version, cat, null, "food", null, null, prices);
        await Fails(HccErrorCode.FORBIDDEN, () => s.SaveMenuItemAsync(db.As(TechAdmin), K(), Body(m.Version, new MenuPriceView("2026-10-24", 20_000), new MenuPriceView("2026-10-25", 22_000))));
        var v2 = (await s.SaveMenuItemAsync(db.As(Who.Owner), K(), Body(m.Version, new MenuPriceView("2026-10-24", 20_000), new MenuPriceView("2026-10-25", 22_000)))).Value.Version;
        Assert.Equal(20_000, (await ItemAsync(item)).PricePaise);
        Assert.Equal(2, (await ItemAsync(item)).Prices!.Count);
        var o = await db.OpenTableAsync("T31");
        Assert.Equal(20_000, (await db.Pos.AddItemAsync(db.As(Who.Cashier), K(), new AddCheckItemRequest(o.OrderId, item, 1, null))).Value.NetPaise);
        try
        {
            await db.SetBusinessDateAsync(PosDb.Day.AddDays(1));
            Assert.Equal(22_000, (await ItemAsync(item)).PricePaise);
            var stale = Body(v2, new MenuPriceView("2026-10-24", 21_000), new MenuPriceView("2026-10-25", 22_000));
            Assert.Equal(MasterReasons.Invalid, Reason(await Fails(HccErrorCode.VALIDATION, () => s.SaveMenuItemAsync(db.As(Who.Owner), K(), stale))));
        }
        finally
        {
            await db.SetBusinessDateAsync(PosDb.Day);
        }

        Assert.True(await db.CountAsync("SELECT COUNT(*) FROM mst.master_history WHERE master = 'menuItems' AND row_id = @r AND field = 'price'", ("@r", SqlDbType.BigInt, (long)item)) >= 2);
    }

    [Fact]
    public async Task Tech_admin_adds_an_unpriced_item_that_cannot_be_sold_and_cannot_price_it()
    {
        var s = await SvcAsync();
        var item = await NewItemAsync(s, db.Restaurant, 0, who: TechAdmin, from: null);
        var m = await ItemAsync(item);
        Assert.Equal((0L, 0), (m.PricePaise, m.Prices!.Count));
        var o = await db.OpenTableAsync("T32");
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.AddItemAsync(db.As(Who.Cashier), K(), new AddCheckItemRequest(o.OrderId, item, 1, null)));
        await Fails(HccErrorCode.FORBIDDEN, () => s.SaveMenuItemAsync(db.As(TechAdmin), K(), new SaveMenuItemRequest(item, m.Code, m.Name, "", 0, true, [db.Restaurant], m.Version, m.CategoryId,
            null, "other", null, null, [])));
    }

    [Fact]
    public async Task Sold_out_today_from_the_master_or_the_pos_switch_lapses_at_the_next_business_date()
    {
        var s = await SvcAsync();
        var item = await NewItemAsync(s, db.Restaurant, 9_000);
        var m = await ItemAsync(item);
        await s.SaveMenuItemAsync(db.As(TechAdmin), K(), new SaveMenuItemRequest(item, m.Code, m.Name, "", 0, true, [db.Restaurant], m.Version, m.CategoryId, null, "food", null, null,
            [.. m.Prices!], true));
        var sold = await ItemAsync(item);
        Assert.Equal((false, "2026-10-24"), (sold.Available, sold.SoldOutOn));
        var o = await db.OpenTableAsync("T33");
        await Fails(HccErrorCode.NOT_AVAILABLE, () => db.Pos.AddItemAsync(db.As(Who.Cashier), K(), new AddCheckItemRequest(o.OrderId, item, 1, null)));
        try
        {
            await db.SetBusinessDateAsync(PosDb.Day.AddDays(1));
            Assert.True((await ItemAsync(item)).Available);
            await db.Pos.SetMenuItemAvailabilityAsync(db.As(Who.Reception), K(), new SetMenuItemAvailabilityRequest(item, false));
            Assert.Equal((false, "2026-10-25"), ((await ItemAsync(item)).Available, (await ItemAsync(item)).SoldOutOn));
        }
        finally
        {
            await db.SetBusinessDateAsync(PosDb.Day);
        }
    }

    [Fact]
    public async Task Gst_class_one_class_per_check_a_banquet_hall_item_bills_at_18_percent()
    {
        var s = await SvcAsync();
        await Fails(HccErrorCode.FORBIDDEN, () => NewItemAsync(s, db.Restaurant, 500_000, "banquet", who: TechAdmin));
        var hall = await NewItemAsync(s, db.Restaurant, 500_000, "banquet");
        var o = await db.OpenTableAsync("T34");
        await db.AddAsync(o.OrderId, "DAL");
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.AddItemAsync(db.As(Who.Cashier), K(), new AddCheckItemRequest(o.OrderId, hall, 1, null)));
        var h = await SentCheckAsync(db.Restaurant, "T39", hall);
        Assert.Equal("banquet", (await db.Queries.GetCheckAsync(db.As(Who.Owner), h)).Lines[0].GstClass);
        var bill = (await db.SettleOutletAsync(h, "Card")).Value;
        Assert.Equal((1800, 90_000L), (bill.TaxBps, bill.TaxPaise));
    }

    [Fact]
    public async Task Kot_by_station_one_ticket_per_station_with_its_label_and_kitchen_station()
    {
        var s = await SvcAsync();
        var outlet = await NewOutletAsync(s);
        await NewTableAsync(s, outlet, "K1");
        var curry = await NewItemAsync(s, outlet, 30_000);
        var naan = await NewItemAsync(s, outlet, 7_000, station: "Tandoor");
        var id = await SentCheckAsync(outlet, "K1", curry, naan);
        var check = await db.Queries.GetCheckAsync(db.As(Who.Owner), id);
        Assert.Equal([("Kitchen", 1), ("Tandoor", 2)], check.Kots.Select(k => (k.Station!, k.KotNo)));
        Assert.EndsWith("-KOT 2 · Tandoor", check.Kots[1].Label, StringComparison.Ordinal);
        Assert.Equal(check.Kots[1].KotId, check.Lines.Single(l => l.ItemId == naan).KotId);
        Assert.Contains(await db.Queries.KitchenAsync(db.As(Who.Owner)), k => k.OrderId == id && k.Station == "Tandoor");
        // Without any station (restaurant fixture): one KOT, no station — as before MA-4.
        var plain = await db.SentTableAsync("T29");
        Assert.Null(Assert.Single((await db.Queries.GetCheckAsync(db.As(Who.Owner), plain)).Kots).Station);
    }

    [Fact]
    public async Task Retired_item_leaves_the_pos_used_item_cannot_be_deleted_unused_item_can_and_saves_are_idempotent()
    {
        var s = await SvcAsync();
        var used = await NewItemAsync(s, db.Restaurant, 5_000);
        await SentCheckAsync(db.Restaurant, "T35", used);
        var v = (await ItemAsync(used)).Version;
        var ret = (await s.RetireAsync(db.As(TechAdmin), K(), "RetireMenuItem", PosMasterTable.Item, used, v, new MenuItemRefRequest(used, v))).Value;
        Assert.False((await ItemAsync(used)).Active);
        var o = await db.OpenTableAsync("T36");
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.AddItemAsync(db.As(Who.Cashier), K(), new AddCheckItemRequest(o.OrderId, used, 1, null)));
        Assert.Equal(MasterReasons.InUse, Reason(await Fails(HccErrorCode.INVALID_TRANSITION, () =>
            s.DeleteAsync(db.As(Who.Owner), K(), "DeleteMenuItem", PosMasterTable.Item, used, ret.Version, new MenuItemRefRequest(used, ret.Version!)))));
        var fresh = await NewItemAsync(s, db.Restaurant, 5_000);
        var fv = (await ItemAsync(fresh)).Version;
        await s.DeleteAsync(db.As(Who.Owner), K(), "DeleteMenuItem", PosMasterTable.Item, fresh, fv, new MenuItemRefRequest(fresh, fv));
        Assert.DoesNotContain(await db.Queries.MenuAsync(db.As(Who.Owner), null), x => x.ItemId == fresh);
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM pos.menu_price WHERE item_id = @i", ("@i", SqlDbType.Int, fresh)));
        // Same Idempotency-Key → same result, one row.
        var key = K();
        var req = new SaveMenuCategoryRequest(null, U("IDEM"), U("Idem "));
        var a = (await s.SaveMenuCategoryAsync(db.As(Who.Owner), key, req)).Value;
        var b = (await s.SaveMenuCategoryAsync(db.As(Who.Owner), key, req)).Value;
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM pos.menu_category WHERE code = @c", ("@c", SqlDbType.VarChar, req.Code)));
    }

    [Fact]
    public async Task Reorder_outlets_needs_every_outlet_once_and_writes_sort_orders_10_20()
    {
        var s = await SvcAsync();
        await NewOutletAsync(s);
        var ids = (await db.Queries.OutletsAsync(db.As(Who.Owner))).Select(o => o.OutletId).ToList();
        var order = ids.Skip(1).Append(ids[0]).ToList();
        await s.ReorderAsync(db.As(Who.Owner), K(), "ReorderOutlets", PosMasterTable.Outlet, order, new ReorderOutletsRequest(order));
        var after = await db.Queries.OutletsAsync(db.As(Who.Owner));
        Assert.Equal(order, after.Select(o => o.OutletId));
        Assert.Equal(Enumerable.Range(1, order.Count).Select(i => i * 10), after.Select(o => o.SortOrder));
        await Fails(HccErrorCode.VALIDATION, () => s.ReorderAsync(db.As(Who.Owner), K(), "ReorderOutlets", PosMasterTable.Outlet, order, new ReorderOutletsRequest(order)));
        await Fails(HccErrorCode.VALIDATION, () => s.ReorderAsync(db.As(Who.Owner), K(), "ReorderOutlets", PosMasterTable.Outlet, order.Take(1).ToList(), new ReorderOutletsRequest(order)));
    }
}
