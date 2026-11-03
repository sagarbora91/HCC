using Hcc.Contracts;
using Hcc.Domain.Access;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;

namespace Hcc.Access.Tests.Unit;

/// <summary>ADM-2 [ADM2] pure role rules (02 Planning/agents/ADM_Briefs.md §0–§2): bases, money lock, SoD, grid → permission ids, closure, union.</summary>
public class RoleRulesTests
{
    private static readonly PermissionInfo[] Perms =
    [
        new("PERM-ACC-001", false), new("PERM-ACC-002", false), new("PERM-ACC-003", true),
        new("PERM-KEY-001", false), new("PERM-KEY-002", false), new("PERM-KEY-003", false),
        new("PERM-RPT-001", false), new("PERM-RPT-002", true),
        new("PERM-HR-004", false), new("PERM-EXP-001", true), new("PERM-EXP-002", true),
        new("PERM-APR-001", true), new("PERM-FOL-003", true), new("PERM-FOL-005", true),
    ];

    private static readonly RoleGrant[] MgrOrigin =
    [
        new("MGR", "PERM-ACC-001", GrantMode.Allow, GrantScope.Property, null),
        new("MGR", "PERM-ACC-003", GrantMode.Allow, GrantScope.Property, null),
        new("MGR", "PERM-RPT-001", GrantMode.Allow, GrantScope.Property, null),
        new("MGR", "PERM-RPT-002", GrantMode.Allow, GrantScope.Property, null),
        new("MGR", "PERM-HR-004", GrantMode.Allow, GrantScope.Property, null),
        new("MGR", "PERM-EXP-001", GrantMode.Allow, GrantScope.Property, null),
        new("MGR", "PERM-EXP-002", GrantMode.Limit, GrantScope.Property, 500_000, "PAISE"),
        new("MGR", "PERM-APR-001", GrantMode.Limit, GrantScope.Property, 100_000, "PAISE"),
    ];

    private static readonly Dictionary<string, RoleFeatureAccess> None = new(StringComparer.Ordinal);

