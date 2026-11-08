using System.Text.Json;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Groups;
using Hcc.Domain.Money;

namespace Hcc.Domain.Billing;

/// <summary>
/// F8 split folio rules (DEC-152, SF-CONTRACT §2.2 / §4 / §7). Pure: the application service loads the snapshot, calls these, then writes.
/// Refusals use the existing error codes with <c>details.reason</c> = one of the <c>SF_*</c> reasons (MEAS pattern).
/// </summary>
public static class SplitFolioRules
{
    public const string FolioLimit = "SF_FOLIO_LIMIT", RouteTaken = "SF_ROUTE_TAKEN", CompanyFolio = "SF_COMPANY_FOLIO",
        CompanyMismatch = "SF_COMPANY_MISMATCH", GroupMember = "SF_GROUP_MEMBER", PayerKindReason = "SF_PAYER_KIND",
        FolioNotEmpty = "SF_FOLIO_NOT_EMPTY", FolioInvoiced = "SF_FOLIO_INVOICED", SplitBillCorrection = "SF_SPLIT_BILL_CORRECTION",
        DepositFolio = "SF_DEPOSIT_FOLIO", FolioUnknown = "SF_FOLIO_UNKNOWN";

    /// <summary>D7 refusal wording: <c>CompanyCreditRules.Message(..., what)</c>.</summary>
    public const string CreditWhat = "split folio";

    public static readonly string[] PayerKinds = ["guest", "company", "group", "udhaar"];

    public static HccException Refuse(HccErrorCode code, string reason, string message, IDictionary<string, object?>? extra = null)
    {
        var d = new Dictionary<string, JsonElement> { ["reason"] = JsonSerializer.SerializeToElement(reason) };
        if (extra is not null) foreach (var (k, v) in extra) d[k] = JsonSerializer.SerializeToElement(v);
        return new HccException(code, message, d);
    }

    /// <summary>Checks for OpenFolio (§4): stay Confirmed or InHouse, not a group member, ≤ 4 live folios, payer kind guest / company / udhaar,
    /// one company folio, company = the booking's company (when it names one), exclusive routes. Returns the seq for the new folio
    /// (the lowest never-used seq: a removed letter is not reused).</summary>
    public static int EnsureCanOpen(FolioRoutingSnapshot snap, string status, bool groupMember, string payerKind, long? companyId, long? bookingCompanyId,
        string? customerKey, IReadOnlyCollection<FolioRouteCategory> routes)
    {
        ArgumentNullException.ThrowIfNull(snap);
        ArgumentNullException.ThrowIfNull(routes);
        if (status is not ("Confirmed" or "InHouse"))
            throw Refuse(HccErrorCode.INVALID_TRANSITION, "INVALID_TRANSITION", $"A folio can be opened only on a Confirmed or In-house stay (this one is {status}).");
        if (groupMember)
            throw Refuse(HccErrorCode.INVALID_TRANSITION, GroupMember, "This stay is a member of a group: its routing belongs to the group master folio (Groups & corporate).");
        if (payerKind is not ("guest" or "company" or "udhaar"))
            throw Refuse(HccErrorCode.VALIDATION, PayerKindReason, "A split folio is paid by a guest, a company or an Udhaar credit guest.");
        if (snap.Folios.Count >= FolioRouting.MaxFolios || snap.Live.Count() >= FolioRouting.MaxFolios)
            throw Refuse(HccErrorCode.INVALID_TRANSITION, FolioLimit, $"A stay has at most {FolioRouting.MaxFolios} folios.");
        if (payerKind == "company")
        {
            if (companyId is null) throw Refuse(HccErrorCode.VALIDATION, PayerKindReason, "Choose the company account that pays this folio.");
            if (snap.Live.Any(f => f.PayerKind == "company"))
                throw Refuse(HccErrorCode.INVALID_TRANSITION, CompanyFolio, "This stay already has a company folio.");
            if (bookingCompanyId is { } bc && bc != companyId)
                throw Refuse(HccErrorCode.INVALID_TRANSITION, CompanyMismatch, "The booking is billed to another company account.");
        }
        if (payerKind == "udhaar" && string.IsNullOrWhiteSpace(customerKey))
            throw Refuse(HccErrorCode.VALIDATION, PayerKindReason, "Choose the Udhaar credit guest who pays this folio.");
        EnsureRoutesFree(snap, routes, exceptSeq: null);
        return snap.Folios.Count == 0 ? 2 : snap.Folios.Max(f => f.Seq) + 1;
    }

    /// <summary>Routes are exclusive across the split folios B–D (folio A takes every category no other folio holds).</summary>
    public static void EnsureRoutesFree(FolioRoutingSnapshot snap, IReadOnlyCollection<FolioRouteCategory> routes, int? exceptSeq)
    {
        ArgumentNullException.ThrowIfNull(snap);
        ArgumentNullException.ThrowIfNull(routes);
        foreach (var c in routes)
        {
            if (!snap.Routes.TryGetValue(c, out var fid)) continue;
            var holder = snap.Folios.FirstOrDefault(f => f.FolioId == fid);
            if (holder is null || holder.Removed || holder.Seq == FolioRouting.PrimarySeq || holder.Seq == exceptSeq) continue;
            throw Refuse(HccErrorCode.VALIDATION, RouteTaken, $"{FolioRouting.Code(c)} is already routed to folio {holder.Letter}.");
        }
    }

