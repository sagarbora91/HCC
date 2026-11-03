using Hcc.Access.Tests.Harness;
using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;

namespace Hcc.Access.Tests.Sql;

/// <summary>
/// ADM-2 [ADM2] roles as data on the server (02 Planning/agents/ADM_Briefs.md §2.4): custom roles copy their base at creation and stay frozen;
/// the money invariant (HKP-based never holds PERM-RPT-002); built-in roles and one's own role are never edited; Owner only; retire refused
/// while held; per-role feature levels (day-shift / night-shift manager) through <see cref="Hcc.Application.Access.IFeatureAccess"/> and
/// /me/capabilities; the snapshot carries the base code; credential kind follows the base.
/// </summary>
[Trait("Category", "Sql")]
public class RoleAdministrationTests(AccessDatabase db) : IClassFixture<AccessDatabase>
{
    private const string Pw = "Sand-and-Maroon-26";

    private static string Key() => Guid.NewGuid().ToString("N");

    private static string Unique(string name) => name + " " + Guid.NewGuid().ToString("N")[..6];

    private async Task<(AccessModule M, AuthenticatedSession Owner, int OwnerId)> OwnerAsync(string login)
    {
        var m = db.Module(FakeClock.At2026());
        var id = await db.SeedUserAsync(login, ["OWN"], password: Pw);
        return (m, await AccessDatabase.SignInPasswordAsync(m, login, Pw), id);
    }

    private static async Task<RoleChangeResult> CreateAsync(AccessModule m, AuthenticatedSession owner, string name, string baseRole, bool? money = null) =>
        (await m.Roles.CreateRoleAsync(owner, Key(), new CreateRoleRequest(name, baseRole, money))).Value;

    private static async Task<RoleChangeResult> FeatureAsync(AccessModule m, AuthenticatedSession owner, string role, string feature, string access) =>
        (await m.Roles.SetRoleFeatureAsync(owner, Key(), new SetRoleFeatureRequest(role, feature, access))).Value;

    private async Task<List<string>> GrantIdsAsync(string role) =>
        (await db.RowsAsync($"SELECT permission_id FROM plat.role_grant WHERE role_code = '{role}' ORDER BY permission_id;")).Select(r => (string)r[0]).ToList();

    [Fact]
    public async Task Create_copies_the_base_grants_and_denies_and_later_base_changes_never_flow_down()
    {
        var (m, owner, _) = await OwnerAsync("adm2.copy.own");
        var r = await CreateAsync(m, owner, Unique("Night desk"), RoleCodes.Manager);
        Assert.True(RoleRules.IsCustomCode(r.RoleCode));
        Assert.Equal(1, r.Version);
        Assert.True(r.PolicyVersion >= 1);
        Assert.Equal(await GrantIdsAsync(RoleCodes.Manager), await GrantIdsAsync(r.RoleCode));
        Assert.Equal(await db.CountAsync("SELECT COUNT(*) FROM plat.role_deny WHERE role_code = 'MGR';"),
            await db.CountAsync("SELECT COUNT(*) FROM plat.role_deny WHERE role_code = @r;", ("@r", r.RoleCode)));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.role WHERE role_code = @r AND is_custom = 1 AND base_role = 'MGR' AND money = 1 AND credential_kind = 'PIN';", ("@r", r.RoleCode)));

        // The base's seed changes afterwards (a later migration edits MGR): the custom role keeps its copy, also after a rebuild.
        await db.ScalarAsync("DELETE plat.role_grant WHERE role_code = 'MGR' AND permission_id = 'PERM-KEY-004';");
        try
        {
            Assert.Contains("PERM-KEY-004", await GrantIdsAsync(r.RoleCode));
            await FeatureAsync(m, owner, r.RoleCode, FeatureIds.Handover, "view");
            Assert.Contains("PERM-KEY-004", await GrantIdsAsync(r.RoleCode));
            Assert.DoesNotContain("PERM-KEY-004", await GrantIdsAsync(RoleCodes.Manager));
        }
        finally
        {
            await db.ScalarAsync("INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES ('MGR', 'PERM-KEY-004', 'A', 'P', NULL, NULL);");
        }

        // Audit + versioned policy change with before / after.
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ROLE_CREATE' AND target = @t;", ("@t", "role:" + r.RoleCode)));
        var history = await m.Roles.HistoryAsync(owner, 50);
        var create = Assert.Single(history.Items, i => i.RoleCode == r.RoleCode && i.Action == "ROLE_CREATE");
        Assert.Null(create.Before);
        Assert.Contains("\"baseRole\":\"MGR\"", create.After!, StringComparison.Ordinal);
        var feature = Assert.Single(history.Items, i => i.RoleCode == r.RoleCode && i.Action == "ROLE_FEATURE");
        Assert.NotNull(feature.Before);
        Assert.Contains("guestsvc.handover", feature.After!, StringComparison.Ordinal);
        Assert.True(feature.Version > create.Version);
    }

