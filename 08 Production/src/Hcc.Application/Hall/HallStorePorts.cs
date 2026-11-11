using Hcc.Application.Commands;

namespace Hcc.Application.Hall;

// ── F6 lane H3: non-money commands (enquiries, halls master, print log) and the reads (venues, calendar, enquiries, quotations, events, snapshot) ──

public sealed record AddHallEnquiryRequest(string Name, string Phone, string EventType, IReadOnlyList<string> Dates, string Slot, int Pax, string Source, string FollowUp,
    int? VenueId = null, string? Note = null, long? GuestId = null, long? CompanyId = null);

/// <summary>Only the fields that are given change; <see cref="Status"/> may only reopen (OPEN) or mark QUOTED — Mark lost has its own command, a quotation accept wins the enquiry.</summary>
public sealed record UpdateHallEnquiryRequest(long EnquiryId, string? FollowUp = null, string? Note = null, int? Pax = null, int? VenueId = null, IReadOnlyList<string>? Dates = null,
    string? Slot = null, string? Status = null, int? ExpectedVersion = null);

public sealed record LoseHallEnquiryRequest(long EnquiryId, string ReasonId, string? Note = null);

/// <summary>Soft hold (Manager, 1–3 days, warns but never blocks); <see cref="Release"/> removes it.</summary>
public sealed record HoldHallEnquiryRequest(long EnquiryId, int? VenueId = null, int Days = 3, bool Release = false);

public sealed record HallVenueSlotIn(string Slot, string From, string To, long RatePaise, int? MinPlates = null);

public sealed record UpsertHallVenueRequest(string Code, string Name, string Kind, IReadOnlyList<HallVenueSlotIn> Slots, int? VenueId = null, IReadOnlyDictionary<string, int>? Capacity = null,
    int? MinPlates = null, string? GstClass = null, IReadOnlyList<int>? CombinedOf = null, bool Active = true, int? ExpectedVersion = null);

public sealed record LogHallPrintRequest(string DocKind, long RefId, int? Version = null, bool WithPrices = true);

public sealed record HallCommandResult(long Id, string Status, string Message);

public static class HallLostReasons
{
    /// <summary>The lost reasons of the contract seed (the H2 table <c>hall.lost_reason</c> supersedes this list when it exists); the id is stored on the enquiry.</summary>
    public static readonly IReadOnlyList<HallLostReasonView> All =
    [
        new("LR-PRICE", "Price too high", false), new("LR-DATE", "Date not available", false), new("LR-OTHERVENUE", "Chose another venue", false),
        new("LR-CANCELLED", "Event cancelled", false), new("LR-NORESPONSE", "No response", false), new("LR-OTHER", "Other", true),
    ];
}

// ── views (money fields are null for a caller without PERM-HAL-002 view: DEC-061, housekeeping / maintenance never receive *Paise) ──

public sealed record HallMenuView(int MenuId, string Code, string Name, long? PerPlatePaise, bool Veg, bool Active, int Version, IReadOnlyList<string> Items);

public sealed record HallTncClauseView(int N, string Heading, string Text);

public sealed record HallTncVersionView(int V, string EffectiveFrom, IReadOnlyList<HallTncClauseView> Clauses);

public sealed record HallTncTemplateView(int TemplateId, string Code, string EventType, IReadOnlyList<HallTncVersionView> Versions);

public sealed record HallLostReasonView(string Id, string Label, bool NeedsNote);

public sealed record HallSlotView(string Slot, string From, string To, long? RatePaise, int? MinPlates);

public sealed record HallVenueView(int VenueId, string Code, string Name, string Kind, bool Active, int Version, IReadOnlyDictionary<string, int> Capacity, IReadOnlyList<int> CombinedOf,
    int? MinPlates, string GstClass, IReadOnlyList<HallSlotView> Slots);

public sealed record HallEnquiryView(long EnquiryId, string Name, string Phone, string EventType, IReadOnlyList<string> Dates, string Slot, int Pax, int? VenueId, string Source,
    string FollowUp, string? Note, string Status, string? LostReasonId, string? LostNote, int? SoftVenueId, string? SoftUntil, int Version, long? GuestId, long? CompanyId, string CreatedUtc);

public sealed record HallCalendarRowView(string Date, int VenueId, string Slot, string Kind, string Ref, string Label, string Status);

public sealed record HallLineView(int Seq, string Kind, string? RefId, string Desc, int Qty, string Unit, long? RatePaise, long? ListRatePaise, long? DiscountPaise, string GstClass, int? TaxBps, bool Extra);

public sealed record HallAdvanceView(int Seq, string Label, int? PctBps, long? AmountPaise, string Due, long? PaidPaise);

public sealed record HallQuotationVersionView(int V, string Status, string EventType, IReadOnlyList<string> Dates, string Slot, int VenueId, string Setup, int Pax, int GuaranteedPax,
    string ValidUntil, string CreatedUtc, string? SentUtc, string? OverrideReason, IReadOnlyList<HallLineView> Lines, IReadOnlyList<HallAdvanceView> Advance,
    string? TncTemplateCode = null, int? TncV = null); // FX-SMALL: the T&C version a sent quotation was locked to (null while a draft)

public sealed record HallQuotationView(long QuotationId, string Number, string Fy, long EnquiryId, int CurrentVersion, string CustomerName, string CustomerPhone, long? CompanyId,
    IReadOnlyList<HallQuotationVersionView> Versions);

