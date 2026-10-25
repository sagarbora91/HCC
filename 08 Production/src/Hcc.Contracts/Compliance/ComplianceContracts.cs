namespace Hcc.Contracts.Compliance;

// Wire contracts for the Compliance calendar (W2-5). Dates are YYYY-MM-DD; versions are base64 rowversion; actor comes from the session.

/// <summary>Due rule on the wire: <c>type</c> = "day" | "month-end" | "fixed" (annual: month + day).</summary>
public sealed record DueRuleDto(string Type, int? Day = null, int? Month = null, int? SpecialMonth = null, int? SpecialDay = null);

public sealed record MarkFiledRequest(long ObligationId, string Period, string Reference);

public sealed record MarkNotApplicableRequest(long ObligationId, string Period, string Reason);

public sealed record ReopenFilingRequest(long ObligationId, string Period, string Reason);

/// <summary>SaveComplianceObligation: no <c>Id</c> = add (needs <c>Kind</c>); with <c>Id</c> = edit (needs <c>ExpectedVersion</c>).</summary>
public sealed record SaveObligationRequest(long? Id, string? Kind, string Name, string? Form, string? Owner, DueRuleDto Rule, string? EffectiveFrom = null, string? Note = null, string? ExpectedVersion = null);

public sealed record ToggleObligationRequest(long ObligationId, string ExpectedVersion);

public sealed record SaveSettingsRequest(string? CaName, string? CaEmail, int? DueSoonDays, string? ExpectedVersion = null);

public sealed record GenerateCaPackRequest;

/// <summary>Creates the default SCC-derived obligations once. Tracking starts at the given month / financial year (default: the latest finished period).</summary>
public sealed record SeedDefaultsRequest(string? TrackedFromMonth = null, string? TrackedFromFy = null);

public sealed record FilingResult(long ObligationId, string Period, string Status, string DueDate, int DaysLate, string AtUtc);

public sealed record ReopenResult(long ObligationId, string Period, string WasStatus);

public sealed record SaveObligationResult(long ObligationId, string Version, string Outcome, string TrackedFrom, bool RuleChanged);

public sealed record ToggleObligationResult(long ObligationId, bool Active, string Version);

public sealed record SettingsDto(string CaName, string CaEmail, int DueSoonDays, string Version);

public sealed record CaPackResult(long PackId, string To, string ToName, int Overdue, int Open, int Lines, string AtUtc);

public sealed record SeedDefaultsResult(int Created);

public sealed record FilingDto(string Status, int By, string AtUtc, string? Reference, string? Reason);

public sealed record CalendarItemDto(long ObligationId, string Kind, string Name, string Form, string Owner, string Period, string PeriodLabel,
    string DueDate, int Days, string Status, string StatusText, FilingDto? Filing);

public sealed record CalendarCountsDto(int Overdue, int Due, int Upcoming, int Filed, int NotApplicable, int Open);

public sealed record CalendarView(string AsOf, CalendarCountsDto Counts, IReadOnlyList<CalendarItemDto> Items, SettingsDto Settings, string Note);

public sealed record DueRuleView(string EffectiveFrom, string Type, string Text, int? Day, int? Month, int? SpecialMonth, int? SpecialDay);

public sealed record ObligationDto(long ObligationId, string Kind, string Name, string Form, string Frequency, string Owner, string TrackedFrom, bool Active,
    string? Note, string Version, IReadOnlyList<DueRuleView> Rules);

public sealed record CaPackDto(long PackId, string AtUtc, int By, string To, string ToName, int Overdue, int Due, int Filed, int NotApplicable, IReadOnlyList<string> Lines);

public sealed record BadgeResult(int Count);
