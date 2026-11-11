using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Invoices;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Invoices;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.Hall;
using Hcc.Domain.Invoices;
using Hcc.Domain.Money;
using Hcc.Domain.MoneyMasters;
using Hcc.Domain.Tax;

namespace Hcc.Application.Hall;

// ── requests / results (F6-CONTRACT §3, §6: POST /api/hall/quotations/{id}/accept, /events/{id}/advance|close|cancel|refund) ──

/// <summary><see cref="TillId"/>: the till whose open shift takes a cash tender; omitted = the property's only open shift (shared till, DEC-052).</summary>
public sealed record HallTenderIn(long AmountPaise, string Method, string? Reference = null, int? TillId = null);

/// <summary>F6-int (spec §10.3): rooms for the event's guests, held as a group block inside the accept transaction (refused NOT_AVAILABLE when the rooms are not free).
/// Arrival / departure default to the first event date / the day after the last; cut-off defaults to two days before arrival (not before today).</summary>
public sealed record HallRoomBlockIn(IReadOnlyList<Hcc.Contracts.Inventory.GroupBlockAllotment> Allotments, DateOnly? Arrival = null, DateOnly? Departure = null, DateOnly? Cutoff = null);

public sealed record AcceptQuotationRequest(long QuotationId, int? Version = null, string? Reason = null, string? CapacityReason = null, HallTenderIn? Advance = null,
    HallRoomBlockIn? RoomBlock = null);

public sealed record HallAdvanceRequest(long EventId, long AmountPaise, string Method, string? Reference = null, int? TillId = null);

public sealed record HallExtraIn(string Desc, int Qty, long RatePaise, long? ListRatePaise = null, string? Kind = null, string? Unit = null, string? GstClass = null);

public sealed record HallSettleIn(string Mode, string? Method = null, string? Reference = null, long? CompanyId = null, int? TillId = null);

/// <summary><see cref="BuyerAddress"/> is needed on an invoice of ₹50,000 or more to an unregistered customer (InvoiceRules); a GSTIN makes it B2B.</summary>
public sealed record CloseHallEventRequest(long EventId, int ActualPax, IReadOnlyList<HallExtraIn>? Extras = null, HallSettleIn? Settle = null, string? BuyerAddress = null,
    string? BuyerGstin = null);

public sealed record CancelHallEventRequest(long EventId, string Reason);

public sealed record HallRefundRequest(long EventId, string Method, string? Reference = null, int? TillId = null);

public sealed record HallEventResult(long EventId, string Status, long? ReceiptId = null, string? InvoiceNo = null, long TotalPaise = 0, long AdvanceAppliedPaise = 0,
    long BalancePaise = 0, long KeptPaise = 0, long RefundPaise = 0, int BilledPax = 0);

// ── store port ──

public sealed record HallVenueRow(int VenueId, string Code, string Name, bool Active, int[]? CombinedOf, IReadOnlyDictionary<string, int> Capacity);

public sealed record HallQuoteRow(long QuotationId, string Number, long EnquiryId, int CurrentVersion, string CustomerName, string CustomerPhone, long? CompanyId,
    long VersionId, int V, string Status, string EventType, IReadOnlyList<DateOnly> Dates, string Slot, int VenueId, string Setup, int Pax, int GuaranteedPax,
    DateOnly ValidUntil, IReadOnlyList<HallLine> Lines, IReadOnlyList<HallAdvanceRow> Advance);

public sealed record HallEventRow(long EventId, int PropertyId, long QuotationId, int VenueId, DateOnly EventDate, IReadOnlyList<DateOnly> Dates, string Slot, string Status,
    int GuaranteedPax, string CustomerName, IReadOnlyList<HallLine> Lines, IReadOnlyList<HallAdvanceRow> Advance, long PaidPaise, string? InvoiceNo,
    string? RefundState, long? RefundPaise, int? CancelBy);

public sealed record NewHallEvent(int PropertyId, long QuotationId, int Version, int VenueId, IReadOnlyList<DateOnly> Dates, string Slot, string EventType, int GuaranteedPax,
    string CustomerName, string CustomerPhone, long? CompanyId, int By, DateTime Utc, string IdemKey);

public sealed record HallReceiptIn(long EventId, DateOnly Date, string Kind, string? Tender, long? CompanyId, bool Udhaar, string? Reference, Paise Amount, int By, DateTime Utc);

public sealed record HallBill(int ActualPax, int BilledPax, IReadOnlyList<HallPricedLine> Lines, HallTotals Totals, Paise Applied, Paise Balance, string SettleMode);

public sealed record HallCancellationIn(DateOnly Date, HallCancelQuote Quote, int By, string Reason);

