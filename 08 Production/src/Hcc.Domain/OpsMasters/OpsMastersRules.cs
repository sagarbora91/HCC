using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hcc.Contracts.Masters;
using Hcc.Contracts.OpsMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;

namespace Hcc.Domain.OpsMasters;

/// <summary>
/// Rules of the operations masters (MA-7; prototype domain/streams/hkmasters.ts and gsvmasters.ts). Row basics (code, name, sort, retire,
/// reorder) are <see cref="MastersRules"/>. Lists are stored as canonical text: comma lists for kinds, floors and rooms, a JSON array for
/// the checklist. Errors carry the master reasons (MASTER_INVALID with the field) like the floors master.
/// </summary>
public static partial class OpsMastersRules
{
    public const int MinutesMin = 5;
    public const int MinutesMax = 240;
    public const int ChecklistMax = 8;
    public const int ChecklistItemMax = 60;
    public const int SlaMin = 1;
    public const int SlaMax = 720;
    public const int FloorsMax = 200;
    public const int RoomsMax = 400;

    public static readonly IReadOnlyList<string> Departments = ["Housekeeping", "Maintenance", "Front desk"];

    public static readonly IReadOnlyList<string> DefaultChecklist = ["Linen & bathroom", "Surfaces & amenities", "Final room check"];

    /// <summary>Built-in values for a kind with no task type (old data, every row retired): minutes, needs inspection, items.</summary>
    public static (int Minutes, bool NeedsInspection, int Items) Fallback(string kind) => kind switch
    {
        HkTypeKinds.Stayover => (20, false, DefaultChecklist.Count),
        HkTypeKinds.Inspection => (10, false, DefaultChecklist.Count),
        _ => (40, true, DefaultChecklist.Count),
    };

