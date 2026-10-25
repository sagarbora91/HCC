using System.Globalization;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Compliance;

// W2-5 Compliance calendar — pure rules, no I/O. Ported from the prototype `03 Prototype/src/domain/streams/compliance.ts`
// (due-date rules originally from SCC www/modules/tax: TDS 7th (March deductions 30 April), GSTR-1 11th, GSTR-3B 20th, EPF 15th,
// ESIC 15th, Professional Tax (Maharashtra) last day of the next month, GSTR-9 31 December after the financial year).
// Illustrative — CA to confirm for a hotel. Rules are DATED settings (effective-from), never constants.

public static class ComplianceKinds
{
    public const string Tds = "TDS", Gstr1 = "GSTR-1", Gstr3B = "GSTR-3B", Gstr9 = "GSTR-9", Epf = "EPF", Esic = "ESIC", Pt = "PT";

    public static readonly IReadOnlyList<string> All = [Tds, Gstr1, Gstr3B, Gstr9, Epf, Esic, Pt];
}

public static class ComplianceFrequency
{
    public const string Monthly = "Monthly", Annual = "Annual";
}

public static class ComplianceStatus
{
    public const string Overdue = "Overdue", DueToday = "Due today", DueSoon = "Due soon", Upcoming = "Upcoming", Filed = "Filed", NotApplicable = "Not applicable";
}

public enum DueRuleType
{
    /// <summary>That day of the month after the period (optional special-month override).</summary>
    Day,

    /// <summary>Last day of the month after the period.</summary>
    MonthEnd,

    /// <summary>Annual: the first such month/day after the financial year ends.</summary>
    Fixed,
}

public sealed record SpecialDate(int Month, int Day);

public sealed record DueRule(DueRuleType Type, int Day = 0, int Month = 0, SpecialDate? Special = null);

/// <summary>A rule and the date from which it applies (periods that began earlier keep the older rule).</summary>
public sealed record DatedRule(DateOnly From, DueRule Rule);

/// <summary>The parts of an obligation the date rules need.</summary>
public sealed record ObligationSchedule(string Frequency, string FromPeriod, IReadOnlyList<DatedRule> Rules);

public sealed record FilingFacts(string Status, DateTime AtUtc, string? Reference, string? Reason);

public sealed record CalendarRow(string Period, string PeriodLabel, DateOnly Due, int Days, string Status);

public sealed record CalendarCounts(int Overdue, int Due, int Upcoming, int Filed, int NotApplicable, int Open);

public static class ComplianceRules
{
    public const int MaxPacks = 20;
    public const int MaxReopened = 200;
    public const int MaxObligations = 30;
    public const int DefaultDueSoonDays = 7;
    public const string DefaultCaName = "Rao & Associates (fictional)";
    public const string DefaultCaEmail = "ca.desk@example.test";
    public const string Illustrative = "Illustrative due dates — CA to confirm for a hotel.";
    public static readonly DateOnly RuleOrigin = new(2000, 1, 1);

    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>Longest length of each calendar month (index 0 = Jan); February 29 so a leap-year date is accepted and clamped where needed.</summary>
    private static readonly int[] MonthMaxDays = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    // ── Periods ──

    private static string Pad(int n) => n.ToString("00", CultureInfo.InvariantCulture);