public interface IHallBillingStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    /// <summary>The caller's role names (Owner, Manager, Reception, Cashier, Accountant …) in force now.</summary>
    Task<IReadOnlySet<string>> RolesAsync(ICommandTransaction tx, int principalId, CancellationToken ct);

    Task<IReadOnlyList<HallVenueRow>> VenuesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    /// <summary>F6-H3: the till of the property's only open cash shift (shared till, DEC-052); null when none or more than one is open.</summary>
    Task<int?> SingleOpenTillAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    /// <summary>The quotation (UPDLOCK) with its latest version, lines and advance rows; null when not on the property.</summary>
    Task<HallQuoteRow?> LockQuotationAsync(ICommandTransaction tx, int propertyId, long quotationId, CancellationToken ct);

    /// <summary>Hard holds of live events on the property for the given dates (venue = the event's venue, not expanded).</summary>
    Task<IReadOnlyList<HallHold>> HoldsAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<DateOnly> dates, CancellationToken ct);

    /// <summary>Inserts the event, its lines, its advance plan and its calendar holds (one per physical hall × date × part of day); a hold that
    /// violates UX_hall_calendar_hold_hard → <see cref="HccErrorCode.HCC_HALL_CLASH"/>.</summary>
    Task<long> InsertEventAsync(ICommandTransaction tx, NewHallEvent e, IReadOnlyList<HallLine> lines, IReadOnlyList<HallAdvanceRow> plan,
        IReadOnlyList<(int VenueId, DateOnly Date, string Part)> holds, CancellationToken ct);

    Task MarkAcceptedAsync(ICommandTransaction tx, long versionId, long enquiryId, CancellationToken ct);

    /// <summary>F6-int: the group block holding rooms for the event's guests.</summary>
    Task SetEventBlockAsync(ICommandTransaction tx, long eventId, long blockId, CancellationToken ct);

    /// <summary>F6-int: the event's group block while it is still Open (id + row version), else null.</summary>
    Task<(long BlockId, byte[] Version)?> OpenEventBlockAsync(ICommandTransaction tx, long eventId, CancellationToken ct);

    Task<HallEventRow?> LockEventAsync(ICommandTransaction tx, int propertyId, long eventId, CancellationToken ct);

    Task<long> InsertReceiptAsync(ICommandTransaction tx, HallReceiptIn r, CancellationToken ct);

    /// <summary>CU-PAY-B: the tenders (as stored) of the event's ADVANCE and BALANCE receipts.</summary>
    Task<IReadOnlyList<string>> PaidTendersAsync(ICommandTransaction tx, long eventId, CancellationToken ct);

    Task SetAdvancePaidAsync(ICommandTransaction tx, long eventId, IReadOnlyList<HallAdvanceRow> rows, CancellationToken ct);

    /// <summary>Writes the bill: actual pax, the extras as event lines, every line's tax snapshot, totals and the settlement mode; status BILLED.</summary>
    Task BillAsync(ICommandTransaction tx, long eventId, DateOnly date, HallBill bill, IReadOnlyList<HallLine> extras, CancellationToken ct);

    Task SetInvoiceAsync(ICommandTransaction tx, long eventId, long invoiceId, string invoiceNo, CancellationToken ct);

    /// <summary>Status CANCELLED, the cancellation figures, refund PENDING / NONE; the event's calendar holds released.</summary>
    Task CancelAsync(ICommandTransaction tx, long eventId, HallCancellationIn c, CancellationToken ct);

    Task MarkRefundedAsync(ICommandTransaction tx, long eventId, int by, CancellationToken ct);

    /// <summary>Priced event lines (tax snapshot set) for the invoice source; null when not billed.</summary>
    Task<(HallEventRow Event, IReadOnlyList<HallPricedLine> Lines, DateOnly InvoiceDate)?> InvoiceLinesAsync(ICommandTransaction tx, int propertyId, long eventId, CancellationToken ct);
}

