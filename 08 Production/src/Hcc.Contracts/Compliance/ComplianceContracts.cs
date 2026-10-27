namespace Hcc.Contracts.Compliance;

// Wire contracts for the Compliance calendar = SCC's tax module in full (DEC-116). Dates are YYYY-MM-DD; versions are base64 rowversion;
// money in paise; actor comes from the session. Item ids are SCC's card ids ("oc4", "jn_10", custom "cust-12-Nov"); fyStart = 2026 … 2032.

// ── Commands ──

/// <summary>SCC confirmDone: every field optional; filedOn defaults to the business date and may be any date.</summary>
public sealed record MarkDoneRequest(int FyStart, string ItemId, string? FiledOn = null, string? Notes = null, string? EvidenceRef = null, string? ReviewerNote = null);

/// <summary>SCC toggleNA (marking side): no reason.</summary>
public sealed record MarkNotApplicableRequest(int FyStart, string ItemId);

/// <summary>HCC safeguard: Owner only, with a reason; removes NA (keeping a done record underneath) or the done record, and keeps what it removed.</summary>
public sealed record ReopenItemRequest(int FyStart, string ItemId, string Reason);

public sealed record SetFyRequest(int FyStart);

/// <summary>SCC saveFirm: one profile per property (Owner). <c>ExpectedVersion</c> null when none is stored yet.</summary>
public sealed record SaveProfileRequest(string Name, string? Pan, string? Gstin, string? Entity, string? GstType, bool HasEmployees, bool Qrmp, string? Notes,
    string? ExpectedVersion = null);

public sealed record ToggleArchiveRequest(string Key);

public sealed record ToggleCategoryArchiveRequest(string Cat);

/// <summary>SCC saveCustomObligation: no <c>CustomId</c> = add; with it = edit (needs <c>ExpectedVersion</c>). Due day 0 = last day.</summary>
public sealed record SaveCustomObligationRequest(long? CustomId, string? Cat, string Name, string? Freq, int DueDay, IReadOnlyList<string> Months, string? Form = null,
    string? Who = null, string? What = null, string? Why = null, string? Penalty = null, string? Responsible = null, string? Reviewer = null, string? Risk = null,
    int? Buffer = null, IReadOnlyList<string>? Checklist = null, IReadOnlyList<string>? Workflow = null, IReadOnlyList<string>? Evidence = null,
    string? ExpectedVersion = null);

public sealed record DeleteCustomObligationRequest(long CustomId);

/// <summary>An evidence file (HCC limit 5 MB), base64.</summary>
public sealed record AddEvidenceRequest(int FyStart, string ItemId, string FileName, string? ContentType, string Base64);

public sealed record DeleteEvidenceRequest(long EvidenceId);

public sealed record SaveSettingsRequest(string? CaName, string? CaEmail, int? DueSoonDays, string? ExpectedVersion = null);

public sealed record GenerateCaPackRequest;

// ── Command results ──

public sealed record MarkDoneResult(int FyStart, string ItemId, string FiledOn, string? DueDate, int DaysLate);

public sealed record ItemResult(int FyStart, string ItemId, string State);

public sealed record FyResult(int FyStart);

public sealed record VersionResult(string Version);

public sealed record ArchiveResult(string Key, bool Archived);

public sealed record CustomObligationResult(long CustomId, string Id, string Version);

public sealed record DeleteCustomResult(long CustomId, int EvidenceRemoved);

public sealed record EvidenceResult(long EvidenceId, int SizeBytes);

public sealed record SettingsDto(string CaName, string CaEmail, int DueSoonDays, string Version);

public sealed record CaPackResult(long PackId, string To, string ToName, int Overdue, int Open, int Lines, string AtUtc);

// ── Reads ──

public sealed record ProfileDto(string Name, string Pan, string Gstin, string Entity, string GstType, bool HasEmployees, bool Qrmp, string Notes, int? UpdatedBy,
    string? UpdatedUtc, string Version);

public sealed record RecordDto(int FyStart, string ItemId, bool Done, bool Na, string? FiledOn, string? Notes, string? EvidenceRef, string? ReviewerNote, string? DueDate,
    int? DoneBy, string? DoneUtc, int? NaBy, string? NaUtc, string? Legacy);

public sealed record CustomObligationDto(long CustomId, string Id, string Cat, string Name, string Freq, int DueDay, IReadOnlyList<string> Months, string Form, string Who,
    string What, string Why, string Penalty, string Responsible, string Reviewer, string? Risk, int? Buffer, IReadOnlyList<string> Checklist, IReadOnlyList<string> Workflow,
    IReadOnlyList<string> Evidence, int? UpdatedBy, string? UpdatedUtc, string Version);