    [GeneratedRegex("^[A-Z0-9-]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Za-z0-9-]{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoomPattern();

    // ── canonical text ──
    public static string Csv(IEnumerable<string> items) => string.Join(',', items);

    public static IReadOnlyList<string> SplitCsv(string? text) => string.IsNullOrEmpty(text) ? [] : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string ChecklistJson(IEnumerable<string> items) => JsonSerializer.Serialize(items, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    public static IReadOnlyList<string> ParseChecklist(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>A value as people read it in the change history ("Master Housekeeping task types: standardMinutes 40 → 45").</summary>
    public static string Show(string key, string? value)
    {
        if (value is null) return "—";
        return key switch
        {
            "needsInspection" or "valuable" => value == "1" ? "yes" : "no",
            "checklist" => string.Join(" / ", ParseChecklist(value)),
            "appliesTo" or "floors" or "rooms" => string.Join(", ", SplitCsv(value)),
            "defaultAttendant" => value.Length == 0 ? "—" : value,
            _ => value.Length == 0 ? "—" : value,
        };
    }

    // ── task types ──
    public sealed record CleanTaskType(string AppliesTo, int Minutes, bool NeedsInspection, IReadOnlyList<string> Checklist);

    public static CleanTaskType ValidateTaskType(IReadOnlyList<string>? appliesTo, int? minutes, bool needsInspection, IReadOnlyList<string>? checklist)
    {
        var kinds = (appliesTo ?? []).Select(k => (k ?? string.Empty).Trim().ToLowerInvariant()).Where(k => k.Length > 0).Distinct().ToList();
        if (kinds.Count == 0) throw MastersRules.Fail(MasterReasons.Invalid, "Enter used for.", "appliesTo");
        if (kinds.Any(k => !HkTypeKinds.All.Contains(k))) throw MastersRules.Fail(MasterReasons.Invalid, "Used for: choose from the list.", "appliesTo");
        if (minutes is not { } m) throw MastersRules.Fail(MasterReasons.Invalid, "Enter standard minutes.", "standardMinutes");
        if (m < MinutesMin || m > MinutesMax) throw MastersRules.Fail(MasterReasons.Invalid, $"Standard minutes: {MinutesMin}–{MinutesMax}.", "standardMinutes");
        var items = (checklist ?? []).Select(x => (x ?? string.Empty).Trim()).Where(x => x.Length > 0).ToList();
        if (items.Count < 1 || items.Count > ChecklistMax) throw MastersRules.Fail(MasterReasons.Invalid, $"Checklist: 1–{ChecklistMax} items, one per line.", "checklist");
        if (items.FirstOrDefault(x => x.Length > ChecklistItemMax) is { } longItem)
            throw MastersRules.Fail(MasterReasons.Invalid, $"Checklist item “{longItem[..20]}…” is longer than {ChecklistItemMax} characters.", "checklist");
        if (items.Select(x => x.ToLowerInvariant()).Distinct().Count() != items.Count) throw MastersRules.Fail(MasterReasons.Invalid, "Checklist: each item once.", "checklist");
        if (kinds.Contains(HkTypeKinds.Stayover) && needsInspection)
            throw MastersRules.Fail(MasterReasons.Invalid, "A stayover service is never inspected (the room stays occupied and its readiness does not change) — switch “Needs inspection” off.", "needsInspection");
        var ordered = HkTypeKinds.All.Where(kinds.Contains);
        return new CleanTaskType(Csv(ordered), m, needsInspection, items);
    }

    /// <summary>Each kind belongs to one active type (prototype HKT_SPEC.validate).</summary>
    public static void EnsureKindsFree(string appliesTo, bool active, IEnumerable<(long Id, string Name, bool Active, string AppliesTo)> others, long? selfId)
    {
        if (!active) return;
        var rivals = others.Where(o => o.Id != selfId && o.Active).ToList();
        foreach (var k in SplitCsv(appliesTo))
            foreach (var other in rivals)
                if (SplitCsv(other.AppliesTo).Contains(k))
                    throw MastersRules.Fail(MasterReasons.Invalid, $"{KindLabel(k)} is already handled by “{other.Name}” — each kind belongs to one active type.", "appliesTo");
    }

    private static string KindLabel(string kind) => kind switch
    {
        HkTypeKinds.Checkout => "Departure clean",
        HkTypeKinds.Move => "Room-move clean",
        HkTypeKinds.Stayover => "Stayover service",
        HkTypeKinds.Inspection => "Supervisor inspection of a finished clean",
        _ => "Deep clean",
    };

    // ── zones ──
    public sealed record CleanZone(string Floors, string Rooms);

    public static CleanZone ValidateZone(IReadOnlyList<string>? floors, IReadOnlyList<string>? rooms)
    {
        var f = (floors ?? []).Select(x => (x ?? string.Empty).Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList();
        var r = (rooms ?? []).Select(x => (x ?? string.Empty).Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (f.Count == 0 && r.Count == 0) throw MastersRules.Fail(MasterReasons.Invalid, "Choose at least one floor or room for the zone.");
        if (f.Any(x => !CodePattern().IsMatch(x))) throw MastersRules.Fail(MasterReasons.Invalid, "Floors: choose from the list.", "floors");
        if (r.Any(x => !RoomPattern().IsMatch(x))) throw MastersRules.Fail(MasterReasons.Invalid, "Extra rooms: choose from the list.", "rooms");
        if (Csv(f).Length > FloorsMax) throw MastersRules.Fail(MasterReasons.Invalid, "Floors: too many for one zone.", "floors");
        if (Csv(r).Length > RoomsMax) throw MastersRules.Fail(MasterReasons.Invalid, "Extra rooms: too many for one zone.", "rooms");
        return new CleanZone(Csv(f), Csv(r));
    }

    /// <summary>A floor or room belongs to one active zone (prototype HKZ_SPEC.validate).</summary>
    public static void EnsureZoneFree(CleanZone zone, bool active, IEnumerable<(long Id, string Name, bool Active, string Floors, string Rooms)> others, long? selfId)
    {
        if (!active) return;
        foreach (var o in others.Where(o => o.Id != selfId && o.Active))
        {
            if (SplitCsv(zone.Floors).FirstOrDefault(x => SplitCsv(o.Floors).Contains(x)) is { } fl)
                throw MastersRules.Fail(MasterReasons.Invalid, $"Floor {fl} is already in zone “{o.Name}” — a floor belongs to one active zone.", "floors");
            if (SplitCsv(zone.Rooms).FirstOrDefault(x => SplitCsv(o.Rooms).Contains(x)) is { } rm)
                throw MastersRules.Fail(MasterReasons.Invalid, $"Room {rm} is already in zone “{o.Name}” — a room belongs to one active zone.", "rooms");
        }
    }

    // ── categories ──
    public static string ValidateDepartment(string? department)
    {
        var d = (department ?? string.Empty).Trim();
        if (d.Length == 0) throw MastersRules.Fail(MasterReasons.Invalid, "Enter department.", "department");
        return Departments.FirstOrDefault(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase)) ?? throw MastersRules.Fail(MasterReasons.Invalid, "Department: choose from the list.", "department");
    }

    public static int ValidateSla(int? hours)
    {
        if (hours is not { } h) throw MastersRules.Fail(MasterReasons.Invalid, "Enter sla (hours).", "slaHours");
        if (h < SlaMin || h > SlaMax) throw MastersRules.Fail(MasterReasons.Invalid, $"SLA (hours): {SlaMin}–{SlaMax}.", "slaHours");
        return h;
    }

    /// <summary>Another active row may not carry the same name (case-insensitive) — shared by every master.</summary>
    public static void EnsureNameFree(string name, bool active, IEnumerable<(long Id, string Name, bool Active)> others, long? selfId, string singular)
    {
        if (active && others.Any(o => o.Id != selfId && o.Active && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {singular} is already called “{name}”.", "name");
    }

    public static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
}