/// <summary>
/// F6 lane H1 money commands (F6-CONTRACT §3, §5) — the server twin of the prototype's handlers/hall.ts H1 block, rules only through
/// <see cref="HallRules"/> / <see cref="HallBilling"/>: AcceptQuotation (latest SENT version, expiry with a Manager reason, capacity override,
/// calendar clash refused — the unique hold index is the backstop), ReceiveHallAdvance (Dr tender · Cr 2000, no GST, DEC-142), CloseHallEvent
/// (max(guaranteed, actual) plates, INV-series tax invoice HPE/2627/… through <see cref="InvoiceService"/>, advances applied, balance by tender /
/// company / Udhaar / due), CancelHallEvent (slab → kept 4300 with GST, refund pending), ApproveHallRefund (Manager ≤ ₹50,000, Owner above,
/// never the canceller). Every money movement publishes <see cref="AccountingEvents.HallPosted"/>. Roles: HallRules (PERM-HAL-003 / 004; the
/// RBAC seed 4345 is lane H3's).
/// </summary>
public sealed class HallBillingCommands(ICommandRunner runner, IHallBillingStore store, IGstMasterStore gstStore, InvoiceService invoices, TimeProvider clock, IAccessPolicy policy, ICashDrawerPort? drawer = null,
    Accounting.CashGuardService? cashGuard = null, Inventory.IInventoryStore? inventory = null, Inventory.GroupBlockOptions? groupBlocks = null,
    MoneyMasters.IPaymentMethodSource? methods = null)
{
    public const string OpAccept = "AcceptQuotation", OpAdvance = "ReceiveHallAdvance", OpClose = "CloseHallEvent", OpCancel = "CancelHallEvent", OpRefund = "ApproveHallRefund";

    // CU-PAY-B (migration 4935): the tender comes from the Payment methods master (mst.payment_method; defaults when the list is empty), as for folio receipts.
    private readonly MoneyMasters.IPaymentMethodSource _methods = methods ?? MoneyMasters.DefaultPaymentMethods.Instance;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Rs(long p) => "₹" + (p / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>The method's CURRENT master name (an earlier name or the code resolves to the row); not usable at the front desk or retired is refused with the master words.
    /// <paramref name="eventId"/> (refunds): a retired method is allowed only when it is the same master row as the tender of one of the event's advance / balance receipts (DEC-MA-11a).</summary>
    private async Task<(string Name, bool Till)> TenderAsync(ICommandTransaction? tx, int propertyId, string? method, string? reference, CancellationToken c, long? eventId = null)
    {
        var list = await _methods.MethodsAsync(tx, propertyId, c).ConfigureAwait(false);
        var allowRetired = false;
        if (eventId is { } ev && tx is not null)
            allowRetired = (await store.PaidTendersAsync(tx, ev, c).ConfigureAwait(false)).Any(t => PaymentMethodBook.SameRow(list, method, t));
        var m = PaymentMethodBook.Pick(list, method, MoneyMasterValues.FrontDesk, reference, requireReference: false, allowRetired: allowRetired);
        if (m.NeedsReference && (reference ?? "").Trim().Length < 3) throw HccException.Validation("Enter the payment reference (UPI / card / bank).");
        return (m.Name, m.SettlesToTill);
    }

    private async Task<string> RoleAsync(ICommandTransaction tx, int principal, Func<string, bool> allowed, string what, CancellationToken ct)
    {
        var roles = await store.RolesAsync(tx, principal, ct).ConfigureAwait(false);
        return roles.FirstOrDefault(allowed) ?? throw new HccException(HccErrorCode.FORBIDDEN, "Access denied: " + what + ".");
    }

    /// <summary>F6-H3: a cash tender moves the drawer of the open shift (hall advance / balance = a desk receipt in, a cash refund = out); other tenders and a host without the till port do nothing.</summary>
    private async Task CashAsync(ICommandTransaction tx, CommandContext ctx, string? method, int? tillId, bool inward, long receiptId, Paise amount, string label, string? reference, CancellationToken c, long? eventId = null)
    {
        if (drawer is null || !PaymentMethodBook.SettlesToTill(await _methods.MethodsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), method)) return;
        if (inward && cashGuard is not null && eventId is { } ev) // F6-int: cash receipt limit per event (rulebook B9; CashParty "hall", this receipt excluded)
            await cashGuard.EnsureReceiptAsync(tx, ctx.PropertyId, new Accounting.CashParty(Accounting.CashParty.Hall, ev, receiptId.ToString(CultureInfo.InvariantCulture)),
                amount.Value, await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), "this hall event", c).ConfigureAwait(false);
        var till = tillId ?? await store.SingleOpenTillAsync(tx, ctx.PropertyId, c).ConfigureAwait(false)
            ?? throw HccException.Validation("Cash goes into the open cash shift: choose the till (no single open shift was found).");
        await drawer.RecordMovementAsync(tx, till, inward ? Hcc.Domain.Payments.CashMovementKind.Payment : Hcc.Domain.Payments.CashMovementKind.Refund,
            inward ? Payments.CashierService.HallSource : Payments.CashierService.HallRefundSource, receiptId, inward ? amount : new Paise(-amount.Value), label, reference ?? label, c).ConfigureAwait(false);
    }

    private static IEnumerable<string> Parts(string slot) => slot == "FULL_DAY" ? ["MORNING", "AFTERNOON", "EVENING"] : [slot];

    public async Task<CommandResult<HallEventResult>> AcceptAsync(CommandContext ctx, AcceptQuotationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HallPermissions.Accept, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpAccept }, req, async (tx, c) =>
        {
            var role = await RoleAsync(tx, ctx.PrincipalId, HallRules.MayAccept, "accepting a quotation (PERM-HAL-003)", c).ConfigureAwait(false);
            var boss = role is "Owner" or "Manager";
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var q = await store.LockQuotationAsync(tx, ctx.PropertyId, req.QuotationId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Quotation not found.");
            if (req.Version is { } v && v != q.CurrentVersion) throw new HccException(HccErrorCode.HCC_QUO_NOT_LATEST, $"Only the latest version (v{q.CurrentVersion}) can be accepted.");
            if (q.Status is not ("SENT" or "EXPIRED")) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a sent quotation can be accepted.");
            var reason = (req.Reason ?? "").Trim();
            if ((q.Status == "EXPIRED" || q.ValidUntil < bd) && !(boss && reason.Length >= 3))
                throw new HccException(HccErrorCode.HCC_QUO_EXPIRED, $"This quotation expired on {q.ValidUntil:yyyy-MM-dd}. Revise it, or a Manager accepts it with a reason.");
            var venues = await store.VenuesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var venue = venues.FirstOrDefault(x => x.VenueId == q.VenueId && x.Active) ?? throw HccException.Validation("The quoted hall is not active.");
            var refs = venues.Select(x => new HallVenueRef(N(x.VenueId), x.CombinedOf?.Select(i => N(i)).ToArray())).ToList();
            var holds = await store.HoldsAsync(tx, ctx.PropertyId, q.Dates, c).ConfigureAwait(false);
            if (HallRules.Clash(refs, holds, [N(q.VenueId)], q.Dates, q.Slot) is { } hit)
                throw new HccException(HccErrorCode.HCC_HALL_CLASH, $"{venue.Name} is already booked on {hit.Dates.First(q.Dates.Contains):yyyy-MM-dd} ({hit.Slot}, event {hit.EventId}).");
            var cap = Capacity(venues, venue, q.Setup);
            if (cap is { } capN && q.Pax > capN && !(boss && (req.CapacityReason ?? "").Trim().Length >= 3))
                throw new HccException(HccErrorCode.HCC_HALL_CAPACITY, $"{q.Pax} guests is above {venue.Name}'s {q.Setup} capacity of {capN}. A Manager can override with a reason.");
            var master = new GstMaster(await gstStore.ActiveVersionsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false));
            var total = HallBilling.Price(master, q.Lines, bd).Totals.Total;
            var plan = q.Advance.Count > 0 ? HallBilling.PlanAmounts(total, q.Advance.Select(r => r with { Paid = Paise.Zero }).ToList()) : HallBilling.AdvancePlan(total, q.Dates[0], bd);
            var physical = venue.CombinedOf is { Length: > 0 } parts ? parts : [venue.VenueId];
            var holdRows = (from vid in physical from d in q.Dates from p in Parts(q.Slot) select (vid, d, p)).ToList();
            var now = Now;
            var eventId = await store.InsertEventAsync(tx, new NewHallEvent(ctx.PropertyId, q.QuotationId, q.V, q.VenueId, q.Dates, q.Slot, q.EventType, q.GuaranteedPax,
                q.CustomerName, q.CustomerPhone, q.CompanyId, ctx.PrincipalId, now, ctx.IdempotencyKey), q.Lines, plan, holdRows, c).ConfigureAwait(false);
            await store.MarkAcceptedAsync(tx, q.VersionId, q.EnquiryId, c).ConfigureAwait(false);
            long? blockId = null;
            if (req.RoomBlock is { } rb) // F6-int: registerRoomHold equivalent — a group block in the same transaction
            {
                if (inventory is null || groupBlocks is not { Enabled: true }) throw Inventory.GroupBlockOptions.Refusal();
                var arrival = rb.Arrival ?? q.Dates.Min();
                var departure = rb.Departure ?? q.Dates.Max().AddDays(1);
                if (arrival < bd || departure <= arrival) throw HccException.Validation("Room block: arrival from today on and departure after arrival.");
                var cutoff = rb.Cutoff ?? (arrival.AddDays(-2) < bd ? bd : arrival.AddDays(-2));
                if (cutoff < bd || cutoff > arrival) throw HccException.Validation("Room block: the cut-off must be between today's business date and the arrival date.");
                var allot = (rb.Allotments ?? []).Where(x => x.Rooms != 0).ToList();
                if (allot.Count == 0 || allot.Any(x => x.Rooms is < 1 or > 100)) throw HccException.Validation("Room block: hold 1–100 rooms per type.");
                if (allot.Select(x => x.RoomTypeId).Distinct().Count() != allot.Count) throw HccException.Validation("Room block: each room type may appear once.");
                var blk = await inventory.CreateGroupBlockAsync(tx, new Inventory.GroupBlockDraft(ctx.PropertyId, "HEV-" + N(eventId), $"Hall event {N(eventId)} · {q.CustomerName}".Trim() is { Length: > 200 } nm ? nm[..200] : $"Hall event {N(eventId)} · {q.CustomerName}",
                    arrival, departure, cutoff, allot), c).ConfigureAwait(false);
                await store.SetEventBlockAsync(tx, eventId, blk.GroupBlockId, c).ConfigureAwait(false);
                await tx.PublishAsync("GroupBlockCreated", blk.GroupBlockId, new { blk.GroupBlockId, blk.Code }, c).ConfigureAwait(false);
                blockId = blk.GroupBlockId;
            }

            long? receiptId = null;
            if (req.Advance is { AmountPaise: > 0 } a)
            {
                var m = (await TenderAsync(tx, ctx.PropertyId, a.Method, a.Reference, c).ConfigureAwait(false)).Name;
                receiptId = await AdvanceAsync(tx, ctx, eventId, q.CustomerName, plan, bd, new Paise(a.AmountPaise), m, a.Reference, a.TillId, now, c).ConfigureAwait(false);
            }

            await tx.AuditAsync("HALL_QUO_ACCEPT", "hall-event-" + N(eventId),
                $"{q.Number} v{q.V} accepted · {venue.Name} {string.Join(", ", q.Dates.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))} {q.Slot} · total {Rs(total.Value)}"
                + (reason.Length > 0 ? " · " + reason : "") + (receiptId is null ? "" : $" · advance {Rs(req.Advance!.AmountPaise)}") + (blockId is null ? "" : $" · room block {blockId}"), c).ConfigureAwait(false);
            return new HallEventResult(eventId, "CONFIRMED", receiptId, TotalPaise: total.Value);
        }, ct).ConfigureAwait(false);
    }

    private static int? Capacity(IReadOnlyList<HallVenueRow> venues, HallVenueRow v, string setup)
    {
        if (v.Capacity.TryGetValue(setup, out var own)) return own;
        if (v.CombinedOf is not { Length: > 0 } parts) return null;
        var t = 0;
        foreach (var p in parts)
        {
            var pv = venues.FirstOrDefault(x => x.VenueId == p);
            if (pv is null || !pv.Capacity.TryGetValue(setup, out var n)) return null;
            t += n;
        }

        return t;
    }

    private async Task<long> AdvanceAsync(ICommandTransaction tx, CommandContext ctx, long eventId, string who, IReadOnlyList<HallAdvanceRow> plan, DateOnly bd, Paise amount,
        string method, string? reference, int? tillId, DateTime now, CancellationToken c)
    {
        var rid = await store.InsertReceiptAsync(tx, new HallReceiptIn(eventId, bd, "ADVANCE", method, null, false, Trim(reference), amount, ctx.PrincipalId, now), c).ConfigureAwait(false);
        await CashAsync(tx, ctx, method, tillId, true, rid, amount, $"Hall advance · {who}", Trim(reference), c, eventId).ConfigureAwait(false);
        await store.SetAdvancePaidAsync(tx, eventId, HallBilling.Allocate(plan, amount), c).ConfigureAwait(false);
        await tx.PublishAsync(AccountingEvents.HallPosted, eventId, new HallPostedEvent("hall:advance:" + N(rid), "hall-advance", bd, "Receipt",
            $"Hall advance · {who} · {method}{(Trim(reference) is { } r ? " · " + r : "")} · event {N(eventId)}",
            [new HallPostedEntry("method:" + method, amount.Value, 0, Tender: method), new HallPostedEntry("receipt:deposit", 0, amount.Value)]), c).ConfigureAwait(false);
        return rid;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim() is { Length: > 60 } t ? t[..60] : s.Trim();

    public async Task<CommandResult<HallEventResult>> ReceiveAdvanceAsync(CommandContext ctx, HallAdvanceRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (req.AmountPaise <= 0) throw HccException.Validation("Advance: enter whole paise greater than zero.");
        var method = (await TenderAsync(null, ctx.PropertyId, req.Method, req.Reference, ct).ConfigureAwait(false)).Name;
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HallPermissions.Accept, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpAdvance }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallRules.MayTakeAdvance, "taking a hall advance (PERM-HAL-003)", c).ConfigureAwait(false);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var e = await store.LockEventAsync(tx, ctx.PropertyId, req.EventId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Hall event not found.");
            if (e.Status != "CONFIRMED") throw new HccException(HccErrorCode.INVALID_TRANSITION, "Advances are taken on a confirmed event only.");
            var rid = await AdvanceAsync(tx, ctx, e.EventId, e.CustomerName, e.Advance, bd, new Paise(req.AmountPaise), method, req.Reference, req.TillId, Now, c).ConfigureAwait(false);
            await tx.AuditAsync("HALL_ADVANCE", "hall-event-" + N(e.EventId), $"advance {Rs(req.AmountPaise)} · {method} · receipt {N(rid)} (no GST, DEC-142)", c).ConfigureAwait(false);
            return new HallEventResult(e.EventId, e.Status, rid);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HallEventResult>> CloseAsync(CommandContext ctx, CloseHallEventRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (req.ActualPax is < 0 or > 5000) throw HccException.Validation("Enter the actual plates (0–5000).");
        var extras = (req.Extras ?? []).Select((x, i) =>
        {
            if (string.IsNullOrWhiteSpace(x.Desc) || x.Desc.Trim().Length < 2) throw HccException.Validation("Each extra needs a description.");
            if (x.Qty <= 0) throw HccException.Validation("Extra quantity: a whole number.");
            if (x.RatePaise <= 0) throw HccException.Validation("Extra rate: enter whole paise greater than zero.");
            var kind = x.Kind is "EXTRA" or "MENU" or "OTHER" or "ROOM" ? x.Kind : "EXTRA";
            return new HallLine("X" + N(i + 1), kind, x.Desc.Trim(), x.Qty, string.IsNullOrWhiteSpace(x.Unit) ? "each" : x.Unit.Trim(), new Paise(x.RatePaise),
                new Paise(x.ListRatePaise ?? x.RatePaise), Paise.Zero, x.GstClass ?? (kind == "MENU" ? HallBilling.ClassBanquetFood : HallBilling.ClassService));
        }).ToList();
        var settle = req.Settle ?? new HallSettleIn("DUE");
        var mode = (settle.Mode ?? "DUE").Trim().ToUpperInvariant();
        if (mode is not ("TENDER" or "COMPANY" or "UDHAAR" or "DUE")) throw HccException.Validation("Settle by tender, company credit, Udhaar, or leave the balance due.");
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HallPermissions.Bill, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpClose }, req, async (tx, c) =>
        {
            var role = await RoleAsync(tx, ctx.PrincipalId, HallRules.MayBill, "the event-day bill (PERM-HAL-004)", c).ConfigureAwait(false);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var e = await store.LockEventAsync(tx, ctx.PropertyId, req.EventId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Hall event not found.");
            if (e.Status != "CONFIRMED") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This event is already billed or cancelled.");
            if (bd < e.EventDate) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"The bill is made on or after the event day ({e.EventDate:yyyy-MM-dd}).");
            if (!HallBilling.DiscountWithin(extras, HallBilling.DiscountLimitBps(role))) throw new HccException(HccErrorCode.HCC_DISCOUNT_LIMIT, "A discount on the bill above your limit needs the Owner's approval.");
            if (mode == "COMPANY" && settle.CompanyId is null) throw HccException.Validation("Choose the company account.");
            if (mode == "UDHAAR" && role is not ("Owner" or "Manager")) throw new HccException(HccErrorCode.FORBIDDEN, "Udhaar (DEC-144) needs the Manager or Owner.");
            var master = new GstMaster(await gstStore.ActiveVersionsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false));
            var lines = HallBilling.BillLines(e.Lines, e.GuaranteedPax, req.ActualPax, extras);
            var (priced, t) = HallBilling.Price(master, lines, bd);
            var paid = new Paise(e.PaidPaise);
            var applied = paid < t.Total ? paid : t.Total;
            var balance = t.Total - applied;
            string? method = null;
            if (balance.Value > 0 && mode == "TENDER") method = (await TenderAsync(tx, ctx.PropertyId, settle.Method, settle.Reference, c).ConfigureAwait(false)).Name;
            var billed = HallBilling.BilledPax(e.GuaranteedPax, req.ActualPax);
            await store.BillAsync(tx, e.EventId, bd, new HallBill(req.ActualPax, billed, priced, t, applied, balance, balance.Value > 0 ? mode : "DUE"), extras, c).ConfigureAwait(false);
            var issued = await invoices.IssueInTransactionAsync(tx, new IssueInvoiceRequest(HallInvoiceSource.SourceKind, e.EventId,
                string.IsNullOrWhiteSpace(req.BuyerGstin) ? "B2C" : "B2B", e.CustomerName, string.IsNullOrWhiteSpace(req.BuyerGstin) ? null : req.BuyerGstin.Trim(), string.IsNullOrWhiteSpace(req.BuyerAddress) ? null : req.BuyerAddress.Trim()), c).ConfigureAwait(false);
            await store.SetInvoiceAsync(tx, e.EventId, issued.InvoiceId, issued.InvoiceNo, c).ConfigureAwait(false);
            var now = Now;
            var who = e.CustomerName;
            var revenue = priced.GroupBy(l => l.Line.Kind == "MENU" ? "line:food" : l.Line.Kind == "ROOM" ? "line:room" : "ledger:4400")
                .Select(g => new HallPostedEntry(g.Key, 0, g.Sum(x => x.Taxable.Value))).ToList();
            await tx.PublishAsync(AccountingEvents.HallPosted, e.EventId, new HallPostedEvent("hall:invoice:" + N(e.EventId), "hall-invoice", bd, "Sales",
                $"Hall invoice {issued.InvoiceNo} · {who} · {billed} plates · event {N(e.EventId)}",
                [new HallPostedEntry("folio:guest", t.Total.Value, 0), .. revenue, new HallPostedEntry("folio:cgst", 0, t.Cgst.Value), new HallPostedEntry("folio:sgst", 0, t.Sgst.Value)]), c).ConfigureAwait(false);
            if (applied.Value > 0)
                await tx.PublishAsync(AccountingEvents.HallPosted, e.EventId, new HallPostedEvent("hall:advance-apply:" + N(e.EventId), "hall-advance-apply", bd, "Journal",
                    $"Hall advance applied · invoice {issued.InvoiceNo} · {who}", [new HallPostedEntry("receipt:deposit", applied.Value, 0), new HallPostedEntry("folio:guest", 0, applied.Value)]), c).ConfigureAwait(false);
            long? receiptId = null;
            if (balance.Value > 0 && mode != "DUE")
            {
                receiptId = await store.InsertReceiptAsync(tx, new HallReceiptIn(e.EventId, bd, "BALANCE", method, mode == "COMPANY" ? settle.CompanyId : null, mode == "UDHAAR",
                    Trim(settle.Reference), balance, ctx.PrincipalId, now), c).ConfigureAwait(false);
                await CashAsync(tx, ctx, method, settle.TillId, true, receiptId.Value, balance, $"Hall balance · {who}", Trim(settle.Reference), c, e.EventId).ConfigureAwait(false);
                var dr = mode switch
                {
                    "COMPANY" => new HallPostedEntry("receipt:company-credit", balance.Value, 0, CompanyId: settle.CompanyId),
                    "UDHAAR" => new HallPostedEntry("receipt:udhaar", balance.Value, 0),
                    _ => new HallPostedEntry("method:" + method, balance.Value, 0, Tender: method),
                };
                await tx.PublishAsync(AccountingEvents.HallPosted, e.EventId, new HallPostedEvent("hall:balance:" + N(receiptId.Value), "hall-balance", bd, mode == "TENDER" ? "Receipt" : "Journal",
                    $"Hall balance · {who} · {(mode == "COMPANY" ? "company credit " + N(settle.CompanyId!.Value) : mode == "UDHAAR" ? "Udhaar" : method)} · invoice {issued.InvoiceNo}",
                    [dr, new HallPostedEntry("folio:guest", 0, balance.Value)]), c).ConfigureAwait(false);
            }

            await tx.AuditAsync("HALL_EVENT_CLOSE", "hall-event-" + N(e.EventId),
                $"{issued.InvoiceNo} · {billed} plates (guaranteed {e.GuaranteedPax}, actual {req.ActualPax}) · total {Rs(t.Total.Value)} · advances applied {Rs(applied.Value)} · balance {Rs(balance.Value)} {mode}", c).ConfigureAwait(false);
            return new HallEventResult(e.EventId, "BILLED", receiptId, issued.InvoiceNo, t.Total.Value, applied.Value, balance.Value, BilledPax: billed);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HallEventResult>> CancelAsync(CommandContext ctx, CancelHallEventRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length < 3) throw HccException.Validation("Give the reason for the cancellation.");
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HallPermissions.Bill, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpCancel }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallRules.MayCancel, "cancelling a hall event (PERM-HAL-004)", c).ConfigureAwait(false);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var e = await store.LockEventAsync(tx, ctx.PropertyId, req.EventId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Hall event not found.");
            if (e.Status != "CONFIRMED") throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a confirmed event can be cancelled (a billed one takes a credit note).");
            var master = new GstMaster(await gstStore.ActiveVersionsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false));
            var q = HallBilling.Cancel(master, e.Lines, e.EventDate, bd, new Paise(e.PaidPaise));
            await store.CancelAsync(tx, e.EventId, new HallCancellationIn(bd, q, ctx.PrincipalId, reason), c).ConfigureAwait(false);
            string released = "";
            if (inventory is not null && await store.OpenEventBlockAsync(tx, e.EventId, c).ConfigureAwait(false) is { } blk) // F6-int: no booking, no held rooms
            {
                var r = await inventory.ReleaseGroupBlockAsync(tx, ctx.PropertyId, blk.BlockId, blk.Version, c).ConfigureAwait(false);
                await tx.PublishAsync("GroupBlockReleased", r.GroupBlockId, new { r.GroupBlockId, r.Code }, c).ConfigureAwait(false);
                released = $" · room block {r.Code} released";
            }
            if (q.Kept.Value > 0)
                await tx.PublishAsync(AccountingEvents.HallPosted, e.EventId, new HallPostedEvent("hall:cancel:" + N(e.EventId), "hall-cancel", bd, "Journal",
                    $"Hall cancellation charge · {e.CustomerName} · {q.DaysBefore} days before · kept {q.KeptBps / 100m:0.##} % · event {N(e.EventId)}",
                    HallBilling.CancelEntries(q.Kept, q.TaxBps).Select(x => x.Ledger switch
                    {
                        "2000" => new HallPostedEntry("receipt:deposit", x.Amount.Value, 0),
                        "4300" => new HallPostedEntry("line:attrition", 0, -x.Amount.Value),
                        "2110" => new HallPostedEntry("folio:cgst", 0, -x.Amount.Value),
                        _ => new HallPostedEntry("folio:sgst", 0, -x.Amount.Value),
                    }).ToList()), c).ConfigureAwait(false);
            await tx.AuditAsync("HALL_EVENT_CANCEL", "hall-event-" + N(e.EventId),
                $"cancelled {q.DaysBefore} days before · kept {Rs(q.Kept.Value)} ({q.KeptBps / 100m:0.##} % slab) · refund {Rs(q.Refund.Value)}{(q.Refund.Value > 0 ? " awaiting approval" : "")} · {reason}{released}", c).ConfigureAwait(false);
            return new HallEventResult(e.EventId, "CANCELLED", KeptPaise: q.Kept.Value, RefundPaise: q.Refund.Value);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HallEventResult>> RefundAsync(CommandContext ctx, HallRefundRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HallPermissions.Bill, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpRefund }, req, async (tx, c) =>
        {
            var role = await RoleAsync(tx, ctx.PrincipalId, r => r is "Owner" or "Manager", "a hall refund is approved by the Manager (up to ₹50,000) or the Owner", c).ConfigureAwait(false);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var e = await store.LockEventAsync(tx, ctx.PropertyId, req.EventId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Hall event not found.");
            if (e.RefundState != "PENDING" || e.RefundPaise is not { } refund) throw new HccException(HccErrorCode.INVALID_TRANSITION, "No refund is waiting on this event.");
            if (e.CancelBy == ctx.PrincipalId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot approve a refund on your own cancellation.");
            var roles = await store.RolesAsync(tx, ctx.PrincipalId, c).ConfigureAwait(false);
            if (!roles.Any(r => HallBilling.RefundApprover(r, new Paise(refund))))
                throw new HccException(HccErrorCode.FORBIDDEN, $"A refund above {Rs(HallBilling.RefundManagerLimitPaise)} needs the Owner.");
            _ = role;
            var method = (await TenderAsync(tx, ctx.PropertyId, req.Method, req.Reference, c, e.EventId).ConfigureAwait(false)).Name; // CU-PAY-B: retired only if the original receipt's row
            var rid = await store.InsertReceiptAsync(tx, new HallReceiptIn(e.EventId, bd, "REFUND", method, null, false, Trim(req.Reference), new Paise(refund), ctx.PrincipalId, Now), c).ConfigureAwait(false);
            await CashAsync(tx, ctx, method, req.TillId, false, rid, new Paise(refund), $"Hall refund · {e.CustomerName}", Trim(req.Reference), c).ConfigureAwait(false);
            await store.MarkRefundedAsync(tx, e.EventId, ctx.PrincipalId, c).ConfigureAwait(false);
            await tx.PublishAsync(AccountingEvents.HallPosted, e.EventId, new HallPostedEvent("hall:refund:" + N(rid), "hall-refund", bd, "Payment",
                $"Hall refund · {e.CustomerName} · {method} · event {N(e.EventId)}",
                [new HallPostedEntry("receipt:deposit", refund, 0), new HallPostedEntry("method:" + method, 0, refund, Tender: method)]), c).ConfigureAwait(false);
            await tx.AuditAsync("HALL_REFUND", "hall-event-" + N(e.EventId), $"refund {Rs(refund)} · {method} · receipt {N(rid)}", c).ConfigureAwait(false);
            return new HallEventResult(e.EventId, "CANCELLED", rid, RefundPaise: refund);
        }, ct).ConfigureAwait(false);
    }
}

