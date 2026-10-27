using System.Text.RegularExpressions;
using Hcc.Contracts.Masters;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Accounting.Tests.Domain;

/// <summary>
/// ACC-1 lane acc1-chart (posting contract §1, §2; rulebook §3): Tally's 28 groups, the built-in ledgers of the §2 table, the mapping
/// re-points, the merged per-property chart and the chart master rules (same messages and MASTER_* reasons as the prototype
/// <c>accounting-chart.ts</c>, pinned by tests/stream-accounting-chart.mjs).
/// </summary>
public sealed partial class ChartTests
{
    /// <summary>The contract §2 table: code → (group, control).</summary>
    private static readonly Dictionary<string, (string Group, bool Control)> Table = new(StringComparer.Ordinal)
    {
        ["1000"] = ("cash", true), ["1005"] = ("cash", false), ["1010"] = ("bank", false), ["1020"] = ("bank", false), ["1030"] = ("bank", true),
        ["1100"] = ("debtors", false), ["1110"] = ("debtors", true), ["1115"] = ("debtors", true), ["1120"] = ("debtors", true),
        ["1200"] = ("loans-advances", true), ["1210"] = ("loans-advances", false), ["1300"] = ("duties-taxes", false), ["1310"] = ("duties-taxes", false),
        ["1320"] = ("duties-taxes", false), ["1330"] = ("duties-taxes", false), ["1340"] = ("duties-taxes", false), ["1350"] = ("duties-taxes", false),
        ["1360"] = ("loans-advances", false), ["1370"] = ("loans-advances", false), ["1400"] = ("suspense", false), ["1500"] = ("fixed-assets", false),
        ["1510"] = ("fixed-assets", false), ["1520"] = ("fixed-assets", false), ["1530"] = ("fixed-assets", false), ["1540"] = ("fixed-assets", false),
        ["2000"] = ("current-liabilities", false), ["2100"] = ("duties-taxes", false), ["2110"] = ("duties-taxes", false), ["2120"] = ("duties-taxes", false),
        ["2130"] = ("duties-taxes", false), ["2140"] = ("duties-taxes", false), ["2150"] = ("duties-taxes", false), ["2200"] = ("creditors", true),
        ["2300"] = ("current-liabilities", false), ["2310"] = ("duties-taxes", false), ["2320"] = ("duties-taxes", false), ["2330"] = ("duties-taxes", false),
        ["2340"] = ("current-liabilities", false), ["2400"] = ("secured-loans", true), ["2410"] = ("unsecured-loans", false), ["3000"] = ("capital", false),
        ["3100"] = ("capital", false), ["4000"] = ("sales", false), ["4100"] = ("sales", false), ["4200"] = ("sales", false), ["4300"] = ("sales", false),
        ["4400"] = ("sales", false), ["4900"] = ("sales", false), ["4910"] = ("sales", false), ["4950"] = ("indirect-income", false),
        ["4960"] = ("indirect-income", false), ["5000"] = ("direct-expenses", false), ["5010"] = ("direct-expenses", false), ["5015"] = ("direct-expenses", false),
        ["5020"] = ("indirect-expenses", false), ["5030"] = ("direct-expenses", false), ["5040"] = ("indirect-expenses", false), ["5050"] = ("indirect-expenses", false),
        ["5060"] = ("indirect-expenses", false), ["5070"] = ("indirect-expenses", false), ["5080"] = ("indirect-expenses", false), ["5090"] = ("indirect-expenses", false),
        ["5100"] = ("indirect-expenses", false), ["5110"] = ("indirect-expenses", false), ["5120"] = ("indirect-expenses", false), ["5200"] = ("indirect-expenses", false),
        ["5300"] = ("indirect-expenses", false), ["5400"] = ("indirect-expenses", false), ["5900"] = ("indirect-expenses", false), ["9999"] = ("suspense", false),
    };

