using System.Globalization;

namespace Hcc.Operations.Meas;

/// <summary>
/// F12 MEAS people captures (MEAS-CONTRACT §2): the pure rules of training, staff exit and the anonymous pulse. They mirror the prototype
/// (<c>hrMeas*</c> in staff.ts) so both sides refuse the same input. Endpoints (SaveMeasTraining, SetMeasExit, AddMeasPulse) call these first,
/// then <c>IMeasurementGate.EnsureOnAsync</c> (MEAS_OFF) and the HR-001 permission check, then write meas.training / staff_exit / pulse.
/// </summary>
public static class PeopleRules
{
    public const string TrainingKey = "meas.people.training";
    public const string ExitKey = "meas.people.attrition";
    public const string PulseKey = "meas.people.pulse";
    public const int ExpiryLeadDays = 30;

    public static readonly IReadOnlyList<string> ExitReasons =
    [
        "Resigned", "Better pay", "Relocated", "Personal / family", "Studies", "Health", "Dismissed", "End of contract", "Absconded", "Retired", "Other",
    ];

    /// <summary>An anonymous pulse answer. There is deliberately no staff, user or actor member (PeopleRulesTests proves it by reflection).</summary>
    public sealed record PulseAnswer(string QuarterKey, string DeptCode, int Workload, int Respect, int Tools, int PayFairness, int Recommend);

    /// <summary>Indian FY quarter key of a date: 2026-27-Q1 = April–June 2026.</summary>
    public static string QuarterKey(DateOnly date)
    {
        var fy = date.Month >= 4 ? date.Year : date.Year - 1;
        var q = date.Month >= 4 ? (date.Month - 4) / 3 + 1 : 4;
        return string.Create(CultureInfo.InvariantCulture, $"{fy}-{(fy + 1) % 100:D2}-Q{q}");
    }

    /// <summary>Null when valid, else the message (VALIDATION).</summary>
    public static string? CheckTraining(string course, DateOnly date, DateOnly today, decimal hours, DateOnly? validTo)
    {
        var c = (course ?? "").Trim();
        if (c.Length is < 2 or > 80) return "Enter the course name (2-80 characters).";
        if (date > today) return "The training date cannot be in the future.";
        if (hours <= 0 || hours > 200) return "Hours must be more than 0 and at most 200.";
        if (validTo is { } v && v < date) return "\"Valid to\" cannot be before the training date.";
        return null;
    }

    public static string? CheckExit(string reason, DateOnly exitDate, DateOnly? joined, DateOnly today)
    {
        if (!ExitReasons.Contains(reason)) return "Choose an exit reason from the list.";
        if (joined is { } j && exitDate < j) return "The exit date cannot be before the joining date.";
        if (exitDate > today.AddDays(31)) return "The exit date cannot be more than a month ahead.";
        return null;
    }

    public static string? CheckPulse(int workload, int respect, int tools, int payFairness, int recommend)
    {
        foreach (var v in new[] { workload, respect, tools, payFairness })
            if (v is < 1 or > 5) return "Answer each question with 1 to 5.";
        return recommend is < 0 or > 10 ? "Recommend score must be 0 to 10." : null;
    }
}