    public static bool IsMonthPeriod(string? p) => p is { Length: 7 } && p[4] == '-' && int.TryParse(p.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)
        && y is >= 2000 and <= 2999 && int.TryParse(p.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m is >= 1 and <= 12;

    public static bool IsFyPeriod(string? p) => p is { Length: 7 } && p[4] == '-' && int.TryParse(p.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)
        && y is >= 2000 and <= 2999 && int.TryParse(p.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var e) && e == (y + 1) % 100;

    public static bool IsValidPeriod(string frequency, string? p) => frequency == ComplianceFrequency.Annual ? IsFyPeriod(p) : IsMonthPeriod(p);

    public static string MonthAdd(string p, int n)
    {
        var y = int.Parse(p.AsSpan(0, 4), CultureInfo.InvariantCulture);
        var m = int.Parse(p.AsSpan(5, 2), CultureInfo.InvariantCulture) - 1 + n;
        var yy = y + (int)Math.Floor(m / 12.0);
        var mm = ((m % 12) + 12) % 12;
        return $"{yy.ToString(CultureInfo.InvariantCulture)}-{Pad(mm + 1)}";
    }

    public static string MonthOf(DateOnly d) => $"{d.Year.ToString(CultureInfo.InvariantCulture)}-{Pad(d.Month)}";

    public static DateOnly MonthEnd(string p)
    {
        var y = int.Parse(p.AsSpan(0, 4), CultureInfo.InvariantCulture);
        var m = int.Parse(p.AsSpan(5, 2), CultureInfo.InvariantCulture);
        return new DateOnly(y, m, DateTime.DaysInMonth(y, m));
    }

    /// <summary>Financial-year label 'YYYY-YY' from its start year.</summary>
    public static string FyLabel(int startYear) => $"{startYear.ToString(CultureInfo.InvariantCulture)}-{Pad((startYear + 1) % 100)}";

    public static int FyStartYear(string p) => int.Parse(p.AsSpan(0, 4), CultureInfo.InvariantCulture);

    /// <summary>Start year of the financial year (April–March) containing <paramref name="d"/>.</summary>
    public static int FyStartYearOf(DateOnly d) => d.Month < 4 ? d.Year - 1 : d.Year;

    public static string PeriodLabel(string frequency, string p) => frequency == ComplianceFrequency.Annual
        ? "FY " + p
        : $"{Months[int.Parse(p.AsSpan(5, 2), CultureInfo.InvariantCulture) - 1]} {p[..4]}";

    public static DateOnly PeriodStart(string frequency, string p) => frequency == ComplianceFrequency.Annual
        ? new DateOnly(FyStartYear(p), 4, 1)
        : new DateOnly(int.Parse(p.AsSpan(0, 4), CultureInfo.InvariantCulture), int.Parse(p.AsSpan(5, 2), CultureInfo.InvariantCulture), 1);

    public static DateOnly PeriodEnd(string frequency, string p) => frequency == ComplianceFrequency.Annual ? new DateOnly(FyStartYear(p) + 1, 3, 31) : MonthEnd(p);

    /// <summary>Periods tracked from <paramref name="fromPeriod"/> up to the latest period that has ended before <paramref name="today"/>.</summary>
    public static IReadOnlyList<string> Periods(string frequency, string fromPeriod, DateOnly today)
    {
        var output = new List<string>();
        if (frequency == ComplianceFrequency.Annual)
        {
            var start = FyStartYear(fromPeriod);
            for (var y = start; y < start + 30; y++)
            {
                var p = FyLabel(y);
                if (PeriodEnd(frequency, p) >= today) break;
                output.Add(p);
            }

            return output;
        }

        var cursor = fromPeriod;
        for (var n = 0; n < 240 && MonthEnd(cursor) < today; n++, cursor = MonthAdd(cursor, 1)) output.Add(cursor);
        return output;
    }

    /// <summary>The tracking start of a newly added obligation: the latest finished period, so adding one never creates an "overdue" backlog.</summary>
    public static string DefaultTrackedFrom(string frequency, DateOnly businessDate) =>
        frequency == ComplianceFrequency.Annual ? FyLabel(FyStartYearOf(businessDate) - 1) : MonthAdd(MonthOf(businessDate), -1);

    // ── Due dates ──

    /// <summary>The rule valid for a period: the latest dated rule whose effective-from is on or before the period's first day.</summary>
    public static DueRule RuleFor(ObligationSchedule o, string period)
    {
        ArgumentNullException.ThrowIfNull(o);
        var start = PeriodStart(o.Frequency, period);
        var ordered = o.Rules.OrderBy(r => r.From).ToList();
        var ok = ordered.Where(r => r.From <= start).ToList();
        return (ok.Count > 0 ? ok[^1] : ordered[0]).Rule;
    }

    public static DateOnly DueDate(ObligationSchedule o, string period)
    {
        var r = RuleFor(o, period);
        // 'after the financial year': Apr–Dec fall in the calendar year the FY ends; Jan–Mar in the year after (never before the FY has ended).
        if (r.Type == DueRuleType.Fixed)
        {
            var y = FyStartYear(period) + (r.Month <= 3 ? 2 : 1);
            return new DateOnly(y, r.Month, Math.Min(r.Day, DateTime.DaysInMonth(y, r.Month)));
        }

        var next = MonthAdd(MonthOf(PeriodStart(ComplianceFrequency.Monthly, period)), 1);
        var ny = int.Parse(next.AsSpan(0, 4), CultureInfo.InvariantCulture);
        var nm = int.Parse(next.AsSpan(5, 2), CultureInfo.InvariantCulture);
        if (r.Type == DueRuleType.MonthEnd) return new DateOnly(ny, nm, DateTime.DaysInMonth(ny, nm));
        var periodMonth = int.Parse(period.AsSpan(5, 2), CultureInfo.InvariantCulture);
        var day = r.Special is { } sp && periodMonth == sp.Month ? sp.Day : r.Day;
        return new DateOnly(ny, nm, Math.Min(day, DateTime.DaysInMonth(ny, nm)));
    }

    private static string Ordinal(int n) => n % 10 == 1 && n != 11 ? "st" : n % 10 == 2 && n != 12 ? "nd" : n % 10 == 3 && n != 13 ? "rd" : "th";

    public static string RuleText(DueRule r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.Type == DueRuleType.MonthEnd) return "Last day of the next month";
        if (r.Type == DueRuleType.Fixed) return $"{r.Day.ToString(CultureInfo.InvariantCulture)} {Months[r.Month - 1]} after the financial year";
        var text = $"{r.Day.ToString(CultureInfo.InvariantCulture)}{Ordinal(r.Day)} of the next month";
        return r.Special is { } sp
            ? text + $" ({Months[sp.Month - 1]} period: {sp.Day.ToString(CultureInfo.InvariantCulture)} {Months[sp.Month % 12]})"
            : text;
    }

