using Hcc.Access.Tests.Harness;
using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;

namespace Hcc.Access.Tests.Sql;

/// <summary>Owns the "shift" scope for the tests: shift 500 belongs to the principal named in the constructor.</summary>
internal sealed class FakeShiftResolver(int ownerId) : IAccessScopeResolver
{
    public string Kind => AccessScopeKinds.Shift;

    public Task<bool> IsOwnAsync(int propertyId, int principalId, AccessScope scope, CancellationToken ct = default) =>
        Task.FromResult(principalId == ownerId && scope.TargetId == 500);
}

/// <summary>Real IAccessPolicy over the seeded matrix; user administration rules (SOD-002, DEC-058, SOD-003, DEC-062); devices; capabilities.</summary>
[Trait("Category", "Sql")]
public class AdminAndPolicyTests(AccessDatabase db) : IClassFixture<AccessDatabase>
{
    private const string Pw = "Sand-and-Maroon-26";

    private async Task<(AccessModule M, AuthenticatedSession Owner, int OwnerId)> OwnerAsync(string login, FakeClock? clock = null, IEnumerable<IAccessScopeResolver>? resolvers = null)
    {
        var m = db.Module(clock ?? FakeClock.At2026(), resolvers: resolvers);
        var id = await db.SeedUserAsync(login, ["OWN"], password: Pw);
        return (m, await AccessDatabase.SignInPasswordAsync(m, login, Pw), id);
    }

    [Fact]
    public async Task Real_policy_allows_forbids_and_requires_approval_from_the_seeded_matrix()
    {
        var recId = await db.SeedUserAsync("pol.rec", ["REC"], pin: "482913");
        var m = db.Module(FakeClock.At2026(), resolvers: [new FakeShiftResolver(recId)]);
        IAccessPolicy p = m.Policy;
        var prop = db.PropertyId;

        await p.RequireAsync(prop, recId, "PERM-RES-002");
        var forbidden = await Assert.ThrowsAsync<HccException>(() => p.RequireAsync(prop, recId, "PERM-ADM-001"));
        Assert.Equal(HccErrorCode.FORBIDDEN, forbidden.Code);
        await p.RequireAsync(prop, recId, "PERM-FOL-003", amountPaise: 25_000);
        var approval = await Assert.ThrowsAsync<HccException>(() => p.RequireAsync(prop, recId, "PERM-FOL-003", amountPaise: 25_001));
        Assert.Equal(HccErrorCode.APPROVAL_REQUIRED, approval.Code);
        Assert.Equal(("LIMIT", 25_000L, "PERM-FOL-003"), (approval.Details!["approvalKind"].GetString(), approval.Details["limit"].GetInt64(), approval.Details["permissionId"].GetString()));
        Assert.Equal(403, HccErrorCatalog.HttpStatus(approval.Code));

        // Own shift (A·S) through the owning module's resolver; another shift or no shift named → FORBIDDEN.
        await p.RequireAsync(prop, recId, "PERM-PAY-001", new AccessScope(AccessScopeKinds.Shift, 500));
        await Assert.ThrowsAsync<HccException>(() => p.RequireAsync(prop, recId, "PERM-PAY-001", new AccessScope(AccessScopeKinds.Shift, 501)));
        await Assert.ThrowsAsync<HccException>(() => p.RequireAsync(prop, recId, "PERM-PAY-001"));
        Assert.True(await p.HasAsync(prop, recId, "PERM-PAY-001"));

        // DEC-063 G2: Reception executes approved refunds from own shift only; G1: Cashier gives no discount.
        await p.RequireAsync(prop, recId, "PERM-PAY-004", new AccessScope(AccessScopeKinds.Shift, 500));
        var cshId = await db.SeedUserAsync("pol.csh", ["CSH"], pin: "482913");
        Assert.False(await p.HasAsync(prop, cshId, "PERM-FOL-003"));
        Assert.False(await p.HasAsync(prop, cshId, "PERM-RES-001"));   // G7

        // Wrong property → denied.
        Assert.False(await p.HasAsync(prop + 99, recId, "PERM-RES-002"));
        // Manager approval limited to ₹1,000 (DEC-063 G1).
        var mgrId = await db.SeedUserAsync("pol.mgr", ["MGR"], pin: "482913");
        await p.RequireAsync(prop, mgrId, "PERM-APR-001", amountPaise: 100_000);
        Assert.Equal(HccErrorCode.APPROVAL_REQUIRED, (await Assert.ThrowsAsync<HccException>(() => p.RequireAsync(prop, mgrId, "PERM-APR-001", amountPaise: 100_001))).Code);
    }

