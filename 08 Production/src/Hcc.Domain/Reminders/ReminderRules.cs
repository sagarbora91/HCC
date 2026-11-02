using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Reminders;

/// <summary>LF lf-alerts B4 desk reminders (LF-CONTRACT §9.2; prototype domain/streams/reminders.ts and handlers/reminders.ts). Pure.</summary>
public static partial class ReminderRules
{
    public const string RoleAll = "all";
    public const int DefaultLeadDays = 3;
    public const string StatusOpen = "open", StatusDone = "done", StatusDismissed = "dismissed";
    public static readonly IReadOnlyList<string> Repeats = ["none", "monthly", "yearly"];

    [GeneratedRegex("^([01]\\d|2[0-3]):[0-5]\\d$", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();

    /// <summary>Date + one month / one year; the day is clamped to the month's last day (31 Jan → 28 Feb).</summary>
    public static DateOnly Roll(DateOnly date, string repeat) => repeat switch
    {
        "monthly" => date.AddMonths(1),
        "yearly" => date.AddYears(1),
        _ => date,
    };

    /// <summary>The date a reminder is next due: the rolled date of a repeating one, else its date.</summary>
    public static DateOnly DueDate(DateOnly date, DateOnly? nextDate) => nextDate ?? date;

    /// <summary>From when the desk banner shows it.</summary>
    public static DateOnly ShowFrom(DateOnly date, DateOnly? nextDate, int leadDays) => DueDate(date, nextDate).AddDays(-leadDays);

    /// <summary>Open and inside its alert days on this business date (overdue reminders stay).</summary>
    public static bool IsDue(string status, DateOnly date, DateOnly? nextDate, int leadDays, DateOnly businessDate) =>
        status == StatusOpen && ShowFrom(date, nextDate, leadDays) <= businessDate;

    /// <summary>A normalised, validated reminder; throws VALIDATION with the prototype's messages.</summary>
    public static (string Subject, DateOnly Date, string? Time, int Lead, string Role, string? Note, string Repeat) Validate(
        string? subject, string? date, string? time, int? leadDays, string? role, string? note, string? repeat, int defaultLead, IReadOnlyCollection<string> knownRoles)
    {
        var s = (subject ?? string.Empty).Trim();
        if (s.Length is < 2 or > 80) throw HccException.Validation("Subject: 2–80 characters.");
        if (!DateOnly.TryParseExact(date ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) throw HccException.Validation("Enter the reminder date.");
        var t = string.IsNullOrWhiteSpace(time) ? null : time.Trim();
        if (t is not null && !TimePattern().IsMatch(t)) throw HccException.Validation("Time as HH:mm.");
        var lead = leadDays ?? defaultLead;
        if (lead is < 0 or > 60) throw HccException.Validation("Lead days: 0–60.");
        var r = string.IsNullOrWhiteSpace(role) ? RoleAll : role.Trim();
        if (r != RoleAll && !knownRoles.Contains(r)) throw HccException.Validation("Choose who the reminder is for.");
        var n = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (n is { Length: > 200 }) throw HccException.Validation("Note: at most 200 characters.");
        var rep = string.IsNullOrWhiteSpace(repeat) ? "none" : repeat.Trim();
        if (!Repeats.Contains(rep)) throw HccException.Validation("Repeat: none, monthly or yearly.");
        return (s, d, t, lead, r, n, rep);
    }

    public static string CleanReason(string? reason)
    {
        var r = (reason ?? string.Empty).Trim();
        if (r.Length is < 3 or > 200) throw HccException.Validation("Give the reason (3–200 characters).");
        return r;
    }

    /// <summary>Who may act on (done / dismiss) a reminder: its author, an Owner / Manager, or anyone it is addressed to.</summary>
    public static bool MayAct(int userId, int createdBy, string reminderRole, IReadOnlyCollection<string> userRoles) =>
        userId == createdBy || userRoles.Contains("OWN") || userRoles.Contains("MGR") || reminderRole == RoleAll || userRoles.Contains(reminderRole);
}
