using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Closing;
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
/// Cash leaving the drawer (Wave B1; DEC-073, DEC-078; audit A03b-01): bank deposits (maker-checker), till-to-till transfers and Owner
/// drawings, each a signed <c>pay.cash_movement</c> so expected cash, counts, the day close and the cash book see them.
/// <list type="bullet">
/// <item>Expected cash drops when the cash is RECORDED (it has physically left the drawer). The confirmation of a deposit is the control: a
/// rejection puts the cash back into that till's open shift (a Banking movement with the opposite sign, dated the rejection's business date,
/// audited). A transfer drops the giver's expected at once and raises the receiver's when they accept it on their own open shift; a rejected
/// transfer returns to the giver's till.</item>
/// <item>Maker-checker reuses <c>bill.approval</c> (actions cash-banking / till-transfer, bound to the record and its amount; the decider is
/// never the requester, enforced in code and by SQL). The requests never lapse (the cash has gone), so they are stored with a far expiry.</item>
/// <item>Nothing may take a drawer below zero; nothing touches a locked cash-book day (DEC-065): recording on the business date, and every
/// decision on a record whose date is locked, is refused until the Owner reopens the day.</item>
/// <item>Accounting (projector): CashBanked = Dr bank / Cr cash; CashBankingRejected reverses it; OwnerDrawingRecorded = Dr Owner's drawings / Cr
/// cash. A till transfer is cash to cash inside the one 1000 Cash ledger: no voucher.</item>
/// </list>
/// </summary>
public sealed class CashOutService(ICommandRunner runner, IBillingStore store, ICashOutStore cashOut, IAccessPolicy policy, TimeProvider clock,
    IBusinessDayGuard? days = null) : ICashOutDecisions
{
    /// <summary>Longest range of the cash-out list (same as the cash book).</summary>
    public const int ListMaxDays = 93;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static CommandContext Ctx(Caller c, string operation, string key) => new(c.PropertyId, c.PrincipalId, operation, key);

    // ── RecordCashBanking ──

    public async Task<CommandResult<CashTransferResult>> RecordCashBankingAsync(Caller caller, string idempotencyKey, RecordCashBankingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, CashOutPermissions.BankingRecord, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        var amount = CashOutRules.Amount(req.AmountPaise);
        var bank = CashOutRules.Bank(req.Bank);
        var slip = CashOutRules.SlipNumber(req.SlipNo);
        var (contentType, image) = CashOutRules.SlipImage(req.SlipImageBase64, req.SlipContentType);
        var note = BillingText.Optional(req.Note, 200);
        return await runner.RunAsync(Ctx(caller, "RecordCashBanking", idempotencyKey), req, async (tx, ct2) =>
        {
            var p = caller.PropertyId;
            var date = await store.BusinessDateAsync(tx, p, ct2).ConfigureAwait(false) ?? throw NotFound("Property not found.");
            if (days is not null) await days.EnsureCashBookOpenAsync(tx, p, date, "a bank deposit", ct2).ConfigureAwait(false); // DEC-065
            var till = await CashDesk.RequireTillAsync(store, tx, p, req.TillId, ct2).ConfigureAwait(false);
            var shift = await CashDesk.RequireOpenShiftAsync(store, tx, p, till.TillId, date, ct2).ConfigureAwait(false);
            // "From their open shift's till": PERM-DRW-001 is own-shift scoped for the Cashier and the Manager (DEC-078: own = opened it).
            await policy.RequireAsync(p, caller.PrincipalId, CashOutPermissions.BankingRecord, AccessScopes.Shift(shift.ShiftId), ct: ct2).ConfigureAwait(false);
            var expected = await CashDesk.ExpectedAsync(store, tx, shift, ct2).ConfigureAwait(false);
            CashOutRules.EnsureCovered(expected.Expected, amount, "A bank deposit");
            var now = Now;
            var (id, _) = await cashOut.InsertAsync(tx, new NewCashTransfer(p, CashOutKind.Banking, CashOutRules.Pending, date, amount, till.TillId, shift.ShiftId, null,
                bank, slip, note.Length == 0 ? null : note, caller.PrincipalId, now), ct2).ConfigureAwait(false);
            await cashOut.InsertSlipAsync(tx, id, contentType, image, ct2).ConfigureAwait(false);
            var label = Clip($"Bank deposit · {bank} · slip {slip}", 200);
            await store.InsertMovementAsync(tx, new NewMovement(shift.ShiftId, till.TillId, date, CashMovementKind.Banking, "banking", id, -amount, label,
                Clip("Slip " + slip, 120), caller.PrincipalId, now), ct2).ConfigureAwait(false);
            var (approvalId, version) = await store.InsertApprovalAsync(tx, new NewApproval(p, AdjustmentAction.CashBanking, null, shift.ShiftId, "banking", id, amount,
                null, null, null, label, caller.PrincipalId, now, now + ApprovalRules.NoLapse), ct2).ConfigureAwait(false);
            await cashOut.SetApprovalAsync(tx, id, approvalId, ct2).ConfigureAwait(false);
            await tx.AuditAsync("CASH_BANKED", Target("banking", id),
                $"{amount} · {bank} · slip {slip} · till {till.Code} shift {shift.ShiftId} · photo {image.Length} bytes · confirmation {approvalId} pending", ct2).ConfigureAwait(false);
            await tx.PublishAsync("CashBanked", id, new { transferId = id, tillId = till.TillId, shiftId = shift.ShiftId, businessDate = date, amountPaise = amount.Value, bank, slipNo = slip }, ct2).ConfigureAwait(false);
            return new CashTransferResult(id, "banking", CashOutRules.Pending, amount.Value, date, till.TillId, null, approvalId, version);
        }, ct).ConfigureAwait(false);
    }

    // ── ConfirmCashBanking / AcceptTillTransfer (own commands; DecideApproval routes here too) ──

    public async Task<CommandResult<CashTransferResult>> ConfirmCashBankingAsync(Caller caller, string idempotencyKey, ConfirmCashBankingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        VersionToken.Validate(req.ExpectedVersion);
        var reason = BillingText.Optional(req.Reason, 200);
        return await runner.RunAsync(Ctx(caller, "ConfirmCashBanking", idempotencyKey), req, (tx, ct2) =>
            DecideByTransferAsync(tx, caller, req.TransferId, CashOutKind.Banking, req.Confirm, reason, req.ExpectedVersion, ct2), ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<CashTransferResult>> AcceptTillTransferAsync(Caller caller, string idempotencyKey, AcceptTillTransferRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        VersionToken.Validate(req.ExpectedVersion);
        var reason = BillingText.Optional(req.Reason, 200);
        return await runner.RunAsync(Ctx(caller, "AcceptTillTransfer", idempotencyKey), req, (tx, ct2) =>
            DecideByTransferAsync(tx, caller, req.TransferId, CashOutKind.Till, req.Accept, reason, req.ExpectedVersion, ct2), ct).ConfigureAwait(false);
    }

    private async Task<CashTransferResult> DecideByTransferAsync(ICommandTransaction tx, Caller caller, long transferId, CashOutKind kind, bool approve, string reason,
        string expectedVersion, CancellationToken ct)
    {
        var t = await cashOut.PeekAsync(tx, transferId, ct).ConfigureAwait(false);
        if (t is null || t.PropertyId != caller.PropertyId || t.Kind != kind || t.ApprovalId is null)
            throw NotFound(kind == CashOutKind.Banking ? "Bank deposit not found." : "Till transfer not found.");
        var (_, row) = await DecideCoreAsync(tx, caller, t, approve, reason, expectedVersion, ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<ApprovalResult> DecideAsync(ICommandTransaction tx, Caller caller, ApprovalRow request, bool approve, string reason, string expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request);
        var t = await cashOut.PeekAsync(tx, request.TargetId, ct).ConfigureAwait(false);
        if (t is null || t.PropertyId != caller.PropertyId || t.ApprovalId != request.ApprovalId) throw NotFound("Approval request not found.");
        var (result, _) = await DecideCoreAsync(tx, caller, t, approve, reason, expectedVersion, ct).ConfigureAwait(false);
        return result;
    }

    private async Task<(ApprovalResult Approval, CashTransferResult Transfer)> DecideCoreAsync(ICommandTransaction tx, Caller caller, CashTransferRow peek, bool approve,
        string reason, string expectedVersion, CancellationToken ct)
    {
        var p = caller.PropertyId;
        var u = caller.PrincipalId;
        var banking = peek.Kind == CashOutKind.Banking;
        if (peek.Kind == CashOutKind.Drawing) throw Invalid("An Owner drawing needs no decision.");
        var what = banking ? "bank deposit" : "till transfer";
        if (!approve && reason.Length == 0) throw HccException.Validation($"A reason is required to reject a {what}.");
        var date = await store.BusinessDateAsync(tx, p, ct).ConfigureAwait(false) ?? throw NotFound("Property not found.");
        if (days is not null) // DEC-065: a decision changes a record dated on that cash-book day (the record's date never changes)
            await days.EnsureCashBookOpenAsync(tx, p, peek.BusinessDate, (approve ? (banking ? "confirming" : "accepting") : "rejecting") + " this " + what, ct).ConfigureAwait(false);

        // Who decides, and the shift that receives cash (lock order till → shift → transfer → approval, as everywhere in the cash desk).
        ShiftRow? into = null;
        if (banking)
        {
            await policy.RequireAsync(p, u, CashOutPermissions.BankingConfirm, ct: ct).ConfigureAwait(false);
            if (!approve) into = await ReturnShiftAsync(tx, p, peek.FromTillId, date, "a rejected deposit's cash", ct).ConfigureAwait(false);
        }
        else if (approve)
        {
            var to = await CashDesk.RequireTillAsync(store, tx, p, peek.ToTillId!.Value, ct).ConfigureAwait(false);
            var active = await store.ActiveShiftAsync(tx, to.TillId, ct).ConfigureAwait(false);
            if (active is null || active.Status != "Open" || active.BusinessDate != date)
                throw Invalid($"Open your own shift on till {to.Code} first: a till transfer is accepted on the receiver's open shift.");
            await policy.RequireAsync(p, u, CashOutPermissions.TillTransfer, AccessScopes.Shift(active.ShiftId), ct: ct).ConfigureAwait(false);
            into = active;
        }
        else
        {
            // The receiver (own open shift on the receiving till) or a Manager / Owner who confirms cash (PERM-DRW-002) may send it back.
            if (!await policy.HasAsync(p, u, CashOutPermissions.BankingConfirm, ct: ct).ConfigureAwait(false))
            {
                var active = await store.ActiveShiftAsync(tx, peek.ToTillId!.Value, ct).ConfigureAwait(false);
                if (active is null) throw Forbidden("Only the receiver (on their own open shift on the receiving till) or a Manager or Owner can reject this transfer.");
                await policy.RequireAsync(p, u, CashOutPermissions.TillTransfer, AccessScopes.Shift(active.ShiftId), ct: ct).ConfigureAwait(false);
            }

            into = await ReturnShiftAsync(tx, p, peek.FromTillId, date, "the returned cash", ct).ConfigureAwait(false);
        }

        var t = (await cashOut.LockAsync(tx, peek.TransferId, ct).ConfigureAwait(false))!;
        var a = await store.LockApprovalAsync(tx, t.ApprovalId!.Value, ct).ConfigureAwait(false) ?? throw NotFound("Approval request not found.");
        EnsureVersion(a.Version, expectedVersion, banking ? "The bank deposit" : "The till transfer");
        if (a.Status != ApprovalStatus.Pending || t.Status != CashOutRules.Pending) throw Invalid($"This {what} has already been decided.");
        if (a.RequestedBy == u)
            throw Forbidden(banking
                ? "A different Manager or Owner must confirm a bank deposit (maker-checker): you recorded this one."
                : "The giver cannot accept or reject their own till transfer: the receiver does.");

        var now = Now;
        string status;
        if (approve && banking)
        {
            status = CashOutRules.Confirmed;
            await tx.AuditAsync("CASH_BANKING_CONFIRMED", Target("banking", t.TransferId),
                $"{t.Amount} · {t.Bank} · slip {t.SlipNo} · recorded by {t.RecordedBy}" + (reason.Length > 0 ? " · " + reason : ""), ct).ConfigureAwait(false);
        }
        else if (approve)
        {
            status = CashOutRules.Accepted;
            await store.InsertMovementAsync(tx, new NewMovement(into!.ShiftId, into.TillId, date, CashMovementKind.TransferIn, "transfer-in", t.TransferId, t.Amount,
                Clip($"Till transfer received · transfer {t.TransferId}", 200), Clip(t.Reason ?? "Till transfer", 120), u, now), ct).ConfigureAwait(false);
            await tx.AuditAsync("TILL_TRANSFER_ACCEPTED", Target("transfer", t.TransferId), $"{t.Amount} · into shift {into.ShiftId} · from shift {t.FromShiftId}", ct).ConfigureAwait(false);
        }
        else
        {
            status = CashOutRules.Rejected;
            var (kind, source, label) = banking
                ? (CashMovementKind.Banking, "banking-return", $"Bank deposit rejected · cash back in the drawer · slip {t.SlipNo}")
                : (CashMovementKind.TransferOut, "transfer-return", $"Till transfer returned · transfer {t.TransferId}");
            await store.InsertMovementAsync(tx, new NewMovement(into!.ShiftId, into.TillId, date, kind, source, t.TransferId, t.Amount, Clip(label, 200), Clip(reason, 120), u, now),
                ct).ConfigureAwait(false);
            await tx.AuditAsync(banking ? "CASH_BANKING_REJECTED" : "TILL_TRANSFER_REJECTED", Target(banking ? "banking" : "transfer", t.TransferId),
                $"{t.Amount} returned to till {into.TillId} shift {into.ShiftId} · {reason}", ct).ConfigureAwait(false);
            if (banking)
                await tx.PublishAsync("CashBankingRejected", t.TransferId, new { transferId = t.TransferId, tillId = into.TillId, shiftId = into.ShiftId, businessDate = date,
                    amountPaise = t.Amount.Value, bank = t.Bank, slipNo = t.SlipNo, reason }, ct).ConfigureAwait(false);
        }

        await cashOut.DecideAsync(tx, t.TransferId, new CashTransferDecision(status, u, now, date, reason.Length == 0 ? null : reason, approve && !banking ? into!.ShiftId : null), ct).ConfigureAwait(false);
        var approvalStatus = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        var v = await store.UpdateApprovalAsync(tx, a.ApprovalId, a.Version, new ApprovalChange(approvalStatus, u, now, reason, null, null, null), ct).ConfigureAwait(false);
        await tx.PublishAsync(banking ? "CashBankingDecided" : "TillTransferDecided", t.TransferId,
            new { transferId = t.TransferId, approvalId = a.ApprovalId, status, businessDate = date }, ct).ConfigureAwait(false);
        return (new ApprovalResult(a.ApprovalId, BillingCodes.Code(a.Action), approvalStatus.ToString(), a.Amount.Value, v, t.TransferId, null, null),
            new CashTransferResult(t.TransferId, CashOutRules.Code(t.Kind), status, t.Amount.Value, t.BusinessDate, t.FromTillId, t.ToTillId, a.ApprovalId, v));
    }

    /// <summary>The open shift (today) on the till the cash goes back into; INVALID_TRANSITION naming the till when there is none.</summary>
    private async Task<ShiftRow> ReturnShiftAsync(ICommandTransaction tx, int propertyId, int tillId, DateOnly date, string what, CancellationToken ct)
    {
        var till = await CashDesk.RequireTillAsync(store, tx, propertyId, tillId, ct).ConfigureAwait(false);
        var s = await store.ActiveShiftAsync(tx, till.TillId, ct).ConfigureAwait(false);
        if (s is null || s.Status != "Open" || s.BusinessDate != date)
            throw Invalid($"Open a shift on till {till.Code} first: {what} goes back into that drawer.");
        return s;
    }

    // ── TransferTillCash ──

    public async Task<CommandResult<CashTransferResult>> TransferTillCashAsync(Caller caller, string idempotencyKey, TransferTillCashRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, CashOutPermissions.TillTransfer, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        var amount = CashOutRules.Amount(req.AmountPaise);
        if (req.FromTillId == req.ToTillId) throw HccException.Validation("Choose a different till to receive the cash.");
        var reason = BillingText.Optional(req.Reason, 200);
        return await runner.RunAsync(Ctx(caller, "TransferTillCash", idempotencyKey), req, async (tx, ct2) =>
        {
            var p = caller.PropertyId;
            var date = await store.BusinessDateAsync(tx, p, ct2).ConfigureAwait(false) ?? throw NotFound("Property not found.");
            if (days is not null) await days.EnsureCashBookOpenAsync(tx, p, date, "a till transfer", ct2).ConfigureAwait(false); // DEC-065
            // Both tills locked in id order, so two opposite transfers cannot deadlock.
            var first = await CashDesk.RequireTillAsync(store, tx, p, Math.Min(req.FromTillId, req.ToTillId), ct2).ConfigureAwait(false);
            var second = await CashDesk.RequireTillAsync(store, tx, p, Math.Max(req.FromTillId, req.ToTillId), ct2).ConfigureAwait(false);
            var (from, to) = first.TillId == req.FromTillId ? (first, second) : (second, first);
            var shift = await CashDesk.RequireOpenShiftAsync(store, tx, p, from.TillId, date, ct2).ConfigureAwait(false);
            await policy.RequireAsync(p, caller.PrincipalId, CashOutPermissions.TillTransfer, AccessScopes.Shift(shift.ShiftId), ct: ct2).ConfigureAwait(false);
            var expected = await CashDesk.ExpectedAsync(store, tx, shift, ct2).ConfigureAwait(false);
            CashOutRules.EnsureCovered(expected.Expected, amount, "A till transfer");
            var now = Now;
            var (id, _) = await cashOut.InsertAsync(tx, new NewCashTransfer(p, CashOutKind.Till, CashOutRules.Pending, date, amount, from.TillId, shift.ShiftId, to.TillId,
                null, null, reason.Length == 0 ? null : reason, caller.PrincipalId, now), ct2).ConfigureAwait(false);
            var label = Clip($"Till transfer to {to.Code} · awaiting the receiver", 200);
            await store.InsertMovementAsync(tx, new NewMovement(shift.ShiftId, from.TillId, date, CashMovementKind.TransferOut, "transfer-out", id, -amount, label,
                Clip(reason.Length == 0 ? "Till transfer" : reason, 120), caller.PrincipalId, now), ct2).ConfigureAwait(false);
            var (approvalId, version) = await store.InsertApprovalAsync(tx, new NewApproval(p, AdjustmentAction.TillTransfer, null, shift.ShiftId, "transfer", id, amount,
                null, null, null, Clip($"Till transfer {from.Code} → {to.Code}" + (reason.Length > 0 ? " · " + reason : ""), 200), caller.PrincipalId, now, now + ApprovalRules.NoLapse),
                ct2).ConfigureAwait(false);
            await cashOut.SetApprovalAsync(tx, id, approvalId, ct2).ConfigureAwait(false);
            await tx.AuditAsync("TILL_TRANSFER_SENT", Target("transfer", id), $"{amount} · {from.Code} shift {shift.ShiftId} → {to.Code} · acceptance {approvalId} pending", ct2).ConfigureAwait(false);
            await tx.PublishAsync("TillCashTransferred", id, new { transferId = id, fromTillId = from.TillId, toTillId = to.TillId, shiftId = shift.ShiftId, businessDate = date, amountPaise = amount.Value }, ct2).ConfigureAwait(false);
            return new CashTransferResult(id, "till", CashOutRules.Pending, amount.Value, date, from.TillId, to.TillId, approvalId, version);
        }, ct).ConfigureAwait(false);
    }

    // ── RecordOwnerDrawing ──

    public async Task<CommandResult<CashTransferResult>> RecordOwnerDrawingAsync(Caller caller, string idempotencyKey, RecordOwnerDrawingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, CashOutPermissions.OwnerDrawing, ct: ct).ConfigureAwait(false);
        var amount = CashOutRules.Amount(req.AmountPaise);
        var reason = CashOutRules.Reason(req.Reason, "Give the reason for the drawing.");
        return await runner.RunAsync(Ctx(caller, "RecordOwnerDrawing", idempotencyKey), req, async (tx, ct2) =>
        {
            var p = caller.PropertyId;
            var date = await store.BusinessDateAsync(tx, p, ct2).ConfigureAwait(false) ?? throw NotFound("Property not found.");
            if (days is not null) await days.EnsureCashBookOpenAsync(tx, p, date, "an Owner drawing", ct2).ConfigureAwait(false); // DEC-065
            var till = await CashDesk.RequireTillAsync(store, tx, p, req.TillId, ct2).ConfigureAwait(false);
            var shift = await CashDesk.RequireOpenShiftAsync(store, tx, p, till.TillId, date, ct2).ConfigureAwait(false);
            var expected = await CashDesk.ExpectedAsync(store, tx, shift, ct2).ConfigureAwait(false);
            CashOutRules.EnsureCovered(expected.Expected, amount, "An Owner drawing");
            var now = Now;
            var (id, version) = await cashOut.InsertAsync(tx, new NewCashTransfer(p, CashOutKind.Drawing, CashOutRules.Posted, date, amount, till.TillId, shift.ShiftId, null,
                null, null, reason, caller.PrincipalId, now), ct2).ConfigureAwait(false);
            await store.InsertMovementAsync(tx, new NewMovement(shift.ShiftId, till.TillId, date, CashMovementKind.OwnerDrawing, "owner-drawing", id, -amount,
                Clip("Owner drawing · " + reason, 200), Clip(reason, 120), caller.PrincipalId, now), ct2).ConfigureAwait(false);
            await tx.AuditAsync("OWNER_DRAWING", Target("drawing", id), $"{amount} · till {till.Code} shift {shift.ShiftId} · {reason}", ct2).ConfigureAwait(false);
            await tx.PublishAsync("OwnerDrawingRecorded", id, new { transferId = id, tillId = till.TillId, shiftId = shift.ShiftId, businessDate = date, amountPaise = amount.Value, reason }, ct2).ConfigureAwait(false);
            return new CashTransferResult(id, "drawing", CashOutRules.Posted, amount.Value, date, till.TillId, null, null, version);
        }, ct).ConfigureAwait(false);
    }

    // ── Queries ──

    /// <summary>GET /cashier/transfers: cash-shift holders (PERM-SHF-001, any shift) and slip viewers (PERM-DRW-003, the Accountant) read the list.</summary>
    public async Task<CashTransferListView> ListAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (!await policy.HasAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ShiftCount, AccessScopes.Any, ct).ConfigureAwait(false))
            await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, CashOutPermissions.SlipView, ct: ct).ConfigureAwait(false);
        if (to < from) throw HccException.Validation("'to' must be on or after 'from'.");
        if (to.DayNumber - from.DayNumber + 1 > ListMaxDays) throw HccException.Validation($"Ask for at most {ListMaxDays} days.");
        var desk = await store.ReadCashDeskAsync(caller.PropertyId, ct).ConfigureAwait(false);
        if (desk.BusinessDate is not { } date) throw NotFound("Property not found.");
        var rows = await cashOut.ListAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false);
        return new CashTransferListView(date, rows.Select(ToView).ToList());
    }

    /// <summary>GET /cashier/transfers/{id}/slip: the slip photo (PERM-DRW-003: Manager, Owner, Accountant read-only).</summary>
    public async Task<BankingSlipView> SlipAsync(Caller caller, long transferId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, CashOutPermissions.SlipView, ct: ct).ConfigureAwait(false);
        var slip = await cashOut.SlipAsync(caller.PropertyId, transferId, ct).ConfigureAwait(false)
            ?? throw NotFound("No slip photo is stored for this record (" + CashOutRules.SlipPurgedNote.TrimEnd('.').ToLowerInvariant() + ", or it is not a bank deposit).");
        return new BankingSlipView(transferId, slip.ContentType, Convert.ToBase64String(slip.Image));
    }

    internal static CashTransferView ToView(CashTransferListRow x)
    {
        var r = x.Row;
        return new CashTransferView(r.TransferId, CashOutRules.Code(r.Kind), r.Status, r.BusinessDate, r.Amount.Value, r.FromTillId, x.FromTillCode, r.ToTillId, x.ToTillCode,
            r.Bank, r.SlipNo, r.Reason, r.RecordedBy, r.RecordedUtc, r.DecidedBy, r.DecidedUtc, r.DecidedDate, r.DecisionReason, r.SlipStored,
            r.Kind == CashOutKind.Banking && r.SlipPurgedUtc is not null ? CashOutRules.SlipPurgedNote : null, r.ApprovalId, x.ApprovalVersion ?? r.Version);
    }
}
