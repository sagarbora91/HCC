using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Application.Expenses;

// ACC-2 flows · a mistaken Udhaar desk payment reversed (02 Planning/agents/ACC2-POSTING-CONTRACT.md §8; rulebook A17b). Prototype twin:
// EXP_UD_PAY_REVERSE (services/mock/handlers/accounting-flows.ts).

/// <summary>The reversal state of one settlement row (migration 4252).</summary>
public sealed record SettlementReversalState(long SettlementId, long? ReversalOf, long? ReversedBy);

public sealed record NewUdhaarReversal(long ReceivableId, long ReversalOf, long AmountPaise, DateOnly Date, string Mode, int RecordedBy, DateTime RecordedUtc, bool Desk,
    string Reference, int? TillId, string Reason);

/// <summary>SQL adapter: SqlFlowsStore (ops.udhaar_settlement reversal_of / reversal_reason).</summary>
public interface IUdhaarReversalStore
{
    Task<SettlementReversalState?> StateAsync(ICommandTransaction tx, long settlementId, CancellationToken ct = default);

    /// <summary>Inserts the negative settlement row (amount = −the payment); returns its id.</summary>
    Task<long> InsertReversalAsync(ICommandTransaction tx, NewUdhaarReversal r, CancellationToken ct = default);
}

/// <summary>
/// ReverseUdhaarPayment (the Owner, PERM-MST-002 — DEC-058 money decisions): a NEGATIVE settlement dated the business date names the payment it
/// reverses (same day: the day nets to zero; a later day: the reversing entry). A cash desk payment's cash leaves the drawer of <c>TillId</c>
/// (open shift; pay.cash_movement kind payment, negative, source type <see cref="ReversalSource"/>). Publishes UdhaarPaymentReversed
/// (Accounting: Receipt Dr 1120/&lt;customer&gt; · Cr tender). A payment is reversed once; a reversal is never reversed; a written-off credit refuses.
/// </summary>
public sealed class UdhaarReversalService(ICommandRunner runner, IAccessPolicy policy, IExpenseStore store, IExpenseRegisterStore registers, IUdhaarReversalStore reversals,
    TimeProvider clock, Hcc.Application.Closing.IBusinessDayGuard? days = null, Billing.ICashDrawerPort? drawer = null)
{
    public const string Op = "ReverseUdhaarPayment";
    public const string ReversalSource = "udhaar-reversal";

    public async Task<CommandResult<UdhaarReversalResult>> ReverseAsync(CommandContext ctx, ReverseUdhaarPaymentRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (!await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, ExpensePermissions.Owner, null, ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, "Only the Owner reverses a mistaken Udhaar payment (rulebook A17b).");
        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length < 3) throw HccException.Validation("Give the reason for the reversal (at least 3 characters).");
        if (reason.Length > 200) throw HccException.Validation("Keep the reason to 200 characters.");
        return await runner.RunAsync(ctx with { Operation = Op }, req, async (tx, c) =>
        {
            var r = await registers.LockUdhaarAsync(tx, ctx.PropertyId, req.ReceivableId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Credit not found.");
            var p = r.Settlements.FirstOrDefault(s => s.SettlementId == req.SettlementId) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Payment not found on this credit.");
            var state = await reversals.StateAsync(tx, p.SettlementId, c).ConfigureAwait(false);
            if (state?.ReversalOf is not null || p.Amount < 0) throw new HccException(HccErrorCode.INVALID_TRANSITION, "A reversal cannot itself be reversed.");
            if (state?.ReversedBy is { } by) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"This payment is already reversed ({by.ToString(CultureInfo.InvariantCulture)}).");
            if (r.Void) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This credit was written off — a payment on it cannot be reversed.");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (days is not null) await days.EnsureBusinessDayOpenAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var cash = p.Desk && p.TillId is not null;
            if (cash)
            {
                if (req.TillId is not { } till) throw HccException.Validation("Choose the till the cash goes back out of (the reversed Udhaar cash leaves the open shift's drawer).");
                if (drawer is null) throw new InvalidOperationException("The Udhaar reversal needs the cash drawer port.");
                if (days is not null) await days.EnsureCashBookOpenAsync(tx, ctx.PropertyId, bd, "cash out of the drawer", c).ConfigureAwait(false);
                _ = await drawer.OpenShiftIdAsync(tx, till, c).ConfigureAwait(false);
            }

            var reference = "Reversal of " + p.SettlementId.ToString(CultureInfo.InvariantCulture);
            var id = await reversals.InsertReversalAsync(tx, new NewUdhaarReversal(r.ReceivableId, p.SettlementId, -p.Amount, bd, p.Mode, ctx.PrincipalId, clock.GetUtcNow().UtcDateTime,
                p.Desk, reference, cash ? req.TillId : null, reason), c).ConfigureAwait(false);
            long? movement = null;
            if (cash)
                movement = await drawer!.RecordMovementAsync(tx, req.TillId!.Value, Hcc.Domain.Payments.CashMovementKind.Payment, ReversalSource, id, new Paise(-p.Amount),
                    "Udhaar payment reversed · " + r.Name, reference, c).ConfigureAwait(false);
            if (p.Desk)
                await tx.PublishAsync(AccountingEvents.UdhaarPaymentReversed, r.ReceivableId,
                    new UdhaarPaymentReversedEvent(r.ReceivableId, id, p.SettlementId, bd, p.Mode, p.Amount, r.Name, reason, movement), c).ConfigureAwait(false);
            var outstanding = Hcc.Domain.Expenses.ExpenseScc.UdhaarOut(r.Amount, r.Paid - p.Amount);
            var msg = $"UD-{r.ReceivableId.ToString(CultureInfo.InvariantCulture)} · {r.Name} · payment {p.SettlementId.ToString(CultureInfo.InvariantCulture)} {new Paise(p.Amount)} reversed by "
                      + $"{id.ToString(CultureInfo.InvariantCulture)}{(p.Date == bd ? " the same day" : " (reversing entry dated " + bd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")")}"
                      + $" · {(cash ? "cash out of the drawer · " : "")}outstanding {new Paise(outstanding)} · {reason}";
            await tx.AuditAsync("UDHAAR_PAYMENT_REVERSED", "udhaar:" + r.ReceivableId.ToString(CultureInfo.InvariantCulture), msg, c).ConfigureAwait(false);
            return new UdhaarReversalResult(id, p.SettlementId, r.ReceivableId, p.Amount, outstanding, msg);
        }, ct).ConfigureAwait(false);
    }
}
