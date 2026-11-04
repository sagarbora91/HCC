using Hcc.Application.Access;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Application.Counter;

public sealed record CounterBillLineRow(int Seq, string Label, string? ExtraId, string? MenuItemId, int Qty, long UnitPaise, long AmountPaise, string GstCategory, int TaxBps, string Sac);

public sealed record CounterBillListRow(long BillId, string InvoiceNo, DateOnly BusinessDate, DateTime IssuedUtc, int IssuedBy, string PartyName, string? PartyPhone, string? PartyGstin,
    string? PartyAddress, long? GuestId, long? CompanyId, long DiscountPaise, string? DiscountReason, long NetPaise, long TaxPaise, long CgstPaise, long SgstPaise, long TotalPaise,
    string? Remark, string SettlementMode, string? Method, string? Reference, long? UdhaarId, string Status, string? CreditNoteNo, string? CreditNoteReason, long? VoidApprovalId,
    IReadOnlyList<CounterBillLineRow> Lines);

/// <summary>A guest with a credit account: limit, active flag, the Udhaar party key and what is still open on it (ageing source).</summary>
public sealed record CreditGuestRow(long GuestId, string Name, string? Phone, long LimitPaise, bool Active, string CustomerKey, string? Note, long OpenPaise, DateOnly? OldestOpenDate);

/// <summary>One applied rate / guest-count change of a stay (gst.stay_rate_change; append-only).</summary>
public sealed record StayRateChangeRow(long ChangeId, long StayId, long ReservationId, string RoomNo, DateOnly BusinessDate, DateTime ChangedUtc, int ChangedBy, long? ApprovalId, int? ApprovedBy,
    string Scope, string Reason, long OldRatePaise, long NewRatePaise, int OldPeople, int NewPeople, int OldChildren, int NewChildren, DateOnly EffectiveFrom, string RepostedJson);

public interface ILfMoneyReadStore
{
    Task<IReadOnlyList<CounterBillListRow>> CounterBillsAsync(int propertyId, int take, string? invoiceNo, DateOnly? from, DateOnly? to, CancellationToken ct);

    Task<IReadOnlyList<CreditGuestRow>> CreditGuestsAsync(int propertyId, CancellationToken ct);

    Task<IReadOnlyList<StayRateChangeRow>> RateChangesAsync(int propertyId, int take, CancellationToken ct);
}

/// <summary>
/// LF-finish list reads (money roles only; housekeeping / maintenance hold none of these permissions, DEC-061): counter bills = PERM-CTR-001 holders,
/// credit guests = PERM-FOL-010 holders (the same money roles as GET /guests/{id}/money), rate changes = PERM-STY-010 holders.
/// </summary>
public sealed class LfMoneyReadService(ILfMoneyReadStore store, IAccessPolicy policy)
{
    public const int Take = 500;

    private async Task RequireAsync(int propertyId, int principalId, string perm, string what, CancellationToken ct)
    {
        if (!await policy.HasAsync(propertyId, principalId, perm, null, ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.FORBIDDEN, $"Access denied: {what} are shown to money roles only.");
    }

    public async Task<IReadOnlyList<CounterBillListRow>> CounterBillsAsync(int propertyId, int principalId, string? invoiceNo = null, DateOnly? from = null, DateOnly? to = null, int? take = null, CancellationToken ct = default)
    {
        await RequireAsync(propertyId, principalId, LfMoneyPermissions.CounterIssue, "counter bills", ct).ConfigureAwait(false);
        var inv = string.IsNullOrWhiteSpace(invoiceNo) ? null : invoiceNo.Trim();
        if (inv is { Length: > 40 }) throw HccException.Validation("The invoice number is too long (40 characters at most).");
        if (from is not null && to is not null && from > to) throw HccException.Validation("The 'from' date is after the 'to' date.");
        var n = Math.Clamp(take ?? Take, 1, Take);
        return await store.CounterBillsAsync(propertyId, n, inv, from, to, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CreditGuestRow>> CreditGuestsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await RequireAsync(propertyId, principalId, LfMoneyPermissions.BillCorrection, "credit accounts", ct).ConfigureAwait(false);
        return await store.CreditGuestsAsync(propertyId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StayRateChangeRow>> RateChangesAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await RequireAsync(propertyId, principalId, LfMoneyPermissions.RateChange, "rate changes", ct).ConfigureAwait(false);
        return await store.RateChangesAsync(propertyId, Take, ct).ConfigureAwait(false);
    }
}
