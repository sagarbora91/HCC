using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Masters;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Contracts.Pos;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;
using Hcc.Domain.Money;
using Hcc.Domain.Pos;

namespace Hcc.Application.Pos;

/// <summary>
/// MA-4 POS masters commands (prototype MST_MASTER_SAVE / _RETIRE / _REACTIVATE / _REORDER / _DELETE for the lists outlets, posTables,
/// menuCategories, menuItems): SaveOutlet, SaveOutletTable, SaveMenuCategory, SaveMenuItem and the generic Retire / Reactivate / Reorder /
/// Delete per list. PERM-MST-001 for every change (DEC-MA-1: Owner, Tech admin); PERM-MST-002 (Owner, DEC-058) when an outlet's GST profile,
/// an item's prices or its GST class change. Every command runs through <see cref="ICommandRunner"/> (Idempotency-Key, audit, outbox
/// "MasterChanged"), checks expectedVersion on edits (STALE_VERSION) and writes one mst.master_history row per changed field.
/// Masters are configuration: no business-day lock. Rules: <see cref="PosMasterRules"/> + <see cref="MastersRules"/>.
/// </summary>
public sealed class PosMastersService(ICommandRunner runner, IAccessPolicy policy, IPosMastersStore store, IPosStore pos, TimeProvider clock)
{
    private sealed record Desc(PosMasterTable Table, string Master, string Label, string Singular, string Audit, string Target, string UsedBy);

    private static readonly Desc DOutlet = new(PosMasterTable.Outlet, PosMasterRules.Outlets, "Outlets", "outlet", "MST_POS_OUTLET", "pos-outlet", "checks, menu items or tables");
    private static readonly Desc DTable = new(PosMasterTable.Table, PosMasterRules.Tables, "Tables and counters", "table", "MST_POS_TABLE", "pos-table", "checks");
    private static readonly Desc DCategory = new(PosMasterTable.Category, PosMasterRules.Categories, "Menu categories", "menu category", "MST_POS_CATEGORY", "pos-category", "menu items");
    private static readonly Desc DItem = new(PosMasterTable.Item, PosMasterRules.Items, "Menu items", "menu item", "MST_POS_ITEM", "pos-menu", "check lines");

    private static Desc Of(PosMasterTable t) => t switch
    {
        PosMasterTable.Outlet => DOutlet,
        PosMasterTable.Table => DTable,
        PosMasterTable.Category => DCategory,
        _ => DItem,
    };

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static CommandContext Ctx(PosCaller c, string operation, string key) => new(c.PropertyId, c.PrincipalId, operation, key);

    private static string Target(Desc d, int id) => d.Target + ":" + id.ToString(CultureInfo.InvariantCulture);

    private static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static HccException NotFound(string m) => new(HccErrorCode.NOT_FOUND, m);

    private Task RequireAsync(PosCaller c, CancellationToken ct) => policy.RequireAsync(c.PropertyId, c.PrincipalId, MastersPermissions.Manage, ct: ct);

