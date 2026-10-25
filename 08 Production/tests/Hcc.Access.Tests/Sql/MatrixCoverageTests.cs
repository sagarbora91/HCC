using System.Text.Json;
using System.Text.RegularExpressions;
using Hcc.Access.Tests.Harness;

namespace Hcc.Access.Tests.Sql;

/// <summary>
/// The seeded roles/grants (migration 0101) equal the RBAC matrix v1 §5 with the DEC-063 rulings applied, §6 limits and
/// DEC-062 credential kinds; and every PERM id used by the prototype (permission-coverage.json) is granted to exactly the
/// roles the (ruled) matrix says.
/// </summary>
[Trait("Category", "Sql")]
public partial class MatrixCoverageTests(AccessDatabase db) : IClassFixture<AccessDatabase>
{
    private static readonly string[] Roles = ["OWN", "MGR", "REC", "CSH", "ACC", "SUP", "HKP", "MNT", "TAD"];

    /// <summary>DEC-063 (RBAC_Mismatch_Rulings_Proposed.md, accepted 25 Oct 2026): cells that differ from matrix v1. The rulings win.</summary>
    public static readonly Dictionary<(string Perm, string Role), string> Dec063 = new()
    {
        [("PERM-PAY-004", "REC")] = "A·S", // G2 Reception pays out a refund approved by someone else, own shift
        [("PERM-FOL-005", "CSH")] = "R",   // G3 Cashier may request corrections / credit notes
        [("PERM-GST-001", "ACC")] = "—",   // G4 Accountant does not need the Customers page
        [("PERM-EXP-003", "CSH")] = "—",   // G5 Cashier cannot reverse expenses
        [("PERM-EXP-001", "SUP")] = "—",   // G6 bills go through Reception/Cashier
        [("PERM-EXP-001", "MNT")] = "—",
        [("PERM-RES-001", "CSH")] = "—",   // G7 no reservations for Cashier, Accountant, Supervisor
        [("PERM-RES-001", "ACC")] = "—",
        [("PERM-RES-001", "SUP")] = "—",
        [("PERM-HK-001", "REC")] = "—",    // G8 Reception: no HK board, day-close checklist or reports; Cashier no checklist; Supervisor no reports
        [("PERM-CLS-001", "REC")] = "—",
        [("PERM-CLS-001", "CSH")] = "—",
        [("PERM-RPT-001", "REC")] = "—",
        [("PERM-RPT-001", "SUP")] = "—",
        [("PERM-ADM-003", "MGR")] = "—",   // G9 no setting requests for Manager (DEC-058)
        [("PERM-ADM-004", "TAD")] = "—",   // G10 Tech admin audit view deferred to M4
    };

    private static readonly Dictionary<(string Perm, string Role), (long Value, string Unit)> Limits = new()
    {
        [("PERM-FOL-003", "REC")] = (25_000, "PAISE"),     // §6 discount Reception ≤ ₹250
        [("PERM-FOL-003", "MGR")] = (100_000, "PAISE"),    // §6 discount Manager ≤ ₹1,000
        [("PERM-RATE-001", "MGR")] = (1_500, "BPS"),       // §6 rate override ≤ 15 %
        [("PERM-PAY-005", "MGR")] = (1_000_000, "PAISE"),  // §6 company credit ≤ ₹10,000 per stay
        [("PERM-SHF-002", "MGR")] = (20_000, "PAISE"),     // §6 cash difference ≤ ₹200
        [("PERM-EXP-002", "MGR")] = (500_000, "PAISE"),    // §6 expense approval ≤ ₹5,000
        [("PERM-APR-001", "MGR")] = (100_000, "PAISE"),    // DEC-063 G1 Manager approves only up to ₹1,000
    };

    [GeneratedRegex(@"^\| (PERM-[A-Z]+-\d{3}) \| [^|]+ \|(.*)\|\s*$")]
    private static partial Regex MatrixRow();

