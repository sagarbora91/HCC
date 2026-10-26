using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Masters;

/// <summary>
/// Masters framework rules (plan Part 2 §9; prototype domain/streams/masters.ts): code ^[A-Z0-9-]{1,20}$ unique per property and
/// immutable once used, name 1–60, sort order 0–1,000,000, retire-not-delete when used, reorder = every row once. Floors (M1): until MA-1
/// links rooms to floors, the floor of a room is the hundreds digit of its number (101 → F1), as the prototype and sync layer show it.
/// </summary>
public static partial class MastersRules
{
    public const int CodeMax = 20;
    public const int NameMax = 60;
    public const int SortMax = 1_000_000;
    public const int SortStep = 10;

    [GeneratedRegex("^[A-Z0-9-]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^F([0-9]{1,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex FloorCodePattern();

    public sealed record CleanRow(string Code, string Name, int? SortOrder);

    /// <summary>A master error: the frozen wire code + details.reason (and details.field).</summary>
    public static HccException Fail(string reason, string message, string? field = null)
    {
        var code = reason is MasterReasons.CodeTaken or MasterReasons.Invalid ? HccErrorCode.VALIDATION : HccErrorCode.INVALID_TRANSITION;
        var details = new Dictionary<string, JsonElement> { ["reason"] = JsonSerializer.SerializeToElement(reason) };
        if (field is not null) details["field"] = JsonSerializer.SerializeToElement(field);
        return new HccException(code, message, details);
    }

    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Validates and normalises code, name and sort order (MASTER_INVALID with the field).</summary>
    public static CleanRow Validate(string? code, string? name, int? sortOrder)
    {
        var c = NormalizeCode(code);
        if (c.Length == 0) throw Fail(MasterReasons.Invalid, "Enter a code.", "code");
        if (!CodePattern().IsMatch(c)) throw Fail(MasterReasons.Invalid, "Code: 1–20 capital letters, digits or “-”.", "code");
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) throw Fail(MasterReasons.Invalid, "Enter a name.", "name");
        if (n.Length > NameMax) throw Fail(MasterReasons.Invalid, $"Name: at most {NameMax} characters.", "name");
        if (sortOrder is { } so && (so < 0 || so > SortMax)) throw Fail(MasterReasons.Invalid, "Sort order: a whole number from 0 to 1,000,000.", "sortOrder");
        return new CleanRow(c, n, sortOrder);
    }

    /// <summary>Code change refused once the row is used (records keep pointing at the code).</summary>
    public static void EnsureCodeMayChange(string oldCode, string newCode, int usage, string what)
    {
        if (!string.Equals(oldCode, newCode, StringComparison.Ordinal) && usage > 0)
            throw Fail(MasterReasons.InUse, $"Code {oldCode} is used by {usage} {what} and cannot change.", "code");
    }

    /// <summary>Delete only a never-used row; otherwise retire it.</summary>
    public static void EnsureDeletable(string name, int usage, string what)
    {
        if (usage > 0) throw Fail(MasterReasons.InUse, $"{name} is used by {usage} {what} — retire it instead of deleting.");
    }

    public static void EnsureActive(bool active, string name)
    {
        if (!active) throw Fail(MasterReasons.Retired, $"{name} is retired — reactivate it before changing it.");
    }

    /// <summary>The new order must name every row exactly once; returns id → new sort order (10, 20, …).</summary>
    public static IReadOnlyDictionary<int, int> Reorder(IReadOnlyCollection<int> current, IReadOnlyList<int>? requested, string singular)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (requested is null || requested.Count != current.Count || requested.Distinct().Count() != requested.Count || !current.All(requested.Contains))
            throw HccException.Validation($"Send every {singular} once in the new order.");
        return requested.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => (x.i + 1) * SortStep);
    }

    /// <summary>Next sort order after the existing ones.</summary>
    public static int NextSort(IEnumerable<int> existing) => Math.Min(SortMax, (existing.DefaultIfEmpty(0).Max() / SortStep + 1) * SortStep);

    /// <summary>Changed fields (field, old, new) between two rows, in a fixed order; values as shown to people.</summary>
    public static IReadOnlyList<(string Field, string? Old, string? New)> Diff(CleanRow? before, CleanRow after, int afterSort)
    {
        ArgumentNullException.ThrowIfNull(after);
        var list = new List<(string, string?, string?)>();
        void Add(string f, string? o, string? n)
        {
            if (before is null || !string.Equals(o, n, StringComparison.Ordinal)) list.Add((f, before is null ? null : o, n));
        }

        Add("code", before?.Code, after.Code);
        Add("name", before?.Name, after.Name);
        Add("sortOrder", before?.SortOrder?.ToString(CultureInfo.InvariantCulture), afterSort.ToString(CultureInfo.InvariantCulture));
        return list;
    }

    /// <summary>"Master Floors: name Floor 1 → Ground; …" (the prototype's audit text).</summary>
    public static string AuditText(string label, IEnumerable<(string Field, string? Old, string? New)> changes) =>
        $"Master {label}: " + string.Join("; ", changes.Select(c => $"{c.Field} {c.Old ?? "—"} → {c.New ?? "—"}"));

    // ── Floors ──
    public static string FloorCode(int n) => "F" + n.ToString(CultureInfo.InvariantCulture);

    /// <summary>Floor number of a code F&lt;n&gt; (null for other codes such as a named wing).</summary>
    public static int? FloorNumber(string code)
    {
        var m = FloorCodePattern().Match(code ?? string.Empty);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Floor of a room number: the hundreds digit (101 → 1, 1205 → 12); other numbers are on floor 1 (sync/rooms.ts floorOf).</summary>
    public static int FloorOfRoom(string roomNo)
    {
        if (string.IsNullOrEmpty(roomNo) || roomNo.Length < 3 || !roomNo.All(char.IsAsciiDigit)) return 1;
        return Math.Max(1, int.Parse(roomNo, CultureInfo.InvariantCulture) / 100);
    }

    public static PageArgs Page(int? page, int? pageSize, int max = 500)
    {
        var p = page ?? 1;
        var s = pageSize ?? 100;
        if (p < 1) throw HccException.Validation("page must be 1 or more.");
        if (s < 1 || s > max) throw HccException.Validation($"pageSize must be 1–{max}.");
        return new PageArgs(p, s);
    }

    public sealed record PageArgs(int Page, int PageSize)
    {
        public int Skip => (Page - 1) * PageSize;
    }
}
