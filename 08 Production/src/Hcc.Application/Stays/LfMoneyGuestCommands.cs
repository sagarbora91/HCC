using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Customers;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Stays;

namespace Hcc.Application.Stays;

/// <summary>LF B1 / B2 permissions (migrations 4678, 4692): both Owner only (money fields, DEC-058).</summary>
public static class LfMoneyPermissions
{
    public const string AgreedRate = "PERM-GST-010";
    public const string CreditAccount = "PERM-GST-011";
    public const string RateChange = "PERM-STY-010";
    public const string BillCorrection = "PERM-FOL-010";
    public const string CounterIssue = "PERM-CTR-001";
    public const string CounterVoid = "PERM-CTR-002";
}

public sealed record SetGuestAgreedRateRequest(long GuestId, int RoomTypeId, long RatePaise, string? PlanCode, DateOnly? ValidTo, string? Note);

public sealed record ClearGuestAgreedRateRequest(long GuestId, int RoomTypeId);

public sealed record SetGuestCreditAccountRequest(long GuestId, long LimitPaise, bool Active, string? Note);

public sealed record GuestAgreedRateView(long GuestId, int RoomTypeId, long RatePaise, string? PlanCode, DateOnly? ValidTo, string? Note);

public sealed record GuestCreditAccountView(long GuestId, long LimitPaise, bool Active, string CustomerKey, string PartyLedger, string? Note);

public sealed record GuestMoneyView(long GuestId, IReadOnlyList<GuestAgreedRateView> AgreedRates, GuestCreditAccountView? CreditAccount);

/// <summary>SQL port of the LF guest money fields (gst.guest_agreed_rate 4677, gst.guest_credit_account 4694).</summary>
public interface ILfGuestMoneyStore
{
    Task<string?> GuestPhoneAsync(ICommandTransaction? tx, int propertyId, long guestId, CancellationToken ct);

    Task<bool> RoomTypeExistsAsync(ICommandTransaction tx, int propertyId, int roomTypeId, CancellationToken ct);

    Task UpsertAgreedRateAsync(ICommandTransaction tx, GuestAgreedRate rate, int by, CancellationToken ct);

    Task<bool> DeleteAgreedRateAsync(ICommandTransaction tx, long guestId, int roomTypeId, CancellationToken ct);

    Task<IReadOnlyList<GuestAgreedRate>> AgreedRatesAsync(ICommandTransaction? tx, int propertyId, long guestId, CancellationToken ct);

    Task<(GuestCreditAccount Account, string? Note)?> CreditAccountAsync(ICommandTransaction? tx, int propertyId, long guestId, CancellationToken ct);

    Task UpsertCreditAccountAsync(ICommandTransaction tx, int propertyId, GuestCreditAccount account, string? note, int by, CancellationToken ct);
}

/// <summary>LF B1 (GUEST_RATE_SET / CLEAR) and B2 (GUEST_CREDIT_ACCOUNT_SET), Owner only; the money read is for money roles only (DEC-MA-11e).</summary>
public sealed class LfMoneyGuestCommands(ICommandRunner runner, IAccessPolicy policy, ILfGuestMoneyStore store)
{
    public const string OpSetAgreedRate = "SetGuestAgreedRate";
    public const string OpClearAgreedRate = "ClearGuestAgreedRate";
    public const string OpSetCreditAccount = "SetGuestCreditAccount";

