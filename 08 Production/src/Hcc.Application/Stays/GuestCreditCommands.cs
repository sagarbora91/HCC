using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Stays;

namespace Hcc.Application.Stays;

/// <summary>POST /api/v1/stays/{id}/guest-credit (LF-CONTRACT §7).</summary>
/// <remarks>F8-FIX (SF-CONTRACT §3 receipt table, §2.2 udhaar, deviation 8): <c>FolioId</c> names the folio of a split stay (from the folio summary) —
/// folio A or an Udhaar folio of the same reservation; absent = the stay's primary folio A, as before.</remarks>
public sealed record GuestCreditRequestBody(long StayId, string Reason, long? FolioId = null);

public sealed record GuestCreditRequestResult(long ApprovalId, string ApprovalVersion, long AmountPaise, string FolioVersion);

/// <summary>The guest of a folio's stay (name, phone) and the property-side facts of their credit.</summary>
public sealed record FolioGuest(long GuestId, string Name, string? Phone);

/// <summary>Persistence of LF B2 (SQL adapter Infrastructure/LfMoney/SqlGuestCreditStore). Inside the command transaction.</summary>
public interface IGuestCreditStore
{
    Task<FolioGuest?> GuestOfFolioAsync(ICommandTransaction tx, long folioId, CancellationToken ct);

    /// <summary>Open (not void, unsettled part) Udhaar receivables of the guest: linked by guest id, or by the account's 10-digit key as mobile.</summary>
    Task<Paise> OpenReceivablesAsync(ICommandTransaction tx, int propertyId, long guestId, string customerKey, CancellationToken ct);

    Task<long> InsertReceivableAsync(ICommandTransaction tx, int propertyId, string name, string? mobile, Paise amount, DateOnly date, long guestId, long folioId,
        long receiptId, string note, int by, DateTime utc, CancellationToken ct);
}