    [Fact]
    public async Task A_housekeeper_based_role_never_gains_a_money_permission()
    {
        var (m, owner, _) = await OwnerAsync("adm2.money.own");
        var r = await CreateAsync(m, owner, Unique("Floor lead"), RoleCodes.Housekeeper, money: true);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.role WHERE role_code = @r AND money = 0;", ("@r", r.RoleCode)));   // forced off

        var e = await Assert.ThrowsAsync<HccException>(() => m.Roles.SetRoleGrantAsync(owner, Key(), new SetRoleGrantRequest(r.RoleCode, "PERM-RPT-002", "A", "P")));
        Assert.Equal(HccErrorCode.FORBIDDEN, e.Code);
        var u = await Assert.ThrowsAsync<HccException>(() => m.Roles.UpdateRoleAsync(owner, Key(), new UpdateRoleRequest(r.RoleCode, null, true, r.Version)));
        Assert.Equal(HccErrorCode.FORBIDDEN, u.Code);

        // The grid adds view + act only, never a money id, even on a money module.
        await FeatureAsync(m, owner, r.RoleCode, "cash.expenses", "full");
        Assert.Equal(0, await db.CountAsync("""
            SELECT COUNT(*) FROM plat.role_grant g JOIN plat.permission p ON p.permission_id = g.permission_id
             WHERE g.role_code = @r AND p.is_money = 1 AND g.permission_id <> 'PERM-HR-004';
            """, ("@r", r.RoleCode)));
        Assert.DoesNotContain("PERM-RPT-002", await GrantIdsAsync(r.RoleCode));
    }

    [Fact]
    public async Task Money_off_removes_money_grants_except_own_pay_and_money_on_restores_the_base_copy()
    {
        var (m, owner, _) = await OwnerAsync("adm2.moneyflag.own");
        var r = await CreateAsync(m, owner, Unique("Desk lead"), RoleCodes.Manager);
        var full = await GrantIdsAsync(r.RoleCode);
        var off = (await m.Roles.UpdateRoleAsync(owner, Key(), new UpdateRoleRequest(r.RoleCode, null, false, r.Version))).Value;
        Assert.Equal(r.Version + 1, off.Version);
        Assert.Equal(0, await db.CountAsync("""
            SELECT COUNT(*) FROM plat.role_grant g JOIN plat.permission p ON p.permission_id = g.permission_id
             WHERE g.role_code = @r AND p.is_money = 1 AND g.permission_id <> 'PERM-HR-004';
            """, ("@r", r.RoleCode)));
        var stale = await Assert.ThrowsAsync<HccException>(() => m.Roles.UpdateRoleAsync(owner, Key(), new UpdateRoleRequest(r.RoleCode, null, true, r.Version)));
        Assert.Equal(HccErrorCode.STALE_VERSION, stale.Code);
        await m.Roles.UpdateRoleAsync(owner, Key(), new UpdateRoleRequest(r.RoleCode, null, true, off.Version));
        Assert.Equal(full, await GrantIdsAsync(r.RoleCode));
    }

