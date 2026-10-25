using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Pos;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;
using Hcc.Domain.Pos;
using static Hcc.Application.Billing.BillingService;
using Application_Payments = Hcc.Application.Payments;

namespace Hcc.Application.Pos;

/// <summary>
/// Restaurant POS commands (module 23): outlets and menu, checks, KOT flow, settlement (outlet cash into the shared drawer — DEC-052 — or one
/// folio line through Billing's folio port), voids with a limit and a second approver. Ports the prototype handlers in
/// <c>03 Prototype/src/services/mock/handlers/pos.ts</c>. Every command runs through <see cref="ICommandRunner"/> (idempotency, audit, outbox).
/// Tax comes only from <see cref="IPosTaxRates"/> on the business date (the lead wires it to the GST master at merge).
/// </summary>
public sealed class PosService(ICommandRunner runner, IPosStore store, IBillingStore billing, IAccessPolicy policy, IPosTaxRates taxRates,
    ICashDrawerPort drawer, IPosFolioPort folio, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static CommandContext Ctx(PosCaller c, string operation, string key) => new(c.PropertyId, c.PrincipalId, operation, key);

    private static HccException NotAvailable(string message) => new(HccErrorCode.NOT_AVAILABLE, message);

    // ── Setup ──

    public async Task<CommandResult<OutletResult>> CreateOutletAsync(PosCaller caller, string key, CreateOutletRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Setup, ct: ct).ConfigureAwait(false);
        var code = BillingText.Required(req.Code, 20, "Enter an outlet code.").ToUpperInvariant();
        if (!code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-')) throw HccException.Validation("An outlet code uses letters, digits and '-' only.");
        var name = BillingText.Required(req.Name, 100, "Enter an outlet name.");
        var kind = req.Kind is "table" or "room" ? req.Kind : throw HccException.Validation("Outlet kind must be table or room.");
        var tables = (req.Tables ?? []).Select(t => BillingText.Required(t, 10, "Enter a table name.")).ToList();
        if (kind == "table" && tables.Count == 0) throw HccException.Validation("Add at least one table.");
        if (kind == "room" && tables.Count > 0) throw HccException.Validation("A room-service outlet has no tables.");
        if (tables.Distinct(StringComparer.OrdinalIgnoreCase).Count() != tables.Count) throw HccException.Validation("Table names must be unique.");
        return await runner.RunAsync(Ctx(caller, "CreateOutlet", key), req, async (tx, ct2) =>
        {
            if (await store.OutletCodeExistsAsync(tx, caller.PropertyId, code, ct2).ConfigureAwait(false)) throw HccException.Validation($"An outlet with code {code} already exists.");
            var o = await store.InsertOutletAsync(tx, caller.PropertyId, code, name, kind, tables, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("POS_OUTLET_CREATED", Target("outlet", o.OutletId), $"{code} · {name} · {kind}", ct2).ConfigureAwait(false);
            return new OutletResult(o.OutletId, o.Code, o.Version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MenuItemResult>> SaveMenuItemAsync(PosCaller caller, string key, SaveMenuItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Setup, ct: ct).ConfigureAwait(false);
        var code = BillingText.Required(req.Code, 20, "Enter an item code.").ToUpperInvariant();
        var name = BillingText.Required(req.Name, 100, "Enter the item name.");
        var category = BillingText.Required(req.Category, 60, "Enter a category.");
        var price = Paise.FromWire(req.PricePaise).EnsureFieldRange();
        if (price < Paise.Zero) throw HccException.Validation("The price cannot be negative.");
        var outlets = (req.OutletIds ?? []).Distinct().ToList();
        if (outlets.Count == 0) throw HccException.Validation("Choose at least one outlet for this item.");
        byte[]? expected = req.ItemId is null ? null : Hcc.Application.Stays.VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(Ctx(caller, "SaveMenuItem", key), req, async (tx, ct2) =>
        {
            if (await store.CountOutletsAsync(tx, caller.PropertyId, outlets, ct2).ConfigureAwait(false) != outlets.Count) throw NotFound("Outlet not found.");
            if (await store.MenuCodeExistsAsync(tx, caller.PropertyId, code, req.ItemId, ct2).ConfigureAwait(false)) throw HccException.Validation($"An item with code {code} already exists.");
            MenuItemRow item;
            if (req.ItemId is { } id)
            {
                _ = await store.MenuItemAsync(tx, caller.PropertyId, id, lockRow: true, ct2).ConfigureAwait(false) ?? throw NotFound("Menu item not found.");
                item = await store.UpdateMenuItemAsync(tx, id, expected!, code, name, category, price, req.Veg, outlets, ct2).ConfigureAwait(false);
            }
            else
            {
                item = await store.InsertMenuItemAsync(tx, caller.PropertyId, code, name, category, price, req.Veg, outlets, ct2).ConfigureAwait(false);
            }

            await tx.AuditAsync("POS_MENU_SAVED", Target("menu", item.ItemId), $"{code} · {name} · {price}", ct2).ConfigureAwait(false);
            return new MenuItemResult(item.ItemId, item.Available, item.Version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<MenuItemResult>> SetMenuItemAvailabilityAsync(PosCaller caller, string key, SetMenuItemAvailabilityRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Operate, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(Ctx(caller, "SetMenuAvailability", key), req, async (tx, ct2) =>
        {
            var item = await store.MenuItemAsync(tx, caller.PropertyId, req.ItemId, lockRow: true, ct2).ConfigureAwait(false) ?? throw NotFound("Menu item not found.");
            var version = await store.SetAvailabilityAsync(tx, item.ItemId, req.Available, ct2).ConfigureAwait(false);
            await tx.AuditAsync("POS_MENU_AVAILABILITY", Target("menu", item.ItemId), $"{item.Name} · {(req.Available ? "available again" : "marked unavailable (86)")}", ct2).ConfigureAwait(false);
            return new MenuItemResult(item.ItemId, req.Available, version);
        }, ct).ConfigureAwait(false);
    }

    // ── Checks ──

    public async Task<CommandResult<CheckResult>> OpenCheckAsync(PosCaller caller, string key, OpenCheckRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Operate, ct: ct).ConfigureAwait(false);
        var covers = req.Covers ?? 1;
        if (covers is < 1 or > 20) throw HccException.Validation("Covers must be 1–20.");
        var guest = BillingText.Optional(req.Guest, 60);
        return await runner.RunAsync(Ctx(caller, "OpenCheck", key), req, async (tx, ct2) =>
        {
            // P6-GUARD: IBusinessDayGuard.AssertOpen(propertyId, businessDate) → DAY_CLOSED once P6 is merged.
            var date = await store.BusinessDateAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var outlet = await store.OutletAsync(tx, caller.PropertyId, req.OutletId, ct2).ConfigureAwait(false) ?? throw NotFound("Choose an outlet.");
            string? table = null;
            int? roomId = null;
            long? stayId = null;
            if (outlet.Kind == "table")
            {
                table = BillingText.Required(req.Table, 10, "Choose a table.");
                if (!await store.OutletHasTableAsync(tx, outlet.OutletId, table, ct2).ConfigureAwait(false)) throw HccException.Validation("Choose a table.");
                if (await store.OpenCheckExistsAsync(tx, outlet.OutletId, table, null, ct2).ConfigureAwait(false)) throw NotAvailable($"Table {table} already has an open check.");
            }
            else
            {
                var roomNo = BillingText.Required(req.RoomNo, 10, "Choose the room.");
                var stay = await store.InHouseByRoomAsync(tx, caller.PropertyId, roomNo, ct2).ConfigureAwait(false)
                    ?? throw Invalid("Room service is only for rooms with an in-house guest.");
                if (await store.OpenCheckExistsAsync(tx, outlet.OutletId, null, stay.RoomId, ct2).ConfigureAwait(false)) throw NotAvailable($"Room {roomNo} already has an open room-service check.");
                (roomId, stayId, guest) = (stay.RoomId, stay.StayId, stay.Guest.Length > 60 ? stay.Guest[..60] : stay.Guest);
            }

            var id = await store.InsertOrderAsync(tx, caller.PropertyId, outlet.OutletId, table, roomId, stayId, guest.Length == 0 ? null : guest, covers, date, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("POS_CHECK_OPENED", Target("check", id), $"{outlet.Name} · {(table is null ? "room " + req.RoomNo : "table " + table)} · {covers} cover(s)", ct2).ConfigureAwait(false);
            return await ResultAsync(tx, caller, id, ct2).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<CheckResult>> AddItemAsync(PosCaller caller, string key, AddCheckItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Operate, ct: ct).ConfigureAwait(false);
        var qty = req.Qty ?? 1;
        if (qty is < 1 or > 50) throw HccException.Validation("Quantity must be 1–50.");
        return await runner.RunAsync(Ctx(caller, "AddCheckItem", key), req, async (tx, ct2) =>
        {
            var o = await EditableAsync(tx, caller, req.OrderId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            var item = await store.MenuItemAsync(tx, caller.PropertyId, req.ItemId, lockRow: false, ct2).ConfigureAwait(false) ?? throw NotFound("Menu item not found.");
            if (!item.OutletIds.Contains(o.OutletId)) throw HccException.Validation("This item is not on this outlet's menu.");
            if (!item.Available) throw NotAvailable($"{item.Name} is unavailable (86) right now.");
            var lines = await store.LinesAsync(tx, o.OrderId, ct2).ConfigureAwait(false);
            var same = lines.FirstOrDefault(l => l.ItemId == item.ItemId && l.KotId is null && !l.Voided);
            if (same is not null)
            {
                if (same.Qty + qty > 50) throw HccException.Validation("Quantity must be 1–50.");
                await store.SetLineQtyAsync(tx, same.LineId, same.Qty + qty, ct2).ConfigureAwait(false);
            }
            else
            {
                await store.InsertLineAsync(tx, o.OrderId, item, qty, ct2).ConfigureAwait(false);
            }

            await store.TouchOrderAsync(tx, o.OrderId, ct2).ConfigureAwait(false);
            await tx.AuditAsync("POS_ITEM_ADDED", Target("check", o.OrderId), $"+{qty} {item.Name}", ct2).ConfigureAwait(false);
            return await ResultAsync(tx, caller, o.OrderId, ct2).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<CheckResult>> SetItemQtyAsync(PosCaller caller, string key, SetCheckItemQtyRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Operate, ct: ct).ConfigureAwait(false);
        if (req.Qty is < 0 or > 50) throw HccException.Validation("Quantity must be 0–50.");
        return await runner.RunAsync(Ctx(caller, "SetCheckItemQty", key), req, async (tx, ct2) =>
        {
            var o = await EditableAsync(tx, caller, req.OrderId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            var line = (await store.LinesAsync(tx, o.OrderId, ct2).ConfigureAwait(false)).FirstOrDefault(l => l.LineId == req.LineId) ?? throw NotFound("Item not found on this check.");
            if (line.KotId is not null || line.Voided) throw Invalid("This item was sent to the kitchen. Void it with a reason instead.");
            if (req.Qty == 0) await store.DeleteLineAsync(tx, line.LineId, ct2).ConfigureAwait(false);
            else await store.SetLineQtyAsync(tx, line.LineId, req.Qty, ct2).ConfigureAwait(false);
            await store.TouchOrderAsync(tx, o.OrderId, ct2).ConfigureAwait(false);
            await tx.AuditAsync("POS_ITEM_QTY", Target("check", o.OrderId), $"{line.Name} {(req.Qty == 0 ? "removed (not sent)" : "qty " + req.Qty.ToString(CultureInfo.InvariantCulture))}", ct2).ConfigureAwait(false);
            return await ResultAsync(tx, caller, o.OrderId, ct2).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<KotResult>> SendKotAsync(PosCaller caller, string key, SendKotRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Operate, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(Ctx(caller, "SendKot", key), req, async (tx, ct2) =>
        {
            var o = await EditableAsync(tx, caller, req.OrderId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            var unsent = (await store.LinesAsync(tx, o.OrderId, ct2).ConfigureAwait(false)).Where(l => !l.Voided && l.KotId is null).ToList();
            if (unsent.Count == 0) throw HccException.Validation("Add items before sending a KOT.");
            var date = await store.BusinessDateAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var (kotId, kotNo) = await store.InsertKotAsync(tx, o, date, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await store.AssignKotAsync(tx, kotId, unsent.Select(l => l.LineId).ToList(), ct2).ConfigureAwait(false);
            var version = await store.TouchOrderAsync(tx, o.OrderId, ct2).ConfigureAwait(false);
            await tx.AuditAsync("POS_KOT_SENT", Target("check", o.OrderId), $"KOT {kotNo} · {unsent.Sum(l => l.Qty)} item(s) to kitchen · {SpotOf(o)}", ct2).ConfigureAwait(false);
            return new KotResult(o.OrderId, kotId, kotNo, PosRules.Preparing, version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<KotResult>> AdvanceKotAsync(PosCaller caller, string key, AdvanceKotRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Operate, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(Ctx(caller, "AdvanceKot", key), req, async (tx, ct2) =>
        {
            var o = await store.LockOrderAsync(tx, caller.PropertyId, req.OrderId, ct2).ConfigureAwait(false) ?? throw NotFound("Check not found.");
            if (o.Status == "Void") throw Invalid("This check was voided.");
            var kot = await store.LockKotAsync(tx, o.OrderId, req.KotId, ct2).ConfigureAwait(false) ?? throw NotFound("KOT not found.");
            var next = PosRules.NextKotStatus(kot.Status);
            if (next is null) throw Invalid($"KOT {kot.KotNo} is already served.");
            if (req.Status != next) throw Invalid($"KOT {kot.KotNo} can only move to {next}.");
            await store.UpdateKotStatusAsync(tx, kot.KotId, next, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("POS_KOT_STATUS", Target("check", o.OrderId), $"KOT {kot.KotNo} → {next}", ct2).ConfigureAwait(false);
            return new KotResult(o.OrderId, kot.KotId, kot.KotNo, next, o.Version);
        }, ct).ConfigureAwait(false);
    }

    // ── Settle ──

    public async Task<CommandResult<SettleResult>> SettleCheckAsync(PosCaller caller, string key, SettleCheckRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        var folioMode = req.Mode switch
        {
            "folio" => true,
            "outlet" => false,
            _ => throw HccException.Validation("Choose how the bill is settled."),
        };
        VersionToken.Validate(req.ExpectedVersion);
        var reference = BillingText.Optional(req.Reference, 120);
        var tender = req.Tender;
        if (!folioMode)
        {
            if (tender is null || !PosRules.Tenders.Contains(tender, StringComparer.Ordinal)) throw HccException.Validation("Select Cash, UPI or Card.");
            if (tender == PosRules.Cash && req.TillId is null) throw HccException.Validation("Choose the till that receives the cash.");
            if (tender != PosRules.Cash && reference.Length == 0) throw HccException.Validation("Enter the transaction reference for a non-cash payment.");
        }

        if (folioMode) await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Operate, ct: ct).ConfigureAwait(false);
        else await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Settle, AccessScopes.Any, ct: ct).ConfigureAwait(false);

        return await runner.RunAsync(Ctx(caller, "SettleCheck", key), req, async (tx, ct2) =>
        {
            var o = await store.LockOrderAsync(tx, caller.PropertyId, req.OrderId, ct2).ConfigureAwait(false) ?? throw NotFound("Check not found.");
            if (o.Status == "Settled") throw Invalid("This check is already settled.");
            RequireEditable(o);
            EnsureVersion(o.Version, req.ExpectedVersion, "The check");
            // P6-GUARD: IBusinessDayGuard.AssertOpen(propertyId, businessDate) → DAY_CLOSED once P6 is merged.
            var date = await store.BusinessDateAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var lines = (await store.LinesAsync(tx, o.OrderId, ct2).ConfigureAwait(false)).Where(l => !l.Voided).ToList();
            if (lines.Count == 0) throw HccException.Validation("This check has no items to settle.");
            if (lines.Any(l => l.KotId is null)) throw Invalid("Send the remaining items to the kitchen (KOT) or remove them before settling.");
            var bps = taxRates.FoodTaxBpsOn(caller.PropertyId, date); // GST-FOOD
            var totals = PosRules.Totals(lines.Select(l => new PosLineAmount(l.Price, l.Qty)), bps);
            if (totals.Net <= Paise.Zero) throw HccException.Validation("This check has no value to settle.");
            var billNo = PosRules.BillNo(date, await store.NextBillSequenceAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false));
            var now = Now;
            long billId, receiptId = 0, movementId = 0, chargeId = 0;
            long? stayId = o.StayId, folioIdOut = null;
            if (folioMode)
            {
                stayId = req.StayId ?? o.StayId ?? throw NotFound("Choose the in-house guest to charge.");
                var stay = await store.StayByIdAsync(tx, caller.PropertyId, stayId.Value, ct2).ConfigureAwait(false) ?? throw NotFound("Choose the in-house guest to charge.");
                if (stay.Status != "InHouse") throw Invalid("Only an in-house guest's folio can take a room charge.");
                var label = $"{o.OutletName} · bill {billNo} · {totals.Items} item{(totals.Items == 1 ? string.Empty : "s")}";
                var charge = await folio.PostServiceChargeAsync(tx, caller.PropertyId, stay.FolioId, date, label, totals.Net, bps, now, ct2).ConfigureAwait(false);
                chargeId = charge.ChargeId;
                folioIdOut = stay.FolioId;
                billId = await store.InsertBillAsync(tx, new NewBill(o.OrderId, caller.PropertyId, billNo, "folio", null, null, totals.Net, bps, totals.Tax, totals.Total,
                    date, now, caller.PrincipalId, null, null, null, stay.StayId, stay.FolioId, chargeId), ct2).ConfigureAwait(false);
            }
            else
            {
                long? shiftId = null;
                long? moveId = null;
                if (tender == PosRules.Cash)
                {
                    // Shared till (DEC-052): outlet cash is a movement in the front-desk drawer, so it needs that till's open shift (and own-shift scope).
                    var shift = await Application_Payments.CashDesk.RequireOpenShiftAsync(billing, tx, caller.PropertyId, req.TillId!.Value, date, ct2).ConfigureAwait(false);
                    await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Settle, new AccessScope(PosPermissions.ShiftScope, shift.ShiftId), ct: ct2).ConfigureAwait(false);
                    shiftId = shift.ShiftId;
                    moveId = await drawer.RecordMovementAsync(tx, req.TillId.Value, CashMovementKind.PosSale, "pos-bill", o.OrderId, totals.Total, $"{o.OutletName} · bill {billNo}",
                        "Outlet cash", ct2).ConfigureAwait(false);
                    movementId = moveId.Value;
                }

                billId = await store.InsertBillAsync(tx, new NewBill(o.OrderId, caller.PropertyId, billNo, "outlet", tender, reference.Length == 0 ? null : reference, totals.Net, bps,
                    totals.Tax, totals.Total, date, now, caller.PrincipalId, tender == PosRules.Cash ? req.TillId : null, shiftId, moveId, stayId, null, null), ct2).ConfigureAwait(false);
                receiptId = await store.InsertReceiptAsync(tx, new NewReceipt(billId, o.OrderId, o.OutletId, "sale", tender!, totals.Total, totals.Net, totals.Tax, bps,
                    reference.Length == 0 ? "Outlet cash" : reference, date, caller.PrincipalId, now), ct2).ConfigureAwait(false);
            }

            await store.SetOrderStatusAsync(tx, o.OrderId, "Settled", null, null, null, stayId, ct2).ConfigureAwait(false);
            var version = await store.TouchOrderAsync(tx, o.OrderId, ct2).ConfigureAwait(false);
            await tx.AuditAsync("POS_BILL_SETTLED", Target("check", o.OrderId),
                $"bill {billNo} · {totals.Total} (net {totals.Net} + tax {totals.Tax}) · " + (folioMode ? $"posted to folio of stay {stayId}" : $"{tender} at {o.OutletName}" + (tender == PosRules.Cash ? " · counted in the front-desk drawer" : "")), ct2).ConfigureAwait(false);
            // Accounting consumes this per settled bill (revenue by outlet, tax by bps, tender / room posting).
            await tx.PublishAsync("PosBillSettled", billId, new
            {
                billId, orderId = o.OrderId, billNo, outletId = o.OutletId, outlet = o.OutletName, mode = req.Mode, tender, businessDate = date,
                netPaise = totals.Net.Value, taxBps = bps, taxPaise = totals.Tax.Value, totalPaise = totals.Total.Value,
                folioId = folioIdOut,
                chargeId = folioMode ? chargeId : (long?)null, cashMovementId = movementId == 0 ? (long?)null : movementId,
            }, ct2).ConfigureAwait(false);
            return new SettleResult(o.OrderId, billId, billNo, req.Mode, totals.Net.Value, bps, totals.Tax.Value, totals.Total.Value,
                receiptId == 0 ? null : receiptId, chargeId == 0 ? null : chargeId, movementId == 0 ? null : movementId, version);
        }, ct).ConfigureAwait(false);
    }

    // ── Voids ──

    public async Task<CommandResult<VoidResult>> VoidItemAsync(PosCaller caller, string key, VoidCheckItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Operate, ct: ct).ConfigureAwait(false);
        var reason = ReasonOf(req.Reason);
        return await runner.RunAsync(Ctx(caller, "VoidCheckItem", key), req, async (tx, ct2) =>
        {
            var o = await EditableAsync(tx, caller, req.OrderId, req.ExpectedVersion, ct2).ConfigureAwait(false);
            var line = (await store.LinesAsync(tx, o.OrderId, ct2).ConfigureAwait(false)).FirstOrDefault(l => l.LineId == req.LineId) ?? throw NotFound("Item not found on this check.");
            if (line.Voided) throw Invalid("This item is already void.");
            if (line.KotId is null) throw Invalid("This item has not been sent to the kitchen — just remove it.");
            var value = new PosLineAmount(line.Price, line.Qty).Value;
            if (!PosRules.NeedsApproval(value.Value))
                return await ApplyVoidAsync(tx, caller, o, "item", line.LineId, reason, caller.PrincipalId, null, ct2).ConfigureAwait(false);
            return await RequestVoidAsync(tx, caller, o, "item", line.LineId, value, reason, ct2).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<VoidResult>> VoidBillAsync(PosCaller caller, string key, VoidBillRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Operate, ct: ct).ConfigureAwait(false);
        var reason = ReasonOf(req.Reason);
        return await runner.RunAsync(Ctx(caller, "VoidBill", key), req, async (tx, ct2) =>
        {
            var o = await store.LockOrderAsync(tx, caller.PropertyId, req.OrderId, ct2).ConfigureAwait(false) ?? throw NotFound("Check not found.");
            if (o.Status == "Void") throw Invalid("This check is already void.");
            if (o.PendingVoidId is not null) throw Invalid("A void request is pending for this check. Ask a manager to decide it first.");
            if (req.ExpectedVersion is not null) EnsureVersion(o.Version, req.ExpectedVersion, "The check");
            Paise value;
            if (o.Status == "Settled")
            {
                await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.Settle, AccessScopes.Any, ct: ct2).ConfigureAwait(false);
                // P6-GUARD: IBusinessDayGuard.AssertOpen(propertyId, bill.BusinessDate) → DAY_CLOSED once P6 is merged.
                var bill = await store.BillForOrderAsync(tx, o.OrderId, ct2).ConfigureAwait(false) ?? throw NotFound("Bill not found.");
                if (bill.Mode != "outlet") throw Invalid("A bill posted to a room folio is corrected on the folio, not voided here.");
                var date = await store.BusinessDateAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
                if (bill.BusinessDate != date) throw Invalid("Only bills settled on the current business date can be voided.");
                value = bill.Total;
            }
            else
            {
                var lines = (await store.LinesAsync(tx, o.OrderId, ct2).ConfigureAwait(false)).Where(l => !l.Voided).ToList();
                value = lines.Aggregate(Paise.Zero, (a, l) => a + new PosLineAmount(l.Price, l.Qty).Value);
            }

            if (!PosRules.NeedsApproval(value.Value) || value <= Paise.Zero)
                return await ApplyVoidAsync(tx, caller, o, "bill", null, reason, caller.PrincipalId, null, ct2).ConfigureAwait(false);
            return await RequestVoidAsync(tx, caller, o, "bill", null, value, reason, ct2).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<DecideVoidResult>> DecideVoidAsync(PosCaller caller, string key, DecideVoidRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.VoidDecide, ct: ct).ConfigureAwait(false);
        var decisionReason = BillingText.Optional(req.Reason, 200);
        return await runner.RunAsync(Ctx(caller, "DecideVoid", key), req, async (tx, ct2) =>
        {
            var peek = await store.PeekVoidRequestAsync(tx, caller.PropertyId, req.VoidRequestId, ct2).ConfigureAwait(false) ?? throw NotFound("Void request not found.");
            var o = await store.LockOrderAsync(tx, caller.PropertyId, peek.OrderId, ct2).ConfigureAwait(false) ?? throw NotFound("Check not found.");
            var r = await store.LockVoidRequestAsync(tx, req.VoidRequestId, ct2).ConfigureAwait(false) ?? throw NotFound("Void request not found.");
            if (r.Status != "Pending") throw Invalid("This request is no longer pending.");
            if (r.RequestedBy == caller.PrincipalId) throw Forbidden("You cannot approve your own request (PERM-SOD-001).");
            var now = Now;
            if (!req.Approve)
            {
                await store.DecideVoidRequestAsync(tx, r.VoidId, r.Version, "Rejected", caller.PrincipalId, now, decisionReason.Length == 0 ? null : decisionReason, ct2).ConfigureAwait(false);
                await store.SetPendingVoidAsync(tx, o.OrderId, null, ct2).ConfigureAwait(false);
                var v = await store.TouchOrderAsync(tx, o.OrderId, ct2).ConfigureAwait(false);
                await tx.AuditAsync("POS_VOID_DECIDED", Target("check", o.OrderId), $"void {r.Scope} {r.Amount} rejected", ct2).ConfigureAwait(false);
                return new DecideVoidResult(r.VoidId, "Rejected", o.OrderId, v);
            }

            // The decider's limit applies to the void value (Manager ₹1,000, DEC-063 G1; Owner unlimited).
            await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, PosPermissions.VoidDecide, amountPaise: r.Amount.Value, ct: ct2).ConfigureAwait(false);
            if (now > r.ExpiresUtc) throw Invalid("This request has expired. Reject it and ask for a new one.");
            if (o.Rev != r.OrderRev) throw Stale("The check changed after this request. Reject it and request again.");
            await store.SetPendingVoidAsync(tx, o.OrderId, null, ct2).ConfigureAwait(false);
            var cleared = o with { PendingVoidId = null };
            await ApplyVoidAsync(tx, caller, cleared, r.Scope, r.LineId, r.Reason, r.RequestedBy, r.VoidId, ct2).ConfigureAwait(false);
            await store.DecideVoidRequestAsync(tx, r.VoidId, r.Version, "Executed", caller.PrincipalId, now, decisionReason.Length == 0 ? null : decisionReason, ct2).ConfigureAwait(false);
            var version = (await store.LockOrderAsync(tx, caller.PropertyId, o.OrderId, ct2).ConfigureAwait(false))!.Version;
            await tx.AuditAsync("POS_VOID_DECIDED", Target("check", o.OrderId), $"void {r.Scope} {r.Amount} approved · {r.Reason}", ct2).ConfigureAwait(false);
            return new DecideVoidResult(r.VoidId, "Executed", o.OrderId, version);
        }, ct).ConfigureAwait(false);
    }

    private async Task<VoidResult> RequestVoidAsync(ICommandTransaction tx, PosCaller caller, OrderRow o, string scope, long? lineId, Paise value, string reason, CancellationToken ct)
    {
        var now = Now;
        // The request binds to the revision the check will have once it is locked.
        var (voidId, _) = await store.InsertVoidRequestAsync(tx, new NewVoidRequest(caller.PropertyId, o.OrderId, scope, lineId, value, reason, o.Rev + 1,
            caller.PrincipalId, now, now + PosRules.VoidRequestTtl), ct).ConfigureAwait(false);
        await store.SetPendingVoidAsync(tx, o.OrderId, voidId, ct).ConfigureAwait(false);
        var version = await store.TouchOrderAsync(tx, o.OrderId, ct).ConfigureAwait(false);
        await tx.AuditAsync("POS_VOID_REQUESTED", Target("check", o.OrderId),
            $"void {scope} {value} above {new Paise(PosRules.VoidLimitPaise)} → request {voidId} · {reason}", ct).ConfigureAwait(false);
        return new VoidResult(o.OrderId, "PendingApproval", voidId, version);
    }

    /// <summary>Executes a void (item or bill); shared by direct voids and approved requests. A settled outlet bill adds the reversal receipt and drawer movement.</summary>
    private async Task<VoidResult> ApplyVoidAsync(ICommandTransaction tx, PosCaller caller, OrderRow o, string scope, long? lineId, string reason, int by,
        long? voidRequestId, CancellationToken ct)
    {
        var date = await store.BusinessDateAsync(tx, caller.PropertyId, ct).ConfigureAwait(false);
        var now = Now;
        if (scope == "item")
        {
            await store.VoidLinesAsync(tx, [lineId!.Value], reason, by, date, voidRequestId, ct).ConfigureAwait(false);
            var v = await store.TouchOrderAsync(tx, o.OrderId, ct).ConfigureAwait(false);
            await tx.AuditAsync("POS_ITEM_VOIDED", Target("check", o.OrderId), $"line {lineId} · {reason}", ct).ConfigureAwait(false);
            return new VoidResult(o.OrderId, "Voided", voidRequestId, v);
        }

        // P6-GUARD: IBusinessDayGuard.AssertOpen(propertyId, businessDate) → DAY_CLOSED once P6 is merged (voiding a bill settled that day).
        if (o.Status == "Settled")
        {
            var bill = await store.BillForOrderAsync(tx, o.OrderId, ct).ConfigureAwait(false) ?? throw NotFound("Bill not found.");
            if (bill.Mode != "outlet") throw Invalid("A bill posted to a room folio is corrected on the folio, not voided here.");
            if (bill.BusinessDate != date) throw Invalid("Only bills settled on the current business date can be voided.");
            long? movement = null;
            if (bill.Tender == PosRules.Cash)
            {
                // The reversal leaves the same drawer; its shift must still be open (a counted/closed shift cannot be reversed).
                movement = await drawer.RecordMovementAsync(tx, bill.TillId!.Value, CashMovementKind.PosVoid, "pos-void", o.OrderId, -bill.Total, $"{o.OutletName} · void of {bill.BillNo}",
                    "Void of " + bill.BillNo, ct).ConfigureAwait(false);
            }

            await store.InsertReceiptAsync(tx, new NewReceipt(bill.BillId, o.OrderId, o.OutletId, "void", bill.Tender!, -bill.Total, -bill.Net, -bill.Tax, bill.TaxBps,
                "Void of " + bill.BillNo, date, by, now), ct).ConfigureAwait(false);
            await store.MarkBillVoidAsync(tx, bill.BillId, reason, by, now, ct).ConfigureAwait(false);
            await tx.PublishAsync("PosBillVoided", bill.BillId, new
            {
                billId = bill.BillId, orderId = o.OrderId, billNo = bill.BillNo, outletId = o.OutletId, outlet = o.OutletName, mode = bill.Mode, tender = bill.Tender,
                businessDate = date, settledOn = bill.BusinessDate, netPaise = bill.Net.Value, taxBps = bill.TaxBps, taxPaise = bill.Tax.Value, totalPaise = bill.Total.Value,
                reason, cashMovementId = movement,
            }, ct).ConfigureAwait(false);
        }

        var active = (await store.LinesAsync(tx, o.OrderId, ct).ConfigureAwait(false)).Where(l => !l.Voided).Select(l => l.LineId).ToList();
        if (active.Count > 0) await store.VoidLinesAsync(tx, active, reason, by, date, voidRequestId, ct).ConfigureAwait(false);
        await store.SetOrderStatusAsync(tx, o.OrderId, "Void", reason, by, now, null, ct).ConfigureAwait(false);
        var version = await store.TouchOrderAsync(tx, o.OrderId, ct).ConfigureAwait(false);
        await tx.AuditAsync("POS_BILL_VOIDED", Target("check", o.OrderId), $"{(o.Status == "Settled" ? "settled bill" : "open check")} void · {reason}", ct).ConfigureAwait(false);
        return new VoidResult(o.OrderId, "Voided", voidRequestId, version);
    }

    // ── helpers ──

    private static string ReasonOf(string? reason)
    {
        var r = reason?.Trim() ?? string.Empty;
        if (r.Length < 3) throw HccException.Validation("Enter a reason for the void (at least 3 characters).");
        if (r.Length > 200) throw HccException.Validation("Keep it to 200 characters.");
        return r;
    }

    private static string SpotOf(OrderRow o) => o.TableNo is not null ? "Table " + o.TableNo : "Room " + o.RoomNo;

    private static void RequireEditable(OrderRow o)
    {
        if (o.Status != "Open") throw Invalid($"This check is already {o.Status.ToLowerInvariant()}.");
        if (o.PendingVoidId is not null) throw Invalid("A void request is pending for this check. Ask a manager to decide it first.");
    }

    /// <summary>An open, unlocked check (row locked); optional version check; day-close guard marker.</summary>
    private async Task<OrderRow> EditableAsync(ICommandTransaction tx, PosCaller caller, long orderId, string? expectedVersion, CancellationToken ct)
    {
        var o = await store.LockOrderAsync(tx, caller.PropertyId, orderId, ct).ConfigureAwait(false) ?? throw NotFound("Check not found.");
        RequireEditable(o);
        if (expectedVersion is not null)
        {
            VersionToken.Validate(expectedVersion);
            EnsureVersion(o.Version, expectedVersion, "The check");
        }

        // P6-GUARD: IBusinessDayGuard.AssertOpen(propertyId, businessDate) → DAY_CLOSED once P6 is merged.
        return o;
    }

    private async Task<CheckResult> ResultAsync(ICommandTransaction tx, PosCaller caller, long orderId, CancellationToken ct)
    {
        var o = await store.LockOrderAsync(tx, caller.PropertyId, orderId, ct).ConfigureAwait(false) ?? throw NotFound("Check not found.");
        PosTotals t;
        if (o.Status == "Settled" && await store.BillForOrderAsync(tx, orderId, ct).ConfigureAwait(false) is { } bill)
        {
            t = new PosTotals(bill.Net, bill.TaxBps, bill.Tax, bill.Total, 0);
        }
        else
        {
            var lines = (await store.LinesAsync(tx, orderId, ct).ConfigureAwait(false)).Where(l => !l.Voided).ToList();
            t = PosRules.Totals(lines.Select(l => new PosLineAmount(l.Price, l.Qty)), taxRates.FoodTaxBpsOn(caller.PropertyId, o.BusinessDate));
        }

        return new CheckResult(o.OrderId, o.Status, o.Version, t.Net.Value, t.TaxBps, t.Tax.Value, t.Total.Value, t.Items);
    }
}