    public async Task<CommandResult<GuestMoneyView>> SetAgreedRateAsync(CommandContext ctx, SetGuestAgreedRateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, LfMoneyPermissions.AgreedRate, null, null, ct).ConfigureAwait(false);
        if (req.RatePaise is <= 0 or > 10_000_000_000) throw HccException.Validation("Enter the agreed rate per night in whole paise (more than 0).");
        if (req.Note is { Length: > 200 }) throw HccException.Validation("Keep the note to 200 characters.");
        return await runner.RunAsync(ctx with { Operation = OpSetAgreedRate }, req, async (tx, c) =>
        {
            _ = await store.GuestPhoneAsync(tx, ctx.PropertyId, req.GuestId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Guest profile not found.");
            if (!await store.RoomTypeExistsAsync(tx, ctx.PropertyId, req.RoomTypeId, c).ConfigureAwait(false)) throw HccException.Validation("Choose a room type.");
            await store.UpsertAgreedRateAsync(tx, new GuestAgreedRate(req.GuestId, req.RoomTypeId, new Paise(req.RatePaise), Blank(req.PlanCode), req.ValidTo, Blank(req.Note)), ctx.PrincipalId, c).ConfigureAwait(false);
            await tx.AuditAsync("GUEST_RATE_SET", "guest-" + req.GuestId, $"room type {req.RoomTypeId} · {req.RatePaise} paise{(req.ValidTo is { } v ? " · valid to " + v.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "")}", c).ConfigureAwait(false);
            return await ViewAsync(tx, ctx.PropertyId, req.GuestId, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<GuestMoneyView>> ClearAgreedRateAsync(CommandContext ctx, ClearGuestAgreedRateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, LfMoneyPermissions.AgreedRate, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpClearAgreedRate }, req, async (tx, c) =>
        {
            if (!await store.DeleteAgreedRateAsync(tx, req.GuestId, req.RoomTypeId, c).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "No agreed rate for that room type.");
            await tx.AuditAsync("GUEST_RATE_CLEAR", "guest-" + req.GuestId, "room type " + req.RoomTypeId, c).ConfigureAwait(false);
            return await ViewAsync(tx, ctx.PropertyId, req.GuestId, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<GuestMoneyView>> SetCreditAccountAsync(CommandContext ctx, SetGuestCreditAccountRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, LfMoneyPermissions.CreditAccount, null, null, ct).ConfigureAwait(false);
        if (req.LimitPaise is < 0 or > 10_000_000_000) throw HccException.Validation("Enter the credit limit in whole paise (0 or more).");
        if (req.Note is { Length: > 200 }) throw HccException.Validation("Keep the note to 200 characters.");
        return await runner.RunAsync(ctx with { Operation = OpSetCreditAccount }, req, async (tx, c) =>
        {
            var phone = await store.GuestPhoneAsync(tx, ctx.PropertyId, req.GuestId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Guest profile not found.");
            var old = await store.CreditAccountAsync(tx, ctx.PropertyId, req.GuestId, c).ConfigureAwait(false);
            var key = old?.Account.CustomerKey ?? CustomerCreditKey.Of(req.GuestId, phone); // the key never moves once the account exists
            await store.UpsertCreditAccountAsync(tx, ctx.PropertyId, new GuestCreditAccount(req.GuestId, new Paise(req.LimitPaise), req.Active, key), Blank(req.Note), ctx.PrincipalId, c).ConfigureAwait(false);
            await tx.AuditAsync("GUEST_CREDIT_ACCOUNT_SET", "guest-" + req.GuestId, $"{(req.Active ? "active" : "inactive")} · limit {req.LimitPaise} paise · party {GuestCreditRules.PartyLedger(key)}", c).ConfigureAwait(false);
            return await ViewAsync(tx, ctx.PropertyId, req.GuestId, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>GET the money fields of a guest: money roles only (PERM-STY-010 or PERM-FOL-010 holders = Owner, Manager, Reception, Cashier, Accountant).</summary>
    public async Task<GuestMoneyView> ReadAsync(int propertyId, int principalId, long guestId, CancellationToken ct = default)
    {
        if (!await policy.HasAsync(propertyId, principalId, LfMoneyPermissions.BillCorrection, null, ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.FORBIDDEN, "Access denied: guest rates and credit are shown to money roles only.");
        return await ViewAsync(null, propertyId, guestId, ct).ConfigureAwait(false);
    }

    private async Task<GuestMoneyView> ViewAsync(ICommandTransaction? tx, int propertyId, long guestId, CancellationToken ct)
    {
        var rates = await store.AgreedRatesAsync(tx, propertyId, guestId, ct).ConfigureAwait(false);
        var acc = await store.CreditAccountAsync(tx, propertyId, guestId, ct).ConfigureAwait(false);
        return new GuestMoneyView(guestId, rates.Select(r => new GuestAgreedRateView(r.GuestId, r.RoomTypeId, r.Rate.Value, r.PlanCode, r.ValidTo, r.Note)).ToList(),
            acc is { } x ? new GuestCreditAccountView(guestId, x.Account.Limit.Value, x.Account.Active, x.Account.CustomerKey, GuestCreditRules.PartyLedger(x.Account.CustomerKey), x.Note) : null);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
