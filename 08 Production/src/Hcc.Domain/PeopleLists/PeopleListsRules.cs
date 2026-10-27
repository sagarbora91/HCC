using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Contracts.Masters;
using Hcc.Contracts.PeopleLists;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;
using Hcc.Domain.Payroll;

namespace Hcc.Domain.PeopleLists;

/// <summary>The four People lists (MA-6).</summary>
public enum PeopleList
{
    Designations,
    ShiftPatterns,
    LeaveTypes,
    Holidays,
}

/// <summary>A validated row of a People list: code, name, sort order and the list's own fields (normalised values: string, int, bool or null).</summary>
public sealed record CleanListRow(string Code, string Name, int? SortOrder, IReadOnlyDictionary<string, object?> Fields);

/// <summary>
/// People lists rules (plan Part 2 §8 M19–M22; prototype <c>domain/streams/people-masters.ts</c>): field validation per list on top of the
/// masters framework rules (<see cref="MastersRules"/>: name 1–60, sort 0–1,000,000, retire-not-delete), seeds that reproduce today's
/// constants, and the field order used for history and audit text.
/// </summary>
public static partial class PeopleListsRules
{
    public static readonly IReadOnlyList<string> PayRules = ["monthly-structured", "monthly-basic", "daily"];

    [GeneratedRegex("^[A-Z0-9]{2,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex DeptPattern();

    [GeneratedRegex("^[A-Za-z0-9 ./-]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex GradePattern();

    [GeneratedRegex("^[A-Z0-9]{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShiftCodePattern();

    [GeneratedRegex("^[A-Z0-9]{1,6}$", RegexOptions.CultureInvariant)]
    private static partial Regex LeaveCodePattern();

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    public static string Id(PeopleList l) => l switch
    {
        PeopleList.Designations => PeopleListIds.Designations,
        PeopleList.ShiftPatterns => PeopleListIds.ShiftPatterns,
        PeopleList.LeaveTypes => PeopleListIds.LeaveTypes,
        _ => PeopleListIds.Holidays,
    };

    /// <summary>"Master Shift patterns: …" label of the audit text (same as the prototype spec labels).</summary>
    public static string Label(PeopleList l) => l switch
    {
        PeopleList.Designations => "Designations",
        PeopleList.ShiftPatterns => "Shift patterns",
        PeopleList.LeaveTypes => "Leave types",
        _ => "Holiday calendar",
    };

    public static string Singular(PeopleList l) => l switch
    {
        PeopleList.Designations => "designation",
        PeopleList.ShiftPatterns => "shift pattern",
        PeopleList.LeaveTypes => "leave type",
        _ => "holiday",
    };

    /// <summary>Audit action of the list ("MST_DESIGNATION" …).</summary>
    public static string AuditAction(PeopleList l) => l switch
    {
        PeopleList.Designations => "MST_DESIGNATION",
        PeopleList.ShiftPatterns => "MST_SHIFT_PATTERN",
        PeopleList.LeaveTypes => "MST_LEAVE_TYPE",
        _ => "MST_HOLIDAY",
    };

    /// <summary>The list's own fields in history / audit order.</summary>
    public static IReadOnlyList<string> FieldKeys(PeopleList l) => l switch
    {
        PeopleList.Designations => ["dept", "grade", "defaultPayRule"],
        PeopleList.ShiftPatterns => ["start", "end", "breakMinutes", "night", "weeklyOffRule"],
        PeopleList.LeaveTypes => ["paid", "yearlyQuota"],
        _ => ["kind"],
    };

    /// <summary>Pay-related fields (Owner only, PERM-MST-002). Holidays are Owner-only as a whole (<see cref="OwnerOnlyList"/>).</summary>
    public static IReadOnlyList<string> PayFieldKeys(PeopleList l) => l switch
    {
        PeopleList.Designations => ["defaultPayRule"],
        PeopleList.LeaveTypes => ["paid", "yearlyQuota"],
        PeopleList.Holidays => ["kind"],
        _ => [],
    };

    /// <summary>Holidays are pay- and statute-related as a whole: every change needs PERM-MST-002.</summary>
    public static bool OwnerOnlyList(PeopleList l) => l == PeopleList.Holidays;

    /// <summary>Value as shown in history and audit text (bool → yes/no, null → null).</summary>
    public static string? Show(object? v) => v switch
    {
        null => null,
        bool b => b ? "yes" : "no",
        int i => i.ToString(CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.##", CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    // ── Validation ──

    private static string? Opt(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public static CleanListRow Designation(string? code, string? name, int? sortOrder, string? dept, string? grade, string? defaultPayRule)
    {
        var c = MastersRules.Validate(code, name, sortOrder);
        var d = Opt(dept)?.ToUpperInvariant();
        if (d is not null && !DeptPattern().IsMatch(d)) throw MastersRules.Fail(MasterReasons.Invalid, "Department code: 2–10 capital letters or digits.", "dept");
        var g = Opt(grade);
        if (g is not null && !GradePattern().IsMatch(g)) throw MastersRules.Fail(MasterReasons.Invalid, "Grade: letters, digits, space, “.”, “/” or “-”, at most 20 characters.", "grade");
        var r = Opt(defaultPayRule);
        if (r is not null && !PayRules.Contains(r)) throw MastersRules.Fail(MasterReasons.Invalid, "Default pay rule: choose from the list.", "defaultPayRule");
        return new CleanListRow(c.Code, c.Name, c.SortOrder, new Dictionary<string, object?> { ["dept"] = d, ["grade"] = g, ["defaultPayRule"] = r });
    }

    public static string MinutesText(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static int ShiftMinutes(TimeOnly start, TimeOnly end)
    {
        var d = (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
        return d > 0 ? d : d + 1440;
    }

    public static CleanListRow ShiftPattern(string? code, string? name, int? sortOrder, string? start, string? end, int? breakMinutes, bool night, string? weeklyOffRule)
    {
        var c = MastersRules.Validate(code, name, sortOrder);
        if (!ShiftCodePattern().IsMatch(c.Code) || c.Code == "OFF")
            throw MastersRules.Fail(MasterReasons.Invalid, "Code: 1–3 capital letters or digits (OFF is reserved for weekly off).", "code");
        var s = (start ?? string.Empty).Trim();
        var e = (end ?? string.Empty).Trim();
        if (!TimePattern().IsMatch(s)) throw MastersRules.Fail(MasterReasons.Invalid, "Start is a time like 07:00 (24-hour).", "start");
        if (!TimePattern().IsMatch(e)) throw MastersRules.Fail(MasterReasons.Invalid, "End is a time like 15:00 (24-hour).", "end");
        if (s == e) throw MastersRules.Fail(MasterReasons.Invalid, "Start and end cannot be the same time.", "end");
        if (string.CompareOrdinal(e, s) < 0 && !night) throw MastersRules.Fail(MasterReasons.Invalid, "A shift that ends after midnight must be marked as a night shift.", "night");
        var br = breakMinutes ?? 0;
        if (br is < 0 or > 240) throw MastersRules.Fail(MasterReasons.Invalid, "Unpaid break: 0–240 minutes.", "breakMinutes");
        if (br >= ShiftMinutes(TimeOnly.ParseExact(s, "HH:mm", CultureInfo.InvariantCulture), TimeOnly.ParseExact(e, "HH:mm", CultureInfo.InvariantCulture)))
            throw MastersRules.Fail(MasterReasons.Invalid, "The break must be shorter than the shift.", "breakMinutes");
        var rule = Opt(weeklyOffRule) ?? "fixed";
        if (rule is not ("fixed" or "roster")) throw MastersRules.Fail(MasterReasons.Invalid, "Weekly off: fixed day or set in the roster.", "weeklyOffRule");
        return new CleanListRow(c.Code, c.Name, c.SortOrder, new Dictionary<string, object?>
            { ["start"] = s, ["end"] = e, ["breakMinutes"] = br, ["night"] = night, ["weeklyOffRule"] = rule });
    }

    /// <summary>SCC's fixed leave categories (SCC VALID_CATEGORIES, SCC order) + HCC's LOP — the only codes of the list (DEC-116).</summary>
    public static readonly IReadOnlyList<string> FixedLeaveCodes = ["CL", "SL", "EL", "ML", "CO", "OTH", "LOP"];

    /// <summary>SCC TRACKED_CATS: the categories with a yearly quota.</summary>
    public static readonly IReadOnlyList<string> TrackedLeaveCodes = ["CL", "SL", "EL"];

    public const string FixedLeave = "SCC’s leave categories are fixed — they cannot be retired.";

    /// <summary>SCC leave port (DEC-116): SCC's fixed categories + LOP; paid is fixed (only LOP unpaid); a quota only for CL / SL / EL (any number ≥ 0,
    /// halves allowed, no maximum). No carry-forward or encashment (SCC has neither).</summary>
    public static CleanListRow LeaveType(string? code, string? name, int? sortOrder, bool paid, decimal? yearlyQuota)
    {
        var c = MastersRules.Validate(code, name, sortOrder);
        if (!LeaveCodePattern().IsMatch(c.Code) || !FixedLeaveCodes.Contains(c.Code))
            throw MastersRules.Fail(MasterReasons.Invalid, "SCC’s leave categories are fixed: CL, SL, EL, ML, CO, OTH and LOP (leave without pay). No other category can be added.", "code");
        if (paid != (c.Code != "LOP"))
            throw MastersRules.Fail(MasterReasons.Invalid, c.Code == "LOP" ? "Leave without pay is always unpaid." : "Every SCC leave category is paid leave (only Leave without pay is unpaid).", "paid");
        if (yearlyQuota is not null && !TrackedLeaveCodes.Contains(c.Code))
            throw MastersRules.Fail(MasterReasons.Invalid, "Only Casual, Sick and Earned leave have a yearly quota (SCC TRACKED_CATS).", "yearlyQuota");
        if (yearlyQuota is < 0 or > 9_999_999) throw MastersRules.Fail(MasterReasons.Invalid, "Yearly quota: a number of days, 0 or more.", "yearlyQuota");
        return new CleanListRow(c.Code, c.Name, c.SortOrder, new Dictionary<string, object?>
            { ["paid"] = paid, ["yearlyQuota"] = yearlyQuota is { } q ? Math.Round(q, 2, MidpointRounding.AwayFromZero) : null });
    }

    public static bool ValidDate(string? code) =>
        code is not null && DatePattern().IsMatch(code) && DateOnly.TryParseExact(code, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static CleanListRow Holiday(string? code, string? name, int? sortOrder, string? kind)
    {
        var c = MastersRules.Validate(code, name, sortOrder);
        if (!ValidDate(c.Code)) throw MastersRules.Fail(MasterReasons.Invalid, "Date: a real calendar date like 2026-11-08.", "code");
        var k = Opt(kind);
        if (k is null) throw MastersRules.Fail(MasterReasons.Invalid, "Choose paid or restricted.", "kind");
        if (k is not ("paid" or "restricted")) throw MastersRules.Fail(MasterReasons.Invalid, "Kind: paid or restricted holiday.", "kind");
        return new CleanListRow(c.Code, c.Name, c.SortOrder, new Dictionary<string, object?> { ["kind"] = k });
    }

    /// <summary>"FY 2026–27" of a holiday date (April–March).</summary>
    public static string Fy(string code)
    {
        var y = int.Parse(code.AsSpan(0, 4), CultureInfo.InvariantCulture);
        var m = int.Parse(code.AsSpan(5, 2), CultureInfo.InvariantCulture);
        var start = m >= 4 ? y : y - 1;
        return string.Create(CultureInfo.InvariantCulture, $"FY {start}–{(start + 1) % 100:D2}");
    }

    // ── Cross-row rules ──

    /// <summary>Two active shift patterns must not have the same times.</summary>
    public static bool SameTimes(string start, string end, string otherStart, string otherEnd) => start == otherStart && end == otherEnd;

    // ── Changes (history and audit) ──

    /// <summary>Changed fields (field, old, new) in fixed order: code, name, sortOrder, then the list's own fields; before = null for a new row.</summary>
    public static IReadOnlyList<(string Field, string? Old, string? New)> Diff(PeopleList l, string? oldCode, string? oldName, int? oldSort, IReadOnlyDictionary<string, object?>? oldFields,
        CleanListRow after, int afterSort)
    {
        ArgumentNullException.ThrowIfNull(after);
        var isNew = oldCode is null;
        var list = new List<(string, string?, string?)>();
        void Add(string f, string? o, string? n)
        {
            if (isNew || !string.Equals(o, n, StringComparison.Ordinal)) list.Add((f, isNew ? null : o, n));
        }

        Add("code", oldCode, after.Code);
        Add("name", oldName, after.Name);
        Add("sortOrder", Show(oldSort), Show(afterSort));
        foreach (var k in FieldKeys(l))
            Add(k, Show(oldFields is not null && oldFields.TryGetValue(k, out var ov) ? ov : null), Show(after.Fields.TryGetValue(k, out var nv) ? nv : null));
        return list;
    }

    // ── Seeds (today's constants; the prototype specs' seed functions) ──

    public sealed record SeedRow(string Code, string Name, int SortOrder, IReadOnlyDictionary<string, object?> Fields);

    public static IReadOnlyList<SeedRow> SeedShiftPatterns() => ShiftCatalog.Defaults.Select((p, i) => new SeedRow(p.Code, p.Name, (i + 1) * 10, new Dictionary<string, object?>
        { ["start"] = MinutesText(p.Start), ["end"] = MinutesText(p.End), ["breakMinutes"] = p.BreakMinutes, ["night"] = p.Night, ["weeklyOffRule"] = p.WeeklyOffRule })).ToList();

    /// <summary>SCC's six leave categories in SCC's order + LOP; CL / SL / EL get the property's current entitlement (the stored settings, else 12 / 7 / 15).</summary>
    public static IReadOnlyList<SeedRow> SeedLeaveTypes(decimal cl, decimal sl, decimal el)
    {
        SeedRow Row(string code, string name, int i, bool paid, decimal? quota) => new(code, name, i * 10, new Dictionary<string, object?>
            { ["paid"] = paid, ["yearlyQuota"] = quota });
        return
        [
            Row("CL", "Casual leave", 1, true, cl),
            Row("SL", "Sick leave", 2, true, sl),
            Row("EL", "Earned leave", 3, true, el),
            Row("ML", "Maternity / paternity leave", 4, true, null),
            Row("CO", "Compensatory off", 5, true, null),
            Row("OTH", "Other", 6, true, null),
            Row("LOP", "Leave without pay", 7, false, null),
        ];
    }

    /// <summary>Designation code of a title: upper-case, non-alphanumerics → "-", at most 20 characters; the caller makes it unique.</summary>
    public static string SlugCode(string name)
    {
        var chars = name.ToUpperInvariant().Select(ch => ch is >= 'A' and <= 'Z' or >= '0' and <= '9' ? ch : '-').ToArray();
        var s = Regex.Replace(new string(chars), "-+", "-").Trim('-');
        s = s.Length > 20 ? s[..20].TrimEnd('-') : s;
        return s.Length == 0 ? "DESIG" : s;
    }

    /// <summary>One designation per distinct staff title (first department and pay type seen), in order of appearance; codes made unique.</summary>
    public static IReadOnlyList<SeedRow> SeedDesignations(IEnumerable<(string Title, string Dept, bool Daily, string? Structure)> staff)
    {
        ArgumentNullException.ThrowIfNull(staff);
        var rows = new List<SeedRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (title0, dept, daily, structure) in staff)
        {
            var title = (title0 ?? string.Empty).Trim();
            if (title.Length == 0 || !seen.Add(title)) continue;
            var code = SlugCode(title);
            for (var n = 2; !codes.Add(code); n++)
            {
                var suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
                code = SlugCode(title)[..Math.Min(SlugCode(title).Length, 20 - suffix.Length)] + suffix;
            }

            var rule = daily ? "daily" : (structure ?? "S") == "B" ? "monthly-basic" : "monthly-structured";
            rows.Add(new SeedRow(code, title.Length > 60 ? title[..60] : title, (rows.Count + 1) * 10, new Dictionary<string, object?> { ["dept"] = dept, ["grade"] = null, ["defaultPayRule"] = rule }));
        }

        return rows;
    }
}
