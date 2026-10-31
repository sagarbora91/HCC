namespace Hcc.Contracts.Accounting;

/// <summary>
/// ACC-2 · lane acc2-purchase — outbox events of purchases the accounting consumer books (02 Planning/agents/ACC2-POSTING-CONTRACT.md §3;
/// rulebook C3, C4, C5, C8). Listed in <see cref="AccountingEvents.Consumed"/>. Money paise, dates yyyy-MM-dd, camelCase JSON.
/// </summary>
public static class PurchaseEvents
{
    /// <summary>A vendor payment / advance (Payment: Dr 2200/[vendor] · Cr the tender).</summary>
    public const string VendorPaymentRecorded = "VendorPaymentRecorded";

    /// <summary>A vendor payment voided on a later business day (the reversal on the void date); a same-day void publishes nothing to book.</summary>
    public const string VendorPaymentVoided = "VendorPaymentVoided";

    /// <summary>The reverse-charge GST of a posted bill or of its reversal / return record (Journal: Dr category or ITC · Cr 2140).</summary>
    public const string ExpenseRcmPosted = "ExpenseRcmPosted";

    /// <summary>A goods return / vendor credit note posted (negative Purchase: Dr 2200/[vendor] · Cr category · Cr ITC taken).</summary>
    public const string ExpenseReturned = "ExpenseReturned";

    public static readonly IReadOnlyList<string> All = [VendorPaymentRecorded, VendorPaymentVoided, ExpenseRcmPosted, ExpenseReturned];
}

/// <summary>
/// <see cref="PurchaseEvents.VendorPaymentRecorded"/> / <see cref="PurchaseEvents.VendorPaymentVoided"/>: the payment, its vendor (code + name), the
/// tender, the amount (positive) and what was allocated to bills (the rest is an advance). A void carries <c>VoidDate</c> = its business date.
/// </summary>
public sealed record VendorPaymentEvent(long PaymentId, DateOnly BusinessDate, string VendorCode, string Vendor, string Tender, long AmountPaise, long AllocatedPaise,
    string? Reference = null, DateOnly? VoidDate = null, string? VoidReason = null);

/// <summary><see cref="PurchaseEvents.ExpenseRcmPosted"/>: the record (bill, reversal or return; negative amounts on the last two), its category,
/// the RCM rate and whether ITC is allowed on the RCM GST; <c>GstKind</c> inter → the ITC goes to 1330.</summary>
public sealed record ExpenseRcmPostedEvent(long ExpenseId, DateOnly BusinessDate, string Category, string Vendor, long AmountPaise, int RcmBps, bool ItcAllowed,
    string? GstKind = null, long? ReversalOf = null, long? ReturnOf = null);

/// <summary><see cref="PurchaseEvents.ExpenseReturned"/>: a return record posted against <c>ReturnOf</c> (amounts POSITIVE: the amount returned and
/// the GST reversed; <c>Itc</c> "allowed" = the ITC taken on the bill is reversed). Funding = the credit method (vendor payable).</summary>
public sealed record ExpenseReturnedEvent(long ExpenseId, long ReturnOf, DateOnly BusinessDate, string Category, string Funding, string Vendor, long AmountPaise,
    long? GstPaise = null, string? Itc = null, string? GstKind = null, long? TdsPaise = null);