    /// <summary>Money fields (DEC-058): PERM-MST-002 (Owner) — checked only when a money field actually changes.</summary>
    private async Task RequireMoneyAsync(PosCaller c, string fields, CancellationToken ct)
    {
        if (!await policy.HasAsync(c.PropertyId, c.PrincipalId, MastersPermissions.ManageMoney, ct: ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, $"Access denied: Owner changes this — money lists and money fields are Owner-only (DEC-058) ({fields}).");
    }

    private static void Unique(IEnumerable<PosMasterBase> rows, int? id, MastersRules.CleanRow clean, Desc d)
    {
        var list = rows.ToList();
        if (list.Any(r => r.Id != id && string.Equals(r.Code, clean.Code, StringComparison.Ordinal)))
            throw MastersRules.Fail(MasterReasons.CodeTaken, $"Code {clean.Code} is already used in {d.Label} (retired rows keep their code).", "code");
        if (list.Any(r => r.Id != id && r.Active && string.Equals(r.Name, clean.Name, StringComparison.OrdinalIgnoreCase)))
            throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {d.Singular} is already called “{clean.Name}”.", "name");
    }

    /// <summary>Changed fields (all non-empty ones for a new row), in the given order.</summary>
    private static List<(string Field, string? Old, string? New)> Diff(bool created, params (string Field, string? Old, string? New)[] pairs) =>
        pairs.Where(p => created ? !string.IsNullOrEmpty(p.New) : !string.Equals(p.Old, p.New, StringComparison.Ordinal)).Select(p => (p.Field, created ? null : p.Old, p.New)).ToList();

    private static string YesNo(bool v) => v ? "Yes" : "No";

    private async Task<PosMasterResult> FinishAsync(ICommandTransaction tx, PosCaller c, Desc d, int id, string code, string name, bool created,
        IReadOnlyList<(string Field, string? Old, string? New)> changes, CancellationToken ct)
    {
        var now = Now;
        foreach (var (f, o, n) in changes) await store.AddHistoryAsync(tx, c.PropertyId, d.Master, id, code, f, o, n, c.PrincipalId, now, ct).ConfigureAwait(false);
        var detail = created ? $"Master {d.Label}: added {code} · {name}" : MastersRules.AuditText(d.Label, changes);
        await tx.AuditAsync(d.Audit, Target(d, id), detail, ct).ConfigureAwait(false);
        await tx.PublishAsync("MasterChanged", id, new { master = d.Master, rowId = id, action = created ? "created" : "changed" }, ct).ConfigureAwait(false);
        return new PosMasterResult(d.Master, id, code, await VersionAsync(tx, c, d, id, ct).ConfigureAwait(false), created ? "created" : "changed");
    }

    private async Task<string?> VersionAsync(ICommandTransaction tx, PosCaller c, Desc d, int id, CancellationToken ct)
    {
        var row = (await store.LockBaseAsync(tx, d.Table, c.PropertyId, ct).ConfigureAwait(false)).FirstOrDefault(r => r.Id == id);
        return row is null ? null : VersionToken.ToWire(row.RowVer);
    }

    // ── Outlets (M26) ──

    public async Task<CommandResult<PosMasterResult>> SaveOutletAsync(PosCaller caller, string key, SaveOutletRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(caller, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(req.Code, req.Name, req.SortOrder);
        var type = PosMasterRules.Type(req.Type);
        var profile = PosMasterRules.Profile(req.GstProfile);
        var opens = PosMasterRules.Time(req.Opens, "opens");
        var closes = PosMasterRules.Time(req.Closes, "closes");
        var station = PosMasterRules.Text(req.KotStation, 20, "kotStation", "KOT station");
        var expected = req.OutletId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(Ctx(caller, "SaveOutlet", key), req, async (tx, c) =>
        {
            var rows = await store.LockOutletsAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            OutletRec? old = null;
            if (req.OutletId is { } id)
            {
                old = rows.FirstOrDefault(r => r.OutletId == id) ?? throw NotFound("This outlet was not found.");
                VersionToken.EnsureMatches(old.RowVer, expected!, "This outlet");
                MastersRules.EnsureActive(old.Active, old.Name);
            }

            Unique(rows.Select(r => new PosMasterBase(r.OutletId, r.Code, r.Name, r.SortOrder, r.Active, r.RetiredOn, r.RowVer)), old?.OutletId, clean, DOutlet);
            if (old is not null)
            {
                var used = await store.UsageAsync(tx, PosMasterTable.Outlet, old.OutletId, c).ConfigureAwait(false);
                MastersRules.EnsureCodeMayChange(old.Code, clean.Code, used.Count, DOutlet.UsedBy);
                if (PosMasterRules.KindOf(old.Type) != PosMasterRules.KindOf(type) && used.Where.Any(w => w.StartsWith("Checks", StringComparison.Ordinal) || w.StartsWith("Tables", StringComparison.Ordinal)))
                    throw MastersRules.Fail(MasterReasons.Invalid, "This outlet already has checks or tables — room service and table outlets cannot be switched. Add a new outlet instead.", "type");
            }

            var sort = clean.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(rows.Select(r => r.SortOrder));
            var next = new OutletRec(old?.OutletId ?? 0, clean.Code, clean.Name, PosMasterRules.KindOf(type), type, opens, closes, profile, station, sort, true, null, old?.RowVer ?? []);
            var created = old is null;
            var changes = Diff(created, ("code", old?.Code, next.Code), ("name", old?.Name, next.Name), ("type", old?.Type, next.Type), ("opens", old?.Opens, next.Opens),
                ("closes", old?.Closes, next.Closes), ("gstProfile", old?.GstProfile, next.GstProfile), ("kotStation", old?.KotStation, next.KotStation),
                ("sortOrder", old is null ? null : Num(old.SortOrder), Num(sort)));
            if (created ? profile != "hotel" : old!.GstProfile != profile) await RequireMoneyAsync(caller, "GST profile", c).ConfigureAwait(false);
            if (!created && changes.Count == 0) throw HccException.Validation("Nothing changed.");
            int outletId;
            if (created) outletId = await store.InsertOutletAsync(tx, caller.PropertyId, next, caller.PrincipalId, Now, c).ConfigureAwait(false);
            else
            {
                outletId = old!.OutletId;
                await store.UpdateOutletAsync(tx, next, expected!, caller.PrincipalId, Now, c).ConfigureAwait(false);
            }

            return await FinishAsync(tx, caller, DOutlet, outletId, next.Code, next.Name, created, changes, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    // ── Tables and counters (M27) ──

    public async Task<CommandResult<PosMasterResult>> SaveOutletTableAsync(PosCaller caller, string key, SaveOutletTableRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(caller, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(req.Code, req.Name, req.SortOrder);
        var tableNo = PosMasterRules.TableNo(req.TableNo);
        var seats = PosMasterRules.Seats(req.Seats);
        var section = PosMasterRules.Text(req.Section, 40, "section", "Section");
        var expected = req.TableId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(Ctx(caller, "SaveOutletTable", key), req, async (tx, c) =>
        {
            var outlets = await store.LockOutletsAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var rows = await store.LockTablesAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            TableRec? old = null;
            if (req.TableId is { } id)
            {
                old = rows.FirstOrDefault(r => r.TableId == id) ?? throw NotFound("This table was not found.");
                VersionToken.EnsureMatches(old.RowVer, expected!, "This table");
                MastersRules.EnsureActive(old.Active, old.Name);
            }

            var outlet = outlets.FirstOrDefault(o => o.OutletId == req.OutletId) ?? throw MastersRules.Fail(MasterReasons.Invalid, "Choose an outlet.", "outlet");
            if (outlet.Kind != "table") throw MastersRules.Fail(MasterReasons.Invalid, $"{outlet.Name} is room service — it takes checks on rooms, not tables.", "outlet");
            if (!outlet.Active && old?.OutletId != outlet.OutletId) throw MastersRules.Fail(MasterReasons.Retired, $"Outlet: {outlet.Name} is retired — choose an active one.", "outlet");
            Unique(rows.Select(r => new PosMasterBase(r.TableId, r.Code, r.Name, r.SortOrder, r.Active, r.RetiredOn, r.RowVer)), old?.TableId, clean, DTable);
            if (rows.Any(r => r.TableId != old?.TableId && r.Active && r.OutletId == outlet.OutletId && string.Equals(r.TableNo, tableNo, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"{outlet.Name} already has an active {tableNo}.", "tableNo");
            if (old is not null)
            {
                var used = await store.UsageAsync(tx, PosMasterTable.Table, old.TableId, c).ConfigureAwait(false);
                MastersRules.EnsureCodeMayChange(old.Code, clean.Code, used.Count, DTable.UsedBy);
                if ((old.TableNo != tableNo || old.OutletId != outlet.OutletId) && used.Count > 0)
                    throw MastersRules.Fail(MasterReasons.Invalid, $"{old.TableNo} has checks — its number and outlet cannot change. Retire it and add a new one.", "tableNo");
            }

            var sort = clean.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(rows.Select(r => r.SortOrder));
            var next = new TableRec(old?.TableId ?? 0, outlet.OutletId, clean.Code, clean.Name, tableNo, seats, section, req.Counter, sort, true, null, old?.RowVer ?? []);
            var oldOutlet = old is null ? null : outlets.FirstOrDefault(o => o.OutletId == old.OutletId)?.Code;
            var created = old is null;
            var changes = Diff(created, ("code", old?.Code, next.Code), ("name", old?.Name, next.Name), ("outlet", oldOutlet, outlet.Code), ("tableNo", old?.TableNo, next.TableNo),
                ("seats", old?.Seats?.ToString(CultureInfo.InvariantCulture), next.Seats?.ToString(CultureInfo.InvariantCulture)), ("section", old?.Section, next.Section),
                ("counter", old is null ? null : YesNo(old.Counter), YesNo(next.Counter)), ("sortOrder", old is null ? null : Num(old.SortOrder), Num(sort)));
            if (!created && changes.Count == 0) throw HccException.Validation("Nothing changed.");
            int tableId;
            if (created) tableId = await store.InsertTableAsync(tx, next, caller.PrincipalId, Now, c).ConfigureAwait(false);
            else
            {
                tableId = old!.TableId;
                await store.UpdateTableAsync(tx, next, expected!, caller.PrincipalId, Now, c).ConfigureAwait(false);
            }

            return await FinishAsync(tx, caller, DTable, tableId, next.Code, next.Name, created, changes, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    // ── Menu categories (M28) ──

    public async Task<CommandResult<PosMasterResult>> SaveMenuCategoryAsync(PosCaller caller, string key, SaveMenuCategoryRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(caller, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(req.Code, req.Name, req.SortOrder);
        var expected = req.CategoryId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(Ctx(caller, "SaveMenuCategory", key), req, async (tx, c) =>
        {
            var rows = await store.LockCategoriesAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            CategoryRec? old = null;
            if (req.CategoryId is { } id)
            {
                old = rows.FirstOrDefault(r => r.CategoryId == id) ?? throw NotFound("This menu category was not found.");
                VersionToken.EnsureMatches(old.RowVer, expected!, "This menu category");
                MastersRules.EnsureActive(old.Active, old.Name);
                var used = await store.UsageAsync(tx, PosMasterTable.Category, id, c).ConfigureAwait(false);
                MastersRules.EnsureCodeMayChange(old.Code, clean.Code, used.Count, DCategory.UsedBy);
            }

            Unique(rows.Select(r => new PosMasterBase(r.CategoryId, r.Code, r.Name, r.SortOrder, r.Active, r.RetiredOn, r.RowVer)), old?.CategoryId, clean, DCategory);
            var sort = clean.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(rows.Select(r => r.SortOrder));
            var next = new CategoryRec(old?.CategoryId ?? 0, clean.Code, clean.Name, sort, true, null, old?.RowVer ?? []);
            var created = old is null;
            var changes = Diff(created, ("code", old?.Code, next.Code), ("name", old?.Name, next.Name), ("sortOrder", old is null ? null : Num(old.SortOrder), Num(sort)));
            if (!created && changes.Count == 0) throw HccException.Validation("Nothing changed.");
            int categoryId;
            if (created) categoryId = await store.InsertCategoryAsync(tx, caller.PropertyId, next, caller.PrincipalId, Now, c).ConfigureAwait(false);
            else
            {
                categoryId = old!.CategoryId;
                await store.UpdateCategoryAsync(tx, next, expected!, caller.PrincipalId, Now, c).ConfigureAwait(false);
            }

            return await FinishAsync(tx, caller, DCategory, categoryId, next.Code, next.Name, created, changes, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    // ── Menu items (M28) ──

    /// <summary>
    /// SaveMenuItem: the W2-6 request (category by name, one price from the business date) or the MA-4 master body (<c>Prices</c> present:
    /// every master field is taken as sent — category id, dated prices, GST class, short code, station, sold out today).
    /// </summary>
    public async Task<CommandResult<MenuItemResult>> SaveMenuItemAsync(PosCaller caller, string key, SaveMenuItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(caller, ct).ConfigureAwait(false);
        var master = req.Prices is not null;
        var clean = MastersRules.Validate(req.Code, req.Name, req.SortOrder);
        var shortCode = PosMasterRules.ShortCode(req.ShortCode);
        var station = PosMasterRules.Text(req.Station, 20, "station", "Kitchen station");
        var cls = req.GstClass is null ? null : PosMasterRules.GstClass(req.GstClass);
        var outletIds = (req.OutletIds ?? []).Distinct().ToList();
        if (outletIds.Count == 0) throw MastersRules.Fail(MasterReasons.Invalid, "Choose at least one outlet for this item.", "outlets");
        var legacyPrice = Paise.FromWire(req.PricePaise).EnsureFieldRange();
        if (legacyPrice < Paise.Zero) throw HccException.Validation("The price cannot be negative.");
        var sent = master ? req.Prices!.Select(p => (From: ParseDate(p.EffectiveFrom), Paise: p.AmountPaise)).ToList() : null;
        var expected = req.ItemId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(Ctx(caller, "SaveMenuItem", key), req, async (tx, c) =>
        {
            var date = await pos.BusinessDateAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var outlets = await store.LockOutletsAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var cats = (await store.LockCategoriesAsync(tx, caller.PropertyId, c).ConfigureAwait(false)).ToList();
            var items = await store.LockItemsAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            ItemRec? old = null;
            if (req.ItemId is { } id)
            {
                old = items.FirstOrDefault(r => r.ItemId == id) ?? throw NotFound("Menu item not found.");
                VersionToken.EnsureMatches(old.RowVer, expected!, "The menu item");
                MastersRules.EnsureActive(old.Active, old.Name);
                var used = await store.UsageAsync(tx, PosMasterTable.Item, id, c).ConfigureAwait(false);
                MastersRules.EnsureCodeMayChange(old.Code, clean.Code, used.Count, DItem.UsedBy);
            }

            Unique(items.Select(r => new PosMasterBase(r.ItemId, r.Code, r.Name, r.SortOrder, r.Active, r.RetiredOn, r.RowVer)), old?.ItemId, clean, DItem);
            foreach (var o in outletIds)
            {
                var outlet = outlets.FirstOrDefault(x => x.OutletId == o) ?? throw NotFound("Outlet not found.");
                if (!outlet.Active && old?.OutletIds.Contains(o) != true) throw MastersRules.Fail(MasterReasons.Retired, $"Sold at: {outlet.Name} is retired — choose an active one.", "outlets");
            }

            // Category: by id (master body), else by name — found among the categories or added to them (W2-6 body, import).
            CategoryRec category;
            if (req.CategoryId is { } cid)
            {
                category = cats.FirstOrDefault(x => x.CategoryId == cid) ?? throw MastersRules.Fail(MasterReasons.Invalid, "Category: not in Menu categories.", "category");
                if (!category.Active && old?.CategoryId != cid) throw MastersRules.Fail(MasterReasons.Retired, $"Category: {category.Name} is retired — choose an active one.", "category");
            }
            else
            {
                var name = (req.Category ?? string.Empty).Trim();
                if (name.Length == 0 || name.Length > MastersRules.NameMax) throw MastersRules.Fail(MasterReasons.Invalid, "Enter a category (at most 60 characters).", "category");
                category = cats.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Active).FirstOrDefault()
                    ?? await AddCategoryAsync(tx, caller, cats, name, c).ConfigureAwait(false);
            }

            // Prices: amounts in effect never change (dated money, DEC-058); the W2-6 body sets one price from the business date.
            var before = old?.Prices ?? [];
            IReadOnlyList<(DateOnly From, long Paise)> prices;
            if (sent is not null) prices = PosMasterRules.Prices(sent, before, date);
            else if (old is null) prices = [(PosMasterRules.PriceSince, legacyPrice.Value)];
            else prices = PosMasterRules.PriceOn(before, date) == legacyPrice.Value ? before
                : [.. before.Where(p => p.From != date), (date, legacyPrice.Value)];
            prices = prices.OrderBy(p => p.From).ToList();
            var gst = cls ?? old?.GstClass ?? "food";
            var shortNext = master ? shortCode : shortCode ?? old?.ShortCode;
            var stationNext = master ? station : station ?? old?.Station;
            var sort = clean.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(items.Select(r => r.SortOrder));
            var wasSold = old is not null && PosMasterRules.SoldOut(old.Available, old.SoldOutOn, date);
            (bool available, DateOnly? soldOutOn) = req.SoldOutToday switch
            {
                true when !wasSold => (false, (DateOnly?)date),
                false => (true, (DateOnly?)null),
                _ => old is null ? (true, (DateOnly?)null) : (old.Available, old.SoldOutOn),
            };
            var next = new ItemRec(old?.ItemId ?? 0, clean.Code, clean.Name, category.CategoryId, category.Name, shortNext, gst, req.Veg, stationNext, sort, true, null,
                available, soldOutOn, outletIds, prices, old?.RowVer ?? []);
            var created = old is null;
            string OutletCodes(IEnumerable<int> ids) => string.Join(", ", ids.Select(i => outlets.FirstOrDefault(o => o.OutletId == i)?.Code ?? Num(i)).Order(StringComparer.Ordinal));
            var oldCat = old?.CategoryId is { } oc ? cats.FirstOrDefault(x => x.CategoryId == oc)?.Code : null;
            var changes = Diff(created, ("code", old?.Code, next.Code), ("name", old?.Name, next.Name), ("category", oldCat, category.Code), ("shortCode", old?.ShortCode, next.ShortCode),
                ("price", old is null ? null : PosMasterRules.PricesText(before), PosMasterRules.PricesText(prices)), ("gstClass", old?.GstClass, gst),
                ("veg", old is null ? null : YesNo(old.Veg), YesNo(next.Veg)), ("outlets", old is null ? null : OutletCodes(old.OutletIds), OutletCodes(outletIds)),
                ("station", old?.Station, next.Station), ("soldOutToday", old is null ? null : YesNo(wasSold), YesNo(PosMasterRules.SoldOut(available, soldOutOn, date))),
                ("sortOrder", old is null ? null : Num(old.SortOrder), Num(sort)));
            var moneyFields = new List<string>();
            if (created ? prices.Count > 0 : PosMasterRules.PricesText(before) != PosMasterRules.PricesText(prices)) moneyFields.Add("Price");
            if (created ? gst != "food" : old!.GstClass != gst) moneyFields.Add("GST class");
            if (moneyFields.Count > 0) await RequireMoneyAsync(caller, string.Join(", ", moneyFields), c).ConfigureAwait(false);
            if (!created && changes.Count == 0) throw HccException.Validation("Nothing changed.");
            int itemId;
            if (created) itemId = await store.InsertItemAsync(tx, caller.PropertyId, next, date, caller.PrincipalId, Now, c).ConfigureAwait(false);
            else
            {
                itemId = old!.ItemId;
                await store.UpdateItemAsync(tx, next, expected!, date, caller.PrincipalId, Now, c).ConfigureAwait(false);
            }

            var done = await FinishAsync(tx, caller, DItem, itemId, next.Code, next.Name, created, changes, c).ConfigureAwait(false);
            await tx.AuditAsync("POS_MENU_SAVED", "menu:" + Num(itemId), $"{next.Code} · {next.Name} · {new Paise(PosMasterRules.PriceOn(prices, date) ?? 0)}", c).ConfigureAwait(false);
            return new MenuItemResult(itemId, available, done.Version ?? string.Empty);
        }, ct).ConfigureAwait(false);
    }

    private static DateOnly ParseDate(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
            : throw MastersRules.Fail(MasterReasons.Invalid, "Price: each amount needs an effective-from date (YYYY-MM-DD).", "price");

    /// <summary>A category named by a W2-6 body or an import that does not exist yet: code from the name, unique among the property's codes.</summary>
    private async Task<CategoryRec> AddCategoryAsync(ICommandTransaction tx, PosCaller caller, List<CategoryRec> cats, string name, CancellationToken ct)
    {
        var baseCode = PosMasterRules.CodeOf(name);
        var code = baseCode;
        for (var n = 2; cats.Any(x => x.Code == code); n++)
        {
            var suffix = "-" + Num(n);
            code = (baseCode.Length + suffix.Length > MastersRules.CodeMax ? baseCode[..(MastersRules.CodeMax - suffix.Length)] : baseCode) + suffix;
        }

        var sort = MastersRules.NextSort(cats.Select(x => x.SortOrder));
        var rec = new CategoryRec(0, code, name, sort, true, null, []);
        var id = await store.InsertCategoryAsync(tx, caller.PropertyId, rec, caller.PrincipalId, Now, ct).ConfigureAwait(false);
        await store.AddHistoryAsync(tx, caller.PropertyId, DCategory.Master, id, code, "name", null, name, caller.PrincipalId, Now, ct).ConfigureAwait(false);
        rec = rec with { CategoryId = id };
        cats.Add(rec);
        return rec;
    }

    // ── Retire / reactivate / delete / reorder (any POS list) ──

    public Task<CommandResult<PosMasterResult>> RetireAsync(PosCaller caller, string key, string operation, PosMasterTable table, int id, string? version, object req, CancellationToken ct = default)
        => SetActiveAsync(caller, key, operation, table, id, version, false, req, ct);

    public Task<CommandResult<PosMasterResult>> ReactivateAsync(PosCaller caller, string key, string operation, PosMasterTable table, int id, string? version, object req, CancellationToken ct = default)
        => SetActiveAsync(caller, key, operation, table, id, version, true, req, ct);

    private async Task<CommandResult<PosMasterResult>> SetActiveAsync(PosCaller caller, string key, string operation, PosMasterTable table, int id, string? version, bool active,
        object req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(version);
        var d = Of(table);
        return await runner.RunAsync(Ctx(caller, operation, key), req, async (tx, c) =>
        {
            var rows = await store.LockBaseAsync(tx, table, caller.PropertyId, c).ConfigureAwait(false);
            var row = rows.FirstOrDefault(r => r.Id == id) ?? throw NotFound($"This {d.Singular} was not found.");
            VersionToken.EnsureMatches(row.RowVer, expected, $"This {d.Singular}");
            if (row.Active == active) throw new HccException(HccErrorCode.INVALID_TRANSITION, active ? $"{row.Name} is already active." : $"{row.Name} is already retired.");
            var used = await store.UsageAsync(tx, table, id, c).ConfigureAwait(false);
            if (!active && used.Open > 0)
                throw MastersRules.Fail(MasterReasons.InUse, table == PosMasterTable.Outlet
                    ? $"{row.Name} has {used.Open} open check{(used.Open == 1 ? string.Empty : "s")} — settle or void {(used.Open == 1 ? "it" : "them")} before retiring the outlet."
                    : $"{row.Name} has an open check — settle or void it first.");
            if (active && rows.Any(r => r.Id != id && r.Active && string.Equals(r.Name, row.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {d.Singular} is already called “{row.Name}” — rename one first.", "name");
            var date = await pos.BusinessDateAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            await store.SetActiveAsync(tx, table, id, expected, active, active ? null : date, caller.PrincipalId, Now, c).ConfigureAwait(false);
            await store.AddHistoryAsync(tx, caller.PropertyId, d.Master, id, row.Code, "active", row.Active ? "active" : "retired", active ? "active" : "retired", caller.PrincipalId, Now, c).ConfigureAwait(false);
            var detail = $"Master {d.Label}: active {(row.Active ? "yes" : "no")} → {(active ? "yes" : "no")} ({row.Code} · {row.Name} {(active ? "reactivated" : "retired")}"
                + (!active && used.Count > 0 ? $"; still used: {used.Count}, old records keep the name)" : ")");
            await tx.AuditAsync(d.Audit, Target(d, id), detail, c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", id, new { master = d.Master, rowId = id, action = active ? "reactivated" : "retired" }, c).ConfigureAwait(false);
            return new PosMasterResult(d.Master, id, row.Code, await VersionAsync(tx, caller, d, id, c).ConfigureAwait(false), active ? "reactivated" : "retired");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<PosMasterResult>> DeleteAsync(PosCaller caller, string key, string operation, PosMasterTable table, int id, string? version, object req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(version);
        var d = Of(table);
        return await runner.RunAsync(Ctx(caller, operation, key), req, async (tx, c) =>
        {
            var row = (await store.LockBaseAsync(tx, table, caller.PropertyId, c).ConfigureAwait(false)).FirstOrDefault(r => r.Id == id) ?? throw NotFound($"This {d.Singular} was not found.");
            VersionToken.EnsureMatches(row.RowVer, expected, $"This {d.Singular}");
            var used = await store.UsageAsync(tx, table, id, c).ConfigureAwait(false);
            if (used.Count > 0) throw MastersRules.Fail(MasterReasons.InUse, $"{row.Name} is used ({used.Count} · {string.Join("; ", used.Where)}) — retire it instead of deleting.");
            await store.DeleteAsync(tx, table, id, expected, c).ConfigureAwait(false);
            await store.AddHistoryAsync(tx, caller.PropertyId, d.Master, id, row.Code, "deleted", row.Name, null, caller.PrincipalId, Now, c).ConfigureAwait(false);
            await tx.AuditAsync(d.Audit, Target(d, id), $"Master {d.Label}: {row.Code} · {row.Name} deleted (never used)", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", id, new { master = d.Master, rowId = id, action = "deleted" }, c).ConfigureAwait(false);
            return new PosMasterResult(d.Master, id, row.Code, null, "deleted");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<PosMasterResult>> ReorderAsync(PosCaller caller, string key, string operation, PosMasterTable table, IReadOnlyList<int>? ids, object req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, ct).ConfigureAwait(false);
        var d = Of(table);
        return await runner.RunAsync(Ctx(caller, operation, key), req, async (tx, c) =>
        {
            var rows = await store.LockBaseAsync(tx, table, caller.PropertyId, c).ConfigureAwait(false);
            var order = MastersRules.Reorder(rows.Select(r => r.Id).ToList(), ids, d.Singular);
            var moved = new List<string>();
            foreach (var r in rows)
            {
                var n = order[r.Id];
                if (n == r.SortOrder) continue;
                await store.SetSortAsync(tx, table, r.Id, n, c).ConfigureAwait(false);
                await store.AddHistoryAsync(tx, caller.PropertyId, d.Master, r.Id, r.Code, "sortOrder", Num(r.SortOrder), Num(n), caller.PrincipalId, Now, c).ConfigureAwait(false);
                moved.Add(r.Code);
            }

            if (moved.Count == 0) throw HccException.Validation("The order did not change.");
            var codes = rows.OrderBy(r => order[r.Id]).Select(r => r.Code);
            await tx.AuditAsync(d.Audit, d.Target + ":order", $"Master {d.Label}: sortOrder changed for {string.Join(", ", moved)} (new order {string.Join(", ", codes)})", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", 0, new { master = d.Master, action = "reordered" }, c).ConfigureAwait(false);
            return new PosMasterResult(d.Master, 0, string.Empty, null, "reordered");
        }, ct).ConfigureAwait(false);
    }
}