    private static string Reason(HccException ex) => ex.Details is not null && ex.Details.TryGetValue("reason", out var r) ? r.GetString()! : "";

    private static HccException Refused(string reason, Action act)
    {
        var ex = Assert.Throws<HccException>(act);
        Assert.True(Reason(ex) == reason, $"Expected {reason} but got {Reason(ex)}: {ex.Message}");
        return ex;
    }

    private static LedgerUpsert New(string code, string name, string group, string? tally = null)
        => new(true, code, name, group, null, tally, null, null);

    private static LedgerUpsert Edit(string code, string? name = null, string? group = null, bool? active = null, string? tally = null)
        => new(false, code, name, group, null, tally, null, active);

    [Fact]
    public void Tally_groups_are_28_with_15_primary_and_sub_groups_inheriting_nature_and_side()
    {
        var g = ChartOfAccounts.Groups;
        Assert.Equal(28, g.Count);
        Assert.Equal(15, g.Count(x => x.Parent is null));
        Assert.Equal(28, g.Select(x => x.Id).Distinct().Count());
        foreach (var s in g.Where(x => x.Parent is not null))
        {
            var p = ChartOfAccounts.GroupOf(s.Parent)!;
            Assert.Null(p.Parent);
            Assert.Equal((p.Nature, p.Side), (s.Nature, s.Side));
        }

        var c = ChartOfAccounts.Builtin;
        Assert.Equal(
            [AccountGroup.Asset, AccountGroup.Asset, AccountGroup.Liability, AccountGroup.Liability, AccountGroup.Income, AccountGroup.Expense, AccountGroup.Suspense, AccountGroup.Liability],
            new[] { "current-assets", "bank", "current-liabilities", "duties-taxes", "sales", "indirect-expenses", "suspense", "capital" }.Select(c.LegacyOf));
        Assert.Equal(["Current Assets", "Bank Accounts"], c.PathOf("bank"));
    }

    [Fact]
    public void Built_in_ledgers_are_the_contract_table_with_group_control_and_derived_legacy_group()
    {
        Assert.Equal(Table.Keys.Order(StringComparer.Ordinal), ChartOfAccounts.All.Select(a => a.Code));
        foreach (var a in ChartOfAccounts.All)
        {
            Assert.Equal(Table[a.Code], (a.GroupId, a.Control));
            Assert.True(a.System && a.Active, a.Code);
            Assert.Equal(ChartOfAccounts.Builtin.LegacyOf(a.GroupId), a.Group);
        }

        Assert.Equal(["1000", "1030", "1110", "1115", "1120", "1200", "2200", "2400"], ChartOfAccounts.All.Where(a => a.Control).Select(a => a.Code));
        Assert.Equal((AccountGroup.Suspense, AccountGroup.Income, AccountGroup.Liability), (ChartOfAccounts.Find("1400")!.Group, ChartOfAccounts.Find("4950")!.Group, ChartOfAccounts.Find("1310")!.Group));
        Assert.Equal(("Food & beverage revenue", "Cash short", "Cash over"), (ChartOfAccounts.NameOf("4200"), ChartOfAccounts.NameOf("5900"), ChartOfAccounts.NameOf("4950")));
    }

    [GeneratedRegex(@"BL\((?:'(?<c>\d{4})'|ACC_SUSPENSE),(?:'(?<n>[^']*)'|""(?<n>[^""]*)""),'(?<g>[a-z-]+)'(?:,\{(?<x>[^}]*)\})?\)")]
    private static partial Regex ProtoLedger();

