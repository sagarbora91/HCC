using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Ota;
using Hcc.Domain.Errors;
using Hcc.Domain.Ota;

namespace Hcc.Application.Ota;

// ACC-2 flows · OTA payouts from the channel's statement (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4; rulebook A13, §4.14; DEC-150).
// Prototype twin: CHN_PAYOUT (services/mock/handlers/channels.ts) + chnPayoutProblem / chnOtaOpen (domain/streams/channels.ts).

public sealed record PayoutChannel(int ChannelId, string Code, string Name, string Status, bool ItcAllowed);

public sealed record NewOtaPayout(int PropertyId, int ChannelId, DateOnly BusinessDate, string StatementRef, long GrossPaise, long CommissionPaise, long CommissionGstPaise,
    long TcsPaise, long NetPaise, string BankAccount, bool ItcAllowed, IReadOnlyList<long> ReservationIds, int RecordedBy, DateTime RecordedUtc);

/// <summary>Persistence of ota.payout / ota.payout_booking (migration 4251). SQL adapter: SqlFlowsStore.</summary>
public interface IOtaPayoutStore
{
    /// <summary>The channel under UPDLOCK (serializes the payouts of one channel); null when absent.</summary>
    Task<PayoutChannel?> LockChannelAsync(ICommandTransaction tx, int propertyId, int channelId, CancellationToken ct = default);

    /// <summary>The channel's open receivable: OTA credits on folios of its reservations less the gross of its payouts.</summary>
    Task<long> OpenReceivableAsync(ICommandTransaction tx, int channelId, CancellationToken ct = default);

    /// <summary>Reservations of the channel with an OTA credit.</summary>
    Task<IReadOnlySet<long>> CreditedReservationsAsync(ICommandTransaction tx, int channelId, CancellationToken ct = default);

    /// <summary>(name, active) of a bank account of kind bank by code; null when there is none.</summary>
    Task<(string Name, bool Active)?> BankAccountAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct = default);

    Task<bool> StatementTakenAsync(ICommandTransaction tx, int channelId, string statementRef, CancellationToken ct = default);

    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<long> InsertPayoutAsync(ICommandTransaction tx, NewOtaPayout payout, CancellationToken ct = default);

    Task SetItcAllowedAsync(ICommandTransaction tx, int channelId, bool itcAllowed, CancellationToken ct = default);

    Task<IReadOnlyList<OtaPayoutView>> ListPayoutsAsync(int propertyId, CancellationToken ct = default);
}

/// <summary>
/// RecordOtaPayout (PERM-OTA-004 holders: Owner, Manager, Accountant): the statement's figures must add up (gross = net + commission + its GST +
/// TCS), the gross may not exceed the channel's open receivable, the bank account must be an active bank account and a statement is recorded
/// once per channel. Publishes OtaPayoutRecorded (Accounting: Receipt Dr 1030/&lt;bank&gt; net · Dr 5070 · Dr 1340 · Cr 1115/&lt;channel&gt;).
/// SetChannelItc (Owner money setting, PERM-MST-002; DEC-058 / DEC-143): whether the commission GST is input tax credit.
/// </summary>
public sealed class OtaPayoutService(ICommandRunner runner, IOtaPayoutStore store, IAccessPolicy policy, TimeProvider clock)
{
    public const string OpRecord = "RecordOtaPayout";
    public const string OpSetItc = "SetChannelItc";
    public const string OwnerMoney = "PERM-MST-002";