    /// <summary>Validates and builds a due rule from wire fields (prototype <c>parseRule</c>); VALIDATION on a bad rule.</summary>
    public static DueRule ParseRule(string frequency, string? type, int? day, int? month, int? specialMonth, int? specialDay)
    {
        if (frequency == ComplianceFrequency.Annual)
        {
            if (month is not (>= 1 and <= 12) || day is not (>= 1 and <= 31)) throw HccException.Validation("Annual due date: choose a valid month and day.");
            return new DueRule(DueRuleType.Fixed, day.Value, month.Value);
        }

        if (type == "month-end") return new DueRule(DueRuleType.MonthEnd);
        if (type != "day" || day is not (>= 1 and <= 28)) throw HccException.Validation("Choose “day of the next month” 1–28, or “last day of the next month”.");
        if (specialMonth is not null || specialDay is not null)
        {
            // The special date falls in the month after the special month, so it is checked against that month's real length (Feb: 29, clamped in leap-less years).
            if (specialMonth is not (>= 1 and <= 12) || specialDay is not >= 1 || specialDay > MonthMaxDays[specialMonth.Value % 12])
                throw HccException.Validation("Special-month date is not valid.");
            return new DueRule(DueRuleType.Day, day.Value, 0, new SpecialDate(specialMonth.Value, specialDay.Value));
        }

        return new DueRule(DueRuleType.Day, day.Value);
    }

    // ── Rows and status ──

    public static string StatusOf(int days, int dueSoonDays) => days < 0 ? ComplianceStatus.Overdue : days == 0 ? ComplianceStatus.DueToday : days <= dueSoonDays ? ComplianceStatus.DueSoon : ComplianceStatus.Upcoming;

    public static CalendarRow Row(ObligationSchedule o, string period, DateOnly businessDate, int dueSoonDays, string? filedStatus)
    {
        ArgumentNullException.ThrowIfNull(o);
        var due = DueDate(o, period);
        var days = due.DayNumber - businessDate.DayNumber;
        return new CalendarRow(period, PeriodLabel(o.Frequency, period), due, days, filedStatus ?? StatusOf(days, dueSoonDays));
    }

    public static bool IsOpen(string status) => status != ComplianceStatus.Filed && status != ComplianceStatus.NotApplicable;

    public static string StatusText(string status, int days) => status switch
    {
        ComplianceStatus.Overdue => $"Overdue by {(-days).ToString(CultureInfo.InvariantCulture)} day{(days == -1 ? string.Empty : "s")}",
        ComplianceStatus.DueToday => "Due today",
        ComplianceStatus.Filed or ComplianceStatus.NotApplicable => status,
        _ => $"Due in {days.ToString(CultureInfo.InvariantCulture)} day{(days == 1 ? string.Empty : "s")}",
    };

    public static CalendarCounts Count(IEnumerable<string> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        int overdue = 0, due = 0, upcoming = 0, filed = 0, na = 0, open = 0;
        foreach (var s in statuses)
        {
            switch (s)
            {
                case ComplianceStatus.Overdue: overdue++; break;
                case ComplianceStatus.DueToday or ComplianceStatus.DueSoon: due++; break;
                case ComplianceStatus.Upcoming: upcoming++; break;
                case ComplianceStatus.Filed: filed++; break;
                default: na++; break;
            }

            if (IsOpen(s)) open++;
        }

        return new CalendarCounts(overdue, due, upcoming, filed, na, open);
    }

