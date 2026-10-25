using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Stock;
using Hcc.Stock.Tests.Harness;

namespace Hcc.Stock.Tests.Sql;

/// <summary>The prototype's stream-inventory tests ported to SQL (figures from the BASE fixture: 17 items, 24 rooms, 23 Oct 2026).</summary>
[Trait("Category", "Sql")]
public class StockSqlTests : IAsyncLifetime
{
    private StockWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await StockWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private Task<CommandResult<StockMoveResult>> Issue(int who, string key, string item, int qty, string from, string to) =>
        _w.Commands.IssueAsync(_w.Ctx(who, key), new IssueStockRequest(item, qty, from, to), Ct);

    private async Task<int> Count(string sql) => await _w.Db.CountAsync(sql, Ct);

    [Fact]
    public async Task Fixture_seeds_17_items_three_alerts_and_the_prototype_par_figures()
    {
        var page = await _w.Queries.ItemsAsync(_w.PropertyId, Users.Manager, 1, 100, ct: Ct);
        Assert.Equal((17, 3, 3), (page.Total, page.AlertCount, page.ParSets));
        var alerts = await _w.Queries.AlertsAsync(_w.PropertyId, Users.Supervisor, Ct);
        Assert.Equal(["AMN-SH", "CLN-GB", "LIN-BT"], alerts.Select(a => a.Code).ToList());
        Assert.Equal(65, alerts.Single(a => a.Code == "LIN-BT").Suggested);
        var bs = page.Rows.Single(r => r.Code == "LIN-BS");
        Assert.Equal((144, 48, 140), (bs.ParRequired, bs.ParPerSet, bs.Total)); // 24 rooms × 2 × 3 sets; 40 + 18 + 48 + 10 + 24 pieces owned
        Assert.Equal(97, bs.ParCoveragePercent); // 140 / 144
        Assert.Equal(140, page.PiecesAtLaundry);
    }