    [Fact]
    public async Task Housekeeper_assigned_task_scope_and_no_money()
    {
        var hkp = await db.SeedUserAsync("pol.hkp", ["HKP"], pin: "482913");
        var m = db.Module(FakeClock.At2026());
        Assert.False(await m.Policy.HasAsync(db.PropertyId, hkp, "PERM-FOL-001"));
        Assert.True(await m.Policy.HasAsync(db.PropertyId, hkp, "PERM-HK-003"));
        // No task resolver registered yet (P3) → a named task is outside scope: deny, never allow by default.
        await Assert.ThrowsAsync<HccException>(() => m.Policy.RequireAsync(db.PropertyId, hkp, "PERM-HK-003", new AccessScope(AccessScopeKinds.Task, 1)));
    }

    [Fact]
    public async Task Create_user_with_dated_roles_audit_and_idempotent_replay()
    {
        var (m, owner, _) = await OwnerAsync("adm.owner1");
        var req = new CreateUserRequest("New.Rec", "New Receptionist", [new RoleAssignmentInput("REC", null, new DateTime(2026, 12, 31, 18, 30, 0, DateTimeKind.Utc))]);
        var first = await m.Users.CreateUserAsync(owner, "create-1", req);
        var again = await m.Users.CreateUserAsync(owner, "create-1", req);
        Assert.False(first.Replayed);
        Assert.True(again.Replayed);
        Assert.Equal(first.Value, again.Value);
        Assert.Equal("new.rec", await db.ScalarAsync("SELECT login_name FROM plat.app_user WHERE user_id = @u;", ("@u", first.Value.UserId)));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.user_role WHERE user_id = @u AND valid_until_utc = '2026-12-31T18:30:00';", ("@u", first.Value.UserId)));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'USER_CREATE' AND target = @t;", ("@t", $"user:{first.Value.UserId}")));
        var dup = await Assert.ThrowsAsync<HccException>(() => m.Users.CreateUserAsync(owner, "create-2", req));
        Assert.Equal(HccErrorCode.VALIDATION, dup.Code);
        // Role expiry is honoured by the policy.
        var later = db.Module(new FakeClock(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(await later.Policy.HasAsync(db.PropertyId, first.Value.UserId, "PERM-RES-002"));
        Assert.True(await m.Policy.HasAsync(db.PropertyId, first.Value.UserId, "PERM-RES-002"));
    }

    [Fact]
    public async Task Never_self_grant_SOD002()
    {
        var (m, owner, ownerId) = await OwnerAsync("adm.self");
        var v = await db.VersionOfAsync(ownerId);
        var ex = await Assert.ThrowsAsync<HccException>(() => m.Users.AssignRoleAsync(owner, "self-1", new AssignRoleRequest(ownerId, "MGR", null, null, v)));
        Assert.Equal(HccErrorCode.FORBIDDEN, ex.Code);
        await Assert.ThrowsAsync<HccException>(() => m.Users.GrantExceptionAsync(owner, "self-2",
            new GrantExceptionRequest(ownerId, "PERM-RES-002", "ALLOW", "A", "P", null, null, "testing", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), v)));
        await Assert.ThrowsAsync<HccException>(() => m.Users.SetCredentialAsync(owner, "self-3", new SetCredentialRequest(ownerId, "Another-long-pass-1", null, v)));
    }

    [Fact]
    public async Task Tech_admin_manages_non_money_roles_only_DEC058()
    {
        var (m, owner, _) = await OwnerAsync("adm.owner2");
        await db.SeedUserAsync("adm.tad", ["TAD"], password: Pw);
        var tad = await AccessDatabase.SignInPasswordAsync(m, "adm.tad", Pw);
        var staff = await db.SeedUserAsync("adm.staff", ["HKP"], pin: "482913");

        await m.Users.AssignRoleAsync(tad, "tad-1", new AssignRoleRequest(staff, "SUP", null, null, await db.VersionOfAsync(staff)));
        foreach (var moneyRole in new[] { "MGR", "REC", "CSH", "ACC", "OWN", "TAD" })
        {
            var ex = await Assert.ThrowsAsync<HccException>(() => m.Users.AssignRoleAsync(tad, "tad-" + moneyRole, new AssignRoleRequest(staff, moneyRole, null, null, "AAAAAAAAAAA=")));
            Assert.Equal(HccErrorCode.FORBIDDEN, ex.Code);
        }

        var created = await Assert.ThrowsAsync<HccException>(() => m.Users.CreateUserAsync(tad, "tad-c", new CreateUserRequest("adm.cash", "Cash", [new RoleAssignmentInput("CSH")])));
        Assert.Equal(HccErrorCode.FORBIDDEN, created.Code);
        await m.Users.AssignRoleAsync(owner, "own-1", new AssignRoleRequest(staff, "MGR", null, null, await db.VersionOfAsync(staff)));

        // A Tech admin cannot touch an Owner account; a receptionist cannot administer users at all.
        var ownerId = owner.UserId;
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() =>
            m.Users.SetUserEnabledAsync(tad, "tad-dis", new SetUserEnabledRequest(ownerId, false, "AAAAAAAAAAA=")))).Code);
        await db.SeedUserAsync("adm.rec", ["REC"], pin: "482913");
        var (dev, devToken) = await db.SeedDeviceAsync();
        var (rec, _) = await m.Sessions.SignInAsync(new SignInRequest("adm.rec", Pin: "482913"), devToken);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() =>
            m.Users.CreateUserAsync(rec, "rec-c", new CreateUserRequest("adm.hk2", "HK", [new RoleAssignmentInput("HKP")])))).Code);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Assert.ThrowsAsync<HccException>(() => m.Users.RegisterDeviceAsync(rec, "rec-d", new RegisterDeviceRequest("Phone")))).Code);
        _ = dev;
    }

    [Fact]
    public async Task Last_active_owner_survives_two_owners_disabling_each_other_at_once_SOD003()
    {
        var (m, ownerA, idA) = await OwnerAsync("last.ownera");
        var idB = await db.SeedUserAsync("last.ownerb", ["OWN"], password: Pw);
        var ownerB = await AccessDatabase.SignInPasswordAsync(m, "last.ownerb", Pw);
        var vA = await db.VersionOfAsync(idA);
        var vB = await db.VersionOfAsync(idB);

        var results = await Task.WhenAll(
            Capture(() => m.Users.SetUserEnabledAsync(ownerA, "x-a", new SetUserEnabledRequest(idB, false, vB))),
            Capture(() => m.Users.SetUserEnabledAsync(ownerB, "x-b", new SetUserEnabledRequest(idA, false, vA))));
        Assert.Equal(1, results.Count(r => r is null));
        Assert.Equal(HccErrorCode.FORBIDDEN, results.Single(r => r is not null)!.Code);   // the loser is no longer an active Owner
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.app_user WHERE user_id IN (@a, @b) AND enabled = 1;", ("@a", idA), ("@b", idB)));

        // The surviving Owner cannot remove their own Owner role either (self change refused).
        var survivor = results[0] is null ? ownerA : ownerB;
        var self = await Assert.ThrowsAsync<HccException>(() => m.Users.RevokeRoleAsync(survivor, "rv-self", new RevokeRoleRequest(survivor.UserId, "OWN", "AAAAAAAAAAA=")));
        Assert.Equal(HccErrorCode.FORBIDDEN, self.Code);
    }

    private static async Task<HccException?> Capture(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (HccException ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task DEC062_owner_credential_must_be_a_password_and_PIN_rules_apply_to_staff()
    {
        var (m, owner, _) = await OwnerAsync("cred.owner");
        var own2 = await db.SeedUserAsync("cred.owner2", ["OWN"], password: Pw);
        var pinForOwner = await Assert.ThrowsAsync<HccException>(async () => await m.Users.SetCredentialAsync(owner, "c-1", new SetCredentialRequest(own2, null, "305718", await db.VersionOfAsync(own2))));
        Assert.Equal(HccErrorCode.VALIDATION, pinForOwner.Code);

        var staff = await db.SeedUserAsync("cred.staff", ["CSH"]);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() =>
            m.Users.SetCredentialAsync(owner, "c-2", new SetCredentialRequest(staff, "A-long-enough-pass-1", null, "AAAAAAAAAAA=")))).Code);
        Assert.Equal(HccErrorCode.VALIDATION, (await Assert.ThrowsAsync<HccException>(() =>
            m.Users.SetCredentialAsync(owner, "c-3", new SetCredentialRequest(staff, null, "123456", "AAAAAAAAAAA=")))).Code);
        await m.Users.SetCredentialAsync(owner, "c-4", new SetCredentialRequest(staff, null, "305718", await db.VersionOfAsync(staff)));
        await m.Users.SetCredentialAsync(owner, "c-5", new SetCredentialRequest(staff, null, "920461", await db.VersionOfAsync(staff)));
        var reuse = await Assert.ThrowsAsync<HccException>(() => m.Users.SetCredentialAsync(owner, "c-6", new SetCredentialRequest(staff, null, "305718", "AAAAAAAAAAA=")));
        Assert.Equal(HccErrorCode.VALIDATION, reuse.Code);   // one of the last 3 PINs
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.credential WHERE user_id = @u AND kind = 'PIN' AND iterations = 1000;", ("@u", staff)));

        // Promoting a PIN user to a password role (Accountant) removes the PIN: PIN sign-in is refused from then on.
        var (_, device) = await db.SeedDeviceAsync();
        await m.Sessions.SignInAsync(new SignInRequest("cred.staff", Pin: "920461"), device);
        await m.Users.AssignRoleAsync(owner, "c-7", new AssignRoleRequest(staff, "ACC", null, null, await db.VersionOfAsync(staff)));
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM plat.credential WHERE user_id = @u AND kind = 'PIN';", ("@u", staff)));
        var refused = await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("cred.staff", Pin: "920461"), device));
        Assert.Equal("WRONG_CREDENTIAL_KIND", refused.Details!["reason"].GetString());
    }

    [Fact]
    public async Task Stale_version_and_unknown_user()
    {
        var (m, owner, _) = await OwnerAsync("stale.owner");
        var staff = await db.SeedUserAsync("stale.staff", ["HKP"]);
        var v = await db.VersionOfAsync(staff);
        await m.Users.AssignRoleAsync(owner, "s-1", new AssignRoleRequest(staff, "SUP", null, null, v));
        Assert.Equal(HccErrorCode.STALE_VERSION, (await Assert.ThrowsAsync<HccException>(() =>
            m.Users.AssignRoleAsync(owner, "s-2", new AssignRoleRequest(staff, "MNT", null, null, v)))).Code);
        Assert.Equal(HccErrorCode.NOT_FOUND, (await Assert.ThrowsAsync<HccException>(() =>
            m.Users.SetUserEnabledAsync(owner, "s-3", new SetUserEnabledRequest(999_999, false, v)))).Code);
    }

    [Fact]
    public async Task User_exception_deny_takes_effect_and_expires()
    {
        var clock = FakeClock.At2026();
        var (m, owner, _) = await OwnerAsync("exc.owner", clock);
        var rec = await db.SeedUserAsync("exc.rec", ["REC"]);
        Assert.True(await m.Policy.HasAsync(db.PropertyId, rec, "PERM-RES-004"));
        var granted = await m.Users.GrantExceptionAsync(owner, "e-1", new GrantExceptionRequest(rec, "PERM-RES-004", "DENY", "A", "P", null, null,
            "Cancellations paused during audit", clock.Now.UtcDateTime.AddHours(2), await db.VersionOfAsync(rec)));
        Assert.False(await m.Policy.HasAsync(db.PropertyId, rec, "PERM-RES-004"));
        clock.Advance(TimeSpan.FromHours(2));
        Assert.True(await m.Policy.HasAsync(db.PropertyId, rec, "PERM-RES-004"));
        await m.Users.RevokeExceptionAsync(owner, "e-2", new RevokeExceptionRequest(rec, granted.Value.ExceptionId, granted.Value.Version));
        // Tech admin holds R on admin.roles: an exception needs an Owner (APPROVAL_REQUIRED, request only).
        await db.SeedUserAsync("exc.tad", ["TAD"], password: Pw);
        var tad = await AccessDatabase.SignInPasswordAsync(m, "exc.tad", Pw);
        var v = await db.VersionOfAsync(rec);
        var req = await Assert.ThrowsAsync<HccException>(() => m.Users.GrantExceptionAsync(tad, "e-3", new GrantExceptionRequest(rec, "PERM-RES-004", "ALLOW", "A", "P", null, null,
            "Need it", clock.Now.UtcDateTime.AddHours(2), v)));
        Assert.Equal(HccErrorCode.APPROVAL_REQUIRED, req.Code);
    }

    [Fact]
    public async Task Devices_are_registered_by_owner_or_tech_admin_and_the_token_is_shown_once()
    {
        var (m, owner, _) = await OwnerAsync("dev.owner");
        var reg = await m.Users.RegisterDeviceAsync(owner, "d-1", new RegisterDeviceRequest("Reception tablet"));
        Assert.NotNull(reg.Value.DeviceToken);
        var replay = await m.Users.RegisterDeviceAsync(owner, "d-1", new RegisterDeviceRequest("Reception tablet"));
        Assert.True(replay.Replayed);
        Assert.Null(replay.Value.DeviceToken);
        Assert.Equal(reg.Value.DeviceId, replay.Value.DeviceId);
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM plat.idempotency WHERE result_json LIKE @t;", ("@t", "%" + reg.Value.DeviceToken + "%")));

        await db.SeedUserAsync("dev.tad", ["TAD"], password: Pw);
        var tad = await AccessDatabase.SignInPasswordAsync(m, "dev.tad", Pw);
        var reg2 = await m.Users.RegisterDeviceAsync(tad, "d-2", new RegisterDeviceRequest("HK phone"));
        await db.SeedUserAsync("dev.hkp", ["HKP"], pin: "482913");
        await m.Sessions.SignInAsync(new SignInRequest("dev.hkp", Pin: "482913"), reg2.Value.DeviceToken);
        await m.Users.RevokeDeviceAsync(tad, "d-3", new RevokeDeviceRequest(reg2.Value.DeviceId));
        await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("dev.hkp", Pin: "482913"), reg2.Value.DeviceToken));
        var list = await m.Users.ListDevicesAsync(owner, 1, 100);
        Assert.Contains(list.Items, d => d.DeviceId == reg2.Value.DeviceId && d.RevokedUtc is not null);
        Assert.Equal(HccErrorCode.NOT_FOUND, (await Assert.ThrowsAsync<HccException>(() => m.Users.RevokeDeviceAsync(tad, "d-4", new RevokeDeviceRequest(reg2.Value.DeviceId)))).Code);
    }

    [Fact]
    public async Task Capabilities_list_held_permissions_and_field_masks()
    {
        var m = db.Module(FakeClock.At2026());
        await db.SeedUserAsync("cap.hkp", ["HKP"], pin: "482913");
        await db.SeedUserAsync("cap.rec", ["REC"], pin: "482913");
        var (_, device) = await db.SeedDeviceAsync();
        var (hkp, _) = await m.Sessions.SignInAsync(new SignInRequest("cap.hkp", Pin: "482913"), device);
        var (rec, _) = await m.Sessions.SignInAsync(new SignInRequest("cap.rec", Pin: "482913"), device);

        var h = await m.Capabilities.GetAsync(hkp);
        Assert.Equal(["HKP"], h.Roles);
        // PERM-HR-004 (0901 payroll): own roster, check-in, leave and OWN payslip, scope O (DEC-061).
        Assert.Equal(["PERM-HK-001", "PERM-HK-003", "PERM-HR-004", "PERM-ROOM-001"], h.Permissions.Select(p => p.PermissionId).ToArray());
        Assert.All(h.Permissions, p => Assert.Equal(p.PermissionId == "PERM-HR-004" ? "O" : "A", p.Scope));
        Assert.Equal((FieldMasks.Hidden, FieldMasks.OwnOnly), (h.FieldMasks[FieldMasks.HotelMoney], h.FieldMasks[FieldMasks.StaffPay]));
        Assert.Equal("PIN", h.CredentialKind);

        var r = await m.Capabilities.GetAsync(rec);
        var discount = r.Permissions.Single(p => p.PermissionId == "PERM-FOL-003");
        Assert.Equal(("L", 25_000L, "PAISE"), (discount.Mode, discount.Limit!.Value, discount.LimitUnit!));
        Assert.Equal("R", r.Permissions.Single(p => p.PermissionId == "PERM-FOL-005").Mode);
        Assert.Equal("S", r.Permissions.Single(p => p.PermissionId == "PERM-PAY-001").Scope);
        Assert.DoesNotContain(r.Permissions, p => p.PermissionId is "PERM-RPT-001" or "PERM-HK-001" or "PERM-ADM-001");   // DEC-063 G8
        Assert.Equal(FieldMasks.Visible, r.FieldMasks[FieldMasks.GuestIdNumber]);
        Assert.Equal(1800, r.Session.IdleTimeoutSeconds);
    }
}

