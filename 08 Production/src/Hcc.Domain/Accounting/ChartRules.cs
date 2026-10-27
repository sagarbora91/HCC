using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;

namespace Hcc.Domain.Accounting;

/// <summary>ACC_LEDGER_UPSERT input (null = keep the current value). <see cref="Create"/> = the Owner adds a ledger (the code must be unused).</summary>
public sealed record LedgerUpsert(bool Create, string? Code, string? Name, string? GroupId, string? Gst, string? TallyName, string? Note, bool? Active);

/// <summary>ACC_GROUP_UPSERT input: no <see cref="Id"/> = a new custom sub-group; else a rename / re-parent of a custom one.</summary>
public sealed record GroupUpsert(string? Id, string? Name, string? Parent);

/// <summary>What uses a ledger: the mapping keys that point at it and the balances (Dr − Cr, paise) of it and of its parties.</summary>
public sealed record LedgerUsage(IReadOnlyList<string> MappedBy, long OwnBalance, IReadOnlyDictionary<string, long> PartyBalances)
{
    public long Balance => OwnBalance + PartyBalances.Values.Sum();

    public static readonly LedgerUsage None = new([], 0, new Dictionary<string, long>());
}

/// <summary>One chart history row (field level; <c>acc.ledger_change</c>, prototype <c>ACCChartChange</c>).</summary>
public sealed record ChartFieldChange(string Field, string Before, string After);

/// <summary>A ledger upsert plan: the row to store (null = the built-in is back to its defaults: no stored row) and the changed fields.</summary>
public sealed record LedgerPlan(string Code, StoredLedger? Row, bool Created, IReadOnlyList<ChartFieldChange> Changes, LedgerAccount Effective);

public sealed record GroupPlan(LedgerGroup Row, bool Created, IReadOnlyList<ChartFieldChange> Changes);

/// <summary>
/// ACC-1 chart master rules (rulebook §3.1, §3.4; posting contract §1, §2) — the prototype's <c>accLedgerUpsertPlan</c>,
/// <c>accGroupUpsertPlan</c> and <c>accLedgerRetireBlocker</c> (<c>03 Prototype/src/domain/streams/accounting-chart.ts</c>), same messages and
/// MASTER_* reasons. "Same nature" = Tally's nature of a group (Assets / Liabilities / Income / Expenses + Suspense) = the legacy group of the
/// group's primary ancestor.
/// </summary>
public static partial class ChartRules
{
    public const string OwnerOnly = "only the Owner changes the chart of accounts (money master, DEC-058; PERM-ACC-007).";