    [Fact]
    public void Built_in_ledgers_equal_the_prototype_ACC_LEDGERS_name_group_control_and_gst()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "03 Prototype", "src", "domain", "streams", "accounting.ts"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var ts = File.ReadAllText(Path.Combine(dir!.FullName, "03 Prototype", "src", "domain", "streams", "accounting.ts"));
        var proto = ProtoLedger().Matches(ts).Select(m => (
            Code: m.Groups["c"].Success ? m.Groups["c"].Value : "9999", Name: m.Groups["n"].Value, Group: m.Groups["g"].Value,
            Control: m.Groups["x"].Value.Contains("control:true", StringComparison.Ordinal),
            Gst: m.Groups["x"].Value.Contains("gst:'input'", StringComparison.Ordinal) ? LedgerGst.Input : m.Groups["x"].Value.Contains("gst:'output'", StringComparison.Ordinal) ? LedgerGst.Output : LedgerGst.None)).ToList();
        Assert.Equal(ChartOfAccounts.All.Count, proto.Count);
        Assert.Equal(proto, ChartOfAccounts.All.Select(a => (a.Code, a.Name, Group: a.GroupId, a.Control, a.Gst)).ToList());
    }

    [Fact]
    public void Mapping_re_points_of_acc1_and_every_default_resolves_to_a_live_ledger()
    {
        var m = ChartOfAccounts.DefaultMapping;
        Assert.Equal(("4200", "4300", "5900", "4950", "1310", "1320", "1120"),
            (m["pos:sales"], m["line:attrition"], m["closing:cash-difference"], m["closing:cash-over"], m["tax:gst-input"], m["tax:gst-input-sgst"], m["receipt:udhaar"]));
        Assert.All(m, kv => Assert.True(ChartOfAccounts.IsValidKey(kv.Key) && ChartOfAccounts.Resolve(m, kv.Key) is not null, kv.Key));
        Assert.Equal(("closing:cash-difference", "closing:cash-over"), (ChartOfAccounts.CashDiffKey(-5_000), ChartOfAccounts.CashDiffKey(5_000)));
        Assert.Equal((4_501L, 4_500L), ChartOfAccounts.ItcSplit(9_001));
        Assert.Equal((-4_501L, -4_500L), ChartOfAccounts.ItcSplit(-9_001));
        Assert.Equal("GST input credit (ITC)", ChartOfAccounts.KeyGroup(ChartOfAccounts.GstInputSgstKey));
    }

    [Fact]
    public void A_party_ledger_id_resolves_to_its_control_and_sorts_with_it()
    {
        Assert.Equal("1110", ChartOfAccounts.Find("1110/CMP-0007")!.Code);
        Assert.Null(ChartOfAccounts.Find("4000/X"));
        Assert.Null(ChartOfAccounts.Find("7777"));
        Assert.Equal(ChartOfAccounts.OrderOf("2200"), ChartOfAccounts.OrderOf("2200/EV-MORNING"));
        Assert.Equal(("1030", "4000"), (ChartOfAccounts.ControlPart("1030/BA-HDFC"), ChartOfAccounts.ControlPart("4000")));
        Assert.Equal(("Cr", "Dr", null), (ChartOfAccounts.Builtin.NatureOf("2200/EV-1"), ChartOfAccounts.Builtin.NatureOf("5100"), ChartOfAccounts.Builtin.NatureOf("7777")));
    }

    [Fact]
    public void Merge_overlays_owner_overrides_and_adds_owner_ledgers_and_custom_groups()
    {
        var c = ChartOfAccounts.Merge(
            [new StoredLedger("4100", "Laundry & other services", "g-services", LedgerGst.None, "Other Income", "", true), new StoredLedger("5130", "Guest amenities", "indirect-expenses", LedgerGst.None, null, null, true),
             new StoredLedger("1030", "Bank accounts (fictional)", "bank", LedgerGst.None, null, null, true)],
            [new LedgerGroup("g-services", "Services", "sales", "Cr", "PL")]);
        var l = c.Find("4100")!;
        Assert.Equal(("Laundry & other services", "g-services", AccountGroup.Income, true, "Other Income", (string?)null), (l.Name, l.GroupId, l.Group, l.System, l.EffectiveTallyName, l.Note));
        var o = c.Find("5130")!;
        Assert.Equal((false, false, AccountGroup.Expense, "Dr"), (o.System, o.Control, o.Group, c.NatureOf("5130")));
        Assert.True(c.Find("1030")!.Control, "a control stays a control");
        Assert.Equal("Bank accounts (fictional)", c.NameOf("1030/BA-HDFC"));
        Assert.Equal(ChartOfAccounts.All.Count + 1, c.Ledgers.Count);
        Assert.True(c.GroupOf("g-services")!.Custom);
        Assert.Equal(["Sales Accounts", "Services"], c.PathOf("g-services"));
        Assert.Equal("Other services revenue", ChartOfAccounts.NameOf("4100"));
        Assert.Equal("5130", ChartOfAccounts.Resolve(new Dictionary<string, string> { ["expense:Amenities"] = "5130" }, "expense:Amenities", c));
        Assert.Null(ChartOfAccounts.Resolve(new Dictionary<string, string> { ["expense:Amenities"] = "5130" }, "expense:Amenities"));
        Assert.Equal("5130", ChartOfAccounts.Effective(null, new Dictionary<string, string> { ["expense:Amenities"] = "5130" }, c)["expense:Amenities"]);
    }

    [Fact]
    public void Upsert_ledger_rules_match_the_prototype()
    {
        var c = ChartOfAccounts.Builtin;
        var p = ChartRules.UpsertLedger(c, New("5130", "Guest amenities (fictional)", "indirect-expenses"), () => LedgerUsage.None);
        Assert.True(p.Created);
        Assert.Equal("created", Assert.Single(p.Changes).Field);
        Assert.Equal(new StoredLedger("5130", "Guest amenities (fictional)", "indirect-expenses", LedgerGst.None, null, null, true), p.Row);
        Refused(MasterReasons.CodeTaken, () => ChartRules.UpsertLedger(c, New("4000", "Other", "sales"), () => LedgerUsage.None));
        Refused(MasterReasons.Invalid, () => ChartRules.UpsertLedger(c, New("51A0", "X", "sales"), () => LedgerUsage.None));
        Refused(MasterReasons.Invalid, () => ChartRules.UpsertLedger(c, New("5140", "", "sales"), () => LedgerUsage.None));
        Refused(MasterReasons.Invalid, () => ChartRules.UpsertLedger(c, New("5140", "X", "nope"), () => LedgerUsage.None));
        Refused(MasterReasons.Invalid, () => ChartRules.UpsertLedger(c, new LedgerUpsert(true, "5140", "X", "sales", "maybe", null, null, null), () => LedgerUsage.None));
        Refused(MasterReasons.CodeTaken, () => ChartRules.UpsertLedger(c, New("5140", "room revenue", "sales"), () => LedgerUsage.None));
        Refused(MasterReasons.CodeTaken, () => ChartRules.UpsertLedger(c, New("5140", "Sundries", "sales", "Room Revenue"), () => LedgerUsage.None));
        Refused(MasterReasons.Invalid, () => ChartRules.UpsertLedger(c, Edit("5140", "X", "sales"), () => LedgerUsage.None));

        // Built-ins: rename; re-group within the same Tally nature only; back to defaults = no stored row.
        var r = ChartRules.UpsertLedger(c, Edit("4100", group: "indirect-income"), () => LedgerUsage.None);
        Assert.Equal(("group", "sales", "indirect-income"), (r.Changes[0].Field, r.Changes[0].Before, r.Changes[0].After));
        Assert.Contains("Income group", Refused(MasterReasons.Invalid, () => ChartRules.UpsertLedger(c, Edit("4100", group: "indirect-expenses"), () => LedgerUsage.None)).Message, StringComparison.Ordinal);
        Refused(MasterReasons.Invalid, () => ChartRules.UpsertLedger(c, Edit("1000", group: "suspense"), () => LedgerUsage.None));
        Refused(MasterReasons.Invalid, () => ChartRules.UpsertLedger(c, Edit("9999", group: "current-assets"), () => LedgerUsage.None));
        Refused(MasterReasons.Invalid, () => ChartRules.UpsertLedger(c, Edit("4400", active: false), () => LedgerUsage.None));
        Assert.Contains("Nothing changed", Refused(MasterReasons.Invalid, () => ChartRules.UpsertLedger(c, Edit("4100", "Other services revenue"), () => LedgerUsage.None)).Message, StringComparison.Ordinal);
        var renamed = ChartOfAccounts.Merge([new StoredLedger("4100", "Laundry & other services", "sales", LedgerGst.None, null, "Laundry, extras and other services", true)], null);
        Assert.Null(ChartRules.UpsertLedger(renamed, Edit("4100", "Other services revenue"), () => LedgerUsage.None).Row);

        // An Owner ledger may change nature only while unused.
        var owned = ChartOfAccounts.Merge([new StoredLedger("5130", "Guest amenities", "indirect-expenses", LedgerGst.None, null, null, true)], null);
        Assert.Equal("sales", ChartRules.UpsertLedger(owned, Edit("5130", group: "sales"), () => LedgerUsage.None).Row!.GroupId);
        Refused(MasterReasons.Invalid, () => ChartRules.UpsertLedger(owned, Edit("5130", group: "sales"), () => new LedgerUsage(["expense:Amenities"], 0, new Dictionary<string, long>())));
        Refused(MasterReasons.Invalid, () => ChartRules.UpsertLedger(owned, Edit("5130", group: "sales"), () => new LedgerUsage([], 100, new Dictionary<string, long>())));
        var retired = ChartOfAccounts.Merge([new StoredLedger("4400", "Banquet & events revenue", "sales", LedgerGst.None, null, "", false)], null);
        Assert.Contains("retired: reactivate", Refused(MasterReasons.CodeTaken, () => ChartRules.UpsertLedger(retired, New("4400", "X", "sales"), () => LedgerUsage.None)).Message, StringComparison.Ordinal);
        Assert.Null(ChartRules.UpsertLedger(retired, Edit("4400", active: true), () => LedgerUsage.None).Row);
    }

    [Fact]
    public void Retire_is_refused_while_mapped_with_a_balance_with_a_party_balance_and_for_structural_ledgers()
    {
        var c = ChartOfAccounts.Builtin;
        Assert.Null(ChartRules.RetireBlocker(c, "4400", LedgerUsage.None));
        Assert.Equal(MasterReasons.Invalid, ChartRules.RetireBlocker(c, "7777", LedgerUsage.None)!.Value.Reason);
        foreach (var code in new[] { "9999", "1400" })
            Assert.Contains("structural", ChartRules.RetireBlocker(c, code, LedgerUsage.None)!.Value.Message, StringComparison.Ordinal);
        var mapped = ChartRules.RetireBlocker(c, "4000", new LedgerUsage(["line:room"], 0, new Dictionary<string, long>()))!.Value;
        Assert.Equal(MasterReasons.InUse, mapped.Reason);
        Assert.Contains("line:room", mapped.Message, StringComparison.Ordinal);
        Assert.Contains("balance of ₹1,680 Cr", ChartRules.RetireBlocker(c, "4400", new LedgerUsage([], -168_000, new Dictionary<string, long>()))!.Value.Message, StringComparison.Ordinal);
        Assert.Contains("CHN-GOIBIBO ₹1,200 Dr", ChartRules.RetireBlocker(c, "1115", new LedgerUsage([], 0, new Dictionary<string, long> { ["CHN-GOIBIBO"] = 120_000, ["CHN-MMT"] = 0 }))!.Value.Message, StringComparison.Ordinal);
        var retired = ChartOfAccounts.Merge([new StoredLedger("4400", "Banquet & events revenue", "sales", LedgerGst.None, null, "", false)], null);
        Assert.Equal(MasterReasons.Retired, ChartRules.RetireBlocker(retired, "4400", LedgerUsage.None)!.Value.Reason);
        Assert.Equal(("₹12,34,567.89 Cr", "₹0 Cr", "₹999 Dr"), (ChartRules.DrCr(-123_456_789), ChartRules.DrCr(0), ChartRules.DrCr(99_900)));
    }

    [Fact]
    public void Custom_sub_groups_inherit_nature_tally_groups_are_fixed_and_re_parenting_keeps_the_nature_without_loops()
    {
        var c = ChartOfAccounts.Builtin;
        var p = ChartRules.UpsertGroup(c, new GroupUpsert(null, "Banquet income", "sales"));
        Assert.Equal(new LedgerGroup("g-banquet-income", "Banquet income", "sales", "Cr", "PL", true), p.Row);
        Assert.True(p.Created);
        Assert.Contains("fixed", Refused(MasterReasons.Invalid, () => ChartRules.UpsertGroup(c, new GroupUpsert("sales", "Revenue", null))).Message, StringComparison.Ordinal);
        Refused(MasterReasons.Invalid, () => ChartRules.UpsertGroup(c, new GroupUpsert(null, "X", "nope")));
        var c2 = ChartOfAccounts.Merge(null, [p.Row, new LedgerGroup("g-weddings", "Weddings", "g-banquet-income", "Cr", "PL")]);
        Refused(MasterReasons.CodeTaken, () => ChartRules.UpsertGroup(c2, new GroupUpsert(null, "Banquet Income", "sales")));
        Assert.Contains("under itself", Refused(MasterReasons.Invalid, () => ChartRules.UpsertGroup(c2, new GroupUpsert("g-banquet-income", null, "g-weddings"))).Message, StringComparison.Ordinal);
        Assert.Contains("Income group", Refused(MasterReasons.Invalid, () => ChartRules.UpsertGroup(c2, new GroupUpsert("g-banquet-income", null, "indirect-expenses"))).Message, StringComparison.Ordinal);
        var moved = ChartRules.UpsertGroup(c2, new GroupUpsert("g-banquet-income", "Banquet & hall income", "indirect-income"));
        Assert.Equal(["name", "parent"], moved.Changes.Select(x => x.Field));
        Assert.Equal("sales", c2.RootOf("g-weddings")!.Id);
        Assert.Equal(AccountGroup.Suspense, ChartOfAccounts.Merge(null, [new LedgerGroup("g-clearing", "Clearing", "suspense", "Dr", "BS")]).LegacyOf("g-clearing"));
        Assert.Equal("g-rooms-deluxe-suites", ChartRules.GroupSlug("  Rooms — Deluxe & Suites  "));
    }

    [Fact]
    public void Chart_problems_find_retired_or_unknown_mapping_targets_missing_groups_broken_groups_and_duplicate_names()
    {
        Assert.Empty(ChartOfAccounts.Builtin.Problems(ChartOfAccounts.DefaultMapping));
        var bad = ChartOfAccounts.Merge(
            [new StoredLedger("4400", "Banquet & events revenue", "sales", LedgerGst.None, null, "", false), new StoredLedger("6000", "Room revenue", "g-gone", LedgerGst.None, null, null, true)],
            [new LedgerGroup("g-orphan", "Orphan", "g-missing", "Dr", "PL")]);
        var mapping = ChartOfAccounts.Effective(new Dictionary<string, string> { ["line:service"] = "4400", ["expense:Laundry"] = "6100" });
        Assert.Equal(["dup:6000", "group:g-orphan", "ledger-group:6000", "map:expense:Laundry", "map:line:service"], bad.Problems(mapping).Select(x => x.Id).Order(StringComparer.Ordinal));
        Assert.All(bad.Problems(mapping), x => Assert.Equal("error", x.Severity));
    }
}
