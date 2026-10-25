namespace Hcc.Contracts.Customers;

// Wire contracts for Customers / CRM (customer 360, W3-5). Money is long paise and only present for readers who may see money (null otherwise).
// Phone and e-mail are masked for roles without PERM-GST-002; no ID document data of any kind is part of these views (DEC-068).

public sealed record CustomerStayView(long ReservationId, string BookingNo, string Status, string Arrival, string Departure, int Nights, string? Room, long? TotalPaise);

public sealed record CustomerSummary(string Key, string Name, string Phone, string? Email, bool Masked, int Stays, int CompletedStays, int Nights, long? LifetimePaise, string? LastStay, bool InHouse, IReadOnlyList<string> Tags);

public sealed record CustomerList(IReadOnlyList<CustomerSummary> Customers, int Total, int Page, int PageSize);

public sealed record CustomerConsentView(bool WhatsApp, bool Sms, bool Email, bool OptOut);

public sealed record CustomerNoteView(long NoteId, string Body, int AuthorId, string CreatedUtc);

public sealed record CustomerProfile(
    string Key, string Name, string Phone, string? Email, bool Masked, IReadOnlyList<long> GuestIds, IReadOnlyList<CustomerStayView> Stays,
    int CompletedStays, int Nights, long? LifetimePaise, long? AvgRatePaise, string? FirstStay, string? LastStay, CustomerStayView? Current, IReadOnlyList<CustomerStayView> Upcoming,
    int Cancelled, IReadOnlyList<string> TopRooms, string? Preferences, string? PreferencesVersion, IReadOnlyList<string> Tags, IReadOnlyList<CustomerNoteView> Notes,
    CustomerConsentView Consent, IReadOnlyList<string> MergedFrom);

public sealed record DuplicateCandidate(string Reason, IReadOnlyList<CustomerSummary> Customers, string SuggestedSurvivor);

public sealed record DuplicateList(IReadOnlyList<DuplicateCandidate> Groups);

public sealed record MergeView(long MergeId, string SourceKey, string TargetKey, string SourceName, string TargetName, string Reason, string Status, int ProposedBy, string ProposedUtc, int? DecidedBy, string? DecidedUtc, string? DecisionNote, string Version);

public sealed record MergeList(IReadOnlyList<MergeView> Merges);

/// <summary>ProposeCustomerMerge K: <c>SourceKey</c> (the duplicate) is folded into <c>TargetKey</c> once another authorised person approves.</summary>
public sealed record ProposeCustomerMergeRequest(string SourceKey, string TargetKey, string Reason);

/// <summary>DecideCustomerMerge K V: approve or reject; never by the proposer.</summary>
public sealed record DecideCustomerMergeRequest(long MergeId, bool Approve, string ExpectedVersion, string? Note = null);

public sealed record AddCustomerTagRequest(string CustomerKey, string Tag);

public sealed record RemoveCustomerTagRequest(string CustomerKey, string Tag);

public sealed record AddCustomerNoteRequest(string CustomerKey, string Body);

/// <summary>SetCustomerPreferences K [V]: empty text clears the override (the guest profiles' preferences show again).</summary>
public sealed record SetCustomerPreferencesRequest(string CustomerKey, string Preferences, string? ExpectedVersion = null);

/// <summary>SetCustomerConsent K: marketing consent flags (stored by Messaging per guest profile); null keeps the current value.</summary>
public sealed record SetCustomerConsentRequest(string CustomerKey, bool? WhatsApp = null, bool? Sms = null, bool? Email = null, bool? OptOut = null);

public sealed record CustomerChangeResult(string CustomerKey, string Summary);