    /// <summary>SetFolioRouting: the folio must be live; routes exclusive.</summary>
    public static FolioRef EnsureCanRoute(FolioRoutingSnapshot snap, int seq, IReadOnlyCollection<FolioRouteCategory> routes)
    {
        var f = Live(snap, seq);
        EnsureRoutesFree(snap, routes, seq);
        return f;
    }

    /// <summary>RemoveFolio: not A, live, no charge and no receipt, not invoiced.</summary>
    public static FolioRef EnsureCanRemove(FolioRoutingSnapshot snap, int seq, int charges, int receipts, bool invoiced)
    {
        if (seq == FolioRouting.PrimarySeq) throw Refuse(HccErrorCode.INVALID_TRANSITION, FolioNotEmpty, "Folio A (the guest's) cannot be removed.");
        var f = Live(snap, seq);
        if (invoiced) throw Refuse(HccErrorCode.INVALID_TRANSITION, FolioInvoiced, $"Folio {f.Letter} is invoiced.");
        if (charges > 0 || receipts > 0)
            throw Refuse(HccErrorCode.INVALID_TRANSITION, FolioNotEmpty, $"Folio {f.Letter} has lines or receipts; only an empty folio is removed.");
        return f;
    }

    /// <summary>A live folio by seq, else NOT_FOUND with SF_FOLIO_UNKNOWN.</summary>
    public static FolioRef Live(FolioRoutingSnapshot snap, int seq)
    {
        ArgumentNullException.ThrowIfNull(snap);
        return snap.Folios.FirstOrDefault(f => f.Seq == seq && !f.Removed)
            ?? throw Refuse(HccErrorCode.NOT_FOUND, FolioUnknown, $"This stay has no folio {(seq is >= 1 and <= 4 ? FolioRouting.Letter(seq) : seq.ToString(System.Globalization.CultureInfo.InvariantCulture))}.");
    }

    /// <summary>Receipt-kind table (§3): deposit, deposit-return, deposit-apply and OTA credit on A only; company credit on the company folio
    /// (single-folio stay: A); guest credit on A or an udhaar folio; no cash on a company folio (DEC-076).</summary>
    public static void EnsureReceiptFolio(FolioRoutingSnapshot snap, string receiptKind, string? method, int seq)
    {
        var f = Live(snap, seq);
        switch (receiptKind)
        {
            case "deposit" or "deposit-return" or "deposit-apply" or "ota-credit" when seq != FolioRouting.PrimarySeq:
                throw Refuse(HccErrorCode.VALIDATION, DepositFolio, "Deposits and OTA credit are held on folio A only.");
            case "company-credit" when snap.IsSplit && f.PayerKind != "company":
                throw Refuse(HccErrorCode.VALIDATION, CompanyFolio, "Company credit goes on the company folio only.");
            case "guest-credit" when seq != FolioRouting.PrimarySeq && f.PayerKind != "udhaar":
                throw Refuse(HccErrorCode.VALIDATION, PayerKindReason, "Guest credit goes on folio A or an Udhaar folio.");
        }
        if (f.PayerKind == "company" && string.Equals(method, "Cash", StringComparison.OrdinalIgnoreCase))
            throw Refuse(HccErrorCode.VALIDATION, CompanyFolio, "A company folio is not paid in cash (bank transfer, UPI or card).");
    }

    /// <summary>LF A2 bill correction is refused on a split stay (SFQ-11): use a credit note per folio.</summary>
    public static void EnsureNoBillCorrection(FolioRoutingSnapshot snap)
    {
        ArgumentNullException.ThrowIfNull(snap);
        if (snap.IsSplit) throw Refuse(HccErrorCode.INVALID_TRANSITION, SplitBillCorrection, "This stay has a split bill: correct it with a credit note on the folio concerned.");
    }

    // ---- D7 (SF-CONTRACT §7) ----

    /// <summary>Exposure of a company folio: posted lines of the folio + their GST + the agreed room nights not yet posted (only when room is
    /// routed to it) with GST − receipts on that folio (payments − refunds, company credit already moved off). Never below 0. Deposits are on A.</summary>
    public static Paise FolioExposure(Paise postedNetAndTax, Paise unpostedRoomNetAndTax, bool roomRouted, Paise received)
    {
        var e = postedNetAndTax + (roomRouted ? unpostedRoomNetAndTax : Paise.Zero) - received;
        return e < Paise.Zero ? Paise.Zero : e;
    }

    /// <summary>Night postings are never refused by D7 (they would stop the day close); every other line onto a company folio is.</summary>
    public static bool IsNightPosting(ChargeKind kind, bool fromNightRun) => fromNightRun && kind == ChargeKind.Room;

    /// <summary>D7 check of a command that adds <paramref name="needed"/> to a company folio: refused when used + needed &gt; limit
    /// (<c>used</c> = the company's exposure without this stay's company folio). Same text and details as <see cref="CompanyCreditRules"/>.</summary>
    public static void EnsureCompanyCredit(long companyId, string company, Paise limit, Paise used, Paise needed)
        => CompanyCreditRules.Ensure(companyId, company, limit, used, needed, CreditWhat);
}