public sealed record EvidenceDto(long EvidenceId, int FyStart, string ItemId, string FileName, string ContentType, int SizeBytes, int AddedBy, string AddedUtc);

public sealed record ReopenedDto(int FyStart, string ItemId, string Part, bool WasDone, bool WasNa, string? FiledOn, string? Notes, string? EvidenceRef, string? ReviewerNote,
    string? DueDate, int? RecordedBy, string? RecordedUtc, int ReopenedBy, string ReopenedUtc, string Reason);

/// <summary>GET /compliance/state: everything the SCC calendar needs; the client computes cards with the same SCC rules.</summary>
public sealed record ComplianceStateView(string AsOf, ProfileDto? Profile, int FyStart, IReadOnlyList<RecordDto> Records, IReadOnlyList<CustomObligationDto> Custom,
    IReadOnlyList<string> Archived, IReadOnlyList<string> ArchivedCats, IReadOnlyList<EvidenceDto> Evidence, IReadOnlyList<ReopenedDto> Reopened, SettingsDto Settings);

public sealed record CardDto(string ItemId, string Month, string Cat, string Name, string Form, string Due, string? DueDate, bool Urgent, string Freq, bool Custom,
    bool Done, bool Na, int? Days, string? DaysTag, bool Overdue, string Risk, string CashFlow, string Responsible, string Reviewer, int Buffer, string? InternalDate,
    string Escalation, long? PayableRupees, string? Advisory, string? FiledOn, string? EvidenceRef, string? ReviewerNote, int Files);

public sealed record MonthStatsDto(int Total, int Completed, int Pending, int HighRisk, int NotApplicable, int Overdue, string Evidence);

/// <summary>GET /compliance/month?fy&amp;month: SCC's month page computed on the server.</summary>
public sealed record MonthView(string AsOf, int FyStart, string Month, int CalendarYear, string Note, MonthStatsDto Stats, bool OverdueDot, IReadOnlyList<CardDto> Cards);

public sealed record ActionRowDto(string ItemId, string Month, string Name, string Form, string Due, string? DueDate, string Cat, int Days, string When, long? PayableRupees);

public sealed record ActionCenterView(string AsOf, int FyStart, int Overdue, int DueSoon, IReadOnlyList<ActionRowDto> Rows);

public sealed record QuarterScoreDto(string Key, string Label, int Total, int Score);

public sealed record QuarterMonthDto(string Month, int Total, int Done, int Overdue, int Score);

public sealed record FocusItemDto(string ItemId, string Month, string Name, string Due, string Risk, string Responsible, string Reviewer, string Escalation, string? InternalDate);

public sealed record DashboardView(string AsOf, int FyStart, string Quarter, int Total, int Done, int Pending, int Overdue, int HighRisk, int Score,
    IReadOnlyList<QuarterScoreDto> Quarters, IReadOnlyList<QuarterMonthDto> Monthly, int CashHigh, int CashMedium, int CashLow,
    IReadOnlyDictionary<string, int> OwnerLoad, IReadOnlyList<FocusItemDto> Focus, int EvidencePct, int EvidenceDone, int EvidenceSatisfied, IReadOnlyList<string> EvidenceMissing);

public sealed record RegisterCsvView(string FileName, string Csv, int Rows);

public sealed record PrintRowDto(string Due, string Name, string Form, string Freq, string Responsible, string Status);

public sealed record PrintView(string Title, string Month, int CalendarYear, string Firm, string FirmType, string Fy, int Obligations, int Done, int NotApplicable,
    IReadOnlyList<PrintRowDto> Rows, string Footer);

public sealed record PayableMonthDto(long PfPaise, long EsicPaise, long PtPaise, long GstPaise, IReadOnlyList<string> Sources, string AsOf);

/// <summary>GET /compliance/payable?fy: HCC's equivalent of SCC's saagar_tax_payable feed (what each figure uses: <c>Basis</c>).</summary>
public sealed record PayableView(IReadOnlyDictionary<string, PayableMonthDto> Months, string Note, string Basis);

public sealed record EvidenceFileView(string FileName, string ContentType, string Base64);

public sealed record EvidenceZipView(string FileName, int Files, string Base64);

public sealed record CaPackDto(long PackId, string AtUtc, int By, string To, string ToName, int Overdue, int Due, int Filed, int NotApplicable, IReadOnlyList<string> Lines);

public sealed record BadgeResult(int Count);