    [Fact]
    public async Task Money_is_null_for_supervisors_and_visible_to_managers()
    {
        var sup = await _w.Queries.ItemsAsync(_w.PropertyId, Users.Supervisor, 1, 100, ct: Ct);
        Assert.All(sup.Rows, r => Assert.Null(r.UnitCostPaise));
        Assert.All(sup.Rows, r => Assert.Null(r.ValuePaise));
        Assert.Null(sup.TotalValuePaise);
        var mgr = await _w.Queries.ItemsAsync(_w.PropertyId, Users.Manager, 1, 100, ct: Ct);
        Assert.Equal(65000, mgr.Rows.Single(r => r.Code == "LIN-BS").UnitCostPaise);
        Assert.Equal(1800, mgr.Rows.Single(r => r.Code == "LIN-BS").LaundryRatePaise);
        Assert.True(mgr.TotalValuePaise > 0);
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Queries.ItemsAsync(_w.PropertyId, Users.Reception, ct: Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Queries.ItemsAsync(_w.PropertyId, Users.Maint, ct: Ct)));
        Assert.NotNull(await _w.Queries.ItemsAsync(_w.PropertyId, Users.Accountant, ct: Ct)); // finance sees stock value
    }

    [Fact]
    public async Task Issue_store_to_floor_moves_stock_and_over_issue_changes_nothing()
    {
        await Issue(Users.Housekeeper, "i1", "LIN-BS", 10, "store", "floor1");
        Assert.Equal((30, 16), (await _w.StockAsync("LIN-BS", "store"), await _w.StockAsync("LIN-BS", "floor1")));
        var moves0 = await Count("SELECT COUNT(*) FROM stk.move");
        var e = await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => Issue(Users.Housekeeper, "i2", "LIN-BS", 31, "store", "floor1"));
        Assert.Contains("Only 30", e.Message, StringComparison.Ordinal);
        Assert.Equal((30, moves0), (await _w.StockAsync("LIN-BS", "store"), await Count("SELECT COUNT(*) FROM stk.move")));
        Assert.Contains("whole number", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => Issue(Users.Housekeeper, "i3", "LIN-BS", 0, "store", "floor1"))).Message, StringComparison.Ordinal);
        Assert.Contains("different", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => Issue(Users.Housekeeper, "i4", "LIN-BS", 1, "store", "store"))).Message, StringComparison.Ordinal);
        Assert.Contains("valid location", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => Issue(Users.Housekeeper, "i5", "LIN-BS", 1, "store", "rooms"))).Message, StringComparison.Ordinal);
        await Expect.ErrorAsync(HccErrorCode.NOT_FOUND, () => Issue(Users.Housekeeper, "i6", "NOPE", 1, "store", "floor1"));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM stk.move WHERE kind = 'issue'"));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM plat.audit_event WHERE operation = 'StockIssue'"));
    }

    [Fact]
    public async Task Same_idempotency_key_replays_without_a_second_movement()
    {
        var first = await Issue(Users.Housekeeper, "same", "AMN-SP", 5, "store", "floor1");
        var second = await Issue(Users.Housekeeper, "same", "AMN-SP", 5, "store", "floor1");
        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.Value.MoveId, second.Value.MoveId);
        Assert.Equal(255, await _w.StockAsync("AMN-SP", "store"));
        await Expect.ErrorAsync(HccErrorCode.IDEMPOTENCY_MISMATCH, () => Issue(Users.Housekeeper, "same", "AMN-SP", 6, "store", "floor1"));
    }

    [Fact]
    public async Task Permissions_reception_accountant_maintenance_denied_housekeeper_cannot_receive_or_decide_and_cost_needs_money_role()
    {
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => Issue(Users.Reception, "p1", "LIN-BS", 1, "store", "floor1")));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => Issue(Users.Maint, "p2", "LIN-BS", 1, "store", "floor1")));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.UseAsync(_w.Ctx(Users.Accountant, "p3"), new UseStockRequest("AMN-SP", 1, "floor1"), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.ReceiveGrnAsync(_w.Ctx(Users.Housekeeper, "p4"), new ReceiveGrnRequest("Morning Supplies", null, [new("AMN-SH", 10)]), Ct)));
        var e = await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => _w.Commands.ReceiveGrnAsync(_w.Ctx(Users.Supervisor, "p5"), new ReceiveGrnRequest("Morning Supplies", null, [new("AMN-SH", 10, 1500)]), Ct));
        Assert.Contains("unit cost", e.Message, StringComparison.Ordinal);
        var count = await _w.Reads.CountsAsync(_w.PropertyId, "Submitted", Ct);
        var cnt = count.Single();
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.DecideCountAsync(_w.Ctx(Users.Housekeeper2, "p6"), new DecideCountRequest(cnt.CountId, Convert.ToBase64String(cnt.Version), true, "test"), Ct)));
        var spVersion = await _w.VersionAsync("AMN-SP");
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.SetItemAsync(_w.Ctx(Users.Housekeeper, "p7"), new SetStockItemRequest("AMN-SP", spVersion, 1, 1), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.SetSettingsAsync(_w.Ctx(Users.Housekeeper, "p8"), new SetStockSettingsRequest(3, null, 1), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.CreateItemAsync(_w.Ctx(Users.Housekeeper, "p9"), new CreateStockItemRequest("TST-1", "Test", "amenity", "pc", 1, 1), Ct)));
    }

    [Fact]
    public async Task Grn_supervisor_receives_and_clears_the_alert_manager_cost_gives_weighted_average()
    {
        var r = (await _w.Commands.ReceiveGrnAsync(_w.Ctx(Users.Supervisor, "g1"), new ReceiveGrnRequest("Morning Supplies", "MS/1", [new("AMN-SH", 300)]), Ct)).Value;
        Assert.Equal("MS/1", r.Ref);
        Assert.Equal(370, await _w.StockAsync("AMN-SH", "store"));
        Assert.DoesNotContain(await _w.Queries.AlertsAsync(_w.PropertyId, Users.Supervisor, Ct), a => a.Code == "AMN-SH");
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM stk.move WHERE kind = 'grn'"));
        var defaulted = (await _w.Commands.ReceiveGrnAsync(_w.Ctx(Users.Supervisor, "g1b"), new ReceiveGrnRequest("Morning Supplies", null, [new("AMN-SP", 1)]), Ct)).Value;
        Assert.Equal($"GRN-{defaulted.GrnId}", defaulted.Ref);

        Assert.Equal(6, await _w.TotalAsync("CLN-GB"));
        await _w.Commands.ReceiveGrnAsync(_w.Ctx(Users.Manager, "g2"), new ReceiveGrnRequest("Deccan Hygiene Traders", null, [new("CLN-GB", 6, 8000)]), Ct);
        Assert.Equal(7000, (await _w.ItemAsync("CLN-GB")).CostPaise);
        Assert.Equal(8000, await _w.Db.CountAsync("SELECT l.cost_paise FROM stk.grn_line l JOIN stk.item i ON i.item_id = l.item_id WHERE i.code = 'CLN-GB'", Ct));

        Assert.Contains("supplier", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.ReceiveGrnAsync(_w.Ctx(Users.Manager, "g3"), new ReceiveGrnRequest("X", null, [new("CLN-GB", 1)]), Ct))).Message, StringComparison.Ordinal);
        Assert.Contains("only once", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.ReceiveGrnAsync(_w.Ctx(Users.Manager, "g4"), new ReceiveGrnRequest("Deccan", null, [new("CLN-GB", 1), new("CLN-GB", 2)]), Ct))).Message, StringComparison.Ordinal);
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.ReceiveGrnAsync(_w.Ctx(Users.Manager, "g5"), new ReceiveGrnRequest("Deccan", null, [new("CLN-GB", 1, 0)]), Ct));
        Assert.Equal(12, await _w.TotalAsync("CLN-GB")); // 6 + 6; the failed receipts changed nothing
    }

    [Fact]
    public async Task Use_is_for_supplies_from_a_floor_and_a_linen_change_sends_used_pieces_to_soiled()
    {
        await _w.Commands.UseAsync(_w.Ctx(Users.Housekeeper, "u1"), new UseStockRequest("AMN-SP", 4, "floor2", "204"), Ct);
        Assert.Equal(24, await _w.StockAsync("AMN-SP", "floor2"));
        Assert.Contains("linen change", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.UseAsync(_w.Ctx(Users.Housekeeper, "u2"), new UseStockRequest("LIN-BS", 1, "floor1"), Ct))).Message, StringComparison.Ordinal);
        Assert.Contains("Only linen", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.RoomChangeAsync(_w.Ctx(Users.Housekeeper, "u3"), new RoomChangeRequest("AMN-SP", 1, "floor1"), Ct))).Message, StringComparison.Ordinal);
        var rooms0 = await _w.StockAsync("LIN-BT", "rooms");
        var total0 = await _w.TotalAsync("LIN-BT");
        await _w.Commands.RoomChangeAsync(_w.Ctx(Users.Housekeeper2, "u4"), new RoomChangeRequest("LIN-BT", 4, "floor3", "206"), Ct);
        Assert.Equal((2, 18, rooms0), (await _w.StockAsync("LIN-BT", "floor3"), await _w.StockAsync("LIN-BT", "soiled"), await _w.StockAsync("LIN-BT", "rooms")));
        Assert.Equal(total0, await _w.TotalAsync("LIN-BT"));
    }

    [Fact]
    public async Task Laundry_send_then_receive_short_puts_clean_in_store_damaged_and_missing_in_the_loss_register_and_computes_the_charge()
    {
        var sent = (await _w.Commands.SendLaundryAsync(_w.Ctx(Users.Housekeeper, "l1"), new SendLaundryRequest([new("LIN-BS", 10), new("LIN-BT", 14)]), Ct)).Value;
        Assert.Equal((24, "Fresh Linen Services"), (sent.Pieces, sent.Vendor));
        Assert.Equal((0, 34), (await _w.StockAsync("LIN-BS", "soiled"), await _w.StockAsync("LIN-BS", "laundry")));
        Assert.Contains("Only 0", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.SendLaundryAsync(_w.Ctx(Users.Housekeeper, "l2"), new SendLaundryRequest([new("LIN-BS", 1)]), Ct))).Message, StringComparison.Ordinal);
        Assert.Contains("Only linen", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.SendLaundryAsync(_w.Ctx(Users.Housekeeper, "l3"), new SendLaundryRequest([new("AMN-SP", 1)]), Ct))).Message, StringComparison.Ordinal);
        var v = sent.ExpectedVersion;
        Assert.Contains("cannot exceed", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => Receive(Users.Housekeeper, "l4", sent.BatchId, v, ("LIN-BS", 10, 1), ("LIN-BT", 14, 0)))).Message, StringComparison.Ordinal);
        Assert.Contains("every item", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => Receive(Users.Housekeeper, "l5", sent.BatchId, v, ("LIN-BS", 10, 0)))).Message, StringComparison.Ordinal);
        await Expect.ErrorAsync(HccErrorCode.STALE_VERSION, () => Receive(Users.Supervisor, "l5b", sent.BatchId, "AAAAAAAAAAA=", ("LIN-BS", 8, 1), ("LIN-BT", 14, 0)));

        var store0 = await _w.StockAsync("LIN-BS", "store");
        var losses0 = await Count("SELECT COUNT(*) FROM stk.loss");
        var done = (await Receive(Users.Supervisor, "lb-recv", sent.BatchId, v, ("LIN-BS", 8, 1), ("LIN-BT", 14, 0))).Value;
        Assert.Equal(("Returned short", 22, 2), (done.Status, done.CleanPieces, done.LossLines));
        Assert.Null(done.ChargePaise); // supervisor holds no money role
        Assert.Equal((store0 + 8, 24), (await _w.StockAsync("LIN-BS", "store"), await _w.StockAsync("LIN-BS", "laundry")));
        Assert.Equal(losses0 + 2, await Count("SELECT COUNT(*) FROM stk.loss"));
        Assert.Equal(9 * 1800 + 14 * 1400, await Count($"SELECT charge_paise FROM stk.laundry_batch WHERE batch_id = {sent.BatchId}"));
        Assert.Equal(65000, await Count("SELECT value_paise FROM stk.loss WHERE reason = 'Damaged at laundry'")); // 1 × ₹650
        Assert.Equal(65000, await Count("SELECT value_paise FROM stk.loss WHERE reason LIKE 'Missing from laundry%'"));
        Assert.True((await Receive(Users.Supervisor, "lb-recv", sent.BatchId, v, ("LIN-BS", 8, 1), ("LIN-BT", 14, 0))).Replayed);
        Assert.Contains("already", (await Expect.ErrorAsync(HccErrorCode.INVALID_TRANSITION, () => Receive(Users.Supervisor, "l6", sent.BatchId, v, ("LIN-BS", 9, 0), ("LIN-BT", 14, 0)))).Message, StringComparison.Ordinal);
        var manager = await _w.Queries.BatchesAsync(_w.PropertyId, Users.Manager, ct: Ct);
        Assert.Equal(9 * 1800 + 14 * 1400, manager.Rows.Single(b => b.BatchId == sent.BatchId).ChargePaise);
        Assert.Equal(1, manager.Rows.Single(b => b.BatchId == sent.BatchId).Lines.Single(l => l.ItemCode == "LIN-BS").Missing);
        Assert.Null((await _w.Queries.BatchesAsync(_w.PropertyId, Users.Supervisor, ct: Ct)).Rows.Single(b => b.BatchId == sent.BatchId).ChargePaise);
    }

    private Task<CommandResult<ReceiveLaundryResult>> Receive(int who, string key, long batch, string version, params (string Item, int Received, int Damaged)[] lines) =>
        _w.Commands.ReceiveLaundryAsync(_w.Ctx(who, key), new ReceiveLaundryRequest(batch, version, lines.Select(l => new ReceiveLaundryLine(l.Item, l.Received, l.Damaged)).ToList()), Ct);

    [Fact]
    public async Task Fixture_batch_LB_S2_receives_in_full_and_empties_the_laundry()
    {
        var b = (await _w.Reads.BatchesAsync(_w.PropertyId, "Out", Ct)).Single();
        Assert.Equal(7, b.Lines.Count);
        var page = await _w.Queries.BatchesAsync(_w.PropertyId, Users.Housekeeper, status: "Out", ct: Ct);
        Assert.Equal(24 + 40 + 8 + 30 + 26 + 10 + 2, page.PiecesOut);
        await Receive(Users.Housekeeper, "s2", b.BatchId, Convert.ToBase64String(b.Version), b.Lines.Select(l => (l.ItemCode, l.Sent, 0)).ToArray());
        Assert.Equal("Returned", (await _w.Reads.BatchesAsync(_w.PropertyId, "Returned", Ct)).Single().Status);
        Assert.Equal(0, await Count("SELECT COUNT(*) FROM stk.stock WHERE loc = 'laundry' AND qty > 0"));
    }

    [Fact]
    public async Task Loss_needs_a_reason_other_needs_a_note_cannot_exceed_stock_and_records_value()
    {
        Task<CommandResult<RecordLossResult>> Loss(string key, string loc, int qty, string reason, string? note = null) =>
            _w.Commands.RecordLossAsync(_w.Ctx(Users.Housekeeper, key), new RecordLossRequest("LIN-BR", qty, loc, reason, note), Ct);
        Assert.Contains("reason", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => Loss("x1", "store", 1, "Because"))).Message, StringComparison.Ordinal);
        Assert.Contains("Describe", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => Loss("x2", "store", 1, "Other", ""))).Message, StringComparison.Ordinal);
        Assert.Contains("Only 6", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => Loss("x3", "store", 7, "Missing"))).Message, StringComparison.Ordinal);
        Assert.Contains("valid location", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => Loss("x4", "laundry", 1, "Missing"))).Message, StringComparison.Ordinal);
        await Loss("x5", "rooms", 1, "Taken by guest", "Room 106");
        Assert.Equal(160000, await Count("SELECT value_paise FROM stk.loss WHERE loc = 'rooms'"));
        Assert.Equal(5, await _w.StockAsync("LIN-BR", "rooms"));
        var losses = await _w.Queries.LossesAsync(_w.PropertyId, Users.Manager, ct: Ct);
        Assert.Equal((1, 160000L), (losses.Total, losses.TotalValuePaise));
        Assert.Null((await _w.Queries.LossesAsync(_w.PropertyId, Users.Supervisor, ct: Ct)).Rows[0].ValuePaise);
    }

    [Fact]
    public async Task Count_matching_posts_at_once_variance_needs_another_reviewer_and_approval_keeps_later_movements()
    {
        var f1 = await _w.CountMapAsync("floor1");
        var match = (await _w.Commands.SubmitCountAsync(_w.Ctx(Users.Housekeeper, "c1"), new SubmitCountRequest("floor1", f1), Ct)).Value;
        Assert.Equal(("Posted", 0), (match.Status, match.VarianceLines));

        var st = await _w.CountMapAsync("store");
        st["AMN-SP"] -= 10;
        var sub = (await _w.Commands.SubmitCountAsync(_w.Ctx(Users.Supervisor, "c2"), new SubmitCountRequest("store", st), Ct)).Value;
        Assert.Equal(("Submitted", 1), (sub.Status, sub.VarianceLines));
        await Expect.ErrorAsync(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.SubmitCountAsync(_w.Ctx(Users.Housekeeper, "c3"), new SubmitCountRequest("store", st), Ct));
        Assert.Contains("own count", (await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => _w.Commands.DecideCountAsync(_w.Ctx(Users.Supervisor, "c4"), new DecideCountRequest(sub.CountId, sub.ExpectedVersion, true, "Own"), Ct))).Message, StringComparison.Ordinal);
        Assert.Contains("reason", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.DecideCountAsync(_w.Ctx(Users.Manager, "c5"), new DecideCountRequest(sub.CountId, sub.ExpectedVersion, true, ""), Ct))).Message, StringComparison.Ordinal);
        await Expect.ErrorAsync(HccErrorCode.STALE_VERSION, () => _w.Commands.DecideCountAsync(_w.Ctx(Users.Manager, "c5b"), new DecideCountRequest(sub.CountId, "AAAAAAAAAAA=", true, "stale"), Ct));
        await Issue(Users.Housekeeper, "c6", "AMN-SP", 20, "store", "floor1"); // movement after the count
        var decided = (await _w.Commands.DecideCountAsync(_w.Ctx(Users.Manager, "c7"), new DecideCountRequest(sub.CountId, sub.ExpectedVersion, true, "Issued without record"), Ct)).Value;
        Assert.Equal(("Posted", 1), (decided.Status, decided.LossLines));
        Assert.Equal(260 - 20 - 10, await _w.StockAsync("AMN-SP", "store"));
        Assert.Equal((10, "Count shortage", Users.Supervisor), (await Count("SELECT qty FROM stk.loss WHERE reason = 'Count shortage'"), "Count shortage", await Count("SELECT actor_id FROM stk.loss WHERE reason = 'Count shortage'")));
        Assert.Equal(-10, await Count("SELECT qty FROM stk.move WHERE kind = 'count'"));
        Assert.Equal(1400 * 10, await Count("SELECT value_paise FROM stk.loss WHERE reason = 'Count shortage'"));
        await Expect.ErrorAsync(HccErrorCode.INVALID_TRANSITION, () => _w.Commands.DecideCountAsync(_w.Ctx(Users.Manager, "c8"), new DecideCountRequest(sub.CountId, sub.ExpectedVersion, true, "again"), Ct));
        Assert.Contains("whole-number count", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.SubmitCountAsync(_w.Ctx(Users.Housekeeper, "c9"), new SubmitCountRequest("floor3", new Dictionary<string, int>()), Ct))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approval_fails_when_stock_moved_below_the_variance_since_the_count()
    {
        var st = await _w.CountMapAsync("floor1");
        st["AMN-SP"] += 5;
        var sub = (await _w.Commands.SubmitCountAsync(_w.Ctx(Users.Housekeeper, "d1"), new SubmitCountRequest("floor1", st), Ct)).Value;
        // The variance is positive here, so it always applies; make a negative one on floor 3 and then drain it.
        var f3 = await _w.CountMapAsync("floor3");
        f3["AMN-SP"] -= 20;
        var neg = (await _w.Commands.SubmitCountAsync(_w.Ctx(Users.Housekeeper, "d2"), new SubmitCountRequest("floor3", f3), Ct)).Value;
        await _w.Commands.UseAsync(_w.Ctx(Users.Housekeeper, "d3"), new UseStockRequest("AMN-SP", 10, "floor3"), Ct); // 26 → 16, count says −20 → would be −4
        var e = await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.DecideCountAsync(_w.Ctx(Users.Manager, "d4"), new DecideCountRequest(neg.CountId, neg.ExpectedVersion, true, "approve"), Ct));
        Assert.Contains("count again", e.Message, StringComparison.Ordinal);
        Assert.Equal(16, await _w.StockAsync("AMN-SP", "floor3"));
        Assert.Equal("Submitted", (await _w.Reads.CountsAsync(_w.PropertyId, "Submitted", Ct)).Single(c => c.CountId == neg.CountId).Status);
        Assert.NotNull(sub);
    }

    [Fact]
    public async Task Fixture_count_CNT_S2_can_be_rejected_by_the_supervisor_with_no_stock_change()
    {
        var c = (await _w.Reads.CountsAsync(_w.PropertyId, "Submitted", Ct)).Single();
        Assert.Equal(("floor2", 2), (c.Loc, c.Lines.Count(l => l.Counted != l.System)));
        var q = await _w.Queries.CountsAsync(_w.PropertyId, Users.Manager, status: "Submitted", ct: Ct);
        Assert.Equal(-(3 * 900) - 18000, q.Rows.Single().VarianceValuePaise); // −3 water bottles at ₹9, −1 hand towel at ₹180
        Assert.Null((await _w.Queries.CountsAsync(_w.PropertyId, Users.Supervisor, status: "Submitted", ct: Ct)).Rows.Single().VarianceValuePaise);
        var before = (await _w.Reads.ItemsAsync(_w.PropertyId, Ct)).Select(i => string.Join(',', i.Stock.OrderBy(k => k.Key).Select(k => k.Value))).ToList();
        await _w.Commands.DecideCountAsync(_w.Ctx(Users.Supervisor, "r1"), new DecideCountRequest(c.CountId, Convert.ToBase64String(c.Version), false, "Recount after rounds"), Ct);
        Assert.Equal("Rejected", (await _w.Reads.CountsAsync(_w.PropertyId, "Rejected", Ct)).Single().Status);
        Assert.Equal(before, (await _w.Reads.ItemsAsync(_w.PropertyId, Ct)).Select(i => string.Join(',', i.Stock.OrderBy(k => k.Key).Select(k => k.Value))).ToList());
        Assert.Equal(0, await Count("SELECT COUNT(*) FROM stk.loss"));
    }

    [Fact]
    public async Task Consumption_per_occupied_room_night_uses_history_and_flags_shampoo()
    {
        var r = await _w.Queries.ConsumptionAsync(_w.PropertyId, Users.Manager, "2026-10-17", "2026-10-23", Ct);
        Assert.Equal(12 + 14 + 15 + 11 + 9 + 10 + 8, r.OccupiedRoomNights);
        var sh = r.Rows.Single(x => x.Code == "AMN-SH");
        var sp = r.Rows.Single(x => x.Code == "AMN-SP");
        Assert.True(sh.Flag && sh.VariancePercent > 15);
        Assert.False(sp.Flag);
        await _w.Commands.UseAsync(_w.Ctx(Users.Housekeeper, "k1"), new UseStockRequest("AMN-SP", 5, "floor1"), Ct);
        var today = await _w.Queries.ConsumptionAsync(_w.PropertyId, Users.Manager, StockWorld.Business, StockWorld.Business, Ct);
        Assert.Equal(8, today.OccupiedRoomNights);
        Assert.True(today.Rows.Single(x => x.Code == "AMN-SP").Used >= 5);
        Assert.Equal(0, (await _w.Queries.ConsumptionAsync(_w.PropertyId, Users.Manager, "2026-10-24", "2026-10-24", Ct)).OccupiedRoomNights);
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Queries.ConsumptionAsync(_w.PropertyId, Users.Manager, "2026-01-01", "2026-10-23", Ct));
    }

    [Fact]
    public async Task Live_stays_override_the_imported_history_for_a_night()
    {
        var room = await _w.Db.CountAsync("SELECT MIN(room_id) FROM inv.room", Ct);
        await _w.Db.ExecAsync($"""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) VALUES ({_w.PropertyId}, 'R-1', N'Demo guest', '2026-10-22', '2026-10-24', 'InHouse');
            INSERT inv.room_night(room_id, night, reservation_id) SELECT {room}, d.n, SCOPE_IDENTITY() FROM (VALUES ('2026-10-22'), ('2026-10-23')) d(n);
            """, Ct);
        var r = await _w.Queries.ConsumptionAsync(_w.PropertyId, Users.Manager, "2026-10-22", "2026-10-23", Ct);
        Assert.Equal(1 + 1, r.OccupiedRoomNights); // live 1 each night replaces history 10 and 8
    }

    [Fact]
    public async Task Item_settings_par_and_minimum_change_alerts_with_validation_and_stale_version()
    {
        var v = await _w.VersionAsync("LIN-BS");
        var set = (await _w.Commands.SetItemAsync(_w.Ctx(Users.Supervisor, "s1"), new SetStockItemRequest("LIN-BS", v, 70, 40, null, new Dictionary<string, int> { ["Classic"] = 3, ["Deluxe"] = 3, ["Suite"] = 3 }), Ct)).Value;
        Assert.NotEqual(v, set.Version);
        var page = await _w.Queries.ItemsAsync(_w.PropertyId, Users.Supervisor, 1, 100, ct: Ct);
        Assert.Equal(216, page.Rows.Single(r => r.Code == "LIN-BS").ParRequired);
        Assert.Contains(await _w.Queries.AlertsAsync(_w.PropertyId, Users.Supervisor, Ct), a => a.Code == "LIN-BS");
        Assert.Equal(150, page.Rows.Single(r => r.Code == "LIN-BS").StdX100); // kept when not supplied
        await Expect.ErrorAsync(HccErrorCode.STALE_VERSION, () => _w.Commands.SetItemAsync(_w.Ctx(Users.Supervisor, "s2"), new SetStockItemRequest("LIN-BS", v, 70, 40), Ct));
        var sp = await _w.VersionAsync("AMN-SP");
        Assert.Contains("linen only", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.SetItemAsync(_w.Ctx(Users.Supervisor, "s3"), new SetStockItemRequest("AMN-SP", sp, 1, 1, null, new Dictionary<string, int> { ["Classic"] = 1 }), Ct))).Message, StringComparison.Ordinal);
        Assert.Contains("whole numbers", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.SetItemAsync(_w.Ctx(Users.Supervisor, "s4"), new SetStockItemRequest("AMN-SP", sp, -1, 1), Ct))).Message, StringComparison.Ordinal);
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.SetItemAsync(_w.Ctx(Users.Supervisor, "s5"), new SetStockItemRequest("LIN-BS", set.Version, 1, 1, null, new Dictionary<string, int> { ["Penthouse"] = 1 }), Ct));
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.SetItemAsync(_w.Ctx(Users.Supervisor, "s6"), new SetStockItemRequest("LIN-BS", set.Version, 1, 1, null, new Dictionary<string, int> { ["Classic"] = 21 }), Ct));
    }

    [Fact]
    public async Task New_items_and_settings()
    {
        var created = (await _w.Commands.CreateItemAsync(_w.Ctx(Users.Supervisor, "n1"), new CreateStockItemRequest("lin-ps", "Pillow protector", "linen", "pc", 10, 5, 20, Par: new Dictionary<string, int> { ["Classic"] = 2 }), Ct)).Value;
        Assert.Equal("LIN-PS", created.Code);
        Assert.Equal(7, await Count("SELECT COUNT(*) FROM stk.stock s JOIN stk.item i ON i.item_id = s.item_id WHERE i.code = 'LIN-PS'"));
        Assert.Equal(24, (await _w.Queries.ItemsAsync(_w.PropertyId, Users.Supervisor, 1, 100, ct: Ct)).Rows.Single(r => r.Code == "LIN-PS").ParPerSet); // 12 Classic rooms × 2
        Assert.Contains("already exists", (await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.CreateItemAsync(_w.Ctx(Users.Manager, "n2"), new CreateStockItemRequest("LIN-PS", "Again", "linen", "pc", 1, 1), Ct))).Message, StringComparison.Ordinal);
        await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => _w.Commands.CreateItemAsync(_w.Ctx(Users.Supervisor, "n3"), new CreateStockItemRequest("AMN-XX", "Comb", "amenity", "pc", 1, 1, UnitCostPaise: 500), Ct));
        var priced = (await _w.Commands.CreateItemAsync(_w.Ctx(Users.Manager, "n4"), new CreateStockItemRequest("AMN-XX", "Comb", "amenity", "pc", 1, 1, UnitCostPaise: 500), Ct)).Value;
        Assert.Equal("AMN-XX", priced.Code);
        Assert.Equal(500, (await _w.ItemAsync("AMN-XX")).CostPaise);
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.CreateItemAsync(_w.Ctx(Users.Manager, "n5"), new CreateStockItemRequest("bad code", "X", "amenity", "pc", 1, 1), Ct));
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.CreateItemAsync(_w.Ctx(Users.Manager, "n6"), new CreateStockItemRequest("AMN-YY", "X", "amenity", "pc", 1, 1, LaundryRatePaise: 100), Ct));

        await _w.Commands.SetSettingsAsync(_w.Ctx(Users.Supervisor, "n7"), new SetStockSettingsRequest(2, "City Laundry", 2), Ct);
        Assert.Equal((2, "City Laundry"), ((await _w.Reads.SettingsAsync(_w.PropertyId, Ct)).ParSets, (await _w.Reads.SettingsAsync(_w.PropertyId, Ct)).VendorName));
        Assert.Equal(96, (await _w.Queries.ItemsAsync(_w.PropertyId, Users.Supervisor, 1, 100, ct: Ct)).Rows.Single(r => r.Code == "LIN-BS").ParRequired);
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Commands.SetSettingsAsync(_w.Ctx(Users.Supervisor, "n8"), new SetStockSettingsRequest(0, null, 1), Ct));
    }

    [Fact]
    public async Task Parallel_issues_never_drive_a_location_negative_or_lose_pieces()
    {
        var total0 = await _w.TotalAsync("LIN-BS");
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
            await Expect.CodeAsync(async () => await Issue(Users.Housekeeper, "par" + i, "LIN-BS", 15, "store", "floor1"))));
        Assert.Equal(2, results.Count(r => r == "NO_ERROR")); // 40 in store: two issues of 15 fit
        Assert.Equal(4, results.Count(r => r == "VALIDATION"));
        Assert.Equal((10, 36), (await _w.StockAsync("LIN-BS", "store"), await _w.StockAsync("LIN-BS", "floor1")));
        Assert.Equal(total0, await _w.TotalAsync("LIN-BS"));
        Assert.Equal(0, await Count("SELECT COUNT(*) FROM stk.stock WHERE qty < 0"));
    }

    [Fact]
    public async Task Events_audit_and_movements_are_recorded_for_the_commands()
    {
        await _w.Commands.ReceiveGrnAsync(_w.Ctx(Users.Manager, "e1"), new ReceiveGrnRequest("Deccan Hygiene Traders", null, [new("CLN-GB", 2, 6000)]), Ct);
        await _w.Commands.RecordLossAsync(_w.Ctx(Users.Housekeeper, "e2"), new RecordLossRequest("AMN-SP", 2, "store", "Expired"), Ct);
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'StockGoodsReceived'"));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'StockLossRecorded'"));
        Assert.Equal(2, await Count("SELECT COUNT(*) FROM plat.audit_event WHERE operation IN ('StockReceiveGrn', 'StockRecordLoss')"));
        var moves = await _w.Queries.MovesAsync(_w.PropertyId, Users.Housekeeper, 1, 10, "AMN-SP", ct: Ct);
        Assert.Equal("loss", moves.Rows[0].Kind);
        Assert.Equal(Users.Housekeeper, moves.Rows[0].ActorId);
    }
}