    public async Task<CommandResult<OtaPayoutResult>> RecordAsync(CommandContext ctx, RecordOtaPayoutRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, OtaPermissions.ViewMoney, ct: ct).ConfigureAwait(false);
        if (OtaPayoutRules.Problem(req.StatementRef, req.GrossPaise, req.CommissionPaise, req.CommissionGstPaise, req.TcsPaise, req.NetPaise) is { } problem)
            throw HccException.Validation(problem);
        var reference = req.StatementRef.Trim();
        var bank = (req.BankAccount ?? string.Empty).Trim();
        var reservations = (req.ReservationIds ?? []).Distinct().ToList();
        return await runner.RunAsync(ctx with { Operation = OpRecord }, req, async (tx, c) =>
        {
            var ch = await store.LockChannelAsync(tx, ctx.PropertyId, req.ChannelId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Channel not found.");
            var account = bank.Length == 0 ? null : await store.BankAccountAsync(tx, ctx.PropertyId, bank, c).ConfigureAwait(false);
            if (account is null) throw HccException.Validation("Choose the bank account the payout reached (Master configuration › Bank accounts).");
            if (!account.Value.Active) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"{account.Value.Name} is retired — choose an active bank account.");
            if (await store.StatementTakenAsync(tx, ch.ChannelId, reference, c).ConfigureAwait(false))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Statement {reference} of {ch.Name} is already recorded.");
            if (reservations.Count > 0)
            {
                var credited = await store.CreditedReservationsAsync(tx, ch.ChannelId, c).ConfigureAwait(false);
                foreach (var r in reservations)
                    if (!credited.Contains(r)) throw HccException.Validation($"Booking {r.ToString(CultureInfo.InvariantCulture)} has no OTA credit with {ch.Name}.");
            }

            var open = await store.OpenReceivableAsync(tx, ch.ChannelId, c).ConfigureAwait(false);
            if (req.GrossPaise > open) throw new HccException(HccErrorCode.INVALID_TRANSITION, OtaPayoutRules.OverReceivable(ch.Name, req.GrossPaise, open));
            var date = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var id = await store.InsertPayoutAsync(tx, new NewOtaPayout(ctx.PropertyId, ch.ChannelId, date, reference, req.GrossPaise, req.CommissionPaise, req.CommissionGstPaise,
                req.TcsPaise, req.NetPaise, bank, ch.ItcAllowed, reservations, ctx.PrincipalId, clock.GetUtcNow().UtcDateTime), c).ConfigureAwait(false);
            await tx.AuditAsync("OTA_PAYOUT_RECORDED", "ota-payout:" + id.ToString(CultureInfo.InvariantCulture),
                $"{ch.Name} statement {reference} · gross {Rs(req.GrossPaise)} = net {Rs(req.NetPaise)} to {account.Value.Name} + commission {Rs(req.CommissionPaise)} + GST {Rs(req.CommissionGstPaise)}"
                + $"{(ch.ItcAllowed ? " (ITC)" : "")} + TCS {Rs(req.TcsPaise)} · receivable {Rs(open)} → {Rs(open - req.GrossPaise)}", c).ConfigureAwait(false);
            await tx.PublishAsync(AccountingEvents.OtaPayoutRecorded, id, new OtaPayoutRecordedEvent(id, ch.ChannelId, ch.Code, ch.Name, date, reference, req.GrossPaise,
                req.CommissionPaise, req.CommissionGstPaise, req.TcsPaise, req.NetPaise, bank, ch.ItcAllowed, reservations.Count), c).ConfigureAwait(false);
            return new OtaPayoutResult(id, ch.ChannelId, req.GrossPaise, open, open - req.GrossPaise);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<OtaPayoutResult>> SetItcAsync(CommandContext ctx, SetChannelItcRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (!await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, OwnerMoney, null, ct).ConfigureAwait(false))
            throw new HccException(HccErrorCode.FORBIDDEN, "Only the Owner turns input tax credit on the commission GST on or off (DEC-058).");
        return await runner.RunAsync(ctx with { Operation = OpSetItc }, req, async (tx, c) =>
        {
            var ch = await store.LockChannelAsync(tx, ctx.PropertyId, req.ChannelId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Channel not found.");
            if (ch.ItcAllowed != req.ItcAllowed) await store.SetItcAllowedAsync(tx, ch.ChannelId, req.ItcAllowed, c).ConfigureAwait(false);
            await tx.AuditAsync("OTA_CHANNEL_ITC", "ota-channel:" + ch.ChannelId.ToString(CultureInfo.InvariantCulture),
                $"{ch.Name} · ITC on commission GST {(req.ItcAllowed ? "allowed" : "not allowed")}", c).ConfigureAwait(false);
            return new OtaPayoutResult(0, ch.ChannelId, 0, 0, 0);
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OtaPayoutView>> ListAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, OtaPermissions.ViewMoney, ct: ct).ConfigureAwait(false);
        return await store.ListPayoutsAsync(propertyId, ct).ConfigureAwait(false);
    }

    private static string Rs(long paise) => Hcc.Domain.Accounting.CashGuard.Rs(paise);
}
