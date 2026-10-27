using System.Text.RegularExpressions;
using Hcc.Access.Tests.Harness;
using Hcc.Contracts;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;

namespace Hcc.Access.Tests.Sql;

/// <summary>
/// Role matrix v2 (DEC-079, owner decisions D1–D7; migration 3700): the seeded module permissions equal the grids of
/// <c>02 Planning/HCC_RBAC_Matrix_v2_Proposal.md</c> §2.3/§2.4 cell by cell (the golden grid of the proposal's test plan §3.3), the money
/// invariant holds, the Restaurant role exists as decided, and the Cashier keeps the collections report without the books.
/// </summary>
[Trait("Category", "Sql")]
public partial class RbacV2MatrixTests(AccessDatabase db) : IClassFixture<AccessDatabase>
{
    /// <summary>Column order of the v2 grids.</summary>
    private static readonly string[] Roles = ["OWN", "MGR", "ACC", "REC", "CSH", "SUP", "HKP", "MNT", "RST", "TAD"];

    /// <summary>Later owner rulings that change a v2 cell (none yet). Key = (permission, role), value = the ruled cell ("—" = no grant).</summary>
    public static readonly Dictionary<(string Perm, string Role), string> Rulings = [];

    /// <summary>
    /// Permissions added after the accepted grids, one row each in <see cref="Roles"/> order ("—" = no grant): every cell is pinned like a
    /// grid cell (and the money invariant covers them).
    /// </summary>
    public static readonly Dictionary<string, string[]> LaterPermissions = new(StringComparer.Ordinal)
    {
        // Wave C3 (audit A09-05, migration 3921): System health — parked outbox events, retry, last backup. Administration: Owner, Tech admin.
        ["PERM-SYS-001"] = ["A", "—", "—", "—", "—", "—", "—", "—", "—", "A"],
    };

    /// <summary>L cells: the Manager's limit in paise (DEC-063 G1 Rs 1,000; GRP-003 Rs 10,000 as seeded by 2201).</summary>
    private static readonly Dictionary<(string Perm, string Role), long> Limits = new()
    {
        [("PERM-POS-003", "MGR")] = 100_000,
        [("PERM-GSV-003", "MGR")] = 100_000,
        [("PERM-GRP-003", "MGR")] = 1_000_000,
    };

    [GeneratedRegex(@"^\| (PERM-[A-Z]+-\d{3}) \| [^|]+ \|(.*)\|\s*$")]
    private static partial Regex GridRow();