    [Fact]
    public async Task SoD_limits_only_go_down_and_credit_note_approval_is_never_added()
    {
        var (m, owner, _) = await OwnerAsync("adm2.sod.own");
        var r = await CreateAsync(m, owner, Unique("Approver"), RoleCodes.Manager);
        var up = await Assert.ThrowsAsync<HccException>(() => m.Roles.SetRoleGrantAsync(owner, Key(), new SetRoleGrantRequest(r.RoleCode, "PERM-APR-001", "L", "P", 100_001, "PAISE")));
        Assert.Equal(HccErrorCode.FORBIDDEN, up.Code);
        await m.Roles.SetRoleGrantAsync(owner, Key(), new SetRoleGrantRequest(r.RoleCode, "PERM-APR-001", "L", "P", 50_000, "PAISE"));
        Assert.Equal(50_000L, Convert.ToInt64(await db.ScalarAsync("SELECT limit_value FROM plat.role_grant WHERE role_code = @r AND permission_id = 'PERM-APR-001';", ("@r", r.RoleCode))));

        var rec = await CreateAsync(m, owner, Unique("Front only"), RoleCodes.Reception);
        var disc = await Assert.ThrowsAsync<HccException>(() => m.Roles.SetRoleGrantAsync(owner, Key(), new SetRoleGrantRequest(rec.RoleCode, "PERM-FOL-003", "L", "P", 25_001, "PAISE")));
        Assert.Equal(HccErrorCode.FORBIDDEN, disc.Code);
        var hkp = await CreateAsync(m, owner, Unique("No credit"), RoleCodes.Supervisor);
        await Assert.ThrowsAsync<HccException>(() => m.Roles.SetRoleGrantAsync(owner, Key(), new SetRoleGrantRequest(hkp.RoleCode, "PERM-FOL-005", "A", "P")));
    }

    [Fact]
    public async Task Built_in_roles_one_s_own_role_and_non_owners_are_refused()
    {
        var (m, owner, ownerId) = await OwnerAsync("adm2.guard.own");
        var builtIn = await Assert.ThrowsAsync<HccException>(() => m.Roles.SetRoleFeatureAsync(owner, Key(), new SetRoleFeatureRequest(RoleCodes.Manager, FeatureIds.Vouchers, "off")));
        Assert.Equal(HccErrorCode.FORBIDDEN, builtIn.Code);
        var ownBase = await Assert.ThrowsAsync<HccException>(() => m.Roles.CreateRoleAsync(owner, Key(), new CreateRoleRequest(Unique("Second owner"), RoleCodes.Owner)));
        Assert.Equal(HccErrorCode.VALIDATION, ownBase.Code);
        var core = await Assert.ThrowsAsync<HccException>(() => m.Roles.SetRoleFeatureAsync(owner, Key(), new SetRoleFeatureRequest("C99", "closing.dayclose", "off")));
        Assert.Equal(HccErrorCode.VALIDATION, core.Code);

        var r = await CreateAsync(m, owner, Unique("Owner helper"), RoleCodes.Manager);
        var sameName = (await NameOf(r.RoleCode)).ToUpperInvariant();   // names are unique, case-insensitive
        var dup = await Assert.ThrowsAsync<HccException>(() => m.Roles.CreateRoleAsync(owner, Key(), new CreateRoleRequest(sameName, RoleCodes.Cashier)));
        Assert.Equal(HccErrorCode.VALIDATION, dup.Code);

        // The Owner holds the custom role too: they cannot edit it (DEC-218).
        await db.ScalarAsync("INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, @r, '2026-01-01', '2026-01-01');", ("@u", ownerId), ("@r", r.RoleCode));
        var self = await Assert.ThrowsAsync<HccException>(() => m.Roles.SetRoleFeatureAsync(owner, Key(), new SetRoleFeatureRequest(r.RoleCode, FeatureIds.Vouchers, "off")));
        Assert.Equal(HccErrorCode.FORBIDDEN, self.Code);

        // A Tech admin (PERM-ADM-001 only) and a Manager cannot edit roles.
        var tad = await db.SeedUserAsync("adm2.guard.tad", ["TAD"], password: Pw);
        var tadSession = await AccessDatabase.SignInPasswordAsync(m, "adm2.guard.tad", Pw);
        // TAD holds PERM-ADM-002 in Request mode (0101 seed), so the policy answers APPROVAL_REQUIRED; either way nothing is created.
        Assert.Contains((await Assert.ThrowsAsync<HccException>(() => m.Roles.CreateRoleAsync(tadSession, Key(), new CreateRoleRequest(Unique("Tad role"), RoleCodes.Housekeeper)))).Code,
            new[] { HccErrorCode.FORBIDDEN, HccErrorCode.APPROVAL_REQUIRED });
        Assert.True(tad > 0);
    }

    private async Task<string> NameOf(string role) => (string)(await db.ScalarAsync("SELECT name FROM plat.role WHERE role_code = @r;", ("@r", role)))!;

