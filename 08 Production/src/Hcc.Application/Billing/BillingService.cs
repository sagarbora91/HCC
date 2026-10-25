using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Payments;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Application.Billing;

/// <summary>
/// Folio commands: PostRoomNight, PostCharge, RecordReceipt, RequestAdjustment, DecideApproval, ExecuteAdjustment (contracts §3).
/// Rules are ported from the prototype stream C handlers (<c>03 Prototype/src/services/mock/handlers/folio.ts</c>) with the
/// production changes the contracts require: DEC-031 refund execution by someone other than the requester, DEC-041 binding to
/// the folio version, refund remainder under a row lock on the parent receipt (spike C3), cash only into an open shift.
/// GST (DEC-069): every new line takes its rate from <see cref="IGstRateResolver"/> (room nights per unit per night on the charged
/// price; services by category; discounts follow the folio's room-night rate; reversals copy the original line). The rate is
/// stored on the charge (tax_bps / tax_paise snapshot) and, when <paramref name="gstSnapshots"/> is wired, the category, ITC flag
/// and CGST / SGST split go to <c>tax.charge_gst</c> in the same transaction.
/// </summary>
public sealed class BillingService(ICommandRunner runner, IBillingStore store, IAccessPolicy policy, IStayNightSource stays,
    IGstRateResolver gst, TimeProvider clock, IGstChargeSnapshots? gstSnapshots = null, Closing.IBusinessDayGuard? days = null)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static CommandContext Ctx(Caller c, string operation, string key) => new(c.PropertyId, c.PrincipalId, operation, key);

    // ── PostRoomNight ──

    public async Task<CommandResult<ChargeResult>> PostRoomNightAsync(Caller caller, string idempotencyKey, PostRoomNightRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.NightPost, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(Ctx(caller, "PostRoomNight", idempotencyKey), req, async (tx, ct2) =>
        {
            var night = await stays.ResolveAsync(tx, caller.PropertyId, req.StayId, req.ServiceDate, ct2).ConfigureAwait(false);
            var f = await RequireFolioAsync(tx, caller, night.FolioId, ct2).ConfigureAwait(false);
            if (f.ReservationStatus != "InHouse") throw Invalid("Check in before posting room nights.");
            if (req.ServiceDate < f.Arrival || req.ServiceDate >= f.Departure) throw Invalid("The service date is outside the stay.");
            if (req.ServiceDate > f.BusinessDate) throw HccException.Validation("A room night cannot be posted ahead of the business date.");
            if (req.ServiceDate < f.BusinessDate) throw Hcc.Domain.Closing.CloseRules.DayClosed(req.ServiceDate); // P6 late-write barrier: earlier dates are closed (DEC-042)

            var existing = await store.FindRoomNightAsync(tx, f.FolioId, req.ServiceDate, ct2).ConfigureAwait(false);
            if (existing is not null)
            {
                await tx.AuditAsync("ROOM_NIGHT_ALREADY_POSTED", Target("folio", f.FolioId), $"{existing.Label} · charge {existing.ChargeId} returned", ct2).ConfigureAwait(false);
                return ToResult(f.FolioId, existing, created: false, f.Version);
            }

            var rate = night.Rate.EnsureFieldRange();
            if (rate < Paise.Zero) throw HccException.Validation("The nightly rate cannot be negative.");
            var g = await gst.RoomNightAsync(tx, caller.PropertyId, req.ServiceDate, rate, ct2).ConfigureAwait(false); // per unit per night, actual price
            var line = new NewCharge(f.FolioId, ChargeKind.Room, req.ServiceDate, FolioStatement.RoomNightLabel(req.ServiceDate), rate, g.Bps,
                GstMath.LineTax(rate, g.Bps), null, null, caller.PrincipalId, Now);
            var id = await store.InsertChargeAsync(tx, line, ct2).ConfigureAwait(false);
            await RecordGstAsync(tx, id, g, line.Tax, ct2).ConfigureAwait(false);
            var version = await store.TouchFolioAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ROOM_NIGHT_POSTED", Target("folio", f.FolioId), $"{line.Label} · {rate} + tax {line.Tax}", ct2).ConfigureAwait(false);
            await tx.PublishAsync("ChargePosted", f.FolioId, new { folioId = f.FolioId, chargeId = id, kind = "room", serviceDate = req.ServiceDate, amountPaise = rate.Value, taxPaise = line.Tax.Value }, ct2).ConfigureAwait(false);
            return new ChargeResult(id, f.FolioId, "room", req.ServiceDate, line.Label, rate.Value, line.TaxBps, line.Tax.Value, true, version);
        }, ct).ConfigureAwait(false);
    }

    // ── PostCharge (service) ──

    public async Task<CommandResult<ChargeResult>> PostChargeAsync(Caller caller, string idempotencyKey, PostChargeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ChargePost, ct: ct).ConfigureAwait(false);
        var label = BillingText.Required(req.Label, 200, "Describe the service.");
        var amount = Paise.FromWire(req.AmountPaise).EnsureReceiptAmount();
        var category = GstCategories.ForCharge(req.GstCategory, label);
        return await runner.RunAsync(Ctx(caller, "PostCharge", idempotencyKey), req, async (tx, ct2) =>
        {
            var f = await RequireFolioAsync(tx, caller, req.FolioId, ct2).ConfigureAwait(false);
            if (!f.IsOpen) throw Invalid("This folio is no longer open.");
            var g = await gst.ChargeAsync(tx, caller.PropertyId, f.BusinessDate, category, ct2).ConfigureAwait(false);
            var bps = g.Bps;
            var line = new NewCharge(f.FolioId, ChargeKind.Service, f.BusinessDate, label, amount, bps, GstMath.LineTax(amount, bps), null, null, caller.PrincipalId, Now);
            var id = await store.InsertChargeAsync(tx, line, ct2).ConfigureAwait(false);
            await RecordGstAsync(tx, id, g, line.Tax, ct2).ConfigureAwait(false);
            var version = await store.TouchFolioAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
            await tx.AuditAsync("CHARGE_POSTED", Target("folio", f.FolioId), $"{label} · {amount} + tax {line.Tax}", ct2).ConfigureAwait(false);
            await tx.PublishAsync("ChargePosted", f.FolioId, new { folioId = f.FolioId, chargeId = id, kind = "service", serviceDate = f.BusinessDate, amountPaise = amount.Value, taxPaise = line.Tax.Value }, ct2).ConfigureAwait(false);
            return new ChargeResult(id, f.FolioId, "service", f.BusinessDate, label, amount.Value, bps, line.Tax.Value, true, version);
        }, ct).ConfigureAwait(false);
    }

    // ── RecordReceipt ──

    public async Task<CommandResult<ReceiptResult>> RecordReceiptAsync(Caller caller, string idempotencyKey, RecordReceiptRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        var kind = req.Kind switch
        {
            "payment" => ReceiptKind.Payment,
            "deposit" => ReceiptKind.Deposit,
            "deposit-return" => ReceiptKind.DepositReturn,
            "refund" => throw Invalid("Refunds are not recorded directly. Request a refund on the original receipt; after approval, execute it."),
            _ => throw HccException.Validation("Receipt type must be payment, deposit or deposit-return."),
        };
        var permission = kind == ReceiptKind.Payment ? BillingPermissions.PaymentRecord : BillingPermissions.DepositRecord;
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, permission, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        var amount = Paise.FromWire(req.AmountPaise).EnsureReceiptAmount();
        var reference = BillingText.Optional(req.Reference, 120);
        var tender = Tenders.Validate(req.Tender, reference);
        if (tender == Tenders.Cash && req.TillId is null) throw HccException.Validation("Choose the till that receives the cash.");

        return await runner.RunAsync(Ctx(caller, "RecordReceipt", idempotencyKey), req, async (tx, ct2) =>
        {
            var f = await RequireFolioAsync(tx, caller, req.FolioId, ct2).ConfigureAwait(false);
            if (kind is ReceiptKind.Payment or ReceiptKind.Deposit && !f.IsOpen) throw Invalid("This booking is no longer open.");
            if (kind == ReceiptKind.DepositReturn)
            {
                var figures = await FiguresAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
                if (amount > figures.Deposit) throw HccException.Validation($"Return cannot exceed the deposit held ({figures.Deposit}).");
            }

            int? tillId = null;
            long? shiftId = null;
            if (tender == Tenders.Cash)
            {
                var shift = await CashDesk.RequireOpenShiftAsync(store, tx, caller.PropertyId, req.TillId!.Value, f.BusinessDate, ct2).ConfigureAwait(false);
                await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, permission, new AccessScope(BillingPermissions.ShiftScope, shift.ShiftId), ct: ct2).ConfigureAwait(false);
                (tillId, shiftId) = (shift.TillId, shift.ShiftId);
            }
            else if (req.TillId is { } t)
            {
                tillId = (await CashDesk.RequireTillAsync(store, tx, caller.PropertyId, t, ct2).ConfigureAwait(false)).TillId;
            }

            var receipt = new NewReceipt(f.FolioId, kind, tender, amount, null, f.BusinessDate, tillId, shiftId,
                tender == Tenders.Cash && reference.Length == 0 ? "Cash" : reference, null, caller.PrincipalId, Now);
            var id = await store.InsertReceiptAsync(tx, receipt, ct2).ConfigureAwait(false);
            if (shiftId is { } sid)
                await CashDesk.RecordReceiptMovementAsync(store, tx, sid, tillId!.Value, f.BusinessDate, kind, id, amount, f.FolioId, receipt.Reference, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            var version = await store.TouchFolioAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
            await tx.AuditAsync("RECEIPT_RECORDED", Target("folio", f.FolioId), $"{FolioTotals.KindCode(kind)} · {tender} · {amount} · receipt {id}" + (tender == Tenders.Cash ? "" : " · manual entry, not verified"), ct2).ConfigureAwait(false);
            await tx.PublishAsync("ReceiptRecorded", f.FolioId, new { folioId = f.FolioId, receiptId = id, kind = FolioTotals.KindCode(kind), tender, amountPaise = amount.Value, businessDate = f.BusinessDate }, ct2).ConfigureAwait(false);
            return new ReceiptResult(id, f.FolioId, FolioTotals.KindCode(kind), tender, amount.Value, f.BusinessDate, shiftId, null, version);
        }, ct).ConfigureAwait(false);
    }

    // ── RequestAdjustment ──

    public async Task<CommandResult<AdjustmentResult>> RequestAdjustmentAsync(Caller caller, string idempotencyKey, RequestAdjustmentRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        var action = BillingCodes.ParseAction(req.Action);
        if (action == AdjustmentAction.CashDifference) throw HccException.Validation("Cash differences are raised by submitting a shift count.");
        var reason = BillingText.Required(req.Reason, 200, "A reason is required.");
        var amount = Paise.FromWire(req.AmountPaise).EnsureReceiptAmount();
        VersionToken.Validate(req.ExpectedVersion);
        var basePermission = action switch
        {
            AdjustmentAction.Discount => BillingPermissions.Discount,
            AdjustmentAction.Refund => BillingPermissions.RefundRequest,
            AdjustmentAction.Credit => BillingPermissions.Checkout,
            _ => BillingPermissions.Correction,
        };
        await RequireRequesterAsync(caller, basePermission, ct).ConfigureAwait(false);

        return await runner.RunAsync(Ctx(caller, "RequestAdjustment", idempotencyKey), req, async (tx, ct2) =>
        {
            var f = await RequireFolioAsync(tx, caller, req.FolioId, ct2).ConfigureAwait(false);
            EnsureVersion(f.Version, req.ExpectedVersion, "The folio");
            var lines = await store.LinesAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
            var receipts = await store.ReceiptsAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
            var approvals = await store.ApprovalsForFolioAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
            var figures = FolioStatement.Compute(lines, receipts);
            var now = Now;
            bool Pending(AdjustmentAction a, long? target = null) => approvals.Any(x => x.Action == a && (target is null || x.TargetId == target)
                && x.Status == ApprovalStatus.Pending && !ApprovalRules.IsExpired(x.Status, x.Action, x.ExpiresUtc, now));

            string targetKind;
            long targetId;
            switch (action)
            {
                case AdjustmentAction.Discount:
                {
                    if (!f.IsOpen) throw Invalid("This booking is no longer open.");
                    if (amount > figures.Net) throw HccException.Validation("Discount cannot exceed existing net charges.");
                    var withinLimit = await WithinLimitAsync(caller, BillingPermissions.Discount, amount, ct2).ConfigureAwait(false);
                    if (withinLimit)
                    {
                        var (chargeId, version) = await PostDiscountAsync(tx, caller, f, amount, "Discount · " + reason, null, ct2).ConfigureAwait(false);
                        await tx.AuditAsync("DISCOUNT_POSTED", Target("folio", f.FolioId), $"{amount} · {reason} · within limit", ct2).ConfigureAwait(false);
                        return new AdjustmentResult("Posted", null, null, chargeId, version);
                    }

                    if (Pending(AdjustmentAction.Discount)) throw Invalid("A discount request is already pending.");
                    (targetKind, targetId) = ("folio", f.FolioId);
                    break;
                }

                case AdjustmentAction.Refund:
                {
                    if (req.TargetId is not { } rid) throw HccException.Validation("Select the original receipt to refund.");
                    var parent = await store.LockReceiptAsync(tx, rid, ct2).ConfigureAwait(false);
                    if (parent is null || parent.FolioId != f.FolioId) throw NotFound("Select the original receipt to refund.");
                    if (parent.Receipt.Kind != ReceiptKind.Payment)
                        throw Invalid("Only a payment / advance receipt can be refunded. Deposits are returned with a deposit return.");
                    var remainder = await RefundRemainderAsync(tx, parent.Receipt, approvals, exceptApprovalId: null, now, ct2).ConfigureAwait(false);
                    if (amount > remainder) throw HccException.Validation($"Refund cannot exceed the refundable remainder of receipt {rid} ({remainder}).");
                    if (f.ReservationStatus == "Departed" && amount > RefundRules.DepartedLimit(figures.Balance, OutstandingRefunds(approvals, null, now)))
                        throw Invalid("A settled departed stay requires a credit note before a refund.");
                    (targetKind, targetId) = ("receipt", rid);
                    break;
                }

                case AdjustmentAction.Credit:
                {
                    if (f.ReservationStatus != "InHouse") throw Invalid("This stay is not in house.");
                    if (figures.Balance <= Paise.Zero) throw Invalid("There is no outstanding balance to place on company credit.");
                    if (amount != figures.Balance) throw HccException.Validation($"Company credit must equal the current balance ({figures.Balance}).");
                    if (Pending(AdjustmentAction.Credit)) throw Invalid("A company-credit request is already pending.");
                    (targetKind, targetId) = ("folio", f.FolioId);
                    break;
                }

                default:
                {
                    if (!f.IsOpen) throw Invalid("This booking is no longer open.");
                    if (req.TargetId is not { } cid) throw HccException.Validation("Select the posted line to correct.");
                    var line = lines.FirstOrDefault(l => l.ChargeId == cid) ?? throw NotFound("Select the posted line to correct.");
                    if (days is not null) await days.EnsureCashBookOpenAsync(tx, caller.PropertyId, line.ServiceDate, "a correction", ct2).ConfigureAwait(false); // P6 DEC-065
                    if (line.ReversesChargeId is not null) throw Invalid("A reversal line cannot itself be reversed.");
                    if (lines.Any(l => l.ReversesChargeId == cid) || Pending(AdjustmentAction.Correction, cid))
                        throw Invalid("This line is already reversed or has a correction pending.");
                    var abs = line.Amount < Paise.Zero ? -line.Amount : line.Amount;
                    if (amount != abs) throw HccException.Validation($"A correction reverses the whole line ({abs}).");
                    (targetKind, targetId) = ("charge", cid);
                    break;
                }
            }

            var (approvalId, approvalVersion) = await store.InsertApprovalAsync(tx, new NewApproval(caller.PropertyId, action, f.FolioId, null, targetKind, targetId,
                amount, f.Version, null, null, reason, caller.PrincipalId, now, now + ApprovalRules.TimeToLive), ct2).ConfigureAwait(false);
            await tx.AuditAsync("ADJUSTMENT_REQUESTED", Target("approval", approvalId), $"{BillingCodes.Code(action)} · {amount} · {targetKind} {targetId} · {reason}", ct2).ConfigureAwait(false);
            await tx.PublishAsync("ApprovalRequested", approvalId, new { approvalId, action = BillingCodes.Code(action), folioId = f.FolioId, amountPaise = amount.Value }, ct2).ConfigureAwait(false);
            return new AdjustmentResult("PendingApproval", approvalId, approvalVersion, null, f.Version);
        }, ct).ConfigureAwait(false);
    }

    // ── DecideApproval ──

    public async Task<CommandResult<ApprovalResult>> DecideApprovalAsync(Caller caller, string idempotencyKey, DecideApprovalRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        VersionToken.Validate(req.ExpectedVersion);
        var decisionReason = BillingText.Optional(req.Reason, 200);
        return await runner.RunAsync(Ctx(caller, "DecideApproval", idempotencyKey), req, async (tx, ct2) =>
        {
            var peek = await store.PeekApprovalAsync(tx, req.ApprovalId, ct2).ConfigureAwait(false);
            if (peek is null || peek.PropertyId != caller.PropertyId) throw NotFound("Approval request not found.");
            if (peek.Action == AdjustmentAction.CashDifference)
                return await CashDesk.DecideCashDifferenceAsync(store, policy, tx, caller, peek, req.Approve, decisionReason, req.ExpectedVersion, Now, ct2).ConfigureAwait(false);

            var f = await RequireFolioAsync(tx, caller, peek.FolioId!.Value, ct2).ConfigureAwait(false);
            var a = (await store.LockApprovalAsync(tx, req.ApprovalId, ct2).ConfigureAwait(false))!;
            EnsureVersion(a.Version, req.ExpectedVersion, "The approval request");
            var now = Now;
            var expired = ApprovalRules.IsExpired(a.Status, a.Action, a.ExpiresUtc, now);
            if (a.Status != ApprovalStatus.Pending) throw Invalid("This request is no longer pending.");
            if (a.RequestedBy == caller.PrincipalId) throw Forbidden("You cannot decide your own request (PERM-SOD-001).");

            if (!req.Approve || expired)
            {
                if (req.Approve) throw Invalid("This request expired (24 hours). Dismiss it and request again.");
                await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ApprovalDecide, ct: ct2).ConfigureAwait(false);
                var status = expired ? ApprovalStatus.Expired : ApprovalStatus.Rejected;
                var v = await store.UpdateApprovalAsync(tx, a.ApprovalId, a.Version, new ApprovalChange(status, caller.PrincipalId, now, decisionReason, null, null, null), ct2).ConfigureAwait(false);
                await tx.AuditAsync("APPROVAL_DECIDED", Target("approval", a.ApprovalId), $"{BillingCodes.Code(a.Action)} · {a.Amount} · {status}" + (decisionReason.Length > 0 ? " · " + decisionReason : ""), ct2).ConfigureAwait(false);
                return new ApprovalResult(a.ApprovalId, BillingCodes.Code(a.Action), status.ToString(), a.Amount.Value, v, null, f.Version, null);
            }

            await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ApprovalDecide, amountPaise: a.Amount.Value, ct: ct2).ConfigureAwait(false);
            if (a.Action == AdjustmentAction.Credit)
                await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.CompanyCreditAuthorise, amountPaise: a.Amount.Value, ct: ct2).ConfigureAwait(false);
            if (a.TargetVersion != f.Version) throw Stale("The folio changed after this request. Reject it and request again.");

            var lines = await store.LinesAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
            var receipts = await store.ReceiptsAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
            var figures = FolioStatement.Compute(lines, receipts);
            ApprovalChange change;
            var folioVersion = f.Version;
            switch (a.Action)
            {
                case AdjustmentAction.Discount:
                {
                    if (!f.IsOpen) throw Invalid("This booking is no longer open.");
                    if (a.Amount > figures.Net) throw HccException.Validation("Discount exceeds the current charges.");
                    var (chargeId, version) = await PostDiscountAsync(tx, caller, f, a.Amount, "Approved discount · " + a.Reason, a.ApprovalId, ct2).ConfigureAwait(false);
                    folioVersion = version;
                    change = new ApprovalChange(ApprovalStatus.Executed, caller.PrincipalId, now, decisionReason, caller.PrincipalId, now, chargeId);
                    break;
                }

                case AdjustmentAction.Refund:
                {
                    var parent = await store.LockReceiptAsync(tx, a.TargetId, ct2).ConfigureAwait(false) ?? throw NotFound("The original receipt no longer exists.");
                    var approvals = await store.ApprovalsForFolioAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
                    var remainder = await RefundRemainderAsync(tx, parent.Receipt, approvals, a.ApprovalId, now, ct2).ConfigureAwait(false);
                    if (a.Amount > remainder) throw HccException.Validation("Refund cannot exceed the refundable remainder of this receipt.");
                    change = new ApprovalChange(ApprovalStatus.Approved, caller.PrincipalId, now, decisionReason, null, null, null);
                    break;
                }

                case AdjustmentAction.Credit:
                {
                    if (f.ReservationStatus != "InHouse") throw Invalid("This stay is not in house.");
                    if (figures.Balance != a.Amount) throw Stale("The folio changed after this request. Reject it and request again.");
                    change = new ApprovalChange(ApprovalStatus.Approved, caller.PrincipalId, now, decisionReason, null, null, null);
                    break;
                }

                default:
                {
                    if (!f.IsOpen) throw Invalid("This booking is no longer open.");
                    var line = lines.FirstOrDefault(l => l.ChargeId == a.TargetId) ?? throw NotFound("The original line no longer exists.");
                    if (days is not null) await days.EnsureCashBookOpenAsync(tx, caller.PropertyId, line.ServiceDate, "posting this correction", ct2).ConfigureAwait(false); // P6 DEC-065
                    if (lines.Any(l => l.ReversesChargeId == line.ChargeId)) throw Invalid("This line is already reversed.");
                    var reversal = new NewCharge(f.FolioId, ChargeKind.Correction, f.BusinessDate, Clip($"Reversal · {line.Label} · {a.Reason}", 200), -line.Amount,
                        line.TaxBps, -line.Tax, line.ChargeId, a.ApprovalId, caller.PrincipalId, now);
                    var chargeId = await store.InsertChargeAsync(tx, reversal, ct2).ConfigureAwait(false);
                    await RecordReversalGstAsync(tx, line, chargeId, reversal.Tax, ct2).ConfigureAwait(false);
                    folioVersion = await store.TouchFolioAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
                    change = new ApprovalChange(ApprovalStatus.Executed, caller.PrincipalId, now, decisionReason, caller.PrincipalId, now, chargeId);
                    break;
                }
            }

            var newVersion = await store.UpdateApprovalAsync(tx, a.ApprovalId, a.Version, change, ct2).ConfigureAwait(false);
            await tx.AuditAsync("APPROVAL_DECIDED", Target("approval", a.ApprovalId), $"{BillingCodes.Code(a.Action)} · {a.Amount} · approved" + (change.ResultId is { } r ? $" · posted {r}" : ""), ct2).ConfigureAwait(false);
            await tx.PublishAsync("ApprovalDecided", a.ApprovalId, new { approvalId = a.ApprovalId, action = BillingCodes.Code(a.Action), status = change.Status.ToString(), folioId = f.FolioId }, ct2).ConfigureAwait(false);
            return new ApprovalResult(a.ApprovalId, BillingCodes.Code(a.Action), change.Status.ToString(), a.Amount.Value, newVersion, change.ResultId, folioVersion, null);
        }, ct).ConfigureAwait(false);
    }

    // ── ExecuteAdjustment ──

    public async Task<CommandResult<ApprovalResult>> ExecuteAdjustmentAsync(Caller caller, string idempotencyKey, ExecuteAdjustmentRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        VersionToken.Validate(req.ExpectedVersion);
        return await runner.RunAsync(Ctx(caller, "ExecuteAdjustment", idempotencyKey), req, (tx, ct2) => ExecuteCoreAsync(tx, caller, req, checkVersion: true, ct2), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Checkout settlement inside the caller's (P3 CheckoutStay) command transaction: executes an approved company credit when
    /// one is named (DEC-041 binding: amount = balance, folio version unchanged since approval), then requires every agreed night
    /// [arrival, departure) posted, balance 0, deposit returned and no pending approval on the folio. Locks the folio row.
    /// </summary>
    public async Task SettleForCheckoutAsync(ICommandTransaction tx, Caller caller, long folioId, DateOnly arrival, DateOnly departure, long? companyCreditApprovalId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(caller);
        var f = await RequireFolioAsync(tx, caller, folioId, ct).ConfigureAwait(false);
        if (companyCreditApprovalId is { } approvalId)
        {
            var peek = await store.PeekApprovalAsync(tx, approvalId, ct).ConfigureAwait(false);
            if (peek is null || peek.PropertyId != caller.PropertyId || peek.FolioId != f.FolioId) throw NotFound("Company credit approval not found for this folio.");
            if (peek.Action != AdjustmentAction.Credit) throw Invalid("The approval named at checkout is not a company credit.");
            await ExecuteCoreAsync(tx, caller, new ExecuteAdjustmentRequest(approvalId, peek.Version, null, null, null), checkVersion: false, ct).ConfigureAwait(false);
        }

        var lines = await store.LinesAsync(tx, f.FolioId, ct).ConfigureAwait(false);
        var reversed = lines.Where(l => l.ReversesChargeId is not null).Select(l => l.ReversesChargeId!.Value).ToHashSet();
        var nights = lines.Where(l => l.Kind == ChargeKind.Room && l.ReversesChargeId is null && !reversed.Contains(l.ChargeId)).Select(l => l.ServiceDate).ToHashSet();
        for (var d = arrival; d < departure; d = d.AddDays(1))
            if (!nights.Contains(d)) throw Invalid("Post all agreed stay charges before checkout.");
        var figures = FolioStatement.Compute(lines, await store.ReceiptsAsync(tx, f.FolioId, ct).ConfigureAwait(false));
        if (figures.Balance != Paise.Zero) throw Invalid("Settle the folio exactly before checkout. Refund any excess receipt.");
        if (figures.Deposit != Paise.Zero) throw Invalid("Return the refundable deposit before checkout.");
        var now = Now;
        var approvals = await store.ApprovalsForFolioAsync(tx, f.FolioId, ct).ConfigureAwait(false);
        if (approvals.Any(a => a.Status == ApprovalStatus.Pending && !ApprovalRules.IsExpired(a.Status, a.Action, a.ExpiresUtc, now)))
            throw Invalid("Decide the pending approval requests on this folio before checkout.");
    }

    private async Task<ApprovalResult> ExecuteCoreAsync(ICommandTransaction tx, Caller caller, ExecuteAdjustmentRequest req, bool checkVersion, CancellationToken ct2)
    {
        var peek = await store.PeekApprovalAsync(tx, req.ApprovalId, ct2).ConfigureAwait(false);
        if (peek is null || peek.PropertyId != caller.PropertyId) throw NotFound("Approval request not found.");
        if (peek.Action is not (AdjustmentAction.Refund or AdjustmentAction.Credit))
            throw Invalid("Discounts and corrections are posted when they are approved; only refunds and company credit are executed separately.");
        var permission = peek.Action == AdjustmentAction.Refund ? BillingPermissions.RefundExecute : BillingPermissions.Checkout;
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, permission, AccessScopes.Any, ct: ct2).ConfigureAwait(false);

        var f = await RequireFolioAsync(tx, caller, peek.FolioId!.Value, ct2).ConfigureAwait(false);
        var a = (await store.LockApprovalAsync(tx, req.ApprovalId, ct2).ConfigureAwait(false))!;
        if (checkVersion) EnsureVersion(a.Version, req.ExpectedVersion, "The approval request");
        var now = Now;
        if (a.Status == ApprovalStatus.Executed) throw Invalid("This request has already been executed.");
        if (a.Status != ApprovalStatus.Approved) throw Invalid("Only an approved request can be executed.");
        if (ApprovalRules.IsExpired(a.Status, a.Action, a.ExpiresUtc, now)) throw Invalid("This approval has expired. Request it again.");

        var receipts = await store.ReceiptsAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
        var lines = await store.LinesAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
        var figures = FolioStatement.Compute(lines, receipts);
        NewReceipt receipt;
        if (a.Action == AdjustmentAction.Refund)
        {
            if (a.RequestedBy == caller.PrincipalId)
                throw Forbidden("The person who requested a refund cannot also execute it (DEC-031).");
            var parent = await store.LockReceiptAsync(tx, a.TargetId, ct2).ConfigureAwait(false) ?? throw NotFound("The original receipt no longer exists.");
            var approvals = await store.ApprovalsForFolioAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
            // Under the parent-receipt lock the physical remainder (captured − executed refunds) decides, first come first served
            // (spike C3). Requests and decisions also reserve outstanding approvals, so for any consistent state this equals the
            // prototype's check; it differs only for an over-committed state, where the prototype would refuse every execution.
            var remainder = await RefundRemainderAsync(tx, parent.Receipt, [], a.ApprovalId, now, ct2).ConfigureAwait(false);
            if (a.Amount > remainder) throw Invalid("Refund cannot exceed the refundable remainder of this receipt.");
            if (f.ReservationStatus == "Departed" && a.Amount > RefundRules.DepartedLimit(figures.Balance, OutstandingRefunds(approvals, a.ApprovalId, now)))
                throw Invalid("A settled departed stay requires a credit note before a refund.");
            var reference = BillingText.Optional(req.Reference, 120);
            if (reference.Length == 0) reference = $"Refund of receipt {parent.Receipt.ReceiptId}";
            var tender = Tenders.Validate(req.Tender ?? parent.Receipt.Tender, reference);
            int? tillId = null;
            long? shiftId = null;
            if (tender == Tenders.Cash)
            {
                if (req.TillId is not { } t) throw HccException.Validation("Choose the till that pays out the cash.");
                var shift = await CashDesk.RequireOpenShiftAsync(store, tx, caller.PropertyId, t, f.BusinessDate, ct2).ConfigureAwait(false);
                await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, permission, new AccessScope(BillingPermissions.ShiftScope, shift.ShiftId), ct: ct2).ConfigureAwait(false);
                (tillId, shiftId) = (shift.TillId, shift.ShiftId);
            }

            receipt = new NewReceipt(f.FolioId, ReceiptKind.Refund, tender, a.Amount, parent.Receipt.ReceiptId, f.BusinessDate, tillId, shiftId, reference, a.ApprovalId, caller.PrincipalId, now);
        }
        else
        {
            if (f.ReservationStatus != "InHouse") throw Invalid("This stay is not in house.");
            if (figures.Balance != a.Amount || a.TargetVersion != f.Version) throw Stale("The folio changed after this approval. Request company credit again.");
            receipt = new NewReceipt(f.FolioId, ReceiptKind.CompanyCredit, Tenders.CompanyCredit, a.Amount, null, f.BusinessDate, null, null,
                $"Company credit · approval {a.ApprovalId}", a.ApprovalId, caller.PrincipalId, now);
        }

        var receiptId = await store.InsertReceiptAsync(tx, receipt, ct2).ConfigureAwait(false);
        if (receipt.ShiftId is { } sid)
            await CashDesk.RecordReceiptMovementAsync(store, tx, sid, receipt.TillId!.Value, f.BusinessDate, receipt.Kind, receiptId, receipt.Amount, f.FolioId, receipt.Reference, caller.PrincipalId, now, ct2).ConfigureAwait(false);
        var folioVersion = await store.TouchFolioAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
        var newVersion = await store.UpdateApprovalAsync(tx, a.ApprovalId, a.Version,
            new ApprovalChange(ApprovalStatus.Executed, a.DecidedBy, null, null, caller.PrincipalId, now, receiptId), ct2).ConfigureAwait(false);
        await tx.AuditAsync("ADJUSTMENT_EXECUTED", Target("approval", a.ApprovalId), $"{BillingCodes.Code(a.Action)} · {a.Amount} · receipt {receiptId} · {receipt.Tender}", ct2).ConfigureAwait(false);
        await tx.PublishAsync("ReceiptRecorded", f.FolioId, new { folioId = f.FolioId, receiptId, kind = FolioTotals.KindCode(receipt.Kind), tender = receipt.Tender, amountPaise = a.Amount.Value, businessDate = f.BusinessDate }, ct2).ConfigureAwait(false);
        return new ApprovalResult(a.ApprovalId, BillingCodes.Code(a.Action), nameof(ApprovalStatus.Executed), a.Amount.Value, newVersion, receiptId, folioVersion, null);
    }

    // ── helpers ──

    private async Task<FolioHeader> RequireFolioAsync(ICommandTransaction tx, Caller caller, long folioId, CancellationToken ct)
    {
        var f = await store.LockFolioAsync(tx, folioId, ct).ConfigureAwait(false);
        if (f is null || f.PropertyId != caller.PropertyId) throw NotFound("Folio not found.");
        return f;
    }

    private async Task<FolioFigures> FiguresAsync(ICommandTransaction tx, long folioId, CancellationToken ct)
        => FolioStatement.Compute(await store.LinesAsync(tx, folioId, ct).ConfigureAwait(false), await store.ReceiptsAsync(tx, folioId, ct).ConfigureAwait(false));

    /// <summary>
    /// The requester must HOLD the permission; holding it with an amount limit ("L") or request-only ("R" cell, DEC-063) is exactly
    /// the case a request exists for, so APPROVAL_REQUIRED from the policy is accepted here and the command creates the approval
    /// request (a within-limit discount still posts directly, see <see cref="WithinLimitAsync"/>). FORBIDDEN (no grant, out of
    /// scope, explicit deny) still refuses.
    /// </summary>
    private async Task RequireRequesterAsync(Caller caller, string permission, CancellationToken ct)
    {
        try
        {
            await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, permission, ct: ct).ConfigureAwait(false);
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.APPROVAL_REQUIRED)
        {
            // Limited or request-only holder: continue to the approval workflow.
        }
    }

    private async Task<bool> WithinLimitAsync(Caller caller, string permission, Paise amount, CancellationToken ct)
    {
        try
        {
            await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, permission, amountPaise: amount.Value, ct: ct).ConfigureAwait(false);
            return true;
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.APPROVAL_REQUIRED)
        {
            return false;
        }
    }

    private async Task<(long ChargeId, string Version)> PostDiscountAsync(ICommandTransaction tx, Caller caller, FolioHeader f, Paise amount, string label,
        long? approvalId, CancellationToken ct)
    {
        // A discount is a negative line posted before tax (DEC-039) that reduces the room supply: it takes the GST rate of that date's
        // room-night line, else the latest earlier one, else the folio's latest, else the room rule at the booked rate (prototype
        // gstForFolioAdjustment; apportionment across mixed-rate folios: CA to confirm).
        var g = await AdjustmentRateAsync(tx, caller, f, ct).ConfigureAwait(false);
        var bps = g.Bps;
        var line = new NewCharge(f.FolioId, ChargeKind.Discount, f.BusinessDate, Clip(label, 200), -amount, bps, GstMath.LineTax(-amount, bps), null, approvalId, caller.PrincipalId, Now);
        var id = await store.InsertChargeAsync(tx, line, ct).ConfigureAwait(false);
        await RecordGstAsync(tx, id, g, line.Tax, ct).ConfigureAwait(false);
        var version = await store.TouchFolioAsync(tx, f.FolioId, ct).ConfigureAwait(false);
        await tx.PublishAsync("ChargePosted", f.FolioId, new { folioId = f.FolioId, chargeId = id, kind = "discount", serviceDate = f.BusinessDate, amountPaise = line.Amount.Value, taxPaise = line.Tax.Value }, ct).ConfigureAwait(false);
        return (id, version);
    }

    private async Task<GstRate> AdjustmentRateAsync(ICommandTransaction tx, Caller caller, FolioHeader f, CancellationToken ct)
    {
        var lines = await store.LinesAsync(tx, f.FolioId, ct).ConfigureAwait(false);
        var rooms = lines.Where(l => l.Kind == ChargeKind.Room && l.ReversesChargeId is null && !lines.Any(r => r.ReversesChargeId == l.ChargeId))
            .OrderBy(l => l.ServiceDate).ToList();
        var source = rooms.FirstOrDefault(l => l.ServiceDate == f.BusinessDate) ?? rooms.LastOrDefault(l => l.ServiceDate <= f.BusinessDate) ?? rooms.LastOrDefault();
        if (source is not null)
        {
            var snap = gstSnapshots is null ? null : await gstSnapshots.FindAsync(tx, source.ChargeId, ct).ConfigureAwait(false);
            return new GstRate(source.TaxBps, snap?.Itc ?? false, GstCategory.Room, "Follows the room-night rate");
        }

        var booked = await store.ReservationNightlyRateAsync(tx, f.ReservationId, ct).ConfigureAwait(false) ?? Paise.Zero;
        return await gst.RoomNightAsync(tx, caller.PropertyId, f.BusinessDate, booked, ct).ConfigureAwait(false);
    }

    private Task RecordGstAsync(ICommandTransaction tx, long chargeId, GstRate rate, Paise tax, CancellationToken ct)
    {
        if (gstSnapshots is null) return Task.CompletedTask;
        var split = GstMath.Split(tax);
        return gstSnapshots.RecordAsync(tx, new GstChargeSnapshot(chargeId, rate.Category, rate.Bps, rate.Itc, tax, split.Cgst, split.Sgst, rate.Rule), ct);
    }

    /// <summary>A reversal copies the original line's category / ITC and posts the negated tax (split per line).</summary>
    private async Task RecordReversalGstAsync(ICommandTransaction tx, PostedLine original, long reversalId, Paise tax, CancellationToken ct)
    {
        if (gstSnapshots is null) return;
        var o = await gstSnapshots.FindAsync(tx, original.ChargeId, ct).ConfigureAwait(false);
        var category = o?.Category ?? (original.Kind is ChargeKind.Room or ChargeKind.Discount ? GstCategory.Room : GstCategory.Other);
        var rule = "Reversal of charge " + original.ChargeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await RecordGstAsync(tx, reversalId, new GstRate(original.TaxBps, o?.Itc ?? false, category, rule), tax, ct).ConfigureAwait(false);
    }

    private async Task<Paise> RefundRemainderAsync(ICommandTransaction tx, PostedReceipt parent, IReadOnlyList<ApprovalRow> approvals, long? exceptApprovalId,
        DateTime now, CancellationToken ct)
    {
        var refunded = await store.RefundedAsync(tx, parent.ReceiptId, ct).ConfigureAwait(false);
        var outstanding = approvals.Where(x => x.Action == AdjustmentAction.Refund && x.TargetId == parent.ReceiptId && x.ApprovalId != exceptApprovalId
                && ApprovalRules.IsOutstanding(x.Status, x.Action, x.ExpiresUtc, now))
            .Aggregate(Paise.Zero, (s, x) => s + x.Amount);
        return RefundRules.Remainder(parent.Amount, refunded, outstanding);
    }

    private static Paise OutstandingRefunds(IReadOnlyList<ApprovalRow> approvals, long? exceptApprovalId, DateTime now)
        => approvals.Where(x => x.Action == AdjustmentAction.Refund && x.ApprovalId != exceptApprovalId && ApprovalRules.IsOutstanding(x.Status, x.Action, x.ExpiresUtc, now))
            .Aggregate(Paise.Zero, (s, x) => s + x.Amount);

    private static ChargeResult ToResult(long folioId, PostedLine l, bool created, string version)
        => new(l.ChargeId, folioId, BillingCodes.Code(l.Kind), l.ServiceDate, l.Label, l.Amount.Value, l.TaxBps, l.Tax.Value, created, version);

    internal static void EnsureVersion(string current, string expected, string what)
    {
        if (!string.Equals(current, expected, StringComparison.Ordinal)) throw Stale($"{what} was changed by someone else. Reload and try again.");
    }

    internal static string Target(string kind, long id) => kind + ":" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    internal static HccException Invalid(string message) => new(HccErrorCode.INVALID_TRANSITION, message);

    internal static HccException NotFound(string message) => new(HccErrorCode.NOT_FOUND, message);

    internal static HccException Forbidden(string message) => new(HccErrorCode.FORBIDDEN, message);

    internal static HccException Stale(string message) => new(HccErrorCode.STALE_VERSION, message);
}

/// <summary>Wire <c>expectedVersion</c> format check (8-byte rowversion, base64) so malformed tokens are VALIDATION, not STALE_VERSION.</summary>
public static class VersionToken
{
    public static void Validate(string? token)
    {
        var buffer = new byte[12];
        if (string.IsNullOrEmpty(token) || !Convert.TryFromBase64String(token, buffer, out var n) || n != 8)
            throw HccException.Validation("expectedVersion is not a valid version token.");
    }
}