/// <summary>The event-day bill as an invoice source (INV series HPE/…, DEC-197): lines with their tax snapshot; credit notes as for rooms
/// (a correction after printing is a credit note; the books get it from the credit-note flow of a later wave — H1 posts none).</summary>
public sealed class HallInvoiceSource(IHallBillingStore store) : IInvoiceSource
{
    public const string SourceKind = "hall";

    public string Kind => SourceKind;

    public bool CreditNoteRequests => true; // F6-H3: a printed hall invoice is corrected by credit note (spec §13), requested and decided like a room invoice's (PERM-INV-005, Owner decides)

    public async Task LockAsync(ICommandTransaction tx, int propertyId, long sourceId, CancellationToken ct) =>
        _ = await store.LockEventAsync(tx, propertyId, sourceId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Hall event not found.");

    public async Task<InvoiceSourceDocument> LoadForIssueAsync(ICommandTransaction tx, int propertyId, long sourceId, CancellationToken ct)
    {
        var (e, lines, date) = await store.InvoiceLinesAsync(tx, propertyId, sourceId, ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "The hall event is not billed yet.");
        var src = lines.Select((l, i) => new SourceLine(i + 1, "hall", date,
            $"{l.Line.Desc} ×{l.Line.Qty.ToString(CultureInfo.InvariantCulture)}", Category(l.Line.GstClass), l.Taxable, l.TaxBps, l.Cgst + l.Sgst, l.Cgst, l.Sgst)).ToList();
        return new InvoiceSourceDocument(SourceKind, e.EventId, "HEV-" + e.EventId.ToString(CultureInfo.InvariantCulture), e.CustomerName, e.EventDate, e.EventDate, src, InvoiceDocType.TaxInvoice);
    }

    public static GstCategory Category(string gstClass) => gstClass switch
    {
        HallBilling.ClassRoom => GstCategory.Room,
        HallBilling.ClassHallRent or HallBilling.ClassBanquetFood => GstCategory.Banquet,
        _ => GstCategory.Other,
    };

    public async Task<IReadOnlyList<long?>> PostCreditAsync(ICommandTransaction tx, int propertyId, long sourceId, string creditNoteNo, string reason, DateOnly date,
        IReadOnlyList<CreditPosting> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(lines);
        var (e, priced, _) = await store.InvoiceLinesAsync(tx, propertyId, sourceId, ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "The hall event is not billed; there is no invoice to credit.");
        // The credit reverses the invoice entry: Dr revenue (taxable per ledger) + CGST + SGST, Cr the event receivable (1100).
        var revenue = new Dictionary<string, long>(StringComparer.Ordinal);
        long cgst = 0, sgst = 0;
        foreach (var l in lines)
        {
            var src = priced[(int)(l.SourceLineId ?? l.CreditLineNo) - 1].Line;
            var key = src.Kind == "MENU" ? "line:food" : src.Kind == "ROOM" ? "line:room" : "ledger:4400";
            revenue[key] = revenue.GetValueOrDefault(key) + l.Taxable.Value;
            cgst += l.Cgst.Value;
            sgst += l.Sgst.Value;
        }

        var total = revenue.Values.Sum() + cgst + sgst;
        List<HallPostedEntry> entries = [.. revenue.Select(x => new HallPostedEntry(x.Key, x.Value, 0)), new HallPostedEntry("folio:cgst", cgst, 0), new HallPostedEntry("folio:sgst", sgst, 0),
            new HallPostedEntry("folio:guest", 0, total)];
        await tx.PublishAsync(AccountingEvents.HallPosted, e.EventId, new HallPostedEvent("hall:credit-note:" + creditNoteNo, "hall-credit-note", date, "Journal",
            $"Hall credit note {creditNoteNo} · {e.CustomerName} · {reason}", entries.Where(x => x.DebitPaise + x.CreditPaise != 0).ToList()), ct).ConfigureAwait(false);
        return lines.Select(_ => (long?)null).ToList();
    }
}
