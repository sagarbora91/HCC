using System.Text.Json;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Domain.Groups;

/// <summary>One line of a company's exposure: <c>receivable</c> (a company-credit receipt, Id = receipt), <c>payment</c> (money received from the
/// company, negative, Id = payment), <c>stay</c> (an open stay billed to the company, Id = reservation) or <c>group</c> (an open group master, Id = group).</summary>
public sealed record CompanyExposureItem(string Kind, long Id, string Reference, Paise Amount);

/// <summary>
/// A company account's credit exposure (DEC-076, audit A03a-05), in paise: what the company owes the hotel now or is committed to owe for
/// business already billed to it.
/// <list type="bullet">
/// <item><see cref="Receivable"/> = company-credit receipts attributed to the company (a stay's balance moved to company credit at checkout;
/// <c>pay.receipt.company_id</c>) − payments received from the company (<c>grp.company_payment</c>). This is the unpaid part of the company's
/// tax invoices; it is never negative (a payment above it is refused).</item>
/// <item><see cref="OpenStays"/> = for every live reservation billed to the company (<c>inv.reservation.company_id</c>; Held, Confirmed or InHouse;
/// not a member of an open group master billed to the same company): posted charges + their GST + the agreed nights not yet posted at the
/// booked rate + GST per night, less what the guest paid (payments − refunds) and less company credit already moved off the folio;
/// never below 0 per stay (<see cref="GroupEstimate.Exposure"/>, the prototype <c>grpBookingExposure</c>). Deposits are held money, not payment.</item>
/// <item><see cref="GroupMasters"/> = for every open group master billed to the company (<c>grp.group_account.company_id</c>): the member lines
/// already routed to the master (persisted routes: ApplyGroupRouting / RerouteGroupCharge) + attrition charges with GST, less the master's
/// receipts (payments + deposits − refunds); never below 0 per group. Lines not yet routed and future nights of a group are not counted.</item>
/// </list>
/// Departed stays paid by the guest count nothing; cancelled, no-show and expired bookings count nothing.
/// </summary>
public sealed record CompanyExposure(Paise Receivable, Paise OpenStays, Paise GroupMasters, IReadOnlyList<CompanyExposureItem> Items)
{
    public Paise Total => Receivable + OpenStays + GroupMasters;

    /// <summary>The exposure of one stay or group in <see cref="Items"/> (0 when absent).</summary>
    public Paise Of(string kind, long id) => Items.Where(i => i.Kind == kind && i.Id == id).Aggregate(Paise.Zero, (a, i) => a + i.Amount);
}

/// <summary>
/// DEC-076 credit rule. The limit is <c>grp.company_account.credit_limit_paise</c> (NOT NULL, default 0, never null): <b>0 = no credit</b>, nothing
/// may be billed to the company account on credit. A command that bills to the company is refused when <c>used + needed &gt; limit</c>, where
/// <c>used</c> is the exposure of everything else billed to the company and <c>needed</c> what this booking / stay / group master adds up to after
/// the command. There is no single-refusal override (not decided): the Owner raises the limit, or the company pays its open invoices first.
/// </summary>
public static class CompanyCreditRules
{
    public const string Reason = "CREDIT_LIMIT";

    /// <summary>Credit left: limit − used, never below 0.</summary>
    public static Paise Remaining(Paise limit, Paise used) => used >= limit ? Paise.Zero : limit - used;

    public static bool Exceeds(Paise limit, Paise used, Paise needed) => needed > Paise.Zero && used + needed > limit;

    /// <summary>The refusal text (prototype <c>grpCreditRefusal</c>, same wording); figures in the prototype's ₹ format.</summary>
    public static string Message(string company, Paise limit, Paise used, Paise needed, string what)
        => $"Credit limit exceeded for {company}: limit {limit}{(limit == Paise.Zero ? " (no credit)" : "")}, already used by other business {used}, headroom {Remaining(limit, used)}; "
            + $"this {what} needs {needed}. Only the Owner can raise the limit (Groups & corporate › Companies › Credit limit).";

    /// <summary>INVALID_TRANSITION with <c>details.reason = CREDIT_LIMIT</c> and the figures (paise), when <see cref="Exceeds"/>.</summary>
    public static void Ensure(long companyId, string company, Paise limit, Paise used, Paise needed, string what)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (!Exceeds(limit, used, needed)) return;
        throw new HccException(HccErrorCode.INVALID_TRANSITION, Message(company, limit, used, needed, what), new Dictionary<string, JsonElement>
        {
            ["reason"] = JsonSerializer.SerializeToElement(Reason),
            ["companyId"] = JsonSerializer.SerializeToElement(companyId),
            ["limitPaise"] = JsonSerializer.SerializeToElement(limit.Value),
            ["usedPaise"] = JsonSerializer.SerializeToElement(used.Value),
            ["remainingPaise"] = JsonSerializer.SerializeToElement(Remaining(limit, used).Value),
            ["neededPaise"] = JsonSerializer.SerializeToElement(needed.Value),
        });
    }

    /// <summary>
    /// A change to something already billed to the company (an amended booking, newly routed group lines): refused only when it raises the
    /// exposure (<paramref name="before"/> → <paramref name="after"/>) and the result is beyond the limit; a change that lowers it always passes.
    /// <paramref name="subject"/> = the booking's / group's exposure after the change; used = after − subject.
    /// </summary>
    public static void EnsureChange(long companyId, string company, Paise limit, Paise before, Paise after, Paise subject, string what)
    {
        if (after <= before) return;
        Ensure(companyId, company, limit, after - subject, subject, what);
    }
}
