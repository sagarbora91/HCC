using Hcc.Catalogue.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Catalogue;
using Hcc.Domain.Errors;

namespace Hcc.Catalogue.Tests.Sql;

/// <summary>Catalogue commands and queries on a disposable HccTest_* database: migration + seed, Owner-only masters with history, idempotency,
/// STALE_VERSION and in-use rules, PostExtra (the ₹500 laundry line at 18 %), meal plan of a booking and plan lines, reasoned discounts.</summary>
[Trait("Category", "Sql")]
public class CatalogueSqlTests : IAsyncLifetime
{
    private CatalogueWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await CatalogueWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    private Task<int> ExtraIdAsync(string code) => _w.IdAsync("bill.extra_item", "extra_item_id", code, Ct);

    private Task<int> PlanIdAsync(string code) => _w.IdAsync("bill.meal_plan", "meal_plan_id", code, Ct);

    [Fact]
    public async Task Migration_seeds_plans_extras_and_reasons_readable_with_folio_view_only()
    {
        var plans = await _w.Queries.MealPlansAsync(_w.PropertyId, _w.Users["reception"], ct: Ct);
        Assert.Equal(["EP", "CP", "MAP", "AP"], plans.Items.Select(p => p.Code));
        Assert.Equal(("B", "bundled", 30000L), (string.Concat(plans.Items[1].Meals), plans.Items[1].GstTreatment, plans.Items[1].AddOn[0].AmountPaise));
        var extras = await _w.Queries.ExtrasAsync(_w.PropertyId, _w.Users["cashier"], ct: Ct);
        Assert.Equal(8, extras.Total);
        var laundry = extras.Items.Single(x => x.Code == "LAUNDRY");
        Assert.Equal(("Laundry", "laundry", "bag", 50000L, "2025-04-01"), (laundry.Name, laundry.GstCategory, laundry.Unit, laundry.Prices[0].AmountPaise, laundry.Prices[0].EffectiveFrom));
        var reasons = await _w.Queries.DiscountReasonsAsync(_w.PropertyId, _w.Users["accountant"], ct: Ct);
        Assert.Equal(["SERVICE", "CORP", "REPEAT", "LONGSTAY", "MGMT"], reasons.Items.Select(r => r.Code));
        Assert.Equal((2500, (int?)1000), (reasons.Items[0].MaxBps, reasons.Items[0].ApprovalAboveBps));
        Assert.Equal(["OWN", "MGR"], reasons.Items[3].Roles);
        foreach (var u in new[] { "housekeeper", "maint", "supervisor", "techadmin" })
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(async () => await _w.Queries.ExtrasAsync(_w.PropertyId, _w.Users[u], ct: Ct))).Code);
        await Assert.ThrowsAnyAsync<Exception>(() => _w.ExecAsync($"INSERT bill.extra_item(property_id, code, name, category, gst_category, unit, sort_order, updated_by, updated_utc) VALUES ({_w.PropertyId}, 'x', N'x', 'misc', 'room', 'each', 1, 1, SYSUTCDATETIME());", Ct));
    }

    [Fact]
    public async Task Masters_are_Owner_only_with_audit_history_idempotency_and_stale_version()
    {
        var req = new SaveExtraItemRequest(null, " iron ", "Ironing", "laundry", "laundry", "piece", "laundry", [new DatedAmount("2026-10-24", 10000)]);
        foreach (var u in new[] { "manager", "techadmin", "reception" })
            Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveExtraItemAsync(_w.Ctx(u, "f-" + u), req, Ct))).Code);
        var first = await _w.Commands.SaveExtraItemAsync(_w.Ctx("owner", "x1"), req, Ct);
        var again = await _w.Commands.SaveExtraItemAsync(_w.Ctx("owner", "x1"), req, Ct);
        Assert.True(again.Replayed);
        Assert.Equal(("IRON", 90, 10000L), (first.Value.Code, first.Value.SortOrder, first.Value.Prices[0].AmountPaise));
        Assert.Equal("Master Extras and services: added IRON · Ironing", (string)(await _w.ScalarAsync("SELECT TOP 1 detail FROM plat.audit_event WHERE action = 'CAT_EXTRA' ORDER BY audit_id DESC;", Ct))!);
        var edit = new SaveExtraItemRequest(first.Value.ExtraItemId, "IRON", "Ironing service", "laundry", "laundry", "piece", "laundry",
            [new DatedAmount("2026-10-24", 10000), new DatedAmount("2026-11-01", 12000)], null, first.Value.Version);
        var edited = (await _w.Commands.SaveExtraItemAsync(_w.Ctx("owner", "x2"), edit, Ct)).Value;
        Assert.Equal(2, edited.Prices.Count);
        Assert.Equal(HccErrorCode.STALE_VERSION, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveExtraItemAsync(_w.Ctx("owner", "x3"), edit with { Name = "Pressing" }, Ct))).Code);
        var h = await _w.Queries.HistoryAsync(_w.PropertyId, _w.Users["owner"], CatalogueIds.Extras, first.Value.ExtraItemId, ct: Ct);
        Assert.Contains(h.Items, x => x.Field == "price" && x.NewValue == "₹100 from 2026-10-24; ₹120 from 2026-11-01");
        Assert.Contains(h.Items, x => x.Field == "name" && x.OldValue == "Ironing" && x.NewValue == "Ironing service");
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(async () => await _w.Queries.HistoryAsync(_w.PropertyId, _w.Users["manager"], CatalogueIds.Extras, ct: Ct))).Code);
        var lid = await ExtraIdAsync("LAUNDRY");
        var past = new SaveExtraItemRequest(lid, "LAUNDRY", "Laundry", "laundry", "laundry", "bag", "laundry", [new DatedAmount("2025-04-01", 1)], null,
            await _w.VersionAsync("bill.extra_item", "extra_item_id", lid, Ct));
        var e = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveExtraItemAsync(_w.Ctx("owner", "x4"), past, Ct));
        Assert.Equal("MASTER_INVALID", Reason(e));
        var taken = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveExtraItemAsync(_w.Ctx("owner", "x5"), req with { Code = "LAUNDRY", Name = "Laundry 2" }, Ct));
        Assert.Equal("MASTER_CODE_TAKEN", Reason(taken));
    }

    [Fact]
    public async Task PostExtra_posts_the_laundry_line_at_18_percent_linked_to_the_item_and_edits_stay_allowed()
    {
        var (_, folio) = await _w.StayAsync(ct: Ct);
        var laundry = await ExtraIdAsync("LAUNDRY");
        var r = (await _w.Commands.PostExtraAsync(_w.Ctx("reception", "p1"), new PostExtraRequest(folio, laundry), Ct)).Value;
        Assert.Equal(("Laundry", 50000L, 1800, 9000L, "service"), (r.Label, r.AmountPaise, r.TaxBps, r.TaxPaise, r.Kind));
        Assert.Equal(laundry, await _w.CountAsync($"SELECT extra_item_id FROM bill.charge_catalogue WHERE charge_id = {r.ChargeId};", Ct));
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM tax.charge_gst WHERE charge_id = {r.ChargeId};", Ct));
        var pickup = await ExtraIdAsync("PICKUP");
        var two = (await _w.Commands.PostExtraAsync(_w.Ctx("cashier", "p2"), new PostExtraRequest(folio, pickup, 2), Ct)).Value;
        Assert.Equal(("Airport pickup × 2", 240000L), (two.Label, two.AmountPaise));
        var minibar = await ExtraIdAsync("MINIBAR");
        var custom = (await _w.Commands.PostExtraAsync(_w.Ctx("reception", "p3"), new PostExtraRequest(folio, minibar, null, "Minibar · juices", 18000, "food"), Ct)).Value;
        Assert.Equal(("Minibar · juices", 18000L, 500), (custom.Label, custom.AmountPaise, custom.TaxBps));
        Assert.Contains("(changed: name, price)", (string)(await _w.ScalarAsync("SELECT TOP 1 detail FROM plat.audit_event WHERE action = 'CHARGE_POSTED' ORDER BY audit_id DESC;", Ct))!, StringComparison.Ordinal);
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ChargePosted';", Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.PostExtraAsync(_w.Ctx("housekeeper", "p4"), new PostExtraRequest(folio, laundry), Ct))).Code);
        Assert.Equal(HccErrorCode.NOT_FOUND, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.PostExtraAsync(_w.Ctx("reception", "p5"), new PostExtraRequest(folio, 99999), Ct))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.PostExtraAsync(_w.Ctx("reception", "p6"), new PostExtraRequest(folio, laundry, 100), Ct))).Code);
        // In use: delete refused, retire allowed; a retired item cannot be charged.
        var v = await _w.VersionAsync("bill.extra_item", "extra_item_id", laundry, Ct);
        Assert.Equal("MASTER_IN_USE", Reason(await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.DeleteExtraItemAsync(_w.Ctx("owner", "p7"), new ExtraItemRef(laundry, v), Ct))));
        var retired = (await _w.Commands.RetireExtraItemAsync(_w.Ctx("owner", "p8"), new ExtraItemRef(laundry, v), Ct)).Value;
        Assert.Equal((false, "2026-10-24", 1), (retired.Active, retired.RetiredOn, retired.UsageCount));
        Assert.Equal("MASTER_RETIRED", Reason(await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.PostExtraAsync(_w.Ctx("reception", "p9"), new PostExtraRequest(folio, laundry), Ct))));
        var hall = await ExtraIdAsync("HALL");
        Assert.True((await _w.Commands.DeleteExtraItemAsync(_w.Ctx("owner", "p10"), new ExtraItemRef(hall, await _w.VersionAsync("bill.extra_item", "extra_item_id", hall, Ct)), Ct)).Value.Deleted);
        var links = await _w.Queries.LinksAsync(_w.PropertyId, _w.Users["reception"], ct: Ct);
        Assert.Equal(3, links.Charges.Count(c => c.ExtraItemId is not null));
    }

    [Fact]
    public async Task Meal_plan_of_a_booking_and_plan_lines_with_the_room_night()
    {
        var (res, folio) = await _w.StayAsync(postNight: false, ct: Ct);
        var set = (await _w.Commands.SetReservationMealPlanAsync(_w.Ctx("cashier", "m1"), new SetReservationMealPlanRequest(res, "map"), Ct)).Value;
        Assert.Equal(("MAP", (string?)null), (set.MealPlanCode, set.Previous));
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SetReservationMealPlanAsync(_w.Ctx("cashier", "m2"), new SetReservationMealPlanRequest(res, "MAP"), Ct))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SetReservationMealPlanAsync(_w.Ctx("housekeeper", "m3"), new SetReservationMealPlanRequest(res, "CP"), Ct))).Code);
        Assert.Equal("MASTER_INVALID", Reason(await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SetReservationMealPlanAsync(_w.Ctx("owner", "m4"), new SetReservationMealPlanRequest(res, "ZZ"), Ct))));
        _w.Clock.Now = _w.Clock.Now.AddMinutes(5);
        await _w.PostRoomNightAsync(folio, 200000, Ct);
        var night = (await _w.Commands.PostMealPlanNightsAsync(_w.Ctx("manager", "m5"), new PostMealPlanNightsRequest(), Ct)).Value;
        Assert.Equal(1, night.Posted);
        Assert.Equal((150000L, 500, "Meal plan MAP · 2 × ₹750 · 2026-10-24"), ((long)await _w.LongAsync($"SELECT amount_paise FROM bill.charge WHERE charge_id = {night.ChargeIds[0]};", Ct),
            await _w.CountAsync($"SELECT tax_bps FROM bill.charge WHERE charge_id = {night.ChargeIds[0]};", Ct), (string)(await _w.ScalarAsync($"SELECT label FROM bill.charge WHERE charge_id = {night.ChargeIds[0]};", Ct))!));
        Assert.Equal(0, (await _w.Commands.PostMealPlanNightsAsync(_w.Ctx("manager", "m6"), new PostMealPlanNightsRequest(), Ct)).Value.Posted);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.PostMealPlanNightsAsync(_w.Ctx("reception", "m7"), new PostMealPlanNightsRequest(), Ct))).Code);
        // A plan chosen after the night was posted applies from the next night.
        var (res2, _) = await _w.StayAsync(room: "106", ct: Ct);
        _w.Clock.Now = _w.Clock.Now.AddMinutes(5);
        await _w.Commands.SetReservationMealPlanAsync(_w.Ctx("reception", "m8"), new SetReservationMealPlanRequest(res2, "AP"), Ct);
        Assert.Equal(0, (await _w.Commands.PostMealPlanNightsAsync(_w.Ctx("owner", "m9"), new PostMealPlanNightsRequest(), Ct)).Value.Posted);
        var links = await _w.Queries.LinksAsync(_w.PropertyId, _w.Users["reception"], ct: Ct);
        Assert.Equal(["MAP", "AP"], links.Plans.Select(p => p.MealPlanCode));
        Assert.Single(links.Charges, c => c.MealPlanCode == "MAP");
        var map = await PlanIdAsync("MAP");
        Assert.Equal("MASTER_IN_USE", Reason(await Assert.ThrowsAsync<HccException>(async () =>
            await _w.Commands.DeleteMealPlanAsync(_w.Ctx("owner", "m10"), new MealPlanRef(map, await _w.VersionAsync("bill.meal_plan", "meal_plan_id", map, Ct)), Ct))));
    }

    [Fact]
    public async Task Bundled_plan_takes_the_room_slab_on_room_plus_meals_and_room_types_are_enforced()
    {
        var gala = (await _w.Commands.SaveMealPlanAsync(_w.Ctx("owner", "g1"), new SaveMealPlanRequest(null, "GALA", "Gala package", ["B", "D"], "bundled", ["Suite"],
            [new DatedAmount("2026-10-24", 200000)]), Ct)).Value;
        Assert.Equal(["Suite"], gala.RoomTypes);
        var (classic, _) = await _w.StayAsync(postNight: false, ct: Ct);
        Assert.Equal("MASTER_INVALID", Reason(await Assert.ThrowsAsync<HccException>(async () =>
            await _w.Commands.SetReservationMealPlanAsync(_w.Ctx("reception", "g2"), new SetReservationMealPlanRequest(classic, "GALA"), Ct))));
        var (suite, folio) = await _w.StayAsync(room: "106", postNight: false, ct: Ct);
        await _w.Commands.SetReservationMealPlanAsync(_w.Ctx("reception", "g3"), new SetReservationMealPlanRequest(suite, "GALA"), Ct);
        _w.Clock.Now = _w.Clock.Now.AddMinutes(5);
        await _w.PostRoomNightAsync(folio, 450000, Ct);
        var r = (await _w.Commands.PostMealPlanNightsAsync(_w.Ctx("owner", "g4"), new PostMealPlanNightsRequest(), Ct)).Value;
        Assert.Equal(1800, await _w.CountAsync($"SELECT tax_bps FROM bill.charge WHERE charge_id = {r.ChargeIds[0]};", Ct)); // ₹4,500 + 2 × ₹2,000 > ₹7,500
        var v = await _w.VersionAsync("bill.meal_plan", "meal_plan_id", gala.MealPlanId, Ct);
        await _w.Commands.RetireMealPlanAsync(_w.Ctx("owner", "g5"), new MealPlanRef(gala.MealPlanId, v), Ct);
        var (other, _) = await _w.StayAsync(room: "106", status: "Confirmed", postNight: false, ct: Ct);
        Assert.Equal("MASTER_RETIRED", Reason(await Assert.ThrowsAsync<HccException>(async () =>
            await _w.Commands.SetReservationMealPlanAsync(_w.Ctx("reception", "g6"), new SetReservationMealPlanRequest(other, "GALA"), Ct))));
    }

    [Fact]
    public async Task Reasoned_discounts_cap_role_and_approval_rules()
    {
        var (_, folio) = await _w.StayAsync(ct: Ct);   // net ₹2,000: SERVICE max 25 % = ₹500, approval above 10 % = ₹200
        var res = (await _w.Commands.RequestReasonedDiscountAsync(_w.Ctx("reception", "d1"), new RequestReasonedDiscountRequest(folio, 15000, "service", "Late towels", await _w.FolioVersionAsync(folio, Ct)), Ct)).Value;
        Assert.Equal("Posted", res.Outcome);
        Assert.Equal(("Discount · Service recovery · Late towels", -15000L, 500), ((string)(await _w.ScalarAsync($"SELECT label FROM bill.charge WHERE charge_id = {res.ChargeId};", Ct))!,
            await _w.LongAsync($"SELECT amount_paise FROM bill.charge WHERE charge_id = {res.ChargeId};", Ct), await _w.CountAsync($"SELECT tax_bps FROM bill.charge WHERE charge_id = {res.ChargeId};", Ct)));
        Assert.Single((await _w.Queries.LinksAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Charges, c => c.DiscountReasonCode == "SERVICE");
        var cap = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.RequestReasonedDiscountAsync(_w.Ctx("owner", "d2"), new RequestReasonedDiscountRequest(folio, 60000, "SERVICE", null, await _w.FolioVersionAsync(folio, Ct)), Ct));
        Assert.Equal(HccErrorCode.VALIDATION, cap.Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.RequestReasonedDiscountAsync(_w.Ctx("reception", "d3"), new RequestReasonedDiscountRequest(folio, 1000, "MGMT", null, await _w.FolioVersionAsync(folio, Ct)), Ct))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.RequestReasonedDiscountAsync(_w.Ctx("cashier", "d4"), new RequestReasonedDiscountRequest(folio, 1000, "SERVICE", null, await _w.FolioVersionAsync(folio, Ct)), Ct))).Code);
        Assert.Equal(HccErrorCode.STALE_VERSION, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.RequestReasonedDiscountAsync(_w.Ctx("owner", "d5"), new RequestReasonedDiscountRequest(folio, 1000, "SERVICE", null, "AAAAAAAAAAA="), Ct))).Code);
        // Manager within the role limit but above the reason's approval % → request with the reason linked; Owner decides directly.
        var mgr = (await _w.Commands.RequestReasonedDiscountAsync(_w.Ctx("manager", "d6"), new RequestReasonedDiscountRequest(folio, 30000, "SERVICE", null, await _w.FolioVersionAsync(folio, Ct)), Ct)).Value;
        Assert.Equal("PendingApproval", mgr.Outcome);
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM bill.approval_discount_reason WHERE approval_id = {mgr.ApprovalId};", Ct));
        Assert.Equal("INVALID_TRANSITION", (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.RequestReasonedDiscountAsync(_w.Ctx("reception", "d7"), new RequestReasonedDiscountRequest(folio, 30000, "CORP", null, await _w.FolioVersionAsync(folio, Ct)), Ct))).Code.ToString());
        var (_, f2) = await _w.StayAsync(room: "106", ct: Ct);
        var own = (await _w.Commands.RequestReasonedDiscountAsync(_w.Ctx("owner", "d8"), new RequestReasonedDiscountRequest(f2, 60000, "SERVICE", null, await _w.FolioVersionAsync(f2, Ct)), Ct)).Value;
        Assert.Equal("Posted", own.Outcome);
        var used = (await _w.Queries.DiscountReasonsAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Items.Single(r => r.Code == "SERVICE");
        Assert.Equal(3, used.UsageCount);
    }

    [Fact]
    public async Task Reorder_and_reactivate_follow_the_masters_framework()
    {
        var ids = new List<int>();
        foreach (var c in new[] { "MGMT", "SERVICE", "CORP", "REPEAT", "LONGSTAY" }) ids.Add(await _w.IdAsync("bill.discount_reason", "discount_reason_id", c, Ct));
        var order = (await _w.Commands.ReorderDiscountReasonsAsync(_w.Ctx("owner", "o1"), new ReorderDiscountReasonsRequest(ids), Ct)).Value;
        Assert.Equal(["MGMT", "SERVICE", "CORP", "REPEAT", "LONGSTAY"], order.Codes);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.ReorderDiscountReasonsAsync(_w.Ctx("owner", "o2"), new ReorderDiscountReasonsRequest(ids.Take(2).ToList()), Ct))).Code);
        var corp = ids[2];
        var r = (await _w.Commands.RetireDiscountReasonAsync(_w.Ctx("owner", "o3"), new DiscountReasonRef(corp, await _w.VersionAsync("bill.discount_reason", "discount_reason_id", corp, Ct)), Ct)).Value;
        Assert.False(r.Active);
        var back = (await _w.Commands.ReactivateDiscountReasonAsync(_w.Ctx("owner", "o4"), new DiscountReasonRef(corp, r.Version), Ct)).Value;
        Assert.True(back.Active);
        var save = new SaveDiscountReasonRequest(corp, "CORP", "Corporate rate", 2000, 2000, [], null, back.Version);
        Assert.Equal("MASTER_INVALID", Reason(await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveDiscountReasonAsync(_w.Ctx("owner", "o5"), save, Ct))));
        var ok = (await _w.Commands.SaveDiscountReasonAsync(_w.Ctx("owner", "o6"), save with { ApprovalAboveBps = 500, Roles = ["REC", "OWN"] }, Ct)).Value;
        Assert.Equal(500, ok.ApprovalAboveBps);
        Assert.Equal(["OWN", "REC"], ok.Roles);
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM mst.master_history WHERE master = 'discountReasons' AND row_id = {corp} AND field = 'approvalAbovePct' AND new_value = N'5 %';", Ct));
    }
}
