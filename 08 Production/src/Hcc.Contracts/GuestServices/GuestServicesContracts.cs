namespace Hcc.Contracts.GuestServices;

// Wire contracts for guest services (module 18). Dates YYYY-MM-DD, event times ISO-8601 UTC, versions = base64 rowversion.
// No money fields except the recovery discount amount (paise); the discount itself is posted by Billing, never here.
// Claimant ID notes hold only the last characters of an ID document (DEC-068) - never a full number.

// ── Service requests ──

/// <summary>LogServiceRequest K (PERM-GST-002). Only for an in-house stay. <c>Time</c> (HH:mm, property local time) is required for a wake-up call.</summary>
public sealed record LogServiceRequestRequest(long StayId, string RequestType, string? Detail, string? Dept, string? Priority, string? Time, int? AssigneeId);

public sealed record AssignServiceRequestRequest(long RequestId, int AssigneeId, string? Dept, string ExpectedVersion);

public sealed record StartServiceRequestRequest(long RequestId, string ExpectedVersion);

/// <summary>Completion needs a short guest-facing note (3–200 characters).</summary>
public sealed record CompleteServiceRequestRequest(long RequestId, string Note, string ExpectedVersion);

public sealed record CancelServiceRequestRequest(long RequestId, string Reason, string ExpectedVersion);

public sealed record ServiceRequestResult(long RequestId, string Status, string Dept, int? AssigneeId, string Priority, string DueUtc, string? ScheduledUtc, string Version);

public sealed record SlaView(string Tone, string Label, int? MinutesLeft);

public sealed record ServiceRequestView(long RequestId, long StayId, string RoomNo, string GuestName, string RequestType, string Detail, string Dept,
    int? AssigneeId, string Priority, string Status, string CreatedUtc, string DueUtc, string? ScheduledUtc, string? StartedUtc, string? DoneUtc,
    string? GuestNote, string? CancelReason, SlaView Sla, string Version);

// ── Complaints ──

public sealed record LogComplaintRequest(long? StayId, string? GuestName, string Category, string Severity, string Description, int? OwnerId);

public sealed record UpdateComplaintRequest(long ComplaintId, int? OwnerId, string? Severity, string? RootCause, string? RecoveryAction, string ExpectedVersion);

public sealed record ResolveComplaintRequest(long ComplaintId, string? RootCause, string? RecoveryAction, DateOnly? FollowUpOn, string ExpectedVersion);

/// <summary>A satisfied follow-up closes the complaint (High/Critical need PERM-APR-001); not satisfied re-opens it as Investigating.</summary>
public sealed record RecordComplaintFollowUpRequest(long ComplaintId, string Outcome, bool Satisfied, string ExpectedVersion);

/// <summary>
/// OfferRecoveryDiscount K V. Runs Billing's RequestAdjustment (discount): within the caller's limit the discount is posted, above it an
/// approval is requested (DEC-041, DEC-063). <c>AmountPaise</c> excludes tax effects: Billing posts the tax snapshot.
/// </summary>
public sealed record OfferRecoveryDiscountRequest(long ComplaintId, long AmountPaise, string? Reason, string ExpectedVersion);

public sealed record ComplaintResult(long ComplaintId, string Status, string Severity, int OwnerId, DateOnly? FollowUpOn, string Version);

/// <summary>Outcome Posted | PendingApproval. RecoveryStatus as in <see cref="ComplaintView.RecoveryStatus"/>.</summary>
public sealed record RecoveryResult(long ComplaintId, string Outcome, long? ApprovalId, long? ChargeId, long AmountPaise, string RecoveryStatus, string Version);

public sealed record RecoveryView(long AmountPaise, string Reason, long? ApprovalId, long? ChargeId, string AtUtc, int By);

public sealed record ComplaintFollowUpView(string Outcome, bool Satisfied, string AtUtc, int By);

/// <summary>RecoveryStatus: None | Requesting | Posted | Pending approval | Approved | Rejected.</summary>
public sealed record ComplaintView(long ComplaintId, long? StayId, string? RoomNo, string GuestName, string Category, string Severity, string Description,
    int OwnerId, string Status, string? RootCause, string? RecoveryAction, DateOnly? FollowUpOn, bool FollowUpDue, ComplaintFollowUpView? FollowUp,
    string CreatedUtc, string? ResolvedUtc, string? ClosedUtc, RecoveryView? Recovery, string RecoveryStatus, string Version);

// ── Lost & found ──

public sealed record LogLostItemRequest(string Description, string Category, string FoundWhere, string Storage, string? RoomNo, int? FoundBy);

public sealed record ClaimLostItemRequest(long ItemId, string Name, string Phone, string IdType, string IdNote, string MatchNote, string ExpectedVersion);

public sealed record HandOverLostItemRequest(long ItemId, string? To, string? Note, string ExpectedVersion);

public sealed record DisposeLostItemRequest(long ItemId, string Method, string? Note, string ExpectedVersion);

public sealed record LostItemResult(long ItemId, string Status, bool Valuable, DateOnly DisposeOn, string Version);

public sealed record LostItemClaimView(string Name, string Phone, string IdType, string IdNote, string MatchNote, string AtUtc, int By);

public sealed record LostItemView(long ItemId, string Description, string Category, bool Valuable, string FoundWhere, string? RoomNo, long? StayId,
    string FoundUtc, DateOnly FoundDate, int FoundBy, string Storage, string Status, DateOnly DisposeOn, bool Disposable, LostItemClaimView? Claim,
    string? HandedOverTo, string? DisposalMethod, string Version);

// ── Settings ──

public sealed record SaveGuestServiceSettingsRequest(IReadOnlyDictionary<string, int>? SlaMinutes, int? RetentionDays, int? ValuableRetentionDays, string? ExpectedVersion);

/// <summary><c>Version</c> is null until the settings have been saved once (defaults apply).</summary>
public sealed record GuestServiceSettingsView(IReadOnlyDictionary<string, int> SlaMinutes, int RetentionDays, int ValuableRetentionDays, string? Version);

// ── Lists, history and reports ──

public sealed record GuestServicePage<T>(int Page, int PageSize, int Total, IReadOnlyList<T> Items, bool OwnOnly);

public sealed record HistoryEntry(string AtUtc, int By, string What);

public sealed record ResponseRow(string Key, int Count, int Done, int AvgMinutes, int WithinSla, int Pct, int Open, int Overdue);

public sealed record ResponseTotals(int Done, int Within, int Pct, int AvgMinutes, int Open, int Overdue);

public sealed record ResponseReport(IReadOnlyList<ResponseRow> Rows, ResponseTotals Totals);

public sealed record ComplaintCategoryRow(string Category, int Total, int Open, IReadOnlyDictionary<string, int> BySeverity, int Recovery);

public sealed record LostFoundSummary(int Stored, int Claimed, int HandedOver, int Disposed, int DueDisposal, int Valuables);