    private static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException("Not found above the test output folder: " + Path.Combine(parts));
    }

    /// <summary>Every grid cell of the proposal (§2.3 and §2.4); "—" = no grant. Cells read "A", "L", "R", "V", "M" or mode·scope ("A·S").</summary>
    public static Dictionary<(string Perm, string Role), string> GridCells()
    {
        var cells = new Dictionary<(string, string), string>();
        foreach (var line in File.ReadLines(RepoFile("02 Planning", "HCC_RBAC_Matrix_v2_Proposal.md")))
        {
            var m = GridRow().Match(line);
            if (!m.Success) continue;
            var values = m.Groups[2].Value.Split('|').Select(v => v.Trim()).ToArray();
            Assert.Equal(Roles.Length, values.Length);
            for (var i = 0; i < Roles.Length; i++) cells[(m.Groups[1].Value, Roles[i])] = values[i];
        }

        foreach (var (perm, row) in LaterPermissions)
        {
            Assert.Equal(Roles.Length, row.Length);
            Assert.DoesNotContain(cells.Keys, k => k.Item1 == perm);   // a later permission is not a grid row
            for (var i = 0; i < Roles.Length; i++) cells[(perm, Roles[i])] = row[i];
        }

        foreach (var (key, value) in Rulings)
        {
            Assert.True(cells.ContainsKey(key), $"Ruling for unknown cell {key}");
            cells[key] = value;
        }

        return cells;
    }

    private async Task<Dictionary<(string Perm, string Role), (string Cell, long? Limit)>> SeededAsync(IReadOnlySet<string> perms)
    {
        var rows = (await db.RowsAsync("SELECT permission_id, role_code, mode, scope, limit_value FROM plat.role_grant;")).Where(r => perms.Contains((string)r[0]));
        return rows.ToDictionary(r => ((string)r[0], (string)r[1]), r =>
        {
            var scope = (string)r[3];
            return (scope == "P" ? (string)r[2] : $"{r[2]}·{scope}", r[4] is DBNull ? (long?)null : (long)r[4]);
        });
    }

    [Fact]
    public async Task Seeded_module_grants_equal_the_v2_grids_cell_by_cell()
    {
        var grid = GridCells();
        var perms = grid.Keys.Select(k => k.Perm).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(64 + LaterPermissions.Count, perms.Count);   // §2.3 (35 rows) + §2.4 (29 rows) of the accepted proposal + later permissions
        var seeded = await SeededAsync(perms);
        var mismatches = new List<string>();
        foreach (var ((perm, role), cell) in grid)
        {
            var has = seeded.TryGetValue((perm, role), out var s);
            if (cell == "—" && has) mismatches.Add($"{perm} {role}: seeded {s.Cell}, grid —");
            else if (cell != "—" && (!has || s.Cell != cell)) mismatches.Add($"{perm} {role}: seeded {(has ? s.Cell : "—")}, grid {cell}");
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
        Assert.DoesNotContain(seeded.Keys, k => !grid.ContainsKey(k));   // no grant on a grid permission for a role outside the grid
        var unseeded = perms.Except((await db.RowsAsync("SELECT permission_id FROM plat.permission;")).Select(r => (string)r[0])).ToList();
        Assert.True(unseeded.Count == 0, "Grid permissions not seeded: " + string.Join(", ", unseeded));
        foreach (var ((perm, role), limit) in Limits) Assert.Equal(limit, seeded[(perm, role)].Limit);
        Assert.All(seeded.Where(s => s.Value.Cell.StartsWith('L')), s => Assert.True(Limits.ContainsKey(s.Key), $"{s.Key} is L without a known limit"));
    }

    [Fact]
    public async Task Money_invariant_housekeeping_maintenance_and_restaurant_hold_no_money_permission()
    {
        // §2.1: SUP, HKP, MNT and RST hold no is_money permission (own pay = PERM-HR-004 is not money-flagged). The one exception is the
        // Restaurant role's request-only cell on the void decision (R never allows or reveals anything: AccessEvaluator answers APPROVAL_REQUIRED).
        var rows = await db.RowsAsync("""
            SELECT g.role_code, g.permission_id, g.mode FROM plat.role_grant g JOIN plat.permission p ON p.permission_id = g.permission_id
             WHERE p.is_money = 1 AND g.role_code IN ('SUP', 'HKP', 'MNT', 'RST');
            """);
        Assert.Equal(["RST PERM-POS-003 R"], rows.Select(r => $"{r[0]} {r[1]} {r[2]}").ToArray());
        // Every module's money fields ride on its own reveal permission (§2.1 number 004), which is money-flagged and a VIEW (so a V cell reveals).
        string[] reveals = ["PERM-CRM-004", "PERM-GRP-004", "PERM-GSV-004", "PERM-HND-004", "PERM-MNT-004", "PERM-OTA-004", "PERM-POS-004", "PERM-STK-004", "PERM-WEB-004"];
        var kinds = (await db.RowsAsync("SELECT permission_id, kind, is_money FROM plat.permission;")).ToDictionary(r => (string)r[0], r => ((string)r[1], (bool)r[2]));
        Assert.All(reveals, id => Assert.Equal(("VIEW", true), kinds[id]));
    }

    [Fact]
    public async Task Restaurant_role_is_a_pin_role_assigned_by_the_Owner_with_pos_operate_and_own_pay_only()
    {
        var role = Assert.Single(await db.RowsAsync("SELECT name, credential_kind, owner_only_assign FROM plat.role WHERE role_code = 'RST';"));
        Assert.Equal(("Restaurant", "PIN", true), ((string)role[0], (string)role[1], (bool)role[2]));
        var grants = await db.RowsAsync("SELECT permission_id, mode, scope FROM plat.role_grant WHERE role_code = 'RST' ORDER BY permission_id;");
        Assert.Equal(["PERM-HR-004 A O", "PERM-POS-001 A P", "PERM-POS-002 A P", "PERM-POS-003 R P"], grants.Select(r => $"{r[0]} {r[1]} {r[2]}").ToArray());
        Assert.Contains(RoleCodes.Restaurant, RoleCodes.All);
        Assert.DoesNotContain(RoleCodes.Restaurant, RoleCodes.PasswordRoles);
    }

    [Fact]
    public async Task Cashier_keeps_the_collections_report_but_not_the_books_and_no_stock_or_maintenance_money()
    {
        // DEC-079: no whole-hotel books (A05-03) and D3: no stock or maintenance money; the finance-report cell V·S stays (collections).
        var csh = (await db.RowsAsync("SELECT permission_id, mode, scope FROM plat.role_grant WHERE role_code = 'CSH';")).Select(r => $"{r[0]} {r[1]}·{r[2]}").ToHashSet();
        Assert.DoesNotContain(csh, g => g.StartsWith("PERM-ACC-", StringComparison.Ordinal) || g.StartsWith("PERM-STK-", StringComparison.Ordinal) || g.StartsWith("PERM-MNT-", StringComparison.Ordinal));
        Assert.Contains("PERM-RPT-002 V·S", csh);
        var clock = FakeClock.At2026();
        var policy = db.Module(clock).Policy;
        var cashier = await db.SeedUserAsync("cashier.v2", ["CSH"]);
        await policy.RequireAsync(db.PropertyId, cashier, "PERM-RPT-002", Hcc.Application.Access.AccessScopes.Any);   // collections (BillingQueries)
        var tb = await Assert.ThrowsAsync<HccException>(() => policy.RequireAsync(db.PropertyId, cashier, "PERM-ACC-001"));   // trial balance
        Assert.Equal(HccErrorCode.FORBIDDEN, tb.Code);
        var daily = await Assert.ThrowsAsync<HccException>(() => policy.RequireAsync(db.PropertyId, cashier, "PERM-RPT-002"));   // property-wide reports
        Assert.Equal(HccErrorCode.FORBIDDEN, daily.Code);
    }

    [Fact]
    public async Task Tech_admin_edits_templates_and_non_money_settings_but_never_money_settings_D4()
    {
        var policy = db.Module(FakeClock.At2026()).Policy;
        var tad = await db.SeedUserAsync("tad.v2", ["TAD"]);
        await policy.RequireAsync(db.PropertyId, tad, "PERM-MSG-003");                                   // message templates
        await policy.RequireAsync(db.PropertyId, tad, "PERM-CFG-002");                                   // non-money groups
        await policy.RequireAsync(db.PropertyId, tad, "PERM-CFG-001");                                   // read the hub (V on a VIEW permission)
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => policy.RequireAsync(db.PropertyId, tad, "PERM-CFG-003"))).Code);
        Assert.False(await policy.HasAsync(db.PropertyId, tad, "PERM-YLD-003"));
        Assert.False(await policy.HasAsync(db.PropertyId, tad, "PERM-POS-006"));
    }

    /// <summary>
    /// Parity (§3.3): for every gate the module code switched from a borrowed core id to its own id, the roles that pass it before and after.
    /// The only differences are the deliberate ones (§3.4 C1–C5, the decisions D1–D7 and the grid cells); every other gate admits the same
    /// roles as before. Old ids with ":VIEW" count V/M cells; <c>Wide</c> = the gate needs a property-wide grant.
    /// </summary>
    public static readonly (string Gate, string[] Old, string[] New, bool Wide, string Gained, string Lost)[] Parity =
    [
        ("POS view", ["PERM-FOL-002"], ["PERM-POS-001"], true, "ACC RST", ""),           // D1 Restaurant; the Accountant reads (grid V)
        ("POS operate", ["PERM-FOL-002"], ["PERM-POS-002"], true, "RST", ""),            // D1
        ("POS settle", ["PERM-PAY-001"], ["PERM-POS-005"], false, "", ""),
        ("POS void decide", ["PERM-APR-001"], ["PERM-POS-003"], true, "CSH REC RST", ""), // request-only cells (R): they ask, never decide
        ("POS day sales", ["PERM-FOL-002"], ["PERM-POS-004"], false, "ACC", "REC"),       // grid: day sales are money
        ("POS setup", ["PERM-ADM-003"], ["PERM-POS-006"], true, "MGR", ""),              // D5: the Manager requests (R)
        ("Stock read", ["PERM-RPT-002:VIEW", "PERM-HK-003"], ["PERM-STK-001", "PERM-STK-004"], false, "", "CSH"),   // C1 / D3
        ("Stock work", ["PERM-HK-003"], ["PERM-STK-002"], false, "", ""),
        ("Stock approve", ["PERM-HK-004"], ["PERM-STK-003"], true, "", ""),
        ("Stock configure", ["PERM-HK-004"], ["PERM-STK-005"], true, "", ""),
        ("Stock money", ["PERM-RPT-002:VIEW"], ["PERM-STK-004"], false, "", "CSH"),      // C1 / D3
        ("Maintenance view", ["PERM-HK-001"], ["PERM-MNT-001"], false, "", ""),
        ("Maintenance work", ["PERM-ROOM-001"], ["PERM-MNT-002"], false, "", "REC"),     // DEC-063 G7: was a hand-written guard
        ("Maintenance manage", ["PERM-HK-002", "PERM-HK-004", "PERM-ROOM-002"], ["PERM-MNT-003"], true, "", ""),
        ("Maintenance money", ["PERM-RPT-002:VIEW"], ["PERM-MNT-004"], false, "", "CSH"), // C1 / D3
        ("AMC contracts", ["PERM-HK-004"], ["PERM-MNT-005", "PERM-MNT-004"], true, "ACC", "SUP"),   // grid MNT-005 (G12: Owner, Manager; Accountant reads)
        ("Guest services view", ["PERM-GST-002", "PERM-HK-002", "PERM-HK-003", "PERM-ROOM-001"], ["PERM-GSV-001"], false, "", ""),
        ("Guest services desk", ["PERM-GST-002"], ["PERM-GSV-002"], true, "SUP", ""),    // grid GSV-001/002: the supervisor logs and sees every department
        ("Guest services assign", ["PERM-HK-002"], ["PERM-GSV-005"], true, "", ""),
        ("Guest services approve", ["PERM-APR-001"], ["PERM-GSV-003"], true, "", ""),
        ("Guest services recovery money", ["PERM-GST-002"], ["PERM-GSV-004"], true, "", ""),
        ("Messages view", ["PERM-RES-001"], ["PERM-MSG-001"], true, "", ""),
        ("Messages send", ["PERM-RES-002"], ["PERM-MSG-002"], true, "", ""),
        ("Messages consent fallback", ["PERM-STY-001"], ["PERM-MSG-005"], true, "", ""),
        ("Message templates", ["PERM-APR-001"], ["PERM-MSG-003"], true, "TAD", ""),      // C2 / D4
        ("Handover view", ["PERM-HK-001", "PERM-PAY-001", "PERM-APR-001"], ["PERM-HND-001"], false, "", ""),
        ("Handover manager", ["PERM-APR-001"], ["PERM-HND-003"], true, "", ""),
        ("Handover cash", ["PERM-PAY-001", "PERM-RPT-002:VIEW"], ["PERM-HND-004"], false, "", ""),   // C4: never HKP / MNT
        ("Yield view", ["PERM-RPT-001"], ["PERM-YLD-001"], true, "", ""),
        ("Yield propose", ["PERM-APR-001"], ["PERM-YLD-002"], true, "", ""),
        ("Yield decide", ["PERM-ADM-003"], ["PERM-YLD-003"], true, "", ""),               // C3: same result, own reason
        ("Configuration view", ["PERM-ADM-003:VIEW"], ["PERM-CFG-001"], true, "", ""),
        ("Configuration money / tax / tills / re-key", ["PERM-ADM-003"], ["PERM-CFG-003"], true, "", ""),
        ("Configuration requests decided", ["PERM-APR-001"], ["PERM-CFG-003"], true, "", "MGR"),   // grid CFG-003: the Manager has no configuration page
        // SCC decisions wave DEC-129 (owner, 27 Oct 2026): expense budgets and recurring templates are Owner-only money settings (DEC-058) —
        // the gate moved from expense.approve to the Owner's masters.manage-money (no new PERM id); posting a due template stays PERM-EXP-001.
        ("Expense budgets and recurring templates", ["PERM-EXP-002"], ["PERM-MST-002"], true, "", "ACC MGR"),
    ];

    [Fact]
    public async Task Parity_every_switched_gate_admits_the_same_roles_except_the_deliberate_changes()
    {
        var kinds = (await db.RowsAsync("SELECT permission_id, kind FROM plat.permission;")).ToDictionary(r => (string)r[0], r => (string)r[1]);
        var grants = await db.RowsAsync("SELECT role_code, permission_id, mode, scope FROM plat.role_grant;");
        HashSet<string> Holders(IEnumerable<string> ids, bool wide) => ids.SelectMany(id =>
        {
            var view = id.EndsWith(":VIEW", StringComparison.Ordinal);
            var baseId = view ? id[..^5] : id;
            return grants.Where(g => (string)g[1] == baseId && (view || kinds[baseId] == "VIEW" || "ALR".Contains((string)g[2], StringComparison.Ordinal))
                && (!wide || (string)g[3] == "P")).Select(g => (string)g[0]);
        }).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (gate, old, @new, wide, gained, lost) in Parity)
        {
            var before = Holders(old, wide);
            var after = Holders(@new, wide);
            var g = string.Join(' ', after.Except(before).Order(StringComparer.Ordinal));
            var l = string.Join(' ', before.Except(after).Order(StringComparer.Ordinal));
            if (g != gained || l != lost) problems.Add($"{gate}: gained [{g}] (documented [{gained}]), lost [{l}] (documented [{lost}])");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task Pos_prices_are_Owner_only_and_the_Manager_requests_D5()
    {
        var evaluator = (SqlAccessPolicy)db.Module(FakeClock.At2026()).Policy;
        var manager = await db.SeedUserAsync("manager.v2", ["MGR"]);
        var owner = await db.SeedUserAsync("owner.v2", ["OWN"]);
        var m = await evaluator.EvaluateAsync(db.PropertyId, manager, "PERM-POS-006");
        Assert.Equal((DecisionOutcome.ApprovalRequired, "REQUEST_ONLY"), (m.Outcome, m.ApprovalKind));
        Assert.Equal(DecisionOutcome.Allow, (await evaluator.EvaluateAsync(db.PropertyId, owner, "PERM-POS-006")).Outcome);
    }
}
