using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Payments;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Application.Billing;

// ACC-2 flows (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4; rulebook A6a, A13; DEC-150). Prototype twins: DEPOSIT_APPLY
// (services/mock/handlers/accounting-flows.ts) and CHECKOUT {otaCredit} (handlers/folio.ts).

/// <summary>The OTA channel a folio's reservation came from (the New message that created it); null = not an OTA booking.</summary>
public sealed record FolioChannel(int ChannelId, string Code, string Name, string Status);

/// <summary>SQL adapter: SqlFlowsStore.</summary>
public interface IFolioChannelSource
{
    Task<FolioChannel?> ChannelOfFolioAsync(ICommandTransaction tx, long folioId, CancellationToken ct = default);
}

/// <summary>
/// ApplyDeposit (A6a): the refundable deposit held is applied to the in-house folio at check-out — receipt kind <c>deposit-apply</c>, no cash
/// (PERM-PAY-001 on the open shift of the till, Reception / Cashier their own); ≤ the deposit held and ≤ the balance due; booked by Accounting
/// from ReceiptRecorded as Journal Dr 2000 · Cr 1100. SettleFolioToOta (A13): the whole balance of an in-house folio whose reservation came from
/// a CONNECTED channel moves to the channel's receivable — receipt kind <c>ota-credit</c> (PERM-STY-003, run before CheckoutStay), booked as
/// Journal Dr 1115/&lt;channel code&gt; · Cr 1100 and settled later by RecordOtaPayout.
/// </summary>
public sealed class DepositFlowsService(ICommandRunner runner, IBillingStore store, IAccessPolicy policy, IFolioChannelSource channels, TimeProvider clock,
    Accounting.IAccountingStore? accounting = null)
{
    /// <summary>Coordinator ruling (28 Oct): <c>Source</c> opening is applied by a Manager / the Owner — PERM-PAY-001 and the opening-balance permission.</summary>
    public const string OpeningPermission = "PERM-ACC-008";

    public const string OpApplyDeposit = "ApplyDeposit";
    public const string OpSettleToOta = "SettleFolioToOta";
    public const string DepositTender = "Deposit applied";
    public const string OtaTender = "OTA credit";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<CommandResult<FolioFlowResult>> ApplyDepositAsync(Caller caller, string idempotencyKey, ApplyDepositRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.PaymentRecord, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        var amount = Paise.FromWire(req.AmountPaise).EnsureReceiptAmount();
        if (req.ExpectedVersion is not null) VersionToken.Validate(req.ExpectedVersion);
        var source = string.IsNullOrWhiteSpace(req.Source) ? "receipts" : req.Source.Trim();
        if (source is not ("receipts" or "opening")) throw HccException.Validation("Choose where the advance comes from: the deposit receipts on the folio or the opening balance.");
        var opening = source == "opening";
        if (opening && !await policy.HasAsync(caller.PropertyId, caller.PrincipalId, OpeningPermission, null, ct).ConfigureAwait(false))
            throw BillingService.Forbidden("Access denied: an advance carried in the opening balance is applied by a Manager or the Owner.");
        return await runner.RunAsync(new CommandContext(caller.PropertyId, caller.PrincipalId, OpApplyDeposit, idempotencyKey), req, async (tx, ct2) =>
        {
            var f = await FolioAsync(tx, caller, req.FolioId, ct2).ConfigureAwait(false);
            if (req.ExpectedVersion is not null) BillingService.EnsureVersion(f.Version, req.ExpectedVersion, "The folio");
            if (f.ReservationStatus != "InHouse") throw BillingService.Invalid("Apply the deposit to an in-house stay at check-out.");
            var t = FolioStatement.Compute(await store.LinesAsync(tx, f.FolioId, ct2).ConfigureAwait(false), await store.ReceiptsAsync(tx, f.FolioId, ct2).ConfigureAwait(false));
            if (!opening)
            {
                if (t.Deposit <= Paise.Zero) throw BillingService.Invalid("This folio holds no refundable deposit.");
                if (amount > t.Deposit) throw HccException.Validation($"Apply at most the deposit held ({CashGuard.Rs(t.Deposit.Value)}).");
            }
            else
            {
                if (accounting is null) throw HccException.NotConfigured("deposit-apply-opening", "Applying an opening-balance advance needs the accounting store.");
                var held = -(await Accounting.ChartService.UsageAsync(accounting, tx, caller.PropertyId, "2000", ct2).ConfigureAwait(false)).OwnBalance;
                if (held <= 0) throw BillingService.Invalid("No advance is held in 2000 Advance deposits.");
                if (amount.Value > held) throw HccException.Validation($"Apply at most the advances held in 2000 Advance deposits ({CashGuard.Rs(held)}).");
            }

            if (t.Balance <= Paise.Zero) throw BillingService.Invalid("Nothing is due on this folio — return the deposit instead.");
            if (amount > t.Balance) throw HccException.Validation($"Apply at most the balance due ({CashGuard.Rs(t.Balance.Value)}); return the rest of the deposit.");
            // Contract §4: Reception / Cashier on an open shift — the till's open shift on the business date and PERM-PAY-001 on it (own shift).
            var shift = await CashDesk.RequireOpenShiftAsync(store, tx, caller.PropertyId, req.TillId, f.BusinessDate, ct2).ConfigureAwait(false);
            await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.PaymentRecord, new AccessScope(BillingPermissions.ShiftScope, shift.ShiftId), ct: ct2)
                .ConfigureAwait(false);
            var tender = opening ? FolioTotals.OpeningAdvanceTender : DepositTender;
            var receipt = new NewReceipt(f.FolioId, ReceiptKind.DepositApply, tender, amount, null, f.BusinessDate, null, null,
                opening ? "Advance from the opening balance applied at check-out" : "Deposit applied at check-out", null, caller.PrincipalId, Now);
            var id = await store.InsertReceiptAsync(tx, receipt, ct2).ConfigureAwait(false);
            var version = await store.TouchFolioAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
            await tx.AuditAsync("DEPOSIT_APPLIED", BillingService.Target("folio", f.FolioId), $"deposit {amount} applied to the folio · receipt {N(id)}", ct2).ConfigureAwait(false);
            await tx.PublishAsync("ReceiptRecorded", f.FolioId, new { folioId = f.FolioId, receiptId = id, kind = FolioTotals.KindCode(ReceiptKind.DepositApply), tender,
                amountPaise = amount.Value, businessDate = f.BusinessDate }, ct2).ConfigureAwait(false);
            return new FolioFlowResult(id, f.FolioId, "deposit-apply", amount.Value, (opening ? t.Deposit : t.Deposit - amount).Value, (t.Balance - amount).Value, version);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<FolioFlowResult>> SettleToOtaAsync(Caller caller, string idempotencyKey, SettleFolioToOtaRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, BillingPermissions.Checkout, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        if (req.ExpectedVersion is not null) VersionToken.Validate(req.ExpectedVersion);
        return await runner.RunAsync(new CommandContext(caller.PropertyId, caller.PrincipalId, OpSettleToOta, idempotencyKey), req, async (tx, ct2) =>
        {
            var f = await FolioAsync(tx, caller, req.FolioId, ct2).ConfigureAwait(false);
            if (req.ExpectedVersion is not null) BillingService.EnsureVersion(f.Version, req.ExpectedVersion, "The folio");
            if (f.ReservationStatus != "InHouse") throw BillingService.Invalid("This stay is not in house.");
            var ch = await channels.ChannelOfFolioAsync(tx, f.FolioId, ct2).ConfigureAwait(false)
                ?? throw BillingService.Invalid("This booking did not come from an OTA channel. Only a booking whose source is a connected OTA channel can be settled to the OTA.");
            if (ch.Status != "Connected")
                throw BillingService.Invalid($"{ch.Name} is {ch.Status.ToLowerInvariant()} — settle the folio to the OTA only through a connected channel.");
            var t = FolioStatement.Compute(await store.LinesAsync(tx, f.FolioId, ct2).ConfigureAwait(false), await store.ReceiptsAsync(tx, f.FolioId, ct2).ConfigureAwait(false));
            if (t.Balance <= Paise.Zero) throw BillingService.Invalid("Nothing is due on this folio.");
            var receipt = new NewReceipt(f.FolioId, ReceiptKind.OtaCredit, OtaTender, t.Balance, null, f.BusinessDate, null, null, $"{ch.Name} · paid by the OTA", null,
                caller.PrincipalId, Now);
            var id = await store.InsertReceiptAsync(tx, receipt, ct2).ConfigureAwait(false);
            var version = await store.TouchFolioAsync(tx, f.FolioId, ct2).ConfigureAwait(false);
            await tx.AuditAsync("FOLIO_TO_OTA", BillingService.Target("folio", f.FolioId), $"{t.Balance} to {ch.Name} (OTA receivable) · receipt {N(id)}", ct2).ConfigureAwait(false);
            await tx.PublishAsync("ReceiptRecorded", f.FolioId, new { folioId = f.FolioId, receiptId = id, kind = FolioTotals.KindCode(ReceiptKind.OtaCredit), tender = OtaTender,
                amountPaise = t.Balance.Value, businessDate = f.BusinessDate }, ct2).ConfigureAwait(false);
            return new FolioFlowResult(id, f.FolioId, "ota-credit", t.Balance.Value, t.Deposit.Value, 0, version, ch.ChannelId);
        }, ct).ConfigureAwait(false);
    }

    private async Task<FolioHeader> FolioAsync(ICommandTransaction tx, Caller caller, long folioId, CancellationToken ct)
    {
        var f = await store.LockFolioAsync(tx, folioId, ct).ConfigureAwait(false);
        if (f is null || f.PropertyId != caller.PropertyId) throw BillingService.NotFound("Folio not found.");
        return f;
    }

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
}
