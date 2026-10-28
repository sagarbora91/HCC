using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Application.Accounting;

// ACC-2 · lane acc2-parity (02 Planning/agents/ACC2-POSTING-CONTRACT.md §8; ACC1-DONE §7 item 6; rulebook A17, DEC-144): Udhaar credits
// raised on the server before migration 4215 published no UdhaarCreditRaised, so they have no Sales voucher. BackfillUdhaarCredits (Owner,
// once) publishes the event for each of them, dated the credit's own date; the accounting consumer books it like any raise (kind absent →
// "other" revenue, no GST lines — the CA note of ACC-1). Idempotent: a credit that already has the event in the outbox or its sale source
// (udhaar:<id>) is skipped, so a second run publishes nothing.

/// <summary>An Udhaar credit without a sale: the fields of its UdhaarCreditRaised event.</summary>
public sealed record UnbookedUdhaarCredit(long ReceivableId, DateOnly CreditDate, string Customer, string? Mobile, long AmountPaise, string? Kind, int GstBps);

/// <summary>Read port: the property's Udhaar credits with neither an UdhaarCreditRaised event nor an <c>udhaar:&lt;id&gt;</c> source.</summary>
public interface IUdhaarBackfillStore
{
    Task<IReadOnlyList<UnbookedUdhaarCredit>> UnbookedAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);
}

/// <summary>The BackfillUdhaarCredits command (contract §8).</summary>
public sealed class UdhaarCreditBackfill(ICommandRunner runner, IUdhaarBackfillStore store, IAccessPolicy policy)
{
    public const string Op = "BackfillUdhaarCredits";

    /// <summary>Owner only: the Owner's books-level permission (PERM-ACC-003, unlock a locked period — Owner).</summary>
    public const string Permission = AccountingPermissions.Unlock;

    public async Task<CommandResult<BackfillUdhaarCreditsResult>> RunAsync(Caller caller, string idempotencyKey, BackfillUdhaarCreditsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, Permission, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(new CommandContext(caller.PropertyId, caller.PrincipalId, Op, idempotencyKey), req, async (tx, c) =>
        {
            var list = await store.UnbookedAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            foreach (var u in list)
                await tx.PublishAsync(AccountingEvents.UdhaarCreditRaised, u.ReceivableId,
                    new UdhaarCreditRaisedEvent(u.ReceivableId, u.CreditDate, u.Customer, u.Mobile, u.AmountPaise, u.Kind, u.GstBps), c).ConfigureAwait(false);
            var total = list.Sum(u => u.AmountPaise);
            var msg = list.Count == 0
                ? "Udhaar back-fill · nothing to publish: every Udhaar credit already has its sale"
                : $"Udhaar back-fill · {list.Count} credit(s) raised before ACC-1 published for booking as sales · ₹{SourceRules.Rupees(total)}"
                  + (list.Any(u => u.Kind is null) ? " · without a kind: booked as other revenue, no GST (CA note)" : "");
            await tx.AuditAsync("ACC_UDHAAR_BACKFILL", "udhaar:backfill", msg, c).ConfigureAwait(false);
            return new BackfillUdhaarCreditsResult(list.Count, total, list.Select(u => u.ReceivableId).ToList(), msg);
        }, ct).ConfigureAwait(false);
    }
}