    /// <summary>Ledger codes that never retire (Suspense and the opening-balance difference are structural).</summary>
    public static readonly IReadOnlyList<string> Fixed = [ChartOfAccounts.Suspense, ChartOfAccounts.OpeningDifference];

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^g-[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex GroupIdPattern();

    [GeneratedRegex("-+")]
    private static partial Regex Dashes();

    public static bool IsGroupId(string? id) => id is not null && GroupIdPattern().IsMatch(id);

    private static HccException Fail(string reason, string message, string? field = null) => MastersRules.Fail(reason, message, field);

    private static string T(string? v) => (v ?? "").Trim();

    private static string GstText(LedgerGst g) => g switch { LedgerGst.Input => "input", LedgerGst.Output => "output", _ => "none" };

    public static LedgerGst? ParseGst(string? v) => T(v) switch { "none" => LedgerGst.None, "input" => LedgerGst.Input, "output" => LedgerGst.Output, _ => null };

    /// <summary><c>g-&lt;slug&gt;</c> id of a custom sub-group name (prototype <c>accGroupSlug</c>).</summary>
    public static string GroupSlug(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var norm = name.ToLowerInvariant().Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder();
        foreach (var ch in norm) sb.Append(ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : '-');
        var s = Dashes().Replace(sb.ToString(), "-").Trim('-');
        if (s.Length > 40) s = s[..40].TrimEnd('-');
        return "g-" + (s.Length == 0 ? "group" : s);
    }

    /// <summary>Plan of ACC_LEDGER_UPSERT. <paramref name="usage"/> is asked only when an Owner ledger changes nature.</summary>
    public static LedgerPlan UpsertLedger(EffectiveChart chart, LedgerUpsert input, Func<LedgerUsage> usage)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(usage);
        var code = T(input.Code);
        if (!CodePattern().IsMatch(code)) throw Fail(MasterReasons.Invalid, "A ledger code is 4 digits, e.g. 5130.", "code");
        var old = chart.Ledgers.FirstOrDefault(l => l.Code == code);
        var builtin = ChartOfAccounts.All.FirstOrDefault(l => l.Code == code);
        var name = input.Name is null && old is not null ? old.Name : T(input.Name);
        if (name.Length is 0 or > 60) throw Fail(MasterReasons.Invalid, "Enter the ledger name (1–60 characters).", "name");
        var group = input.GroupId is null && old is not null ? old.GroupId : T(input.GroupId);
        if (chart.GroupOf(group) is null) throw Fail(MasterReasons.Invalid, "Choose the group from the chart (one of Tally’s groups or a sub-group).", "group");
        var gst = input.Gst is null ? old?.Gst ?? LedgerGst.None : ParseGst(input.Gst) ?? throw Fail(MasterReasons.Invalid, "GST flag is none, input or output.", "gst");
        var tally = input.TallyName is null ? old?.TallyName ?? "" : T(input.TallyName);
        if (tally.Length > 60) throw Fail(MasterReasons.Invalid, "The Tally name is at most 60 characters.", "tallyName");
        var note = input.Note is null ? old?.Note ?? "" : T(input.Note);
        if (note.Length > 200) throw Fail(MasterReasons.Invalid, "The note is at most 200 characters.", "note");
        var active = old?.Active ?? true;
        if (input.Active is { } a)
        {
            if (!a && (old is null || old.Active)) throw Fail(MasterReasons.Invalid, "Retire a ledger with Retire (it checks balances and mappings).", "active");
            active = a;
        }

        if (input.Create && old is not null)
            throw Fail(MasterReasons.CodeTaken, $"Code {code} is already used by {old.Name}{(old.Active ? "" : " (retired: reactivate it instead)")}.", "code");
        if (old is null && !input.Create) throw Fail(MasterReasons.Invalid, $"No ledger {code} in the chart — add it as a new ledger.", "code");
        if (old is not null && old.GroupId != group)
        {
            var from = chart.LegacyOf(old.GroupId);
            var to = chart.LegacyOf(group);
            if (from != to)
            {
                if (old.System) throw Fail(MasterReasons.Invalid, $"{code} {old.Name} is a built-in {from} ledger: move it only to another {from} group (Tally nature).", "group");
                var u = usage();
                if (u.MappedBy.Count > 0 || u.Balance != 0) throw Fail(MasterReasons.Invalid, $"{code} {old.Name} is in use as a {from} ledger: move it only to another {from} group.", "group");
            }
        }

        var tName = tally.Length > 0 ? tally : name;
        foreach (var l in chart.Ledgers.Where(l => l.Code != code))
        {
            if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) throw Fail(MasterReasons.CodeTaken, $"{l.Code} is already called “{l.Name}”.", "name");
            if (string.Equals(l.EffectiveTallyName, tName, StringComparison.OrdinalIgnoreCase))
                throw Fail(MasterReasons.CodeTaken, $"{l.Code} already uses the Tally name “{l.EffectiveTallyName}”.", tally.Length > 0 ? "tallyName" : "name");
        }

        var changes = new List<ChartFieldChange>();
        if (old is null) changes.Add(new("created", "", $"{code} {name} · {string.Join(" › ", chart.PathOf(group))}"));
        else
        {
            void F(string f, string x, string y) { if (x != y) changes.Add(new(f, x, y)); }
            F("name", old.Name, name);
            F("group", old.GroupId, group);
            F("gst", GstText(old.Gst), GstText(gst));
            F("tallyName", old.TallyName ?? "", tally);
            F("note", old.Note ?? "", note);
            F("active", old.Active ? "active" : "retired", active ? "active" : "retired");
            if (changes.Count == 0) throw Fail(MasterReasons.Invalid, "Nothing changed.");
        }

