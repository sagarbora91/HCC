namespace Hcc.Contracts.Reminders;

/// <summary>SaveReminder K [V]: create (ReminderId null) or edit an open reminder. Date/Time as yyyy-MM-dd / HH:mm; Role = "all" or a role code (OWN, MGR, REC …).</summary>
public sealed record SaveReminderRequest(long? ReminderId, string Subject, string Date, string? Time = null, int? LeadDays = null, string? Role = null, string? Note = null, string? Repeat = null);

/// <summary>MarkReminderDone K — a repeating reminder rolls to its next date and stays open.</summary>
public sealed record ReminderIdRequest(long ReminderId);

/// <summary>DismissReminder K — a reason of 3–200 characters is required.</summary>
public sealed record DismissReminderRequest(long ReminderId, string Reason);

public sealed record ReminderView(long ReminderId, string Subject, string Date, string? Time, int LeadDays, string Role, string? Note, string Repeat, string Status,
    string? NextDate, string DueDate, string ShowFrom, int CreatedBy, string CreatedUtc, int? DoneBy, string? DoneUtc);

/// <summary>GET /reminders?bucket=today|upcoming|done — reminders the caller may see; today = open and inside the alert days (overdue included).</summary>
public sealed record ReminderList(string Bucket, string BusinessDate, IReadOnlyList<ReminderView> Items);
