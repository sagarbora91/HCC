namespace Hcc.Contracts.Handover;

// Wire contracts for the Shift handover module (module 19, W2-11). Roles are role codes (OWN MGR REC CSH SUP HKP MNT).
// Money appears only as HandoverCashView.AmountPaise on Front desk notes and is null for readers without PERM-PAY-001 / PERM-RPT-002 (R10).

public static class HandoverDesk
{
    public const string FrontDesk = "Front desk";
    public const string Housekeeping = "Housekeeping";
    public const string Maintenance = "Maintenance";
}

public static class HandoverShift
{
    public const string Morning = "Morning";
    public const string Evening = "Evening";
    public const string Night = "Night";
}

public sealed record HandoverItemInput(string Text, bool Mandatory = false, string? Due = null, bool Photo = false, string? Link = null);

/// <summary>SaveHandoverTemplate K [V]: <c>TemplateId</c> null = create; an edit needs <c>ExpectedVersion</c> (base64 rowversion) and bumps the template version.</summary>
public sealed record SaveHandoverTemplateRequest(long? TemplateId, string Name, string Desk, string Shift, IReadOnlyList<string> Roles, IReadOnlyList<HandoverItemInput> Items, string? ExpectedVersion = null);

public sealed record HandoverTemplateItemView(string Key, string Text, bool Mandatory, string? Due, bool Photo, string? Link);

public sealed record HandoverTemplateView(long TemplateId, string Name, string Desk, string Shift, IReadOnlyList<string> Roles, IReadOnlyList<HandoverTemplateItemView> Items, bool Active, int TemplateVersion, string Version);

/// <summary>ToggleHandoverTemplate K V: active ↔ inactive.</summary>
public sealed record ToggleHandoverTemplateRequest(long TemplateId, string ExpectedVersion);

/// <summary>TickChecklistItem K: creates today's run on the first tick. <c>Done</c> null = only note/photo change. <c>PhotoName</c> is a file name (.jpg/.png/.heic), simulated: no upload.</summary>
public sealed record TickChecklistItemRequest(long TemplateId, string Item, bool? Done = null, string? Note = null, string? PhotoName = null);

public sealed record SubmitChecklistRunRequest(long TemplateId);

/// <summary>ReopenChecklistRun K: Owner/Manager only; <c>Date</c> defaults to the business date.</summary>
public sealed record ReopenChecklistRunRequest(long TemplateId, string Reason, string? Date = null);

public sealed record ChecklistItemView(string Key, string Text, bool Mandatory, string? Due, bool Photo, string? Link, bool Done, int? DoneBy, string? DoneUtc, string? Note, string? PhotoName, bool Overdue);

public sealed record ChecklistProgressView(int Total, int Done, int Pct, int Mandatory, int MandatoryDone, int Overdue, bool Late);

/// <summary>A checklist card: a saved run, or (<c>Started</c> false, <c>RunId</c> null) an active template not yet ticked today.</summary>
public sealed record ChecklistRunView(long? RunId, long TemplateId, int TemplateVersion, string Name, string Desk, string Shift, IReadOnlyList<string> Roles, string Date, string Status, bool Started,
    int? StartedBy, string? SubmittedUtc, int? SubmittedBy, string? ReopenReason, IReadOnlyList<ChecklistItemView> Items, ChecklistProgressView Progress);

public sealed record HandoverCashInput(long AmountPaise, string? Note = null);

/// <summary>SubmitHandoverNote K: one note per desk + business date + outgoing shift (replaced in place until acknowledged). <c>Shift</c> defaults to the running shift.</summary>
public sealed record SubmitHandoverNoteRequest(string Desk, string? Shift = null, string? Issues = null, bool Nothing = false, string? Keys = null, string? Guests = null, HandoverCashInput? Cash = null, IReadOnlyList<string>? Carry = null);

/// <summary>AcknowledgeHandoverNote K: next shift of the desk; never the author.</summary>
public sealed record AcknowledgeHandoverNoteRequest(long NoteId, string? Remark = null);

public sealed record CompleteCarryItemRequest(long NoteId, long CarryId);

public sealed record HandoverCarryView(long CarryId, string Text, bool Done, int? DoneBy, string? DoneUtc);

public sealed record HandoverCashView(long? AmountPaise, string? Note);

public sealed record HandoverNoteView(long NoteId, string Ref, string Desk, string Date, string Shift, string ToShift, int AuthorId, string AuthorName, string WrittenUtc,
    string Issues, bool Nothing, string Keys, string Guests, HandoverCashView? Cash, IReadOnlyList<HandoverCarryView> Carry, string Status, int? AckBy, string? AckUtc, string? AckRemark, string Version);

public sealed record OpenCarryView(long CarryId, long NoteId, string Text, string Desk, string FromName, string Date);

public sealed record HandoverDashboardRow(string Key, string Label, int Runs, int Submitted, int Items, int Done, int Pct, int Overdue, int Late, int NotStarted);

public sealed record HandoverOverdueRow(long? RunId, long TemplateId, string RunName, string Desk, string Shift, string ItemKey, string ItemText, string? Due);

public sealed record HandoverDashboardView(string Date, string By, IReadOnlyList<HandoverDashboardRow> Rows, IReadOnlyList<HandoverOverdueRow> Overdue, int UnacknowledgedNotes);

public sealed record HandoverTemplateList(IReadOnlyList<HandoverTemplateView> Templates);

public sealed record ChecklistList(string Date, string CurrentShift, IReadOnlyList<ChecklistRunView> Runs);

public sealed record HandoverNoteList(IReadOnlyList<HandoverNoteView> Notes, IReadOnlyList<long> IncomingNoteIds);

public sealed record OpenCarryList(IReadOnlyList<OpenCarryView> Items);
