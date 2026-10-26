using Hcc.Access.Tests.Harness;
using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;

namespace Hcc.Access.Tests.Sql;

/// <summary>W4-5 admin reads: role templates (seeded matrix), user exceptions and the audit log, with their permission gates.</summary>
[Trait("Category", "Sql")]
public class AdminReadsTests(AccessDatabase db) : IClassFixture<AccessDatabase>
{
    private const string Pw = "Sand-and-Maroon-26";

    private AuthenticatedSession As(int userId) =>
        new(0, userId, db.PropertyId, "test", CredentialKind.Pin, null, new DateTime(2026, 10, 25, 7, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 25, 18, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task Roles_list_the_seeded_templates_with_modes_limits_and_denies_for_admins_only()
    {
        var m = db.Module(FakeClock.At2026());
        var owner = await db.SeedUserAsync("rd.owner", ["OWN"], password: Pw);
        var tad = await db.SeedUserAsync("rd.tad", ["TAD"], password: Pw);
        var rec = await db.SeedUserAsync("rd.rec", ["REC"], pin: "482913");

        var r = await m.AdminQueries.RolesAsync(As(owner));
        Assert.Equal(RoleCodes.All.Count, r.Roles.Count);
        var own = r.Roles.Single(x => x.RoleCode == "OWN");
        Assert.Contains(own.Grants, g => g.PermissionId == "PERM-ADM-001" && g.Mode == "A" && g.Scope == "P");
        var mgr = r.Roles.Single(x => x.RoleCode == "MGR");
        Assert.Contains(mgr.Grants, g => g.PermissionId == "PERM-APR-001" && g.Mode == "L" && g.Limit == 100_000 && g.LimitUnit == "PAISE");
        Assert.Contains(r.Roles.Single(x => x.RoleCode == "TAD").Grants, g => g.PermissionId == "PERM-ADM-003" && g.Mode == "V");
        Assert.Contains(r.Permissions, p => p.PermissionId == "PERM-FOL-003" && p.IsMoney);

        // Tech admin reads them (PERM-ADM-001 A); Reception may not.
        Assert.Equal(r.Roles.Count, (await m.AdminQueries.RolesAsync(As(tad))).Roles.Count);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => m.AdminQueries.RolesAsync(As(rec)))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => m.AdminQueries.ExceptionsAsync(As(rec), false))).Code);
    }

    [Fact]
    public async Task Exceptions_and_audit_show_what_the_admin_commands_did()
    {
        var m = db.Module(FakeClock.At2026());
        await db.SeedUserAsync("rd.owner2", ["OWN"], password: Pw);
        var ownerSession = await AccessDatabase.SignInPasswordAsync(m, "rd.owner2", Pw);
        var mgr = await db.SeedUserAsync("rd.mgr", ["MGR"], pin: "482913");
        var acc = await db.SeedUserAsync("rd.acc", ["ACC"], password: Pw);
        var hkp = await db.SeedUserAsync("rd.hkp", ["HKP"], pin: "482913");
        var until = new DateTime(2026, 10, 31, 18, 30, 0, DateTimeKind.Utc);
        var granted = await m.Users.GrantExceptionAsync(ownerSession, "rd-exc-1",
            new GrantExceptionRequest(hkp, "PERM-ROOM-002", "ALLOW", "A", "P", null, null, "Covering the supervisor", until, await db.VersionOfAsync(hkp)));
        var revoked = await m.Users.GrantExceptionAsync(ownerSession, "rd-exc-2",
            new GrantExceptionRequest(hkp, "PERM-HK-002", "ALLOW", "A", "P", null, null, "One day only", until, granted.Value.Version));
        await m.Users.RevokeExceptionAsync(ownerSession, "rd-exc-3", new RevokeExceptionRequest(hkp, revoked.Value.ExceptionId, revoked.Value.Version));

        var active = (await m.AdminQueries.ExceptionsAsync(ownerSession, false)).Items.Where(e => e.UserId == hkp).ToList();
        var e = Assert.Single(active);
        Assert.Equal((granted.Value.ExceptionId, "PERM-ROOM-002", "ALLOW", "Covering the supervisor", until), (e.ExceptionId, e.PermissionId, e.Effect, e.Reason, e.ValidUntilUtc));
        Assert.Null(e.RevokedUtc);
        Assert.Equal(2, (await m.AdminQueries.ExceptionsAsync(ownerSession, true)).Items.Count(x => x.UserId == hkp));

        // Audit: Owner, Manager and Accountant (PERM-ADM-004); newest first; filter by action; Housekeeper refused.
        var page = await m.AdminQueries.AuditAsync(As(mgr), 1, 50, "EXCEPTION_GRANT", null, null, null);
        Assert.True(page.Total >= 2);
        Assert.All(page.Items, a => Assert.Equal("EXCEPTION_GRANT", a.Action));
        Assert.True(page.Items[0].AuditId > page.Items[1].AuditId);
        Assert.Contains(page.Items, a => a.Target == $"user:{hkp}" && a.Actor == "User rd.owner2");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330));   // audit times come from the database clock (hotel date, IST)
        var all = await m.AdminQueries.AuditAsync(As(acc), 1, 2, null, null, today.AddDays(-1), today.AddDays(1));
        Assert.Equal(2, all.Items.Count);
        Assert.Equal(0, (await m.AdminQueries.AuditAsync(As(acc), 1, 10, null, null, new DateOnly(2020, 9, 1), new DateOnly(2020, 9, 2))).Total);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => m.AdminQueries.AuditAsync(As(hkp), 1, 10, null, null, null, null))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() => m.AdminQueries.AuditAsync(As(acc), 1, ApiConventions.MaxPageSize + 1, null, null, null, null))).Code);
    }
}