    [Fact]
    public async Task Retire_is_refused_while_an_enabled_user_holds_the_role_and_a_retired_role_is_never_assigned()
    {
        var (m, owner, _) = await OwnerAsync("adm2.retire.own");
        var r = await CreateAsync(m, owner, Unique("Seasonal"), RoleCodes.Housekeeper);
        var holder = await db.SeedUserAsync("adm2.retire.hkp", [r.RoleCode], pin: "482913");
        var held = await Assert.ThrowsAsync<HccException>(() => m.Roles.RetireRoleAsync(owner, Key(), new RetireRoleRequest(r.RoleCode)));
        Assert.Equal(HccErrorCode.FORBIDDEN, held.Code);

        await db.ScalarAsync("UPDATE plat.app_user SET enabled = 0 WHERE user_id = @u;", ("@u", holder));
        await m.Roles.RetireRoleAsync(owner, Key(), new RetireRoleRequest(r.RoleCode));
        Assert.Empty(await GrantIdsAsync(r.RoleCode));
        var other = await db.SeedUserAsync("adm2.retire.next", ["HKP"], pin: "482913");
        var assign = await Assert.ThrowsAsync<HccException>(async () =>
            await m.Users.AssignRoleAsync(owner, Key(), new AssignRoleRequest(other, r.RoleCode, null, null, await db.VersionOfAsync(other))));
        Assert.Equal(HccErrorCode.VALIDATION, assign.Code);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'ROLE_RETIRE' AND target = @t;", ("@t", "role:" + r.RoleCode)));
    }

    // Every switchable Accounting sub-function (F5 added year end and bank reconciliation after ADM-2 was written).
    private static string[] AccountingSwitchable => FeatureCatalog.Modules.Single(x => x.Id == "accounting").Features.Where(f => !f.Core).Select(f => f.Id).ToArray();

    [Fact]
    public async Task Day_and_night_shift_managers_get_their_feature_levels_and_keep_the_base_code_in_the_snapshot()
    {
        var (m, owner, _) = await OwnerAsync("adm2.shift.own");
        var day = await CreateAsync(m, owner, Unique("Day shift manager"), RoleCodes.Manager);
        foreach (var f in AccountingSwitchable)
            await FeatureAsync(m, owner, day.RoleCode, f, "off");
        var night = await CreateAsync(m, owner, Unique("Night shift manager"), RoleCodes.Manager);
        foreach (var f in AccountingSwitchable)
            await FeatureAsync(m, owner, night.RoleCode, f, "view");

        var dayUser = await db.SeedUserAsync("adm2.shift.day", [day.RoleCode], pin: "482913");
        var nightUser = await db.SeedUserAsync("adm2.shift.night", [night.RoleCode], pin: "482913");
        var p = db.PropertyId;

        Assert.Equal(RoleFeatureAccess.Off, await m.FeatureAccess.LevelAsync(p, dayUser, FeatureIds.Vouchers));
        Assert.Equal(RoleFeatureAccess.View, await m.FeatureAccess.LevelAsync(p, nightUser, FeatureIds.Vouchers));
        Assert.Equal(RoleFeatureAccess.Full, await m.FeatureAccess.LevelAsync(p, nightUser, "closing.dayclose"));   // core: follows the base
        var refused = await Assert.ThrowsAsync<HccException>(() => m.FeatureAccess.EnsureFullAsync(p, nightUser, FeatureIds.Vouchers));
        Assert.Equal(HccErrorCode.FORBIDDEN, refused.Code);
        Assert.Equal(RoleRules.ModuleOffReason, refused.Details!["reason"].GetString());
        Assert.Equal(FeatureIds.Vouchers, refused.Details["featureId"].GetString());
        Assert.Equal(night.RoleCode, refused.Details["roleCode"].GetString());
        await m.FeatureAccess.EnsureFullAsync(p, nightUser, "closing.dayclose");

        // Two roles = the union: a day-shift manager who also holds a built-in role (always full) gets accounting back.
        var both = await db.SeedUserAsync("adm2.shift.both", [day.RoleCode, RoleCodes.Reception], pin: "482913");
        Assert.Equal(RoleFeatureAccess.Full, await m.FeatureAccess.LevelAsync(p, both, FeatureIds.Vouchers));

        // Capabilities: base code MGR + the custom code, and the non-full levels.
        var session = new AuthenticatedSession(1, dayUser, p, "Day", CredentialKind.Pin, null, DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow.AddHours(12));
        var caps = await m.Capabilities.GetAsync(session);
        Assert.Contains(RoleCodes.Manager, caps.Roles);
        Assert.Contains(day.RoleCode, caps.Roles);
        Assert.NotNull(caps.Features);
        Assert.Equal("off", caps.Features![FeatureIds.Vouchers]);
        Assert.All(FeatureCatalog.Modules.Single(x => x.Id == "accounting").Features.Where(f => !f.Core), f => Assert.Equal("off", caps.Features[f.Id]));
        Assert.DoesNotContain("accounting.ledgers", caps.Features.Keys);

        // A built-in user has no feature entries at all.
        var mgr = await db.SeedUserAsync("adm2.shift.mgr", [RoleCodes.Manager], pin: "482913");
        Assert.Empty(await m.FeatureAccess.LevelsAsync(p, mgr));

        // GET /admin/roles shape for custom roles.
        var roles = await m.AdminQueries.RolesAsync(owner);
        var view = Assert.Single(roles.Roles, x => x.RoleCode == night.RoleCode);
        Assert.True(view.IsCustom);
        Assert.Equal(RoleCodes.Manager, view.BaseRole);
        Assert.Equal("view", view.Features![FeatureIds.Vouchers]);
        var builtIn = Assert.Single(roles.Roles, x => x.RoleCode == RoleCodes.Manager);
        Assert.False(builtIn.IsCustom);
        Assert.Null(builtIn.BaseRole);
    }

