using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Masters;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Catalogue;
using Hcc.Contracts.Masters;
using Hcc.Domain.Billing;
using Hcc.Domain.Catalogue;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;
using VersionToken = Hcc.Application.Stays.VersionToken;

namespace Hcc.Application.Catalogue;

/// <summary>
/// Catalogue commands (MA-2; prototype MST_MASTER_* for the masters mealPlans / extras / discountReasons, CAT_CHARGE, CAT_DISCOUNT,
/// CAT_PLAN_SET and the plan line posted with each room night):
/// • SaveMealPlan / SaveExtraItem / SaveDiscountReason, Retire*, Reactivate*, Delete*, Reorder* — PERM-MST-002 (money masters, Owner, DEC-058),
///   expectedVersion on edits (STALE_VERSION), code immutable once used, delete only when unused, amounts in effect never change, one
///   mst.master_history row per changed field.
/// • PostExtra — a catalogue charge (PERM-FOL-002): defaults from the item, all editable (DEC-MA-4), GST from the dated GST master, linked
///   to the item (bill.charge_catalogue) so reports separate catalogue and free-text charges.
/// • SetReservationMealPlan — a booking's plan; PostMealPlanNights — one plan line per in-house folio and posted room night.
/// • RequestReasonedDiscount — DISCOUNT with a reason from the master (roles, max %, approval %), else the folio's discount rules.
/// Every command runs through <see cref="ICommandRunner"/> (Idempotency-Key, audit and outbox in the same transaction).
/// </summary>
public sealed class CatalogueCommands(ICommandRunner runner, IAccessPolicy policy, ICatalogueStore store, IMastersStore history, IBillingStore billing,
    IGstRateResolver gst, TimeProvider clock, IGstChargeSnapshots? snapshots = null)
{
    public const string OpSaveMealPlan = "SaveMealPlan";
    public const string OpRetireMealPlan = "RetireMealPlan";
    public const string OpReactivateMealPlan = "ReactivateMealPlan";
    public const string OpDeleteMealPlan = "DeleteMealPlan";
    public const string OpReorderMealPlans = "ReorderMealPlans";
    public const string OpSaveExtraItem = "SaveExtraItem";
    public const string OpRetireExtraItem = "RetireExtraItem";
    public const string OpReactivateExtraItem = "ReactivateExtraItem";
    public const string OpDeleteExtraItem = "DeleteExtraItem";
    public const string OpReorderExtraItems = "ReorderExtraItems";
    public const string OpSaveDiscountReason = "SaveDiscountReason";
    public const string OpRetireDiscountReason = "RetireDiscountReason";
    public const string OpReactivateDiscountReason = "ReactivateDiscountReason";
    public const string OpDeleteDiscountReason = "DeleteDiscountReason";
    public const string OpReorderDiscountReasons = "ReorderDiscountReasons";
    public const string OpPostExtra = "PostExtra";
    public const string OpSetReservationMealPlan = "SetReservationMealPlan";
    public const string OpPostMealPlanNights = "PostMealPlanNights";
    public const string OpRequestReasonedDiscount = "RequestReasonedDiscount";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private Task RequireMoneyAsync(CommandContext ctx, CancellationToken ct) =>
        policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CataloguePermissions.ManageMoney, null, null, ct);

    // ───────────────────────── Masters: save ─────────────────────────

    public async Task<CommandResult<MealPlanView>> SaveMealPlanAsync(CommandContext ctx, SaveMealPlanRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var fields = new Dictionary<string, string?>
        {
            ["meals"] = CatalogueRules.MealsCode(req.Meals),
            ["gst_treatment"] = CatalogueRules.GstTreatment(req.GstTreatment),
            ["room_types"] = CatalogueRules.RoomTypes(req.RoomTypes),
        };
        var r = await SaveAsync(ctx, OpSaveMealPlan, CatalogueMaster.MealPlans, req, req.MealPlanId, req.Code, req.Name, req.SortOrder, req.ExpectedVersion,
            fields, req.AddOn, "Add-on per guest per night", "addOn", ct).ConfigureAwait(false);
        return new CommandResult<MealPlanView>(MealPlanOf(r.Value.Row, r.Value.Usage), r.Replayed);
    }

    public async Task<CommandResult<ExtraItemView>> SaveExtraItemAsync(CommandContext ctx, SaveExtraItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var fields = new Dictionary<string, string?>
        {
            ["category"] = CatalogueRules.Category(req.Category),
            ["gst_category"] = CatalogueRules.GstCategory(req.GstCategory),
            ["unit"] = CatalogueRules.Unit(req.Unit),
            ["department"] = CatalogueRules.Department(req.Department),
        };
        var r = await SaveAsync(ctx, OpSaveExtraItem, CatalogueMaster.Extras, req, req.ExtraItemId, req.Code, req.Name, req.SortOrder, req.ExpectedVersion,
            fields, req.Prices, "Default price", "price", ct).ConfigureAwait(false);
        return new CommandResult<ExtraItemView>(ExtraOf(r.Value.Row, r.Value.Usage), r.Replayed);
    }

    public async Task<CommandResult<DiscountReasonView>> SaveDiscountReasonAsync(CommandContext ctx, SaveDiscountReasonRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var (max, above) = CatalogueRules.DiscountLimits(req.MaxBps, req.ApprovalAboveBps);
        var fields = new Dictionary<string, string?>
        {
            ["max_bps"] = max.ToString(CultureInfo.InvariantCulture),
            ["approval_above_bps"] = above?.ToString(CultureInfo.InvariantCulture),
            ["roles"] = CatalogueRules.Roles(req.Roles),
        };
        var r = await SaveAsync(ctx, OpSaveDiscountReason, CatalogueMaster.DiscountReasons, req, req.DiscountReasonId, req.Code, req.Name, req.SortOrder,
            req.ExpectedVersion, fields, null, "", "", ct).ConfigureAwait(false);
        return new CommandResult<DiscountReasonView>(ReasonOf(r.Value.Row, r.Value.Usage), r.Replayed);
    }

    /// <summary>Prototype field key of a column (history rows read like the prototype's).</summary>
    private static string Key(string column) => column switch
    {
        "gst_treatment" or "gst_category" => "gst",
        "room_types" => "roomTypes",
        "max_bps" => "maxPct",
        "approval_above_bps" => "approvalAbovePct",
        _ => column,
    };

    private static string? Show(string column, string? v) => column is "max_bps" or "approval_above_bps" && v is not null
        ? (int.Parse(v, CultureInfo.InvariantCulture) / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " %"
        : string.IsNullOrEmpty(v) ? "—" : v;

    private sealed record Saved(CatRow Row, int Usage);

    private async Task<CommandResult<Saved>> SaveAsync<TReq>(CommandContext ctx, string op, CatalogueMaster m, TReq req, int? id, string code, string name,
        int? sortOrder, string? expectedVersion, Dictionary<string, string?> fields, IReadOnlyList<DatedAmount>? prices, string priceLabel, string priceKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await RequireMoneyAsync(ctx, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(code, name, sortOrder);
        byte[]? expected = id is null ? null : VersionToken.Parse(expectedVersion);
        return await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            var rows = await store.LockRowsAsync(tx, m, ctx.PropertyId, c).ConfigureAwait(false);
            CatRow? old = null;
            if (id is { } rid)
            {
                old = rows.FirstOrDefault(x => x.Id == rid) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {m.Singular} was not found.");
                VersionToken.EnsureMatches(old.RowVer, expected!, $"This {m.Singular}");
                MastersRules.EnsureActive(old.Active, old.Name);
                var used = await store.UsageAsync(tx, m, rid, c).ConfigureAwait(false);
                MastersRules.EnsureCodeMayChange(old.Code, clean.Code, used.Count, string.Join(", ", used.Where));
            }

            if (rows.Any(x => x.Id != old?.Id && string.Equals(x.Code, clean.Code, StringComparison.Ordinal)))
                throw MastersRules.Fail(MasterReasons.CodeTaken, $"Code {clean.Code} is already used in {m.Label} (retired rows keep their code).", "code");
            if (rows.Any(x => x.Id != old?.Id && x.Active && string.Equals(x.Name, clean.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {m.Singular} is already called “{clean.Name}”.", "name");
            var date = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var dated = m.PriceTable is null ? [] : CatalogueRules.DatedAmounts(prices, old?.Prices ?? [], date, priceLabel, priceKey);
            var sort = clean.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(rows.Select(x => x.SortOrder));
            var input = new CatRowInput(clean.Code, clean.Name, fields, dated);

            var changes = new List<(string Field, string? Old, string? New)>();
            void Add(string f, string? o, string? n)
            {
                if (old is null || !string.Equals(o, n, StringComparison.Ordinal)) changes.Add((f, old is null ? null : o, n));
            }

            Add("code", old?.Code, clean.Code);
            Add("name", old?.Name, clean.Name);
            Add("sortOrder", old?.SortOrder.ToString(CultureInfo.InvariantCulture), sort.ToString(CultureInfo.InvariantCulture));
            foreach (var col in m.Columns) Add(Key(col), Show(col, old?.F(col)), Show(col, fields[col]));
            if (m.PriceTable is not null) Add(priceKey, old is null ? null : CatalogueRules.ShowDated(old.Prices), CatalogueRules.ShowDated(dated));

            int rowId;
            string detail;
            if (old is not null)
            {
                if (changes.Count == 0) throw HccException.Validation("Nothing changed.");
                rowId = old.Id;
                await store.UpdateAsync(tx, m, rowId, expected!, input, sort, ctx.PrincipalId, Now, c).ConfigureAwait(false);
                detail = MastersRules.AuditText(m.Label, changes);
            }
            else
            {
                rowId = await store.InsertAsync(tx, m, ctx.PropertyId, input, sort, ctx.PrincipalId, Now, c).ConfigureAwait(false);
                detail = $"Master {m.Label}: added {clean.Code} · {clean.Name}";
            }

            foreach (var (f, o, n) in changes)
                await history.AddHistoryAsync(tx, ctx.PropertyId, m.Id, rowId, clean.Code, f, o, n, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            return await FinishAsync(tx, ctx, m, rowId, old is null ? "created" : "changed", detail, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task<Saved> FinishAsync(ICommandTransaction tx, CommandContext ctx, CatalogueMaster m, int rowId, string action, string detail, CancellationToken ct)
    {
        await tx.AuditAsync(m.AuditAction, Target(m, rowId), detail, ct).ConfigureAwait(false);
        await tx.PublishAsync("MasterChanged", rowId, new { master = m.Id, rowId, action }, ct).ConfigureAwait(false);
        var row = (await store.LockRowsAsync(tx, m, ctx.PropertyId, ct).ConfigureAwait(false)).First(x => x.Id == rowId);
        return new Saved(row, (await store.UsageAsync(tx, m, rowId, ct).ConfigureAwait(false)).Count);
    }

    // ───────────────────────── Masters: status, delete, reorder ─────────────────────────

    public Task<CommandResult<MealPlanView>> RetireMealPlanAsync(CommandContext ctx, MealPlanRef req, CancellationToken ct = default) =>
        StatusAs(ctx, OpRetireMealPlan, CatalogueMaster.MealPlans, req, req?.MealPlanId ?? 0, req?.ExpectedVersion, false, MealPlanOf, ct);

    public Task<CommandResult<MealPlanView>> ReactivateMealPlanAsync(CommandContext ctx, MealPlanRef req, CancellationToken ct = default) =>
        StatusAs(ctx, OpReactivateMealPlan, CatalogueMaster.MealPlans, req, req?.MealPlanId ?? 0, req?.ExpectedVersion, true, MealPlanOf, ct);

    public Task<CommandResult<ExtraItemView>> RetireExtraItemAsync(CommandContext ctx, ExtraItemRef req, CancellationToken ct = default) =>
        StatusAs(ctx, OpRetireExtraItem, CatalogueMaster.Extras, req, req?.ExtraItemId ?? 0, req?.ExpectedVersion, false, ExtraOf, ct);

    public Task<CommandResult<ExtraItemView>> ReactivateExtraItemAsync(CommandContext ctx, ExtraItemRef req, CancellationToken ct = default) =>
        StatusAs(ctx, OpReactivateExtraItem, CatalogueMaster.Extras, req, req?.ExtraItemId ?? 0, req?.ExpectedVersion, true, ExtraOf, ct);

    public Task<CommandResult<DiscountReasonView>> RetireDiscountReasonAsync(CommandContext ctx, DiscountReasonRef req, CancellationToken ct = default) =>
        StatusAs(ctx, OpRetireDiscountReason, CatalogueMaster.DiscountReasons, req, req?.DiscountReasonId ?? 0, req?.ExpectedVersion, false, ReasonOf, ct);

    public Task<CommandResult<DiscountReasonView>> ReactivateDiscountReasonAsync(CommandContext ctx, DiscountReasonRef req, CancellationToken ct = default) =>
        StatusAs(ctx, OpReactivateDiscountReason, CatalogueMaster.DiscountReasons, req, req?.DiscountReasonId ?? 0, req?.ExpectedVersion, true, ReasonOf, ct);

    public Task<CommandResult<CatalogueDeleteResult>> DeleteMealPlanAsync(CommandContext ctx, MealPlanRef req, CancellationToken ct = default) =>
        DeleteAsync(ctx, OpDeleteMealPlan, CatalogueMaster.MealPlans, req, req?.MealPlanId ?? 0, req?.ExpectedVersion, ct);

    public Task<CommandResult<CatalogueDeleteResult>> DeleteExtraItemAsync(CommandContext ctx, ExtraItemRef req, CancellationToken ct = default) =>
        DeleteAsync(ctx, OpDeleteExtraItem, CatalogueMaster.Extras, req, req?.ExtraItemId ?? 0, req?.ExpectedVersion, ct);

    public Task<CommandResult<CatalogueDeleteResult>> DeleteDiscountReasonAsync(CommandContext ctx, DiscountReasonRef req, CancellationToken ct = default) =>
        DeleteAsync(ctx, OpDeleteDiscountReason, CatalogueMaster.DiscountReasons, req, req?.DiscountReasonId ?? 0, req?.ExpectedVersion, ct);

    public Task<CommandResult<CatalogueOrderResult>> ReorderMealPlansAsync(CommandContext ctx, ReorderMealPlansRequest req, CancellationToken ct = default) =>
        ReorderAsync(ctx, OpReorderMealPlans, CatalogueMaster.MealPlans, req, req?.MealPlanIds, ct);

    public Task<CommandResult<CatalogueOrderResult>> ReorderExtraItemsAsync(CommandContext ctx, ReorderExtraItemsRequest req, CancellationToken ct = default) =>
        ReorderAsync(ctx, OpReorderExtraItems, CatalogueMaster.Extras, req, req?.ExtraItemIds, ct);

    public Task<CommandResult<CatalogueOrderResult>> ReorderDiscountReasonsAsync(CommandContext ctx, ReorderDiscountReasonsRequest req, CancellationToken ct = default) =>
        ReorderAsync(ctx, OpReorderDiscountReasons, CatalogueMaster.DiscountReasons, req, req?.DiscountReasonIds, ct);

    private async Task<CommandResult<TView>> StatusAs<TReq, TView>(CommandContext ctx, string op, CatalogueMaster m, TReq req, int id, string? version, bool active,
        Func<CatRow, int, TView> view, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireMoneyAsync(ctx, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(version);
        var r = await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            var rows = await store.LockRowsAsync(tx, m, ctx.PropertyId, c).ConfigureAwait(false);
            var row = rows.FirstOrDefault(x => x.Id == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {m.Singular} was not found.");
            VersionToken.EnsureMatches(row.RowVer, expected, $"This {m.Singular}");
            if (row.Active == active) throw new HccException(HccErrorCode.INVALID_TRANSITION, active ? $"{row.Name} is already active." : $"{row.Name} is already retired.");
            if (active && rows.Any(x => x.Id != row.Id && x.Active && string.Equals(x.Name, row.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {m.Singular} is already called “{row.Name}” — rename one first.", "name");
            var date = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await store.SetActiveAsync(tx, m, row.Id, expected, active, active ? null : date, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            await history.AddHistoryAsync(tx, ctx.PropertyId, m.Id, row.Id, row.Code, "active", row.Active ? "active" : "retired", active ? "active" : "retired", ctx.PrincipalId, Now, c).ConfigureAwait(false);
            var used = await store.UsageAsync(tx, m, row.Id, c).ConfigureAwait(false);
            var detail = $"Master {m.Label}: active {(row.Active ? "yes" : "no")} → {(active ? "yes" : "no")} ({row.Code} · {row.Name} {(active ? "reactivated" : "retired")}"
                + (!active && used.Count > 0 ? $"; still used: {used.Count}, old records keep the name)" : ")");
            return await FinishAsync(tx, ctx, m, row.Id, active ? "reactivated" : "retired", detail, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return new CommandResult<TView>(view(r.Value.Row, r.Value.Usage), r.Replayed);
    }

    private async Task<CommandResult<CatalogueDeleteResult>> DeleteAsync<TReq>(CommandContext ctx, string op, CatalogueMaster m, TReq req, int id, string? version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireMoneyAsync(ctx, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(version);
        return await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            var row = (await store.LockRowsAsync(tx, m, ctx.PropertyId, c).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {m.Singular} was not found.");
            VersionToken.EnsureMatches(row.RowVer, expected, $"This {m.Singular}");
            var used = await store.UsageAsync(tx, m, row.Id, c).ConfigureAwait(false);
            MastersRules.EnsureDeletable(row.Name, used.Count, string.Join(", ", used.Where));
            await store.DeleteAsync(tx, m, row.Id, expected, c).ConfigureAwait(false);
            await history.AddHistoryAsync(tx, ctx.PropertyId, m.Id, row.Id, row.Code, "deleted", row.Name, null, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            await tx.AuditAsync(m.AuditAction, Target(m, row.Id), $"Master {m.Label}: {row.Code} · {row.Name} deleted (never used)", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", row.Id, new { master = m.Id, rowId = row.Id, action = "deleted" }, c).ConfigureAwait(false);
            return new CatalogueDeleteResult(m.Id, row.Id, row.Code, true);
        }, ct).ConfigureAwait(false);
    }

    private async Task<CommandResult<CatalogueOrderResult>> ReorderAsync<TReq>(CommandContext ctx, string op, CatalogueMaster m, TReq req, IReadOnlyList<int>? ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireMoneyAsync(ctx, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            var rows = await store.LockRowsAsync(tx, m, ctx.PropertyId, c).ConfigureAwait(false);
            var order = MastersRules.Reorder(rows.Select(x => x.Id).ToList(), ids, m.Singular);
            var moved = new List<string>();
            foreach (var row in rows)
            {
                var n = order[row.Id];
                if (n == row.SortOrder) continue;
                await store.SetSortAsync(tx, m, row.Id, n, ctx.PrincipalId, Now, c).ConfigureAwait(false);
                await history.AddHistoryAsync(tx, ctx.PropertyId, m.Id, row.Id, row.Code, "sortOrder", row.SortOrder.ToString(CultureInfo.InvariantCulture),
                    n.ToString(CultureInfo.InvariantCulture), ctx.PrincipalId, Now, c).ConfigureAwait(false);
                moved.Add(row.Code);
            }

            if (moved.Count == 0) throw HccException.Validation("The order did not change.");
            var codes = rows.OrderBy(x => order[x.Id]).Select(x => x.Code).ToList();
            await tx.AuditAsync(m.AuditAction, $"{m.Table}:order", $"Master {m.Label}: sortOrder changed for {string.Join(", ", moved)} (new order {string.Join(", ", codes)})", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", 0, new { master = m.Id, action = "reordered" }, c).ConfigureAwait(false);
            return new CatalogueOrderResult(m.Id, codes);
        }, ct).ConfigureAwait(false);
    }

    // ───────────────────────── PostExtra (catalogue charge) ─────────────────────────

    public async Task<CommandResult<ChargeResult>> PostExtraAsync(CommandContext ctx, PostExtraRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CataloguePermissions.ChargePost, null, null, ct).ConfigureAwait(false);
        var qty = CatalogueRules.Quantity(req.Quantity);
        string? gstOverride = string.IsNullOrWhiteSpace(req.GstCategory) ? null : CatalogueRules.GstCategory(req.GstCategory);
        return await runner.RunAsync(ctx with { Operation = OpPostExtra }, req, async (tx, c) =>
        {
            var f = await FolioAsync(tx, ctx, req.FolioId, c).ConfigureAwait(false);
            if (!f.IsOpen) throw Invalid("This folio is no longer open.");
            var item = (await store.LockRowsAsync(tx, CatalogueMaster.Extras, ctx.PropertyId, c).ConfigureAwait(false)).FirstOrDefault(x => x.Id == req.ExtraItemId)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Choose an item from the catalogue, or “Other (describe)” for a free-text charge.");
            if (!item.Active) throw MastersRules.Fail(MasterReasons.Retired, $"{item.Name} is retired — choose another item or “Other (describe)”.", "extraItemId");
            var price = CatalogueRules.AmountOn(item.Prices, f.BusinessDate);
            var amountValue = req.AmountPaise ?? (price is { } p ? p * qty : throw HccException.Validation($"{item.Name} has no price on {f.BusinessDate:yyyy-MM-dd} — enter the amount."));
            if (amountValue <= 0 || amountValue > CatalogueRules.AmountMax) throw HccException.Validation("Enter an amount greater than zero.");
            var amount = new Paise(amountValue);
            var label = CatalogueRules.ChargeLabel(req.Label, item.Name, qty);
            var category = GstCategories.Parse(gstOverride ?? item.F("gst_category") ?? "other");
            var g = await gst.ChargeAsync(tx, ctx.PropertyId, f.BusinessDate, category, c).ConfigureAwait(false);
            var line = new NewCharge(f.FolioId, ChargeKind.Service, f.BusinessDate, label, amount, g.Bps, GstMath.LineTax(amount, g.Bps), null, null, ctx.PrincipalId, Now);
            var chargeId = await billing.InsertChargeAsync(tx, line, c).ConfigureAwait(false);
            await RecordGstAsync(tx, chargeId, g, line.Tax, c).ConfigureAwait(false);
            await store.LinkChargeAsync(tx, chargeId, item.Id, null, null, qty, c).ConfigureAwait(false);
            var version = await billing.TouchFolioAsync(tx, f.FolioId, c).ConfigureAwait(false);
            var edited = new[] { label != CatalogueRules.ChargeLabel(null, item.Name, qty) ? "name" : "", price is { } q && amountValue != q * qty ? "price" : "",
                gstOverride is not null && gstOverride != item.F("gst_category") ? "GST class" : "" }.Where(x => x.Length > 0).ToList();
            await tx.AuditAsync("CHARGE_POSTED", "folio:" + f.FolioId.ToString(CultureInfo.InvariantCulture),
                $"{label} · {amount} + tax {line.Tax} · catalogue {item.Code}{(qty > 1 ? " × " + qty.ToString(CultureInfo.InvariantCulture) : "")}{(edited.Count > 0 ? $" (changed: {string.Join(", ", edited)})" : "")}", c).ConfigureAwait(false);
            await tx.PublishAsync("ChargePosted", f.FolioId, new { folioId = f.FolioId, chargeId, kind = "service", serviceDate = f.BusinessDate, amountPaise = amount.Value, taxPaise = line.Tax.Value }, c).ConfigureAwait(false);
            return new ChargeResult(chargeId, f.FolioId, "service", f.BusinessDate, label, amount.Value, g.Bps, line.Tax.Value, true, version);
        }, ct).ConfigureAwait(false);
    }

    // ───────────────────────── Meal plan of a booking, plan lines ─────────────────────────

    public async Task<CommandResult<ReservationMealPlanResult>> SetReservationMealPlanAsync(CommandContext ctx, SetReservationMealPlanRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var code = string.IsNullOrWhiteSpace(req.MealPlanCode) ? null : req.MealPlanCode.Trim().ToUpperInvariant();
        return await runner.RunAsync(ctx with { Operation = OpSetReservationMealPlan }, req, async (tx, c) =>
        {
            var res = await store.LockReservationAsync(tx, ctx.PropertyId, req.ReservationId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Booking not found.");
            var perm = res.Status switch
            {
                "Held" or "Confirmed" => CataloguePermissions.Reservation,
                "InHouse" => CataloguePermissions.ChargePost,
                _ => throw Invalid("This booking is no longer open."),
            };
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, perm, null, null, c).ConfigureAwait(false);
            CatRow? plan = null;
            if (code is not null)
            {
                plan = (await store.LockRowsAsync(tx, CatalogueMaster.MealPlans, ctx.PropertyId, c).ConfigureAwait(false)).FirstOrDefault(x => x.Code == code)
                    ?? throw MastersRules.Fail(MasterReasons.Invalid, $"Meal plan {code} is not in the Meal plans list.", "plan");
                if (!plan.Active && plan.Id != res.MealPlanId) throw MastersRules.Fail(MasterReasons.Retired, $"{plan.Name} is retired — choose an active meal plan.", "plan");
                if (res.RoomTypeCode is { } rt && !CatalogueRules.AllowsRoomType(plan.F("room_types") ?? "", rt))
                    throw MastersRules.Fail(MasterReasons.Invalid, $"{plan.Name} is not offered for {rt} rooms.", "plan");
            }

            if (plan?.Id == res.MealPlanId) throw HccException.Validation("Nothing changed.");
            await store.SetReservationPlanAsync(tx, res.ReservationId, plan?.Id, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            await tx.AuditAsync("CAT_MEAL_PLAN_SET", "reservation:" + res.ReservationId.ToString(CultureInfo.InvariantCulture),
                $"meal plan {res.MealPlanCode ?? "none"} → {plan?.Code ?? "none"}{(res.Status == "InHouse" ? " (from the next room night posted)" : "")}", c).ConfigureAwait(false);
            await tx.PublishAsync("ReservationMealPlanChanged", res.ReservationId, new { reservationId = res.ReservationId, mealPlan = plan?.Code }, c).ConfigureAwait(false);
            return new ReservationMealPlanResult(res.ReservationId, plan?.Code, res.MealPlanCode);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MealPlanNightsResult>> PostMealPlanNightsAsync(CommandContext ctx, PostMealPlanNightsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CataloguePermissions.NightPost, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpPostMealPlanNights }, req, async (tx, c) =>
        {
            var business = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var date = req.ServiceDate ?? business;
            if (date != business) throw date > business ? HccException.Validation("Plan lines cannot be posted ahead of the business date.") : Hcc.Domain.Closing.CloseRules.DayClosed(date);
            var posted = new List<long>();
            var already = 0;
            foreach (var x in await store.PlanNightCandidatesAsync(tx, ctx.PropertyId, date, c).ConfigureAwait(false))
            {
                var each = x.AddOnPaise ?? 0;
                var amount = new Paise(each * x.Guests);
                if (amount <= Paise.Zero) continue;
                var g = x.GstTreatment == "bundled"
                    ? await gst.RoomNightAsync(tx, ctx.PropertyId, date, new Paise(x.RoomAmountPaise) + amount, c).ConfigureAwait(false)
                    : await gst.ChargeAsync(tx, ctx.PropertyId, date, GstCategory.Food, c).ConfigureAwait(false);
                var label = CatalogueRules.PlanLineLabel(x.PlanCode, x.Guests, each, date);
                var line = new NewCharge(x.FolioId, ChargeKind.Service, date, label, amount, g.Bps, GstMath.LineTax(amount, g.Bps), null, null, ctx.PrincipalId, Now);
                var chargeId = await billing.InsertChargeAsync(tx, line, c).ConfigureAwait(false);
                if (!await store.InsertPlanNightAsync(tx, x.FolioId, date, chargeId, c).ConfigureAwait(false))
                    throw new HccException(HccErrorCode.STALE_VERSION, "Plan lines were posted at the same time. Run again.");
                await RecordGstAsync(tx, chargeId, g, line.Tax, c).ConfigureAwait(false);
                await store.LinkChargeAsync(tx, chargeId, null, x.MealPlanId, null, null, c).ConfigureAwait(false);
                await billing.TouchFolioAsync(tx, x.FolioId, c).ConfigureAwait(false);
                await tx.PublishAsync("ChargePosted", x.FolioId, new { folioId = x.FolioId, chargeId, kind = "service", serviceDate = date, amountPaise = amount.Value, taxPaise = line.Tax.Value }, c).ConfigureAwait(false);
                posted.Add(chargeId);
            }

            await tx.AuditAsync("CAT_PLAN_NIGHTS", "property:" + ctx.PropertyId.ToString(CultureInfo.InvariantCulture),
                $"Meal plan lines {date:yyyy-MM-dd} · {posted.Count} posted", c).ConfigureAwait(false);
            return new MealPlanNightsResult(date, posted.Count, already, posted);
        }, ct).ConfigureAwait(false);
    }

    // ───────────────────────── Reasoned discount ─────────────────────────

    public async Task<CommandResult<AdjustmentResult>> RequestReasonedDiscountAsync(CommandContext ctx, RequestReasonedDiscountRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireRequesterAsync(ctx, ct).ConfigureAwait(false);
        if (req.AmountPaise <= 0 || req.AmountPaise > CatalogueRules.AmountMax) throw HccException.Validation("Enter an amount greater than zero.");
        var amount = new Paise(req.AmountPaise);
        var code = (req.ReasonCode ?? string.Empty).Trim().ToUpperInvariant();
        var note = (req.Note ?? string.Empty).Trim();
        if (note.Length > 100) throw HccException.Validation("Keep the note to 100 characters.");
        return await runner.RunAsync(ctx with { Operation = OpRequestReasonedDiscount }, req, async (tx, c) =>
        {
            var f = await FolioAsync(tx, ctx, req.FolioId, c).ConfigureAwait(false);
            if (!string.Equals(f.Version, req.ExpectedVersion, StringComparison.Ordinal))
                throw new HccException(HccErrorCode.STALE_VERSION, "The folio was changed by someone else. Reload and try again.");
            if (!f.IsOpen) throw Invalid("This booking is no longer open.");
            var r = (await store.LockRowsAsync(tx, CatalogueMaster.DiscountReasons, ctx.PropertyId, c).ConfigureAwait(false)).FirstOrDefault(x => x.Code == code)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Choose a discount reason, or “Other” and type the reason.");
            if (!r.Active) throw MastersRules.Fail(MasterReasons.Retired, $"{r.Name} is retired — choose another reason.", "reasonCode");
            var mine = await store.RoleCodesAsync(tx, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            var allowed = CatalogueRules.SplitList(r.F("roles"));
            if (allowed.Count > 0 && !allowed.Any(mine.Contains)) throw new HccException(HccErrorCode.FORBIDDEN, $"Access denied: “{r.Name}” is used by {string.Join(", ", allowed)} only.");
            var lines = await billing.LinesAsync(tx, f.FolioId, c).ConfigureAwait(false);
            var figures = FolioStatement.Compute(lines, await billing.ReceiptsAsync(tx, f.FolioId, c).ConfigureAwait(false));
            var maxBps = int.Parse(r.F("max_bps")!, CultureInfo.InvariantCulture);
            int? aboveBps = r.F("approval_above_bps") is { } s ? int.Parse(s, CultureInfo.InvariantCulture) : null;
            var caps = CatalogueRules.ReasonCaps(figures.Net.Value, maxBps, aboveBps);
            if (amount.Value > caps.Max)
                throw HccException.Validation($"“{r.Name}” allows at most {maxBps / 100m:0.##} % of the charges ({new Paise(caps.Max)} on this folio).");
            if (amount > figures.Net) throw HccException.Validation("Discount cannot exceed existing net charges.");
            var reason = note.Length > 0 ? $"{r.Name} · {note}" : r.Name;
            var forced = caps.ApprovalAbove is { } t && amount.Value > t && !mine.Contains("OWN");
            if (!forced && await WithinLimitAsync(ctx, amount, c).ConfigureAwait(false))
            {
                var g = await AdjustmentRateAsync(tx, ctx, f, lines, c).ConfigureAwait(false);
                var line = new NewCharge(f.FolioId, ChargeKind.Discount, f.BusinessDate, "Discount · " + reason, -amount, g.Bps, GstMath.LineTax(-amount, g.Bps), null, null, ctx.PrincipalId, Now);
                var chargeId = await billing.InsertChargeAsync(tx, line, c).ConfigureAwait(false);
                await RecordGstAsync(tx, chargeId, g, line.Tax, c).ConfigureAwait(false);
                await store.LinkChargeAsync(tx, chargeId, null, null, r.Id, null, c).ConfigureAwait(false);
                var version = await billing.TouchFolioAsync(tx, f.FolioId, c).ConfigureAwait(false);
                await tx.AuditAsync("DISCOUNT_POSTED", "folio:" + f.FolioId.ToString(CultureInfo.InvariantCulture), $"{amount} · {reason} · reason {r.Code} · within limit", c).ConfigureAwait(false);
                await tx.PublishAsync("ChargePosted", f.FolioId, new { folioId = f.FolioId, chargeId, kind = "discount", serviceDate = f.BusinessDate, amountPaise = line.Amount.Value, taxPaise = line.Tax.Value }, c).ConfigureAwait(false);
                return new AdjustmentResult("Posted", null, null, chargeId, version);
            }

            var now = Now;
            var approvals = await billing.ApprovalsForFolioAsync(tx, f.FolioId, c).ConfigureAwait(false);
            if (approvals.Any(x => x.Action == AdjustmentAction.Discount && x.Status == ApprovalStatus.Pending && !ApprovalRules.IsExpired(x.Status, x.Action, x.ExpiresUtc, now)))
                throw Invalid("A discount request is already pending.");
            var (approvalId, approvalVersion) = await billing.InsertApprovalAsync(tx, new NewApproval(ctx.PropertyId, AdjustmentAction.Discount, f.FolioId, null, "folio", f.FolioId,
                amount, f.Version, null, null, reason, ctx.PrincipalId, now, now + ApprovalRules.TimeToLive), c).ConfigureAwait(false);
            await store.LinkApprovalAsync(tx, approvalId, r.Id, c).ConfigureAwait(false);
            await tx.AuditAsync("ADJUSTMENT_REQUESTED", "approval:" + approvalId.ToString(CultureInfo.InvariantCulture),
                $"discount · {amount} · folio {f.FolioId} · {reason} · reason {r.Code}{(forced ? $" (above {aboveBps / 100m:0.##} % of the charges)" : "")}", c).ConfigureAwait(false);
            await tx.PublishAsync("ApprovalRequested", approvalId, new { approvalId, action = "discount", folioId = f.FolioId, amountPaise = amount.Value }, c).ConfigureAwait(false);
            return new AdjustmentResult("PendingApproval", approvalId, approvalVersion, null, f.Version);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Holding PERM-FOL-003 with a limit (APPROVAL_REQUIRED) is enough to request; FORBIDDEN still refuses.</summary>
    private async Task RequireRequesterAsync(CommandContext ctx, CancellationToken ct)
    {
        try
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CataloguePermissions.Discount, null, null, ct).ConfigureAwait(false);
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.APPROVAL_REQUIRED)
        {
            // limited holder: the request path decides
        }
    }

    private async Task<bool> WithinLimitAsync(CommandContext ctx, Paise amount, CancellationToken ct)
    {
        try
        {
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CataloguePermissions.Discount, null, amount.Value, ct).ConfigureAwait(false);
            return true;
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.APPROVAL_REQUIRED)
        {
            return false;
        }
    }

    /// <summary>Discounts reduce the room supply (BillingService.AdjustmentRateAsync, prototype gstForFolioAdjustment).</summary>
    private async Task<GstRate> AdjustmentRateAsync(ICommandTransaction tx, CommandContext ctx, FolioHeader f, IReadOnlyList<PostedLine> lines, CancellationToken ct)
    {
        var rooms = lines.Where(l => l.Kind == ChargeKind.Room && l.ReversesChargeId is null && !lines.Any(r => r.ReversesChargeId == l.ChargeId)).OrderBy(l => l.ServiceDate).ToList();
        var source = rooms.FirstOrDefault(l => l.ServiceDate == f.BusinessDate) ?? rooms.LastOrDefault(l => l.ServiceDate <= f.BusinessDate) ?? rooms.LastOrDefault();
        if (source is not null)
        {
            var snap = snapshots is null ? null : await snapshots.FindAsync(tx, source.ChargeId, ct).ConfigureAwait(false);
            return new GstRate(source.TaxBps, snap?.Itc ?? false, GstCategory.Room, "Follows the room-night rate");
        }

        var booked = await billing.ReservationNightlyRateAsync(tx, f.ReservationId, ct).ConfigureAwait(false) ?? Paise.Zero;
        return await gst.RoomNightAsync(tx, ctx.PropertyId, f.BusinessDate, booked, ct).ConfigureAwait(false);
    }

    private Task RecordGstAsync(ICommandTransaction tx, long chargeId, GstRate rate, Paise tax, CancellationToken ct)
    {
        if (snapshots is null) return Task.CompletedTask;
        var split = GstMath.Split(tax);
        return snapshots.RecordAsync(tx, new GstChargeSnapshot(chargeId, rate.Category, rate.Bps, rate.Itc, tax, split.Cgst, split.Sgst, rate.Rule), ct);
    }

    private async Task<FolioHeader> FolioAsync(ICommandTransaction tx, CommandContext ctx, long folioId, CancellationToken ct)
    {
        var f = await billing.LockFolioAsync(tx, folioId, ct).ConfigureAwait(false);
        if (f is null || f.PropertyId != ctx.PropertyId) throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
        return f;
    }

    private static HccException Invalid(string message) => new(HccErrorCode.INVALID_TRANSITION, message);

    private static string Target(CatalogueMaster m, int id) => m.Table + ":" + id.ToString(CultureInfo.InvariantCulture);

    // ───────────────────────── Views ─────────────────────────

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IReadOnlyList<DatedAmount> Dated(CatRow r) => r.Prices.Select(p => new DatedAmount(Day(p.From), p.Amount)).ToList();

    private static string Utc(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    public static MealPlanView MealPlanOf(CatRow r, int usage)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new(r.Id, r.Code, r.Name, (r.F("meals") ?? "").Select(ch => ch.ToString()).ToList(), r.F("gst_treatment") ?? "separate",
            CatalogueRules.SplitList(r.F("room_types")), Dated(r), r.SortOrder, r.Active, r.RetiredOn is { } d ? Day(d) : null, usage, Utc(r.UpdatedUtc), VersionToken.ToWire(r.RowVer));
    }

    public static ExtraItemView ExtraOf(CatRow r, int usage)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new(r.Id, r.Code, r.Name, r.F("category") ?? "misc", r.F("gst_category") ?? "other", r.F("unit") ?? "each", r.F("department"), Dated(r), r.SortOrder,
            r.Active, r.RetiredOn is { } d ? Day(d) : null, usage, Utc(r.UpdatedUtc), VersionToken.ToWire(r.RowVer));
    }

    public static DiscountReasonView ReasonOf(CatRow r, int usage)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new(r.Id, r.Code, r.Name, int.Parse(r.F("max_bps") ?? "10000", CultureInfo.InvariantCulture),
            r.F("approval_above_bps") is { } a ? int.Parse(a, CultureInfo.InvariantCulture) : null, CatalogueRules.SplitList(r.F("roles")), r.SortOrder, r.Active,
            r.RetiredOn is { } d ? Day(d) : null, usage, Utc(r.UpdatedUtc), VersionToken.ToWire(r.RowVer));
    }
}
