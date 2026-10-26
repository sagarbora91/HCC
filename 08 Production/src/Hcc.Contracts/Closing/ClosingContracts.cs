using Hcc.Contracts.Reporting;

namespace Hcc.Contracts.Closing;

// Wire contracts for the business-day close (contracts package §3 RunBusinessDayClose; WF-DAY-001; DEC-042 business date,
// DEC-065 cash-book day lock). Dates YYYY-MM-DD; money integer paise.

/// <summary>
/// POST /api/v1/commands/RunBusinessDayClose (K). Resumable: preflight → room nights (P4 PostRoomNight) → shift/cash checks →
/// close + snapshot (late-write barrier) → next business date. A rerun after a crash continues where the last attempt stopped and
/// never posts a night twice. <c>RunId</c> is the client's id for this close (1–40 characters).
/// </summary>
public sealed record RunBusinessDayCloseRequest(DateOnly BusinessDate, string RunId);

/// <summary>One persisted step of a close run.</summary>
/// <param name="Status">Done or Blocked.</param>
/// <param name="Detail">Step result as JSON (counts, blockers).</param>
public sealed record CloseStepView(string Step, string Status, int Attempt, string Detail, DateTime UpdatedUtc);

/// <summary>A run of the close for one business date (one per date; attempts = how many times it was started or resumed).</summary>
/// <param name="Status">Running · Blocked (a check failed; fix it and run again) · Closed (closed, next day pending) · Completed.</param>
public sealed record CloseRunResult(long RunId, string RunRef, DateOnly BusinessDate, string Status, string CurrentStep, int Attempts,
    DateOnly? NextBusinessDate, long? SnapshotId, IReadOnlyList<CloseStepView> Steps);

/// <summary>One item behind a preflight check (with the record to open).</summary>
public sealed record PreflightItem(string Label, string? Ref);

/// <summary>A preflight check (prototype closingPreflight); a check with items blocks the close at its step.</summary>
public sealed record PreflightCheck(string Id, string Title, string Hint, bool Blocking, IReadOnlyList<PreflightItem> Items);

/// <summary>GET /api/v1/closing/preflight?date= (PERM-CLS-001). All checks in close order.</summary>
public sealed record PreflightResult(DateOnly BusinessDate, bool Closed, IReadOnlyList<PreflightCheck> Checks);

/// <summary>
/// The immutable closing record of one business date (prototype ClosingRecord). <see cref="Figures"/> is exactly the daily report
/// of that date at close; point-in-time values (deposits held) sit outside it.
/// </summary>
public sealed record ClosingRecord(DateOnly BusinessDate, int ClosedBy, DateTime ClosedUtc, long RunId, int Attempts, bool NightAuditResumed,
    int NightAuditStays, long DepositsHeldAtClosePaise, IReadOnlyList<string> Exceptions, DailyReport Figures);

/// <summary>GET /api/v1/closing/snapshots/{date} (PERM-RPT-002). <c>Sha256</c> = hex of the stored canonical JSON.</summary>
public sealed record ClosingSnapshotView(long SnapshotId, string Sha256, ClosingRecord Record);

/// <summary>POST /api/v1/commands/LockCashBookDay (K). A checker (not the counter, the difference approver or the closer) locks a closed day.</summary>
public sealed record LockCashBookDayRequest(DateOnly BusinessDate);

/// <summary>POST /api/v1/commands/ReopenCashBookDay (K). Owner only, with a reason (audited).</summary>
public sealed record ReopenCashBookDayRequest(DateOnly BusinessDate, string Reason);

public sealed record CashBookEventView(string Action, int PrincipalId, DateTime AtUtc, string? Reason);

/// <summary>Cash-book day: lock state and history (DEC-065), and the day's drawer from the closing snapshot.</summary>
public sealed record CashBookDayResult(DateOnly BusinessDate, bool Closed, bool Locked, int? LockedBy, DateTime? LockedUtc,
    IReadOnlyList<CashBookEventView> History, DrawerFigures? Drawer);

/// <summary>POST /api/v1/commands/PostRoomNights (K). Posts tonight's room line for every in-house stay of the current business date, once each (the same step the day close runs; no close, no date change).</summary>
public sealed record PostRoomNightsRequest(DateOnly BusinessDate);

/// <summary>Stays that needed tonight's night, how many were posted by this call and how many had it already.</summary>
public sealed record PostRoomNightsResult(DateOnly BusinessDate, int Stays, int PostedNow, int AlreadyPosted);

/// <summary>GET /api/v1/closing/business-date (any session). The property's current business date (DEC-042) and whether it is already closed (next day not yet opened).</summary>
public sealed record BusinessDateView(DateOnly BusinessDate, bool Closed);

/// <summary>GET /api/v1/closing/records?from=&amp;to= (PERM-RPT-002): the closing records of the closed dates in the range, oldest first.</summary>
public sealed record ClosingRecordsView(DateOnly From, DateOnly To, IReadOnlyList<ClosingSnapshotView> Items);