    [Fact]
    public void Owner_is_never_a_base_and_the_four_non_money_bases_are_locked()
    {
        Assert.False(RoleRules.CanBeBase(RoleCodes.Owner));
        Assert.All(RoleCodes.All.Where(r => r != RoleCodes.Owner), r => Assert.True(RoleRules.CanBeBase(r)));
        Assert.False(RoleRules.CanBeBase("C01"));
        foreach (var r in new[] { "HKP", "SUP", "MNT", "RST" })
        {
            Assert.True(RoleRules.MoneyLocked(r));
            Assert.False(RoleRules.EffectiveMoney(r, true));
        }

        Assert.True(RoleRules.EffectiveMoney("MGR", true));
        Assert.True(RoleRules.IsCustomCode("C01"));
        Assert.False(RoleRules.IsCustomCode("C00"));
        Assert.False(RoleRules.IsCustomCode("MGR"));
        Assert.Equal("C02", RoleRules.NextCode(["C01", "C03"]));
        Assert.Throws<HccException>(() => RoleRules.NextCode(Enumerable.Range(1, 99).Select(i => "C" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Throws<HccException>(() => RoleRules.NormalizeName("x"));
        Assert.Equal("Night shift", RoleRules.NormalizeName("  Night shift "));
    }

    [Fact]
    public void A_copy_with_no_changes_equals_the_base_and_money_off_keeps_only_own_pay()
    {
        var same = RoleRules.Materialize("C01", true, None, MgrOrigin, [], Perms);
        Assert.Equal(MgrOrigin.Select(g => g.PermissionId).Order(StringComparer.Ordinal), same.Select(g => g.PermissionId));
        Assert.All(same, g => Assert.Equal("C01", g.RoleCode));

        var noMoney = RoleRules.Materialize("C01", false, None, MgrOrigin, [], Perms);
        Assert.DoesNotContain(noMoney, g => Perms.Single(p => p.PermissionId == g.PermissionId).IsMoney);
        Assert.Contains(noMoney, g => g.PermissionId == RoleRules.OwnPay);
        Assert.Contains(noMoney, g => g.PermissionId == "PERM-RPT-001");
    }

    [Fact]
    public void Off_removes_the_prefix_view_keeps_001_and_full_adds_001_and_002_never_approve_or_money()
    {
        // Expenses off (and its dependant Purchases, which shares PERM-EXP-): every PERM-EXP- id goes.
        var off = RoleRules.ApplyFeature(None, FeatureIds.Expenses, RoleFeatureAccess.Off);
        Assert.Equal(RoleFeatureAccess.Off, off[FeatureIds.Purchases]);
        Assert.DoesNotContain(RoleRules.Materialize("C01", true, off, MgrOrigin, [], Perms), g => g.PermissionId.StartsWith("PERM-EXP-", StringComparison.Ordinal));

        // An untouched feature the base lacks adds nothing; Keys set to full explicitly: view + act are added, not -003.
        Assert.DoesNotContain(RoleRules.Materialize("C01", true, None, MgrOrigin, [], Perms), g => g.PermissionId.StartsWith("PERM-KEY-", StringComparison.Ordinal));
        var keysFull = RoleRules.ApplyFeature(None, FeatureIds.Keys, RoleFeatureAccess.Full);
        var keys = RoleRules.Materialize("C01", true, keysFull, MgrOrigin, [], Perms);
        Assert.Contains(keys, g => g.PermissionId == "PERM-KEY-001");
        Assert.Contains(keys, g => g.PermissionId == "PERM-KEY-002");
        Assert.DoesNotContain(keys, g => g.PermissionId == "PERM-KEY-003");

        // Keys view: only -001 stays.
        var keysView = RoleRules.ApplyFeature(None, FeatureIds.Keys, RoleFeatureAccess.View);
        var withKeys = MgrOrigin.Append(new RoleGrant("MGR", "PERM-KEY-002", GrantMode.Allow, GrantScope.Property, null)).ToList();
        var viewed = RoleRules.Materialize("C01", true, keysView, withKeys, [], Perms);
        Assert.Contains(viewed, g => g.PermissionId == "PERM-KEY-001");
        Assert.DoesNotContain(viewed, g => g.PermissionId == "PERM-KEY-002");

        // accounting.vouchers off: PERM-ACC- is shared with the core ledgers feature, so the ids stay (the route gate refuses vouchers).
        var acc = RoleRules.ApplyFeature(None, FeatureIds.Vouchers, RoleFeatureAccess.Off);
        Assert.Equal(RoleFeatureAccess.Full, RoleRules.PrefixLevel(acc, "PERM-ACC-"));
        Assert.Contains(RoleRules.Materialize("C01", true, acc, MgrOrigin, [], Perms), g => g.PermissionId == "PERM-ACC-001");
    }

    [Fact]
    public void Core_features_have_no_row_and_an_explicit_full_is_kept()
    {
        Assert.Throws<HccException>(() => RoleRules.ApplyFeature(None, "closing.dayclose", RoleFeatureAccess.Off));
        Assert.Throws<HccException>(() => RoleRules.ApplyFeature(None, "no.such", RoleFeatureAccess.Off));
        var off = RoleRules.ApplyFeature(None, FeatureIds.Vouchers, RoleFeatureAccess.View);
        Assert.Equal(RoleFeatureAccess.Full, RoleRules.ApplyFeature(off, FeatureIds.Vouchers, RoleFeatureAccess.Full)[FeatureIds.Vouchers]);
        Assert.Equal(RoleFeatureAccess.Full, RoleRules.LevelOf(off, "closing.dayclose"));
        Assert.Equal(RoleFeatureAccess.Off, RoleRules.Parse("OFF"));
        Assert.Equal(RoleFeatureAccess.View, RoleRules.Parse("view"));
        Assert.Throws<HccException>(() => RoleRules.Parse("money"));
        Assert.Equal("full", RoleRules.ToWire(RoleFeatureAccess.Full));
    }

    [Fact]
    public void Two_roles_give_the_union_and_built_ins_are_always_full()
    {
        var day = new RoleDefinition("C01", "Day shift manager", true, "MGR", true,
            new Dictionary<string, RoleFeatureAccess>(StringComparer.Ordinal) { [FeatureIds.Vouchers] = RoleFeatureAccess.Off }, 1);
        var night = day with { RoleCode = "C02", Features = new Dictionary<string, RoleFeatureAccess>(StringComparer.Ordinal) { [FeatureIds.Vouchers] = RoleFeatureAccess.View } };
        var rec = new RoleDefinition("REC", "Reception", false, null, true, None, 0);
        Assert.Equal(RoleFeatureAccess.Off, RoleRules.UserLevel([day], FeatureIds.Vouchers));
        Assert.Equal(RoleFeatureAccess.View, RoleRules.UserLevel([day, night], FeatureIds.Vouchers));
        Assert.Equal(RoleFeatureAccess.Full, RoleRules.UserLevel([day, rec], FeatureIds.Vouchers));
        Assert.Equal(RoleFeatureAccess.Full, RoleRules.UserLevel([], FeatureIds.Vouchers));
        Assert.Empty(RoleRules.UserLevels([rec]));
        Assert.Equal(RoleFeatureAccess.Off, RoleRules.UserLevels([day])[FeatureIds.Vouchers]);

        var e = RoleRules.FeatureRefusal(FeatureCatalog.Feature(FeatureIds.Vouchers)!, RoleFeatureAccess.View, "C02", "Night shift manager");
        Assert.Equal(HccErrorCode.FORBIDDEN, e.Code);
        Assert.Equal("Manual vouchers & loans (Accounting) is view only for your role Night shift manager.", e.Message);
        Assert.Equal("C02", e.Details!["roleCode"].GetString());
    }

    [Fact]
    public void Advanced_cells_keep_the_money_invariant_and_SoD()
    {
        var rpt = new PermissionInfo("PERM-RPT-002", true);
        var cell = new GrantOverride("PERM-RPT-002", GrantMode.Allow, GrantScope.Property, null, null);
        Assert.Equal(HccErrorCode.FORBIDDEN, Assert.Throws<HccException>(() => RoleRules.ValidateOverride("HKP", false, rpt, cell, null)).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, Assert.Throws<HccException>(() => RoleRules.ValidateOverride("MGR", false, rpt, cell, null)).Code);
        RoleRules.ValidateOverride("MGR", true, rpt, cell, null);
        RoleRules.ValidateOverride("HKP", false, new PermissionInfo(RoleRules.OwnPay, false), new GrantOverride(RoleRules.OwnPay, GrantMode.Allow, GrantScope.Property, null, null), null);
        RoleRules.ValidateOverride("HKP", false, rpt, cell with { Mode = null }, null);   // removing is always fine

        var apr = new PermissionInfo("PERM-APR-001", true);
        var baseApr = MgrOrigin.Single(g => g.PermissionId == "PERM-APR-001");
        RoleRules.ValidateOverride("MGR", true, apr, new GrantOverride(apr.PermissionId, GrantMode.Limit, GrantScope.Property, 50_000, "PAISE"), baseApr);
        Assert.Throws<HccException>(() => RoleRules.ValidateOverride("MGR", true, apr, new GrantOverride(apr.PermissionId, GrantMode.Limit, GrantScope.Property, 100_001, "PAISE"), baseApr));
        Assert.Throws<HccException>(() => RoleRules.ValidateOverride("MGR", true, apr, new GrantOverride(apr.PermissionId, GrantMode.Allow, GrantScope.Property, null, null), baseApr));
        Assert.Throws<HccException>(() => RoleRules.ValidateOverride("REC", true, apr, new GrantOverride(apr.PermissionId, GrantMode.Limit, GrantScope.Property, 1, "PAISE"), null));
        Assert.Throws<HccException>(() => RoleRules.ValidateOverride("REC", true, new PermissionInfo("PERM-FOL-005", true),
            new GrantOverride("PERM-FOL-005", GrantMode.Allow, GrantScope.Property, null, null), null));
        Assert.Throws<HccException>(() => RoleRules.ValidateOverride("MGR", true, apr, new GrantOverride(apr.PermissionId, GrantMode.Allow, GrantScope.Property, 5, "PAISE"), baseApr));

        Assert.NotNull(RoleRules.AdvancedWarning(new PermissionInfo("PERM-ACC-003", true), new GrantOverride("PERM-ACC-003", GrantMode.Allow, GrantScope.Property, null, null)));
        Assert.Null(RoleRules.AdvancedWarning(new PermissionInfo("PERM-ACC-001", false), new GrantOverride("PERM-ACC-001", GrantMode.Allow, GrantScope.Property, null, null)));
    }

    [Fact]
    public void Every_catalogue_prefix_is_a_PERM_prefix_and_core_features_never_get_a_level()
    {
        Assert.All(RoleRules.AllPrefixes, p => Assert.Matches("^PERM-[A-Z]+-$", p));
        Assert.All(FeatureCatalog.All.Where(f => f.Core), f => Assert.Equal(RoleFeatureAccess.Full, RoleRules.LevelOf(
            new Dictionary<string, RoleFeatureAccess>(StringComparer.Ordinal) { [f.Id] = RoleFeatureAccess.Off }, f.Id)));
    }
}
