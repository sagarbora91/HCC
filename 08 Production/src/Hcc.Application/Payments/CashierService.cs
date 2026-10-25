using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;
using static Hcc.Application.Billing.BillingService;

namespace Hcc.Application.Payments;

/// <summary>
/// Tills, shifts, counts and the shared-till movement port (DEC-052, DEC-035). Ports the prototype stream F cashier handlers
/// (<c>SHIFT_SUBMIT</c>, <c>SHIFT_DIFFERENCE_DECIDE</c> in <c>03 Prototype/src/services/mock/handlers/closing.ts</c>) to
/// production shifts: one active shift per till, several per business date, float carried from the previous counted close.
/// </summary>
public sealed class CashierService(ICommandRunner runner, IBillingStore store, IAccessPolicy policy, TimeProvider clock) : ICashDrawerPort
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static CommandContext Ctx(Caller c, string operation, string key) => new(c.PropertyId, c.PrincipalId, operation, key);

    public async Task<CommandResult<TillResult>> CreateTillAsync(Caller caller, string idempotencyKey, CreateTillRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.Configuration, ct: ct).ConfigureAwait(false);
        var code = BillingText.Required(req.Code, 20, "Enter a till code.").ToUpperInvariant();
        if (!code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-')) throw HccException.Validation("A till code uses letters, digits and '-' only.");
        var name = BillingText.Required(req.Name, 100, "Enter a till name.");
        return await runner.RunAsync(Ctx(caller, "CreateTill", idempotencyKey), req, async (tx, ct2) =>
        {
            if (await store.TillCodeExistsAsync(tx, caller.PropertyId, code, ct2).ConfigureAwait(false))
                throw HccException.Validation($"A till with code {code} already exists.");
            var t = await store.InsertTillAsync(tx, caller.PropertyId, code, name, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("TILL_CREATED", Target("till", t.TillId), $"{code} · {name}", ct2).ConfigureAwait(false);
            return new TillResult(t.TillId, t.Code, t.Name, t.Version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ShiftResult>> OpenShiftAsync(Caller caller, string idempotencyKey, OpenShiftRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ShiftCount, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(Ctx(caller, "OpenShift", idempotencyKey), req, async (tx, ct2) =>
        {
            var till = await CashDesk.RequireTillAsync(store, tx, caller.PropertyId, req.TillId, ct2).ConfigureAwait(false);
            var date = await store.BusinessDateAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false) ?? throw NotFound("Property not found.");
            if (await store.ActiveShiftAsync(tx, till.TillId, ct2).ConfigureAwait(false) is { } active)
                throw Invalid(active.Status == "Submitted" ? "The last count on this till is awaiting difference approval." : "A shift is already open on this till.");
            var last = await store.LastClosedShiftAsync(tx, till.TillId, ct2).ConfigureAwait(false);
            Paise opening;
            if (last is not null)
            {
                // DEC-035: the opening float is the cash counted at the previous close.
                opening = last.Counted!.Value;
                if (req.OpeningFloatPaise is { } given && given != opening.Value)
                    throw HccException.Validation($"The opening float is carried from the last count ({opening}).");
            }
            else
            {
                if (req.OpeningFloatPaise is not { } given) throw HccException.Validation("Enter the opening float for this till's first shift.");
                opening = Paise.FromWire(given);
                if (opening < Paise.Zero) throw HccException.Validation("The opening float cannot be negative.");
            }

            var s = await store.InsertShiftAsync(tx, till.TillId, date, opening, last?.ShiftId, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("SHIFT_OPENED", Target("shift", s.ShiftId), $"till {till.Code} · {date:yyyy-MM-dd} · float {opening}", ct2).ConfigureAwait(false);
            return ToResult(s);
        }, ct).ConfigureAwait(false);
    }

    // ETP-REUSE: printed cash-count sheet / day pack PDF and owner e-mail from ETP when V:\ETP is available.
    public async Task<CommandResult<ShiftResult>> SubmitShiftCountAsync(Caller caller, string idempotencyKey, SubmitShiftCountRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        VersionToken.Validate(req.ExpectedVersion);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ShiftCount, new AccessScope(BillingPermissions.ShiftScope, req.ShiftId), ct: ct).ConfigureAwait(false);
        var counted = Paise.FromWire(req.CountedPaise);
        if (counted < Paise.Zero) throw HccException.Validation("Enter a valid physical cash count.");
        string? denominations = null;
        if (req.Notes is not null || req.CoinsPaise is not null)
        {
            var notes = req.Notes ?? new Dictionary<string, int>();
            var coins = req.CoinsPaise ?? 0;
            if (CashDrawer.DenominationTotal(notes, coins) != counted) throw HccException.Validation("The denomination grid does not add up to the counted total.");
            denominations = JsonSerializer.Serialize(new { notes = new SortedDictionary<string, int>(notes.ToDictionary(), StringComparer.Ordinal), coinsPaise = coins });
        }

        var reason = BillingText.Optional(req.Reason, 200);
        return await runner.RunAsync(Ctx(caller, "SubmitShiftCount", idempotencyKey), req, async (tx, ct2) =>
        {
            var peek = await store.PeekShiftAsync(tx, req.ShiftId, ct2).ConfigureAwait(false);
            if (peek is null || peek.PropertyId != caller.PropertyId) throw NotFound("Shift not found.");
            await store.LockTillAsync(tx, peek.TillId, ct2).ConfigureAwait(false);
            var s = (await store.LockShiftAsync(tx, req.ShiftId, ct2).ConfigureAwait(false))!;
            EnsureVersion(s.Version, req.ExpectedVersion, "The shift");
            if (s.Status == "Submitted") throw Invalid("A cash count is already awaiting difference approval.");
            if (s.Status != "Open") throw Invalid("This shift is already closed.");
            var expected = await CashDesk.ExpectedAsync(store, tx, s, ct2).ConfigureAwait(false);
            var diff = counted - expected.Expected;
            var now = Now;
            ShiftRow updated;
            if (diff == Paise.Zero)
            {
                updated = await store.UpdateShiftAsync(tx, s.ShiftId, s.Version, new ShiftUpdate("Closed", expected.Expected, counted, caller.PrincipalId, now, null, null, caller.PrincipalId, now), ct2).ConfigureAwait(false);
                await store.InsertShiftCountAsync(tx, s.ShiftId, expected.Expected, counted, denominations, null, null, caller.PrincipalId, now, ct2).ConfigureAwait(false);
                await tx.AuditAsync("SHIFT_COUNTED", Target("shift", s.ShiftId), $"Cash counted and reconciled exactly · {expected.Expected}", ct2).ConfigureAwait(false);
            }
            else
            {
                if (reason.Length == 0) throw HccException.Validation("A reason is required when the count differs from the expected cash.");
                var abs = diff < Paise.Zero ? -diff : diff;
                var (approvalId, _) = await store.InsertApprovalAsync(tx, new NewApproval(caller.PropertyId, AdjustmentAction.CashDifference, null, s.ShiftId, "shift", s.ShiftId,
                    abs, null, expected.Expected, counted, reason, caller.PrincipalId, now, now + ApprovalRules.TimeToLive), ct2).ConfigureAwait(false);
                updated = await store.UpdateShiftAsync(tx, s.ShiftId, s.Version, new ShiftUpdate("Submitted", expected.Expected, counted, caller.PrincipalId, now, reason, approvalId, null, null), ct2).ConfigureAwait(false);
                await store.InsertShiftCountAsync(tx, s.ShiftId, expected.Expected, counted, denominations, reason, approvalId, caller.PrincipalId, now, ct2).ConfigureAwait(false);
                await tx.AuditAsync("SHIFT_COUNTED", Target("shift", s.ShiftId),
                    $"Cash count submitted · expected {expected.Expected}, counted {counted} ({(diff > Paise.Zero ? "over" : "short")} {abs}) · approval {approvalId} pending", ct2).ConfigureAwait(false);
            }

            await tx.PublishAsync("ShiftCounted", s.ShiftId, new { shiftId = s.ShiftId, tillId = s.TillId, status = updated.Status, expectedPaise = expected.Expected.Value, countedPaise = counted.Value }, ct2).ConfigureAwait(false);
            return ToResult(updated);
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> RecordMovementAsync(ICommandTransaction tx, int tillId, CashMovementKind kind, string sourceType, long sourceId, Paise amount,
        string label, string reference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        if (kind is CashMovementKind.Payment or CashMovementKind.Deposit or CashMovementKind.Refund or CashMovementKind.DepositReturn or CashMovementKind.CompanyCredit)
            throw new InvalidOperationException("Folio cash moves only through RecordReceipt / ExecuteAdjustment.");
        var inward = kind is CashMovementKind.PosSale or CashMovementKind.ExpenseReversal or CashMovementKind.TransferIn;
        if (amount == Paise.Zero || (inward ? amount < Paise.Zero : amount > Paise.Zero))
            throw HccException.Validation($"A {CashDrawer.Code(kind)} movement must be {(inward ? "positive (into the drawer)" : "negative (out of the drawer)")}.");
        var p = tx.Context.PropertyId;
        var date = await store.BusinessDateAsync(tx, p, ct).ConfigureAwait(false) ?? throw NotFound("Property not found.");
        var shift = await CashDesk.RequireOpenShiftAsync(store, tx, p, tillId, date, ct).ConfigureAwait(false);
        return await store.InsertMovementAsync(tx, new NewMovement(shift.ShiftId, shift.TillId, date, kind, sourceType, sourceId, amount.EnsureFieldRange(),
            Clip(label, 200), Clip(reference, 120), tx.Context.PrincipalId, Now), ct).ConfigureAwait(false);
    }

    internal static ShiftResult ToResult(ShiftRow s) => new(s.ShiftId, s.TillId, s.BusinessDate, s.Status, s.OpeningFloat.Value, s.Expected?.Value,
        s.Counted?.Value, s.ApprovalId, s.Version);
}

/// <summary>Shared till/shift steps used by receipts, refunds and cash-difference decisions (lock order: till → shift → approval).</summary>
internal static class CashDesk
{
    public static async Task<TillRow> RequireTillAsync(IBillingStore store, ICommandTransaction tx, int propertyId, int tillId, CancellationToken ct)
    {
        var t = await store.LockTillAsync(tx, tillId, ct).ConfigureAwait(false);
        if (t is null || t.PropertyId != propertyId) throw NotFound("Till not found.");
        return t;
    }

    /// <summary>Cash requires an open shift on that till for the current business date (contracts §3 RecordReceipt).</summary>
    public static async Task<ShiftRow> RequireOpenShiftAsync(IBillingStore store, ICommandTransaction tx, int propertyId, int tillId, DateOnly businessDate, CancellationToken ct)
    {
        var till = await RequireTillAsync(store, tx, propertyId, tillId, ct).ConfigureAwait(false);
        var s = await store.ActiveShiftAsync(tx, till.TillId, ct).ConfigureAwait(false);
        if (s is null) throw Invalid("No cash shift is open on this till. Open a shift first.");
        if (s.Status == "Submitted") throw Invalid("The cash count on this till is awaiting difference approval; cash cannot move until it is decided.");
        if (s.BusinessDate != businessDate) throw Invalid("The open shift belongs to an earlier business date. Count and close it first.");
        return s;
    }

    public static async Task RecordReceiptMovementAsync(IBillingStore store, ICommandTransaction tx, long shiftId, int tillId, DateOnly date, ReceiptKind kind,
        long receiptId, Paise amount, long folioId, string reference, int principalId, DateTime now, CancellationToken ct)
    {
        var m = CashDrawer.ForReceipt(kind, amount);
        await store.InsertMovementAsync(tx, new NewMovement(shiftId, tillId, date, m.Kind, "receipt", receiptId, m.Amount,
            $"{FolioTotals.KindCode(kind)} · folio {folioId}", Clip(reference, 120), principalId, now), ct).ConfigureAwait(false);
    }

    public static async Task<ExpectedCash> ExpectedAsync(IBillingStore store, ICommandTransaction tx, ShiftRow s, CancellationToken ct)
    {
        var mv = await store.MovementsAsync(tx, s.ShiftId, ct).ConfigureAwait(false);
        var e = CashDrawer.Expected(s.OpeningFloat, mv.Select(m => new CashMovement(m.Kind, m.Amount)).ToList());
        if (!e.Consistent) throw new InvalidOperationException("Expected-cash parts do not add up.");
        return e;
    }

    /// <summary>PERM-SHF-002 / PERM-SOD-004: a manager or owner (not the submitter) accepts or rejects a counted difference.</summary>
    public static async Task<ApprovalResult> DecideCashDifferenceAsync(IBillingStore store, IAccessPolicy policy, ICommandTransaction tx, Caller caller,
        ApprovalRow peek, bool approve, string decisionReason, string expectedVersion, DateTime now, CancellationToken ct)
    {
        var peekShift = await store.PeekShiftAsync(tx, peek.ShiftId!.Value, ct).ConfigureAwait(false) ?? throw NotFound("Shift not found.");
        await store.LockTillAsync(tx, peekShift.TillId, ct).ConfigureAwait(false);
        var s = (await store.LockShiftAsync(tx, peekShift.ShiftId, ct).ConfigureAwait(false))!;
        var a = (await store.LockApprovalAsync(tx, peek.ApprovalId, ct).ConfigureAwait(false))!;
        EnsureVersion(a.Version, expectedVersion, "The approval request");
        if (a.Status != ApprovalStatus.Pending) throw Invalid("This request is no longer pending.");
        if (a.RequestedBy == caller.PrincipalId) throw Forbidden("You cannot approve the difference on your own count (PERM-SOD-004).");
        if (s.Status != "Submitted" || s.ApprovalId != a.ApprovalId) throw Invalid("This count does not belong to the current shift.");

        ShiftRow updated;
        ApprovalStatus status;
        if (approve)
        {
            await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ShiftDifferenceApprove, amountPaise: a.Amount.Value, ct: ct).ConfigureAwait(false);
            var expected = await ExpectedAsync(store, tx, s, ct).ConfigureAwait(false);
            if (expected.Expected != a.Expected) throw Stale("Cash movements changed after this count. Reject it and recount.");
            updated = await store.UpdateShiftAsync(tx, s.ShiftId, s.Version, new ShiftUpdate("Closed", s.Expected, s.Counted, s.SubmittedBy, null, null, s.ApprovalId, caller.PrincipalId, now), ct).ConfigureAwait(false);
            status = ApprovalStatus.Approved;
        }
        else
        {
            await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ShiftDifferenceApprove, ct: ct).ConfigureAwait(false);
            updated = await store.UpdateShiftAsync(tx, s.ShiftId, s.Version, new ShiftUpdate("Open", null, null, null, null, null, null, null, null), ct).ConfigureAwait(false);
            status = ApprovalStatus.Rejected;
        }

        var v = await store.UpdateApprovalAsync(tx, a.ApprovalId, a.Version, new ApprovalChange(status, caller.PrincipalId, now, decisionReason, null, null, null), ct).ConfigureAwait(false);
        var diff = a.Counted!.Value - a.Expected!.Value;
        await tx.AuditAsync("SHIFT_DIFFERENCE_DECIDED", Target("shift", s.ShiftId),
            approve ? $"difference {diff} accepted · shift closed" : "difference rejected · shift reopened for recount", ct).ConfigureAwait(false);
        return new ApprovalResult(a.ApprovalId, BillingCodes.Code(a.Action), status.ToString(), a.Amount.Value, v, null, null, updated.Version);
    }
}