    private static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException("Not found above the test output folder: " + Path.Combine(parts));
    }

    /// <summary>Expected cell per (perm, role) = matrix v1 with DEC-063 applied; "—" = no grant.</summary>
    public static Dictionary<(string Perm, string Role), string> ExpectedCells()
    {
        var cells = new Dictionary<(string, string), string>();
        foreach (var line in File.ReadLines(RepoFile("02 Planning", "HCC_RBAC_Matrix_v1.md")))
        {
            var m = MatrixRow().Match(line);
            if (!m.Success) continue;
            var values = m.Groups[2].Value.Split('|').Select(v => v.Trim()).ToArray();
            Assert.Equal(Roles.Length, values.Length);
            for (var i = 0; i < Roles.Length; i++) cells[(m.Groups[1].Value, Roles[i])] = values[i];
        }

        foreach (var (key, value) in Dec063)
        {
            Assert.True(cells.ContainsKey(key), $"DEC-063 override for unknown cell {key}");
            cells[key] = value;
        }

        return cells;
    }

    private async Task<Dictionary<(string Perm, string Role), (string Cell, long? Limit, string? Unit)>> SeededAsync()
    {
        // Wave 2: module migrations (e.g. 0901 payroll) seed their own PERM ids outside matrix v1; this test covers the matrix ids only.
        var matrix = ExpectedCells().Keys.Select(k => k.Perm).ToHashSet(StringComparer.Ordinal);
        var rows = (await db.RowsAsync("SELECT permission_id, role_code, mode, scope, limit_value, limit_unit FROM plat.role_grant;")).Where(r => matrix.Contains((string)r[0]));
        return rows.ToDictionary(r => ((string)r[0], (string)r[1]), r =>
        {
            var scope = (string)r[3];
            var cell = scope == "P" ? (string)r[2] : $"{r[2]}·{scope}";
            return (cell, r[4] is DBNull ? (long?)null : (long)r[4], r[5] is DBNull ? null : (string)r[5]);
        });
    }

    [Fact]
    public async Task Seeded_grants_equal_the_matrix_with_DEC063_rulings()
    {
        var expected = ExpectedCells();
        Assert.Equal(45 * 9, expected.Count);
        var seeded = await SeededAsync();
        var mismatches = new List<string>();
        foreach (var ((perm, role), cell) in expected)
        {
            var has = seeded.TryGetValue((perm, role), out var s);
            if (cell == "—" && has) mismatches.Add($"{perm} {role}: seeded {s.Cell}, matrix —");
            else if (cell != "—" && (!has || s.Cell != cell)) mismatches.Add($"{perm} {role}: seeded {(has ? s.Cell : "—")}, matrix {cell}");
        }

        Assert.DoesNotContain(seeded.Keys, k => !expected.ContainsKey(k));
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
        var matrixIds = expected.Keys.Select(k => k.Perm).Distinct().ToList();
        Assert.Equal(45, matrixIds.Count);
        Assert.Equal(45, await db.CountAsync("SELECT COUNT(*) FROM plat.permission WHERE permission_id IN (" + string.Join(",", matrixIds.Select(i => "'" + i + "'")) + ");"));
    }

    [Fact]
    public async Task Every_PERM_id_in_permission_coverage_is_granted_to_exactly_the_matrix_roles()
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(RepoFile("07 Integration Lab", "evidence", "permission-coverage.json"), TestContext.Current.CancellationToken));
        var ids = json.RootElement.GetProperty("rows").EnumerateArray()
            .SelectMany(r => r.GetProperty("authorize").EnumerateArray().Concat(r.GetProperty("intended").EnumerateArray()))
            .Select(e => e.GetString()!)
            .Where(id => !id.StartsWith("PERM-SOD-", StringComparison.Ordinal))   // separation-of-duty rules are enforced in code, not granted
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(ids.Count >= 25, $"Only {ids.Count} PERM ids found in permission-coverage.json");

        var expected = ExpectedCells();
        var seeded = await SeededAsync();
        foreach (var id in ids.Order(StringComparer.Ordinal))
        {
            var want = Roles.Where(r => expected.TryGetValue((id, r), out var c) ? c != "—" : throw new Xunit.Sdk.XunitException($"{id} is not in the matrix")).ToArray();
            var got = Roles.Where(r => seeded.ContainsKey((id, r))).ToArray();
            Assert.True(want.SequenceEqual(got), $"{id}: matrix roles [{string.Join(",", want)}], seeded [{string.Join(",", got)}]");
        }
    }

    [Fact]
    public async Task Limits_follow_section6_and_DEC063()
    {
        var seeded = await SeededAsync();
        foreach (var ((perm, role), (value, unit)) in Limits)
            Assert.Equal((value, unit), (seeded[(perm, role)].Limit!.Value, seeded[(perm, role)].Unit!));
        Assert.Equal(Limits.Count, seeded.Values.Count(v => v.Limit is not null));
        Assert.All(seeded.Where(s => s.Value.Cell.StartsWith('L')), s => Assert.True(Limits.ContainsKey(s.Key), $"{s.Key} is L without a limit"));
    }

    [Fact]
    public async Task Credential_kind_per_role_is_DEC062_and_money_roles_are_owner_only_DEC058()
    {
        var roles = await db.RowsAsync("SELECT role_code, credential_kind, owner_only_assign FROM plat.role ORDER BY sort_order;");
        Assert.Equal(Roles, roles.Select(r => (string)r[0]).ToArray());
        Assert.Equal(["OWN", "ACC", "TAD"], roles.Where(r => (string)r[1] == "PASSWORD").Select(r => (string)r[0]).ToArray());
        // owner_only_assign = the role holds a money permission or user/role administration.
        var expectedOwnerOnly = await db.RowsAsync("""
            SELECT DISTINCT g.role_code FROM plat.role_grant g JOIN plat.permission p ON p.permission_id = g.permission_id
             WHERE p.is_money = 1 OR g.permission_id IN ('PERM-ADM-001', 'PERM-ADM-002');
            """);
        Assert.Equal(expectedOwnerOnly.Select(r => (string)r[0]).Order().ToArray(), roles.Where(r => (bool)r[2]).Select(r => (string)r[0]).Order().ToArray());
        Assert.Equal(["HKP", "MNT", "SUP"], roles.Where(r => !(bool)r[2]).Select(r => (string)r[0]).Order().ToArray());
    }
}