/// <summary>
/// LF B2 — individual credit guests. <c>RequestGuestCredit</c> (PERM-STY-003 checkout, as company credit): in house, balance &gt; 0, the guest has an
/// active credit account and balance + open receivables ≤ limit → approval 'guest-credit' for the balance, bound to the folio version. The decision
/// (DecideApproval → this handler) needs PERM-APR-001 and PERM-PAY-005 for the amount (Manager within its limit, the Owner beyond; never the requester).
/// At CheckoutStay naming the approval, BillingService executes it: receipt kind 'guest-credit' (no cash) + an Udhaar receivable (kind room, linked to
/// the guest / folio / receipt, 4691); accounting: Journal Dr 1120/UD-&lt;customer key&gt; · Cr 1100 (SourceRules.FolioReceipt).
/// </summary>
public sealed class GuestCreditCommands(ICommandRunner runner, IAccessPolicy policy, IBillingStore billing, IGuestCreditStore store, ILfGuestMoneyStore accounts,
    TimeProvider clock, ISplitFolioStore? split = null) : IGuestCreditReceivables, ILfApprovalHandler // F8-FIX: split = guest credit on an Udhaar folio
{
    public const string OpRequest = "RequestGuestCredit";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public AdjustmentAction Action => AdjustmentAction.GuestCredit;

    public async Task<CommandResult<GuestCreditRequestResult>> RequestAsync(CommandContext ctx, GuestCreditRequestBody req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length is 0 or > 200) throw HccException.Validation("A reason is required (up to 200 characters).");
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, BillingPermissions.Checkout, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpRequest }, req, async (tx, c) =>
        {
            var folioId = await billing.StayFolioAsync(tx, ctx.PropertyId, req.StayId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Stay not found.");
            var f = await billing.LockFolioAsync(tx, await TargetFolioAsync(tx, ctx.PropertyId, folioId.ReservationId, folioId.FolioId, req.FolioId, c).ConfigureAwait(false), c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
            if (f.ReservationStatus != "InHouse") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This stay is not in house.");
            var figures = FolioStatement.Compute(await billing.LinesAsync(tx, f.FolioId, c).ConfigureAwait(false), await billing.ReceiptsAsync(tx, f.FolioId, c).ConfigureAwait(false));
            if (figures.Balance <= Paise.Zero) throw new HccException(HccErrorCode.INVALID_TRANSITION, "There is no outstanding balance to place on guest credit.");
            var now = Now;
            var approvals = await billing.ApprovalsForFolioAsync(tx, f.FolioId, c).ConfigureAwait(false);
            if (approvals.Any(a => a.Action is AdjustmentAction.GuestCredit or AdjustmentAction.Credit && a.Status is ApprovalStatus.Pending or ApprovalStatus.Approved
                    && !ApprovalRules.IsExpired(a.Status, a.Action, a.ExpiresUtc, now)))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "A company- or guest-credit request is already open on this folio.");
            await EnsureWithinLimitAsync(tx, new Caller(ctx.PropertyId, ctx.PrincipalId), f, figures.Balance, c).ConfigureAwait(false);
            var (id, version) = await billing.InsertApprovalAsync(tx, new NewApproval(ctx.PropertyId, AdjustmentAction.GuestCredit, f.FolioId, null, "folio", f.FolioId,
                figures.Balance, f.Version, null, null, reason, ctx.PrincipalId, now, now + ApprovalRules.TimeToLive), c).ConfigureAwait(false);
            await tx.AuditAsync("GUEST_CREDIT_REQUESTED", "approval-" + N(id), $"folio {N(f.FolioId)} · {figures.Balance.Value} paise · {reason}", c).ConfigureAwait(false);
            await tx.PublishAsync("ApprovalRequested", id, new { approvalId = id, action = "guest-credit", folioId = f.FolioId, amountPaise = figures.Balance.Value }, c).ConfigureAwait(false);
            return new GuestCreditRequestResult(id, version, figures.Balance.Value, f.Version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<ApprovalResult> DecideAsync(ICommandTransaction tx, Caller caller, ApprovalRow approval, bool approve, string reason, string expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(approval);
        var f = await billing.LockFolioAsync(tx, approval.FolioId!.Value, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
        var a = (await billing.LockApprovalAsync(tx, approval.ApprovalId, ct).ConfigureAwait(false))!;
        BillingService.EnsureVersion(a.Version, expectedVersion, "The approval request");
        var now = Now;
        var expired = ApprovalRules.IsExpired(a.Status, a.Action, a.ExpiresUtc, now);
        if (a.Status != ApprovalStatus.Pending) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request is no longer pending.");
        if (a.RequestedBy == caller.PrincipalId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot decide your own request (PERM-SOD-001).");
        ApprovalChange change;
        if (!approve || expired)
        {
            if (approve) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request expired (24 hours). Dismiss it and request again.");
            await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ApprovalDecide, ct: ct).ConfigureAwait(false);
            change = new ApprovalChange(expired ? ApprovalStatus.Expired : ApprovalStatus.Rejected, caller.PrincipalId, now, reason, null, null, null);
        }
        else
        {
            await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.ApprovalDecide, amountPaise: a.Amount.Value, ct: ct).ConfigureAwait(false);
            await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.CompanyCreditAuthorise, amountPaise: a.Amount.Value, ct: ct).ConfigureAwait(false);
            if (f.ReservationStatus != "InHouse") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This stay is not in house.");
            var figures = FolioStatement.Compute(await billing.LinesAsync(tx, f.FolioId, ct).ConfigureAwait(false), await billing.ReceiptsAsync(tx, f.FolioId, ct).ConfigureAwait(false));
            if (a.TargetVersion != f.Version || figures.Balance != a.Amount)
                throw new HccException(HccErrorCode.STALE_VERSION, "The folio changed after this request. Reject it and request again.");
            await EnsureWithinLimitAsync(tx, caller, f, a.Amount, ct).ConfigureAwait(false);
            change = new ApprovalChange(ApprovalStatus.Approved, caller.PrincipalId, now, reason, null, null, null);
        }

        var v = await billing.UpdateApprovalAsync(tx, a.ApprovalId, a.Version, change, ct).ConfigureAwait(false);
        await tx.AuditAsync("APPROVAL_DECIDED", "approval-" + N(a.ApprovalId), $"guest-credit · {a.Amount.Value} · {change.Status}" + (reason.Length > 0 ? " · " + reason : ""), ct).ConfigureAwait(false);
        await tx.PublishAsync("ApprovalDecided", a.ApprovalId, new { approvalId = a.ApprovalId, action = "guest-credit", status = change.Status.ToString(), folioId = f.FolioId }, ct).ConfigureAwait(false);
        return new ApprovalResult(a.ApprovalId, "guest-credit", change.Status.ToString(), a.Amount.Value, v, null, f.Version, null);
    }

    public async Task EnsureWithinLimitAsync(ICommandTransaction tx, Caller caller, FolioHeader folio, Paise amount, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(folio);
        var g = await store.GuestOfFolioAsync(tx, folio.FolioId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "The stay has no registered guest.");
        var acc = await accounts.CreditAccountAsync(tx, caller.PropertyId, g.GuestId, ct).ConfigureAwait(false);
        var used = acc is { } x ? await store.OpenReceivablesAsync(tx, caller.PropertyId, g.GuestId, x.Account.CustomerKey, ct).ConfigureAwait(false) : Paise.Zero;
        var refusal = GuestCreditRules.Refusal(g.Name, acc?.Account, used, amount);
        if (refusal is not null) throw new HccException(HccErrorCode.INVALID_TRANSITION, refusal);
    }

    public async Task<long> RaiseAsync(ICommandTransaction tx, Caller caller, FolioHeader folio, Paise amount, long receiptId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(folio);
        var g = await store.GuestOfFolioAsync(tx, folio.FolioId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "The stay has no registered guest.");
        var acc = await accounts.CreditAccountAsync(tx, caller.PropertyId, g.GuestId, ct).ConfigureAwait(false);
        var key = acc?.Account.CustomerKey ?? "";
        var mobile = key.Length == 10 && key.All(char.IsAsciiDigit) ? key : null;
        var name = g.Name.Length <= 80 ? g.Name : g.Name[..80];
        var id = await store.InsertReceivableAsync(tx, caller.PropertyId, name, mobile, amount, folio.BusinessDate, g.GuestId, folio.FolioId, receiptId,
            $"Guest credit · folio {N(folio.FolioId)} · reservation {N(folio.ReservationId)}", caller.PrincipalId, Now, ct).ConfigureAwait(false);
        await tx.AuditAsync("GUEST_CREDIT_RAISED", "folio-" + N(folio.FolioId), $"receivable UD-{N(id)} · {amount.Value} paise · party 1120/UD-{key}", ct).ConfigureAwait(false);
        return id;
    }

    /// <summary>
    /// F8-FIX: the folio the guest credit is requested on. Without <paramref name="requested"/> (or naming the primary) the stay's folio A, as before.
    /// Otherwise it must be a live folio of the same reservation that may take a guest-credit receipt (SF-CONTRACT §3: folio A or an Udhaar folio;
    /// <see cref="SplitFolioRules.EnsureReceiptFolio"/>); the limit is then the Udhaar payer's credit account (GuestOfFolioAsync, review H9).
    /// </summary>
    private async Task<long> TargetFolioAsync(ICommandTransaction tx, int propertyId, long reservationId, long primaryFolioId, long? requested, CancellationToken ct)
    {
        if (requested is not { } want || want == primaryFolioId) return primaryFolioId;
        var stay = split is null ? null : await split.LockStayAsync(tx, propertyId, reservationId, ct).ConfigureAwait(false);
        var me = stay?.Snapshot.Folios.FirstOrDefault(x => x.FolioId == want && !x.Removed)
            ?? throw SplitFolioRules.Refuse(HccErrorCode.NOT_FOUND, SplitFolioRules.FolioUnknown, "This stay has no such open folio.");
        SplitFolioRules.EnsureReceiptFolio(stay!.Snapshot, "guest-credit", null, me.Seq);
        return me.FolioId;
    }

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
}
