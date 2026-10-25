using System.Reflection;
using System.Text.RegularExpressions;
using Hcc.Application.Billing;
using Hcc.Application.Housekeeping;
using Hcc.Application.Stays;
using Hcc.Contracts.Inventory;

namespace Hcc.Access.Tests.Unit;

/// <summary>
/// Wave 1 integration: every PERM id a module handler asks the policy for is seeded by 0101 (with at least one role grant), so
/// no command can be permanently denied by a typo or a permission the seed forgot. Reads the source and the migration text
/// (no database).
/// </summary>
public sealed partial class PermissionSeedCoverageTests
{
    [GeneratedRegex(@"""(PERM-[A-Z]+-\d{3})""")]
    private static partial Regex QuotedPermission();

    [GeneratedRegex(@"\('(PERM-[A-Z]+-\d{3})', '[^']*', '(?:VIEW|ACTION)', [01]\)")]
    private static partial Regex SeededPermission();

    [GeneratedRegex(@"\('[A-Z]{3}', '(PERM-[A-Z]+-\d{3})', '[ALRVM]', '[PSAO]'")]
    private static partial Regex SeededGrant();

    private static DirectoryInfo ProductionRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Hcc.slnx"))) return d;
        throw new InvalidOperationException("Hcc.slnx not found above the test output folder.");
    }

    private static string SeedSql() => File.ReadAllText(Path.Combine(ProductionRoot().FullName, "database", "migrations", "0101_access_rbac_seed.sql"));

    /// <summary>0101 plus the Wave 2 module seeds (<c>NNNN_*_rbac_seed.sql</c> in each module's migration block, e.g. 0901 payroll).</summary>
    private static string AllSeedsSql() => string.Join('\n', Directory.EnumerateFiles(Path.Combine(ProductionRoot().FullName, "database", "migrations"), "*_rbac_seed.sql")
        .Order(StringComparer.Ordinal).Select(File.ReadAllText));

    /// <summary>PERM ids written as string literals anywhere in src (handlers, permission classes, field masks).</summary>
    private static SortedSet<string> UsedInSource()
    {
        var src = Path.Combine(ProductionRoot().FullName, "src");
        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            if (rel.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || rel.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            foreach (Match m in QuotedPermission().Matches(File.ReadAllText(file))) used.Add(m.Groups[1].Value);
        }

        return used;
    }

    /// <summary>PERM ids declared on the module permission classes (P2, P3, P4).</summary>
    private static SortedSet<string> DeclaredByModules() =>
        new(new[] { typeof(InventoryPermissions), typeof(StaysPermissions), typeof(HousekeepingPermissions), typeof(BillingPermissions) }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(v => v.StartsWith("PERM-", StringComparison.Ordinal)), StringComparer.Ordinal);

    [Fact]
    public void Every_permission_used_in_code_is_seeded_with_at_least_one_role_grant()
    {
        Assert.Equal(45, SeededPermission().Matches(SeedSql()).Count); // RBAC matrix v1 §5 (DEC-063)
        var sql = AllSeedsSql();
        var seeded = SeededPermission().Matches(sql).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var granted = SeededGrant().Matches(sql).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        var used = UsedInSource();
        used.UnionWith(DeclaredByModules());
        Assert.True(used.Count >= 30, $"Expected the module PERM ids in source, found {used.Count}.");
        var notSeeded = used.Where(p => !seeded.Contains(p)).ToList();
        var notGranted = used.Where(p => !granted.Contains(p)).ToList();
        Assert.True(notSeeded.Count == 0, "Not seeded by 0101 or a module rbac seed: " + string.Join(", ", notSeeded));
        Assert.True(notGranted.Count == 0, "Seeded without any role grant: " + string.Join(", ", notGranted));
    }

    [Fact]
    public void Module_permission_classes_cover_the_handlers_contract_ids()
    {
        var declared = DeclaredByModules();
        string[] expected =
        [
            "PERM-RES-001", "PERM-RES-002", "PERM-RES-003", "PERM-RES-004",                       // P2 reservations
            "PERM-GST-001", "PERM-GST-002", "PERM-GST-003", "PERM-STY-001", "PERM-STY-003",       // P3 stays
            "PERM-HK-001", "PERM-HK-002", "PERM-HK-003", "PERM-HK-004", "PERM-ROOM-001", "PERM-ROOM-002", // P3 housekeeping
            "PERM-FOL-001", "PERM-FOL-002", "PERM-FOL-003", "PERM-FOL-005", "PERM-PAY-001", "PERM-PAY-002", "PERM-PAY-003",
            "PERM-PAY-004", "PERM-PAY-005", "PERM-SHF-001", "PERM-SHF-002", "PERM-CLS-002", "PERM-APR-001",  // P4
        ];
        var missing = expected.Where(p => !declared.Contains(p)).ToList();
        Assert.True(missing.Count == 0, "Not declared by a module permission class: " + string.Join(", ", missing));
    }
}