/// <summary>Bootstrap of the first Owner, on its own empty database.</summary>
[Trait("Category", "Sql")]
public class BootstrapOwnerTests(AccessDatabase db) : IClassFixture<AccessDatabase>
{
    [Fact]
    public async Task First_owner_only_on_an_empty_installation_then_signs_in_with_password()
    {
        var m = db.Module(FakeClock.At2026());
        await Assert.ThrowsAsync<HccException>(() => m.Users.BootstrapOwnerAsync(new BootstrapOwnerRequest(db.PropertyId, "boot.owner", "Owner", "short")));
        var first = await m.Users.BootstrapOwnerAsync(new BootstrapOwnerRequest(db.PropertyId, "Boot.Owner", "Owner", "Sand-and-Maroon-26"));
        var again = await Assert.ThrowsAsync<HccException>(() => m.Users.BootstrapOwnerAsync(new BootstrapOwnerRequest(db.PropertyId, "boot.two", "Owner 2", "Sand-and-Maroon-26")));
        Assert.Equal(HccErrorCode.FORBIDDEN, again.Code);
        var s = await AccessDatabase.SignInPasswordAsync(m, "boot.owner", "Sand-and-Maroon-26");
        Assert.Equal(first.UserId, s.UserId);
        Assert.True(await m.Policy.HasAsync(db.PropertyId, s.UserId, "PERM-ADM-001"));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'OWNER_BOOTSTRAP';"));
    }
}
