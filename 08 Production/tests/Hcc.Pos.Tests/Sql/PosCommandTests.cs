using System.Data;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Pos;
using Hcc.Domain.Errors;
using Hcc.Pos.Tests.Harness;
using static Hcc.Pos.Tests.Harness.Assertions;

namespace Hcc.Pos.Tests.Sql;

/// <summary>POS commands against SQL Server: KOT flow, settlement into the shared drawer and the folio, voids with approval, day summary, events.</summary>
public class PosCommandTests(PosDb db) : IClassFixture<PosDb>
{
    [Fact]
    public async Task Open_add_merge_qty_kot_and_kitchen_flow()
    {
        var o = await db.OpenTableAsync("T10", Who.Reception);
        await Fails(HccErrorCode.NOT_AVAILABLE, () => db.OpenTableAsync("T10", Who.Reception));
        await Fails(HccErrorCode.VALIDATION, () => db.SendAsync(o.OrderId));
        await db.AddAsync(o.OrderId, "PANEER");
        await db.AddAsync(o.OrderId, "PANEER", 2);
        var afterNaan = await db.AddAsync(o.OrderId, "NAAN", 3);
        var check = await db.Queries.GetCheckAsync(db.As(Who.Owner), o.OrderId);
        Assert.Equal(2, check.Lines.Count);
        Assert.Equal(3, check.Lines[0].Qty);
        Assert.Equal(afterNaan.TotalPaise, check.TotalPaise);
        var naan = check.Lines[1].LineId;
        await db.Pos.SetItemQtyAsync(db.As(Who.Reception), PosDb.Key(), new SetCheckItemQtyRequest(o.OrderId, naan, 0, null));
        Assert.Single((await db.Queries.GetCheckAsync(db.As(Who.Owner), o.OrderId)).Lines);
        await Fails(HccErrorCode.NOT_AVAILABLE, () => db.AddAsync(o.OrderId, "BIRYANI"));            // 86'd in the fixture
        await Fails(HccErrorCode.NOT_FOUND, () => db.Pos.AddItemAsync(db.As(Who.Cashier), PosDb.Key(), new AddCheckItemRequest(o.OrderId, 9999, 1, null)));

        var kot = await db.SendAsync(o.OrderId);
        Assert.Equal("Preparing", kot.Status);
        var line = (await db.Queries.GetCheckAsync(db.As(Who.Owner), o.OrderId)).Lines[0];
        Assert.Equal("Preparing", line.Status);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.SetItemQtyAsync(db.As(Who.Reception), PosDb.Key(), new SetCheckItemQtyRequest(o.OrderId, line.LineId, 1, null)));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.AdvanceKotAsync(db.As(Who.Reception), PosDb.Key(), new AdvanceKotRequest(o.OrderId, kot.KotId, "Served")));
        await db.Pos.AdvanceKotAsync(db.As(Who.Reception), PosDb.Key(), new AdvanceKotRequest(o.OrderId, kot.KotId, "Ready"));
        Assert.Contains((await db.Queries.KitchenAsync(db.As(Who.Owner))), k => k.KotId == kot.KotId && k.Status == "Ready");
        await db.Pos.AdvanceKotAsync(db.As(Who.Reception), PosDb.Key(), new AdvanceKotRequest(o.OrderId, kot.KotId, "Served"));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.AdvanceKotAsync(db.As(Who.Reception), PosDb.Key(), new AdvanceKotRequest(o.OrderId, kot.KotId, "Served")));
        // KOT numbers continue per outlet and business date.
        var second = await db.OpenTableAsync("T11");
        await db.AddAsync(second.OrderId, "TEA");
        Assert.Equal(kot.KotNo + 1, (await db.SendAsync(second.OrderId)).KotNo);
    }

    [Fact]
    public async Task Outlet_settlement_rules_idempotency_and_bill_snapshot()
    {
        var (till, shift) = await db.OpenTillAsync(500_000);
        var id = await db.SentTableAsync("T12");
        var version = await db.VersionAsync(id);
        await Fails(HccErrorCode.VALIDATION, () => db.Pos.SettleCheckAsync(db.As(Who.Cashier), PosDb.Key(), new SettleCheckRequest(id, "outlet", "UPI", null, null, null, version)));
        await Fails(HccErrorCode.VALIDATION, () => db.Pos.SettleCheckAsync(db.As(Who.Cashier), PosDb.Key(), new SettleCheckRequest(id, "outlet", "Cheque", "x", null, null, version)));
        await Fails(HccErrorCode.VALIDATION, () => db.Pos.SettleCheckAsync(db.As(Who.Cashier), PosDb.Key(), new SettleCheckRequest(id, "outlet", "Cash", null, null, null, version)));
        var key = PosDb.Key();
        var before = await db.DrawerSumAsync(shift.ShiftId);
        var first = await db.Pos.SettleCheckAsync(db.As(Who.Cashier), key, new SettleCheckRequest(id, "outlet", "Cash", null, till, null, version));
        Assert.Equal((36_000L, 1_800L, 37_800L, 500), (first.Value.NetPaise, first.Value.TaxPaise, first.Value.TotalPaise, first.Value.TaxBps));
        Assert.StartsWith("POS/2610/", first.Value.BillNo, StringComparison.Ordinal);
        Assert.Equal(before + 37_800, await db.DrawerSumAsync(shift.ShiftId));
        var replay = await db.Pos.SettleCheckAsync(db.As(Who.Cashier), key, new SettleCheckRequest(id, "outlet", "Cash", null, till, null, version));
        Assert.True(replay.Replayed);
        Assert.Equal(first.Value.BillId, replay.Value.BillId);
        Assert.Equal(before + 37_800, await db.DrawerSumAsync(shift.ShiftId));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.SettleCheckAsync(db.As(Who.Cashier), PosDb.Key(), new SettleCheckRequest(id, "outlet", "Cash", null, till, null, version)));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.AddAsync(id, "TEA"));
        // the snapshot is stored with the bill and the audit/outbox rows exist
        Assert.Equal(500, await db.CountAsync("SELECT tax_bps FROM pos.bill WHERE bill_id = @b", ("@b", SqlDbType.BigInt, first.Value.BillId)));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'PosBillSettled' AND aggregate_id = @b", ("@b", SqlDbType.BigInt, first.Value.BillId)));
        Assert.True(await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'POS_BILL_SETTLED'") >= 1);
    }

    [Fact]
    public async Task Cash_needs_an_open_shift_and_unsent_or_empty_checks_cannot_settle()
    {
        var (till, _) = await db.OpenTillAsync(100_000);
        var closed = await db.OpenTillAsync(0);
        // a till whose shift is submitted/closed cannot take cash: submit an exact count on the second till
        var id = await db.SentTableAsync("T13");
        await db.Cashier.SubmitShiftCountAsync(new Hcc.Application.Billing.Caller(db.PropertyId, Who.Cashier), PosDb.Key(),
            new SubmitShiftCountRequest(closed.Shift.ShiftId, 0, null, null, null, closed.Shift.Version));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.SettleOutletAsync(id, "Cash", closed.TillId));
        var unsent = await db.OpenTableAsync("T14");
        await db.AddAsync(unsent.OrderId, "TEA");
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.SettleOutletAsync(unsent.OrderId, "UPI"));
        var empty = await db.OpenTableAsync("T15");
        await Fails(HccErrorCode.VALIDATION, () => db.SettleOutletAsync(empty.OrderId, "UPI"));
        await db.SettleOutletAsync(id, "Cash", till);
    }

    [Fact]
    public async Task Stale_check_version_is_refused()
    {
        var id = await db.SentTableAsync("T16");
        var old = await db.VersionAsync(id);
        await db.AddAsync(id, "TEA");
        await Fails(HccErrorCode.STALE_VERSION, () => db.Pos.SettleCheckAsync(db.As(Who.Cashier), PosDb.Key(), new SettleCheckRequest(id, "outlet", "UPI", "R1", null, null, old)));
    }

    [Fact]
    public async Task Room_posting_adds_one_folio_service_line_with_the_tax_snapshot_once()
    {
        var (stay, folio, _) = await db.NewInHouseAsync("301");
        var o = (await db.Pos.OpenCheckAsync(db.As(Who.Reception), PosDb.Key(), new OpenCheckRequest(db.RoomService, null, "301", 1, null))).Value;
        await db.AddAsync(o.OrderId, "PARATHA", 1, Who.Reception);
        await db.AddAsync(o.OrderId, "TEA", 1, Who.Reception);
        await db.AddAsync(o.OrderId, "LASSI", 1, Who.Reception);
        await db.SendAsync(o.OrderId, Who.Reception);
        var before = await db.FolioQueries.GetFolioAsync(new Hcc.Application.Billing.Caller(db.PropertyId, Who.Owner), folio);
        var key = PosDb.Key();
        var ver = await db.VersionAsync(o.OrderId);
        var s = (await db.Pos.SettleCheckAsync(db.As(Who.Reception), key, new SettleCheckRequest(o.OrderId, "folio", null, null, null, null, ver))).Value;
        Assert.Equal((44_000L, 2_200L, 46_200L), (s.NetPaise, s.TaxPaise, s.TotalPaise));
        Assert.NotNull(s.ChargeId);
        var after = await db.FolioQueries.GetFolioAsync(new Hcc.Application.Billing.Caller(db.PropertyId, Who.Owner), folio);
        Assert.Equal(before.Totals.TotalPaise + 46_200, after.Totals.TotalPaise);
        Assert.NotEqual(before.Version, after.Version);
        var line = after.Lines.Single(l => l.ChargeId == s.ChargeId);
        Assert.Equal(("service", 44_000L, 500, 2_200L), (line.Kind, line.AmountPaise, line.TaxBps, line.TaxPaise));
        Assert.Contains("Room service", line.Label, StringComparison.Ordinal);
        Assert.Contains(s.BillNo, line.Label, StringComparison.Ordinal);
        // no outlet receipt, no drawer movement for a room charge
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM pos.receipt WHERE order_id = @o", ("@o", SqlDbType.BigInt, o.OrderId)));
        // replay with the same key is the same bill; a second settle is refused and the folio has one line
        var replay = await db.Pos.SettleCheckAsync(db.As(Who.Reception), key, new SettleCheckRequest(o.OrderId, "folio", null, null, null, null, ver));
        Assert.True(replay.Replayed);
        Assert.Equal(s.BillId, replay.Value.BillId);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.SettleFolioAsync(o.OrderId, null, Who.Reception));
        Assert.Single((await db.FolioQueries.GetFolioAsync(new Hcc.Application.Billing.Caller(db.PropertyId, Who.Owner), folio)).Lines, l => l.Kind == "service");
        // a bill posted to a folio is corrected on the folio, not voided here
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.VoidBillAsync(db.As(Who.Reception), PosDb.Key(), new VoidBillRequest(o.OrderId, "Wrong room", null)));
        Assert.Equal(stay, await db.LongAsync("SELECT stay_id FROM pos.bill WHERE bill_id = @b", ("@b", SqlDbType.BigInt, s.BillId)));
        // DEC-069 wiring: the folio line has its per-charge GST record (food, CGST/SGST), the bill stores the split, and Accounting gets ONE
        // event to book it from (ChargePosted for the folio line; PosBillSettled carries mode = folio and is skipped there).
        var c = ("@c", SqlDbType.BigInt, (object)s.ChargeId!.Value);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM tax.charge_gst WHERE charge_id = @c AND category = 'food' AND tax_bps = 500 AND cgst_paise = 1100 AND sgst_paise = 1100", c));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM pos.bill WHERE bill_id = @b AND cgst_paise = 1100 AND sgst_paise = 1100", ("@b", SqlDbType.BigInt, s.BillId)));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ChargePosted' AND JSON_VALUE(payload, '$.chargeId') = CAST(@c AS varchar(20))", c));
    }

    [Fact]
    public async Task Folio_posting_rules_in_house_only_and_a_restaurant_check_can_be_charged_to_a_room()
    {
        var id = await db.SentTableAsync("T17");
        var departed = await db.NewInHouseAsync("302", inHouse: false);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.SettleFolioAsync(id, departed.StayId));
        await Fails(HccErrorCode.NOT_FOUND, () => db.SettleFolioAsync(id));                // no guest chosen
        await Fails(HccErrorCode.NOT_FOUND, () => db.SettleFolioAsync(id, 99999));
        var live = await db.NewInHouseAsync("303");
        var s = (await db.SettleFolioAsync(id, live.StayId)).Value;
        Assert.Equal(36_000L, s.NetPaise);
        Assert.Equal(live.StayId, await db.LongAsync("SELECT stay_id FROM pos.check_order WHERE order_id = @o", ("@o", SqlDbType.BigInt, id)));
    }

    [Fact]
    public async Task Room_service_is_for_in_house_rooms_only_one_open_check_per_room_and_menu_per_outlet()
    {
        await db.NewInHouseAsync("304");
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.OpenCheckAsync(db.As(Who.Reception), PosDb.Key(), new OpenCheckRequest(db.RoomService, null, "999", 1, null)));
        await db.NewInHouseAsync("305", inHouse: false);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.OpenCheckAsync(db.As(Who.Reception), PosDb.Key(), new OpenCheckRequest(db.RoomService, null, "305", 1, null)));
        var o = (await db.Pos.OpenCheckAsync(db.As(Who.Reception), PosDb.Key(), new OpenCheckRequest(db.RoomService, null, "304", 1, null))).Value;
        await Fails(HccErrorCode.NOT_AVAILABLE, () => db.Pos.OpenCheckAsync(db.As(Who.Reception), PosDb.Key(), new OpenCheckRequest(db.RoomService, null, "304", 1, null)));
        await Fails(HccErrorCode.VALIDATION, () => db.AddAsync(o.OrderId, "KEBAB"));        // restaurant only
        var view = await db.Queries.GetCheckAsync(db.As(Who.Owner), o.OrderId);
        Assert.Equal("Room 304", view.Spot);
        Assert.Equal("Fictional guest", view.Guest);
    }

    [Fact]
    public async Task Void_item_within_the_limit_is_immediate_unsent_cannot_be_voided_reason_required()
    {
        var id = await db.SentTableAsync("T18");
        var c = await db.Queries.GetCheckAsync(db.As(Who.Owner), id);
        var dal = c.Lines[0].LineId;
        await Fails(HccErrorCode.VALIDATION, () => db.Pos.VoidItemAsync(db.As(Who.Cashier), PosDb.Key(), new VoidCheckItemRequest(id, dal, "  ", null)));
        var r = (await db.Pos.VoidItemAsync(db.As(Who.Cashier), PosDb.Key(), new VoidCheckItemRequest(id, dal, "Guest changed order", null))).Value;
        Assert.Equal("Voided", r.Outcome);
        var after = await db.Queries.GetCheckAsync(db.As(Who.Owner), id);
        Assert.Equal(8_000L, after.NetPaise);                                                // only 2 x roti left
        Assert.True(after.Lines[0].Voided);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.VoidItemAsync(db.As(Who.Cashier), PosDb.Key(), new VoidCheckItemRequest(id, dal, "again", null)));
        await db.AddAsync(id, "TEA");
        var unsent = (await db.Queries.GetCheckAsync(db.As(Who.Owner), id)).Lines.Last();
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.VoidItemAsync(db.As(Who.Cashier), PosDb.Key(), new VoidCheckItemRequest(id, unsent.LineId, "not sent yet", null)));
    }

    [Fact]
    public async Task Void_above_the_limit_needs_a_second_person_within_their_own_limit()
    {
        var id = await db.SentTableAsync("T19", ("THALI", 4));                                // Rs 1,680 line
        var line = (await db.Queries.GetCheckAsync(db.As(Who.Owner), id)).Lines[0].LineId;
        var req = (await db.Pos.VoidItemAsync(db.As(Who.Cashier), PosDb.Key(), new VoidCheckItemRequest(id, line, "Kitchen error", null))).Value;
        Assert.Equal("PendingApproval", req.Outcome);
        var voidId = req.VoidRequestId!.Value;
        // the check is locked while the request is pending, and it blocks the day close
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.AddAsync(id, "TEA"));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.VoidBillAsync(db.As(Who.Cashier), PosDb.Key(), new VoidBillRequest(id, "also void", null)));
        Assert.Equal(1, (await db.Queries.BlockersAsync(db.As(Who.Owner))).PendingVoids);
        // no self-approval, no approval right for Reception, Manager is capped at Rs 1,000 (DEC-063 G1)
        await Fails(HccErrorCode.FORBIDDEN, () => db.Pos.DecideVoidAsync(db.As(Who.Cashier), PosDb.Key(), new DecideVoidRequest(voidId, true, null)));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Pos.DecideVoidAsync(db.As(Who.Reception), PosDb.Key(), new DecideVoidRequest(voidId, true, null)));
        await Fails(HccErrorCode.APPROVAL_REQUIRED, () => db.Pos.DecideVoidAsync(db.As(Who.Manager), PosDb.Key(), new DecideVoidRequest(voidId, true, null)));
        var done = (await db.Pos.DecideVoidAsync(db.As(Who.Owner), PosDb.Key(), new DecideVoidRequest(voidId, true, "ok"))).Value;
        Assert.Equal("Executed", done.Status);
        var after = await db.Queries.GetCheckAsync(db.As(Who.Owner), id);
        Assert.Null(after.PendingVoidId);
        Assert.True(after.Lines[0].Voided);
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.DecideVoidAsync(db.As(Who.Owner), PosDb.Key(), new DecideVoidRequest(voidId, true, null)));
        Assert.Equal(0, (await db.Queries.BlockersAsync(db.As(Who.Owner))).PendingVoids);
    }

    [Fact]
    public async Task Rejected_and_expired_void_requests()
    {
        var id = await db.SentTableAsync("T20", ("THALI", 2));                                // Rs 840: above Rs 500, Manager may decide
        var line = (await db.Queries.GetCheckAsync(db.As(Who.Owner), id)).Lines[0].LineId;
        var req = (await db.Pos.VoidItemAsync(db.As(Who.Cashier), PosDb.Key(), new VoidCheckItemRequest(id, line, "Wrong table", null))).Value;
        var rej = (await db.Pos.DecideVoidAsync(db.As(Who.Manager), PosDb.Key(), new DecideVoidRequest(req.VoidRequestId!.Value, false, "Keep it"))).Value;
        Assert.Equal("Rejected", rej.Status);
        Assert.False((await db.Queries.GetCheckAsync(db.As(Who.Owner), id)).Lines[0].Voided);
        await db.AddAsync(id, "TEA");                                                         // unlocked again
        var req2 = (await db.Pos.VoidItemAsync(db.As(Who.Cashier), PosDb.Key(), new VoidCheckItemRequest(id, line, "Again", null))).Value;
        db.Clock.Now = db.Clock.Now.AddHours(25);
        try
        {
            await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.DecideVoidAsync(db.As(Who.Manager), PosDb.Key(), new DecideVoidRequest(req2.VoidRequestId!.Value, true, null)));
            var rej2 = (await db.Pos.DecideVoidAsync(db.As(Who.Manager), PosDb.Key(), new DecideVoidRequest(req2.VoidRequestId!.Value, false, "Expired"))).Value;
            Assert.Equal("Rejected", rej2.Status);
        }
        finally
        {
            db.Clock.Now = db.Clock.Now.AddHours(-25);
        }
    }

    [Fact]
    public async Task Voiding_a_settled_cash_bill_reverses_the_drawer_and_the_outlet_receipts_and_publishes_the_event()
    {
        var (till, shift) = await db.OpenTillAsync(200_000);
        var id = await db.SentTableAsync("T21");
        var s = (await db.SettleOutletAsync(id, "Cash", till)).Value;
        Assert.Equal(37_800L, await db.DrawerSumAsync(shift.ShiftId));
        var r = (await db.Pos.VoidBillAsync(db.As(Who.Cashier), PosDb.Key(), new VoidBillRequest(id, "Customer disputed", null))).Value;
        Assert.Equal("Voided", r.Outcome);
        Assert.Equal(0L, await db.DrawerSumAsync(shift.ShiftId));
        Assert.Equal(-37_800L, await db.DrawerSumAsync(shift.ShiftId, "pos-void"));
        Assert.Equal(0L, await db.LongAsync("SELECT ISNULL(SUM(amount_paise), 0) FROM pos.receipt WHERE order_id = @o", ("@o", SqlDbType.BigInt, id)));
        Assert.Equal("Void", (await db.Queries.GetCheckAsync(db.As(Who.Owner), id)).Status);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'PosBillVoided' AND aggregate_id = @b", ("@b", SqlDbType.BigInt, s.BillId)));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.VoidBillAsync(db.As(Who.Cashier), PosDb.Key(), new VoidBillRequest(id, "twice", null)));
    }

    [Fact]
    public async Task A_cash_bill_cannot_be_reversed_once_its_shift_is_closed_and_old_bills_cannot_be_voided()
    {
        var (till, shift) = await db.OpenTillAsync(0);
        var id = await db.SentTableAsync("T22");
        await db.SettleOutletAsync(id, "Cash", till);
        await db.Cashier.SubmitShiftCountAsync(new Hcc.Application.Billing.Caller(db.PropertyId, Who.Cashier), PosDb.Key(),
            new SubmitShiftCountRequest(shift.ShiftId, 37_800, null, null, null, shift.Version));
        await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.VoidBillAsync(db.As(Who.Cashier), PosDb.Key(), new VoidBillRequest(id, "too late", null)));
        // a UPI bill of an earlier business date is not voidable either
        var upi = await db.SentTableAsync("T23");
        await db.SettleOutletAsync(upi, "UPI");
        await db.SetBusinessDateAsync(PosDb.Day.AddDays(1));
        try
        {
            await Fails(HccErrorCode.INVALID_TRANSITION, () => db.Pos.VoidBillAsync(db.As(Who.Cashier), PosDb.Key(), new VoidBillRequest(upi, "next day", null)));
        }
        finally
        {
            await db.SetBusinessDateAsync(PosDb.Day);
        }
    }

    [Fact]
    public async Task Voiding_an_open_check_voids_its_lines_and_frees_the_table()
    {
        var id = await db.SentTableAsync("T24", ("TEA", 1));
        var r = (await db.Pos.VoidBillAsync(db.As(Who.Cashier), PosDb.Key(), new VoidBillRequest(id, "Walked out", null))).Value;
        Assert.Equal("Voided", r.Outcome);
        Assert.Equal(0, (await db.Queries.GetCheckAsync(db.As(Who.Owner), id)).Items);
        await db.OpenTableAsync("T24");                                                        // table is free again
    }

    [Fact]
    public async Task Permissions_housekeeper_has_no_pos_access_and_only_the_owner_sets_up_the_menu()
    {
        await Fails(HccErrorCode.FORBIDDEN, () => db.Pos.OpenCheckAsync(db.As(Who.Housekeeper), PosDb.Key(), new OpenCheckRequest(db.Restaurant, "T1", null, 1, null)));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Queries.OpenChecksAsync(db.As(Who.Housekeeper)));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Pos.SaveMenuItemAsync(db.As(Who.Manager), PosDb.Key(), new SaveMenuItemRequest(null, "X1", "Test", "Misc", 1000, true, [db.Restaurant], null)));
        await Fails(HccErrorCode.FORBIDDEN, () => db.Pos.CreateOutletAsync(db.As(Who.Cashier), PosDb.Key(), new CreateOutletRequest("BAR", "Bar", "table", ["B1"])));
    }

    [Fact]
    public async Task Menu_setup_update_needs_the_current_version_and_86_toggle_works()
    {
        var created = (await db.Pos.SaveMenuItemAsync(db.As(Who.Owner), PosDb.Key(), new SaveMenuItemRequest(null, "SPECIAL", "Chef special", "Mains", 55_000, true, [db.Restaurant], null))).Value;
        await Fails(HccErrorCode.VALIDATION, () => db.Pos.SaveMenuItemAsync(db.As(Who.Owner), PosDb.Key(), new SaveMenuItemRequest(null, "SPECIAL", "Dup", "Mains", 100, true, [db.Restaurant], null)));
        var updated = (await db.Pos.SaveMenuItemAsync(db.As(Who.Owner), PosDb.Key(), new SaveMenuItemRequest(created.ItemId, "SPECIAL", "Chef special", "Mains", 60_000, true, [db.Restaurant, db.RoomService], created.Version))).Value;
        await Fails(HccErrorCode.STALE_VERSION, () => db.Pos.SaveMenuItemAsync(db.As(Who.Owner), PosDb.Key(), new SaveMenuItemRequest(created.ItemId, "SPECIAL", "Chef special", "Mains", 70_000, true, [db.Restaurant], created.Version)));
        var menu = await db.Queries.MenuAsync(db.As(Who.Reception), db.RoomService);
        Assert.Contains(menu, m => m.ItemId == created.ItemId && m.PricePaise == 60_000);
        await db.Pos.SetMenuItemAvailabilityAsync(db.As(Who.Reception), PosDb.Key(), new SetMenuItemAvailabilityRequest(created.ItemId, false));
        var o = await db.OpenTableAsync("T25");
        await Fails(HccErrorCode.NOT_AVAILABLE, () => db.Pos.AddItemAsync(db.As(Who.Cashier), PosDb.Key(), new AddCheckItemRequest(o.OrderId, created.ItemId, 1, null)));
        Assert.NotEqual(created.Version, updated.Version);
    }

    [Fact]
    public async Task Two_settles_of_the_same_check_race_to_exactly_one_bill()
    {
        var (till, _) = await db.OpenTillAsync(0);
        var id = await db.SentTableAsync("T26");
        var version = await db.VersionAsync(id);
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            try
            {
                await db.Pos.SettleCheckAsync(db.As(Who.Cashier), PosDb.Key(), new SettleCheckRequest(id, "outlet", "Cash", null, till, null, version));
                return true;
            }
            catch (HccException ex) when (ex.Code is HccErrorCode.INVALID_TRANSITION or HccErrorCode.STALE_VERSION)
            {
                return false;
            }
        })).ToArray();
        var wins = (await Task.WhenAll(tasks)).Count(x => x);
        Assert.Equal(1, wins);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM pos.bill WHERE order_id = @o", ("@o", SqlDbType.BigInt, id)));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM pay.cash_movement WHERE source_type = 'pos-bill' AND source_id = @o", ("@o", SqlDbType.BigInt, id)));
    }

    [Fact]
    public async Task Two_checks_cannot_be_opened_on_the_same_table_at_once()
    {
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            try
            {
                await db.Pos.OpenCheckAsync(db.As(Who.Cashier), PosDb.Key(), new OpenCheckRequest(db.Restaurant, "T27", null, 2, null));
                return true;
            }
            catch (HccException ex) when (ex.Code == HccErrorCode.NOT_AVAILABLE)
            {
                return false;
            }
        })).ToArray();
        Assert.Equal(1, (await Task.WhenAll(tasks)).Count(x => x));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM pos.check_order WHERE status = 'Open' AND table_no = 'T27'"));
    }
}