    [Fact]
    public async Task Off_cascades_to_dependants_and_full_adds_view_and_act_ids_the_base_lacked()
    {
        var (m, owner, _) = await OwnerAsync("adm2.grid.own");
        var r = await CreateAsync(m, owner, Unique("Grid"), RoleCodes.Supervisor);
        await FeatureAsync(m, owner, r.RoleCode, FeatureIds.Expenses, "off");
        var features = (await m.AdminQueries.RolesAsync(owner)).Roles.Single(x => x.RoleCode == r.RoleCode).Features!;
        Assert.Equal("off", features[FeatureIds.Expenses]);
        Assert.Equal("off", features[FeatureIds.Purchases]);   // stock.purchases requires cash.expenses

        Assert.DoesNotContain("PERM-KEY-002", await GrantIdsAsync(r.RoleCode));
        await FeatureAsync(m, owner, r.RoleCode, FeatureIds.Keys, "full");
        var ids = await GrantIdsAsync(r.RoleCode);
        Assert.Contains("PERM-KEY-001", ids);
        Assert.Contains("PERM-KEY-002", ids);
        Assert.DoesNotContain("PERM-KEY-003", ids);   // approve-level ids are never added by the grid
    }

    [Fact]
    public async Task A_custom_role_signs_in_like_its_base_and_a_tech_admin_may_assign_a_non_money_custom_role()
    {
        var (m, owner, _) = await OwnerAsync("adm2.cred.own");
        var books = await CreateAsync(m, owner, Unique("Books"), RoleCodes.Accountant);
        var user = await db.SeedUserAsync("adm2.cred.acc", [RoleCodes.Housekeeper], pin: "482913");
        await m.Users.AssignRoleAsync(owner, Key(), new AssignRoleRequest(user, books.RoleCode, null, null, await db.VersionOfAsync(user)));
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM plat.credential WHERE user_id = @u AND kind = 'PIN';", ("@u", user)));   // DEC-062 via the base
        var list = await m.Users.ListUsersAsync(owner, 1, 100);
        Assert.Equal("PASSWORD", list.Items.Single(u => u.UserId == user).CredentialKind);

        var floor = await CreateAsync(m, owner, Unique("Floor"), RoleCodes.Housekeeper);
        await db.SeedUserAsync("adm2.cred.tad", [RoleCodes.TechAdmin], password: Pw);
        var tad = await AccessDatabase.SignInPasswordAsync(m, "adm2.cred.tad", Pw);
        var staff = await db.SeedUserAsync("adm2.cred.hkp", [RoleCodes.Housekeeper], pin: "482913");
        await m.Users.AssignRoleAsync(tad, Key(), new AssignRoleRequest(staff, floor.RoleCode, null, null, await db.VersionOfAsync(staff)));
        var staff2 = await db.SeedUserAsync("adm2.cred.hkp2", [RoleCodes.Housekeeper], pin: "482913");
        var moneyRole = await Assert.ThrowsAsync<HccException>(async () =>
            await m.Users.AssignRoleAsync(tad, Key(), new AssignRoleRequest(staff2, books.RoleCode, null, null, await db.VersionOfAsync(staff2))));
        Assert.Equal(HccErrorCode.FORBIDDEN, moneyRole.Code);
    }
}