    // ── Validation shared by the commands ──

    private static bool ReferenceChar(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '/' or '.' or ' ';

    public static string CleanReference(string? raw)
    {
        var reference = (raw ?? string.Empty).Trim();
        if (reference.Length is < 4 or > 40 || !reference.All(ReferenceChar))
            throw HccException.Validation("Enter the acknowledgement / challan reference (4–40 characters: letters, digits and - / .).");
        return reference;
    }

    public static string CleanReason(string? raw)
    {
        var reason = (raw ?? string.Empty).Trim();
        if (reason.Length is < 5 or > 120) throw HccException.Validation("Give a reason of 5–120 characters.");
        return reason;
    }

    /// <summary>The CA pack lines (prototype <c>cmpPackLines</c>): position line, every open row, filings of the last 35 days; at most 60 lines.</summary>
    public static IReadOnlyList<string> PackLines(DateOnly businessDate, IReadOnlyList<PackRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var lines = new List<string>();
        var open = rows.Where(r => IsOpen(r.Status)).ToList();
        lines.Add($"Position as on {businessDate:yyyy-MM-dd}: {rows.Count(r => r.Status == ComplianceStatus.Filed).ToString(CultureInfo.InvariantCulture)} filed, {open.Count.ToString(CultureInfo.InvariantCulture)} open, {rows.Count(r => r.Status == ComplianceStatus.NotApplicable).ToString(CultureInfo.InvariantCulture)} not applicable.");
        foreach (var r in open) lines.Add($"OPEN · {r.Name} · {r.PeriodLabel} · due {r.Due:yyyy-MM-dd} · {StatusText(r.Status, r.Days)}");
        foreach (var r in rows.Where(r => r.Status == ComplianceStatus.Filed && r.FiledOn is { } on && businessDate.DayNumber - on.DayNumber <= 35))
            lines.Add($"FILED · {r.Name} · {r.PeriodLabel} · ref {r.Reference} · {r.FiledOn!.Value:yyyy-MM-dd}");
        return lines.Take(60).ToList();
    }
}

public sealed record PackRow(string Name, string PeriodLabel, DateOnly Due, int Days, string Status, DateOnly? FiledOn, string? Reference);

/// <summary>The default SCC-derived obligations (prototype fixture; illustrative).</summary>
public sealed record DefaultObligation(string Kind, string Name, string Form, string Frequency, DueRule Rule, string Owner, string? Note);

public static class ComplianceDefaults
{
    public static readonly IReadOnlyList<DefaultObligation> Obligations =
    [
        new(ComplianceKinds.Tds, "TDS deposit", "Challan 281 · TIN-NSDL", ComplianceFrequency.Monthly, new DueRule(DueRuleType.Day, 7, 0, new SpecialDate(3, 30)), "Accountant",
            "Salary, rent and professional-fee deductions. March deductions have an extended date (SCC)."),
        new(ComplianceKinds.Gstr1, "GSTR-1 · outward supplies", "GST portal", ComplianceFrequency.Monthly, new DueRule(DueRuleType.Day, 11), "Accountant", "Monthly filer assumed (not QRMP)."),
        new(ComplianceKinds.Gstr3B, "GSTR-3B · summary return and tax", "GST portal", ComplianceFrequency.Monthly, new DueRule(DueRuleType.Day, 20), "Accountant", null),
        new(ComplianceKinds.Gstr9, "GSTR-9 · annual return", "GST portal", ComplianceFrequency.Annual, new DueRule(DueRuleType.Fixed, 31, 12), "Owner", "Filed with the CA after the audit of the year."),
        new(ComplianceKinds.Epf, "EPF contribution", "EPFO unified portal · ECR", ComplianceFrequency.Monthly, new DueRule(DueRuleType.Day, 15), "Accountant", null),
        new(ComplianceKinds.Esic, "ESIC contribution", "ESIC portal · challan", ComplianceFrequency.Monthly, new DueRule(DueRuleType.Day, 15), "Accountant", null),
        new(ComplianceKinds.Pt, "Professional Tax (Maharashtra)", "State PT portal · Form III-B", ComplianceFrequency.Monthly, new DueRule(DueRuleType.MonthEnd), "Accountant", null),
    ];
}