public sealed record HallReceiptView(long ReceiptId, string Date, string Kind, string? Tender, long? CompanyId, bool Udhaar, string? Reference, long? AmountPaise, string AtUtc);

public sealed record HallEventView(long EventId, long QuotationId, int Version, int VenueId, string EventDate, IReadOnlyList<string> Dates, string Slot, string EventType, string Status,
    int GuaranteedPax, int? ActualPax, string CustomerName, string CustomerPhone, long? CompanyId, long? BlockId, string? InvoiceNo, string? InvoiceDate, long? TotalPaise,
    long? TaxablePaise, long? CgstPaise, long? SgstPaise, long? AdvanceAppliedPaise, long? BalancePaise, string? SettleMode, string? CancelDate, long? CancelKeptPaise,
    long? CancelRefundPaise, string? RefundState, string? CancelReason, int? BilledPax, IReadOnlyList<HallLineView> Lines, IReadOnlyList<HallAdvanceView> Advance,
    IReadOnlyList<HallReceiptView> Receipts, string CreatedUtc, long? InvoiceId = null);

public sealed record HallSnapshotView(bool SeesMoney, IReadOnlyList<HallVenueView> Venues, IReadOnlyList<HallEnquiryView> Enquiries, IReadOnlyList<HallQuotationView> Quotations,
    IReadOnlyList<HallEventView> Events, IReadOnlyList<HallLostReasonView> LostReasons);

// ── store port ──

public sealed record NewHallEnquiry(int PropertyId, string Name, string Phone, string EventType, IReadOnlyList<DateOnly> Dates, string Slot, int Pax, int? VenueId, string Source,
    DateOnly FollowUp, string? Note, long? GuestId, long? CompanyId, int By, DateTime Utc);

public sealed record HallEnquiryRow(long EnquiryId, string Name, string EventType, IReadOnlyList<DateOnly> Dates, string Slot, int Pax, int? VenueId, DateOnly FollowUp, string? Note,
    string Status, string? LostReasonId, string? LostNote, int? SoftVenueId, DateOnly? SoftUntil, int Version);

public interface IHallStore
{
    Task<long> InsertEnquiryAsync(ICommandTransaction tx, NewHallEnquiry e, CancellationToken ct);

    Task<HallEnquiryRow?> LockEnquiryAsync(ICommandTransaction tx, int propertyId, long enquiryId, CancellationToken ct);

    /// <summary>Writes every mutable column of the row (version + 1); a changed expected version → STALE_VERSION.</summary>
    Task UpdateEnquiryAsync(ICommandTransaction tx, HallEnquiryRow e, int? by, CancellationToken ct);

    /// <summary>Soft holds (open / quoted enquiries, held through <paramref name="businessDate"/>) other than <paramref name="exceptEnquiry"/>: (enquiry id, venue id, slot, dates).
    /// A soft hold is kept as its last business date (stored in <c>soft_until_utc</c> as that date at 00:00): it lasts through that day, as the prototype's does on the simulated clock.</summary>
    Task<IReadOnlyList<(long EnquiryId, int VenueId, string Slot, IReadOnlyList<DateOnly> Dates)>> SoftHoldsAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<DateOnly> dates,
        DateOnly businessDate, long exceptEnquiry, CancellationToken ct);

    Task<bool> VenueActiveAsync(ICommandTransaction tx, int propertyId, int venueId, CancellationToken ct);

    /// <summary>Insert (VenueId null) or update the venue with its slots; a duplicate name / code → VALIDATION; a changed expected version → STALE_VERSION. Returns the venue id and version.</summary>
    Task<(int VenueId, int Version)> UpsertVenueAsync(ICommandTransaction tx, int propertyId, UpsertHallVenueRequest v, IReadOnlyList<string> combinedCodes, int by, DateTime utc, CancellationToken ct);

    /// <summary>Confirmed events ahead on a venue (a hall with any cannot be retired).</summary>
    Task<int> ConfirmedAheadAsync(ICommandTransaction tx, int propertyId, int venueId, DateOnly from, CancellationToken ct);

    Task<string?> VenueCodeAsync(ICommandTransaction tx, int propertyId, int venueId, CancellationToken ct);

    Task InsertPrintLogAsync(ICommandTransaction tx, int propertyId, string docKind, long refId, int? version, bool withPrices, int by, DateTime utc, CancellationToken ct);

    // ── reads (own connection; money fields are null when seesMoney is false) ──
    Task<IReadOnlyList<HallVenueView>> VenuesAsync(int propertyId, bool seesMoney, CancellationToken ct);

    /// <summary>F6-int: menu packages (per-plate price null without the money view) with their POS item codes.</summary>
    Task<IReadOnlyList<HallMenuView>> MenusAsync(int propertyId, bool seesMoney, CancellationToken ct);

    /// <summary>F6-int: T&amp;C templates with every version and its clauses (no money).</summary>
    Task<IReadOnlyList<HallTncTemplateView>> TncTemplatesAsync(int propertyId, CancellationToken ct);

    Task<IReadOnlyList<HallEnquiryView>> EnquiriesAsync(int propertyId, string? status, CancellationToken ct);

    Task<IReadOnlyList<HallCalendarRowView>> CalendarAsync(int propertyId, DateOnly from, DateOnly to, int? venueId, CancellationToken ct);

    Task<IReadOnlyList<HallQuotationView>> QuotationsAsync(int propertyId, string? status, long? quotationId, CancellationToken ct);

    Task<IReadOnlyList<HallEventView>> EventsAsync(int propertyId, string? status, long? eventId, bool seesMoney, CancellationToken ct);
}