        var row = new StoredLedger(code, name, group, gst, tally.Length > 0 ? tally : null, builtin is not null ? note : note.Length > 0 ? note : null, active);
        var isDefault = builtin is not null && builtin.Name == name && builtin.GroupId == group && builtin.Gst == gst && tally.Length == 0 && active && (builtin.Note ?? "") == note;
        var effective = new LedgerAccount(code, name, chart.LegacyOf(group), note.Length > 0 ? note : null)
        {
            GroupId = group, System = builtin is not null, Control = builtin?.Control ?? false, Gst = gst, TallyName = tally.Length > 0 ? tally : null, Active = active,
        };
        return new LedgerPlan(code, isDefault ? null : row, old is null, changes, effective);
    }

    /// <summary>Why <paramref name="code"/> cannot be retired now (null = it can) and the MASTER_* reason (prototype <c>accLedgerRetireBlocker</c>).</summary>
    public static (string Reason, string Message)? RetireBlocker(EffectiveChart chart, string code, LedgerUsage usage)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(usage);
        var l = chart.Ledgers.FirstOrDefault(x => x.Code == code);
        if (l is null) return (MasterReasons.Invalid, "This ledger was not found in the chart of accounts.");
        if (!l.Active) return (MasterReasons.Retired, $"{l.Code} {l.Name} is already retired.");
        if (Fixed.Contains(code)) return (MasterReasons.InUse, $"{l.Code} {l.Name} is structural and never retires.");
        var keys = usage.MappedBy;
        if (keys.Count > 0)
            return (MasterReasons.InUse, $"{l.Code} {l.Name} is the ledger of {keys.Count} mapping key{(keys.Count == 1 ? "" : "s")} ({string.Join(", ", keys.Take(3))}{(keys.Count > 3 ? ", …" : "")}) — map {(keys.Count == 1 ? "it" : "them")} to another ledger first.");
        if (usage.OwnBalance != 0) return (MasterReasons.InUse, $"{l.Code} {l.Name} has a balance of {DrCr(usage.OwnBalance)} — retire it only once the balance is zero.");
        var p = usage.PartyBalances.Where(x => x.Value != 0).ToList();
        if (p.Count > 0)
            return (MasterReasons.InUse, $"{p.Count} part{(p.Count == 1 ? "y" : "ies")} under {l.Code} {l.Name} still ha{(p.Count == 1 ? "s" : "ve")} a balance ({string.Join(", ", p.Take(3).Select(x => $"{x.Key} {DrCr(x.Value)}"))}) — retire it only once every party is zero.");
        return null;
    }

    /// <summary>"₹1,200 Dr" (Indian grouping, paise shown only when not whole rupees; prototype <c>money</c>).</summary>
    public static string DrCr(long paise) => $"{Rupees(Math.Abs(paise))} {(paise > 0 ? "Dr" : "Cr")}";

    private static string Rupees(long paise)
    {
        var whole = paise / 100;
        var digits = whole.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        if (digits.Length > 3)
        {
            var head = digits[..^3];
            for (var i = 0; i < head.Length; i++)
            {
                if (i > 0 && (head.Length - i) % 2 == 0) sb.Append(',');
                sb.Append(head[i]);
            }

            sb.Append(',').Append(digits[^3..]);
        }
        else sb.Append(digits);

        return "₹" + sb + (paise % 100 == 0 ? "" : "." + (paise % 100).ToString("00", CultureInfo.InvariantCulture));
    }

    /// <summary>Plan of ACC_GROUP_UPSERT (prototype <c>accGroupUpsertPlan</c>).</summary>
    public static GroupPlan UpsertGroup(EffectiveChart chart, GroupUpsert input)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(input);
        var id = T(input.Id);
        var old = id.Length > 0 ? chart.GroupOf(id) : null;
        if (id.Length > 0 && old is null) throw Fail(MasterReasons.Invalid, "This group was not found.", "id");
        if (old is { Custom: false }) throw Fail(MasterReasons.Invalid, $"{old.Name} is one of Tally’s 28 groups: it is fixed. Add a sub-group under it instead.", "id");
        var name = input.Name is null && old is not null ? old.Name : T(input.Name);
        if (name.Length is 0 or > 60) throw Fail(MasterReasons.Invalid, "Enter the group name (1–60 characters).", "name");
        var parent = input.Parent is null && old is not null ? old.Parent ?? "" : T(input.Parent);
        var p = chart.GroupOf(parent) ?? throw Fail(MasterReasons.Invalid, "Choose the parent group (a custom group is always a sub-group).", "parent");
        var gid = old?.Id ?? GroupSlug(name);
        if (old is null && chart.GroupOf(gid) is not null) throw Fail(MasterReasons.CodeTaken, $"A group with the id {gid} already exists.", "name");
        if (chart.Groups.Any(g => g.Id != gid && string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw Fail(MasterReasons.CodeTaken, $"A group called “{name}” already exists.", "name");
        if (old is not null && old.Parent != parent)
        {
            if (chart.IsUnder(parent, old.Id)) throw Fail(MasterReasons.Invalid, "A group cannot sit under itself or one of its own sub-groups.", "parent");
            var from = chart.LegacyOf(old.Id);
            var to = chart.LegacyOf(parent);
            if (from != to) throw Fail(MasterReasons.Invalid, $"{old.Name} is a {from} group: move it only under another {from} group (Tally nature).", "parent");
        }

        var changes = new List<ChartFieldChange>();
        if (old is null) changes.Add(new("created", "", $"{name} · under {string.Join(" › ", chart.PathOf(parent))}"));
        else
        {
            if (old.Name != name) changes.Add(new("name", old.Name, name));
            if (old.Parent != parent) changes.Add(new("parent", old.Parent ?? "", parent));
            if (changes.Count == 0) throw Fail(MasterReasons.Invalid, "Nothing changed.");
        }

        return new GroupPlan(new LedgerGroup(gid, name, parent, p.Nature, p.Side, true), old is null, changes);
    }
}
