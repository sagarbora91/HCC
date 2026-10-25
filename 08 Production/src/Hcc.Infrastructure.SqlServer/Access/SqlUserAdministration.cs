using System.Data;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Access;

/// <summary>Input rules for user administration.</summary>
public static partial class UserAdminRules
{
    [GeneratedRegex(@"^[a-z0-9._-]{3,40}$")]
    private static partial Regex Login();

    public static string NormalizeLogin(string? login)
    {
        var l = (login ?? string.Empty).Trim().ToLowerInvariant();
        if (!Login().IsMatch(l)) throw HccException.Validation("The sign-in name must be 3–40 characters: letters, digits, dot, dash or underscore.");
        return l;
    }

    public static string DisplayName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length is < 1 or > 100) throw HccException.Validation("The display name must be 1–100 characters.");
        return n;
    }
}

/// <summary>
/// User, role, credential, exception and device administration. Every command goes through <see cref="ICommandRunner"/>
/// (Idempotency-Key, audit, one transaction). Rules: PERM-ADM-001 (users/devices: Owner, Tech admin), PERM-ADM-002
/// (exceptions: Owner); never self-grant (PERM-SOD-002); roles holding money/admin permissions are assigned and revoked by an
/// Owner only, as is any change to an Owner account (DEC-058); the last active Owner cannot be disabled or lose the Owner role
/// (PERM-SOD-003, DEC-033); credential kind per role (DEC-062). Each change bumps the user's security stamp, so the user's open
/// sessions end on their next request (AS-07).
/// </summary>
public sealed class SqlUserAdministration(SqlConnectionFactory connections, ICommandRunner runner, IAccessPolicy policy, ISecretHasher hasher, AccessOptions options, TimeProvider clock) : IUserAdministration
{
    private const string AdmUsers = "PERM-ADM-001";
    private const string AdmRoles = "PERM-ADM-002";
    private const string OwnersLock = "hcc.access.owners";

    // ---------- users ----------

    public async Task<CommandResult<UserVersionResult>> CreateUserAsync(AuthenticatedSession actor, string idempotencyKey, CreateUserRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmUsers, ct: ct).ConfigureAwait(false);
        var login = UserAdminRules.NormalizeLogin(request.LoginName);
        var display = UserAdminRules.DisplayName(request.DisplayName);
        if (request.Roles is null || request.Roles.Count == 0) throw HccException.Validation("Give the user at least one role.");
        if (request.Roles.Select(r => r.RoleCode).Distinct(StringComparer.Ordinal).Count() != request.Roles.Count) throw HccException.Validation("A role is listed twice.");

        return await runner.RunAsync(actor.CommandContext("CreateUser", idempotencyKey), request with { LoginName = login, DisplayName = display }, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            var actorRoles = await ActiveRolesAsync(t, actor.UserId, now, c).ConfigureAwait(false);
            foreach (var r in request.Roles) await EnsureMayAssignAsync(t, actorRoles, r.RoleCode, c).ConfigureAwait(false);

            await using (var exists = t.Command("SELECT COUNT(*) FROM plat.app_user WITH (UPDLOCK, HOLDLOCK) WHERE login_name = @l;"))
            {
                exists.Parameters.Add("@l", SqlDbType.VarChar, 40).Value = login;
                if ((int)(await exists.ExecuteScalarAsync(c).ConfigureAwait(false))! > 0) throw HccException.Validation("This sign-in name is already used.");
            }

            int userId;
            await using (var ins = t.Command("""
                INSERT plat.app_user(property_id, login_name, display_name, created_utc, created_by) OUTPUT INSERTED.user_id VALUES (@p, @l, @n, @now, @by);
                """))
            {
                ins.Parameters.Add("@p", SqlDbType.Int).Value = actor.PropertyId;
                ins.Parameters.Add("@l", SqlDbType.VarChar, 40).Value = login;
                ins.Parameters.Add("@n", SqlDbType.NVarChar, 100).Value = display;
                ins.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
                ins.Parameters.Add("@by", SqlDbType.Int).Value = actor.UserId;
                userId = (int)(await ins.ExecuteScalarAsync(c).ConfigureAwait(false))!;
            }

            foreach (var r in request.Roles) await UpsertRoleAsync(t, userId, r.RoleCode, r.ValidFromUtc ?? now, r.ValidUntilUtc, actor.UserId, now, c).ConfigureAwait(false);
            await t.AuditAsync("USER_CREATE", $"user:{userId}", $"Created '{login}' with roles {string.Join(",", request.Roles.Select(r => r.RoleCode))}.", c).ConfigureAwait(false);
            return new UserVersionResult(userId, await VersionAsync(t, userId, c).ConfigureAwait(false));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<UserVersionResult>> SetUserEnabledAsync(AuthenticatedSession actor, string idempotencyKey, SetUserEnabledRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmUsers, ct: ct).ConfigureAwait(false);
        EnsureNotSelf(actor, request.UserId);
        var version = SqlValues.RowVersionFromWire(request.ExpectedVersion);

        return await runner.RunAsync(actor.CommandContext("SetUserEnabled", idempotencyKey), request, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            await LockOwnersAsync(t, c).ConfigureAwait(false);
            var target = await LoadTargetAsync(t, actor, request.UserId, now, c).ConfigureAwait(false);
            await EnsureOwnerForOwnerAccountAsync(t, actor, target, now, c).ConfigureAwait(false);
            if (!request.Enabled && target.IsActiveOwner) await EnsureAnotherActiveOwnerAsync(t, target.UserId, now, c).ConfigureAwait(false);

            await using (var u = t.Command("UPDATE plat.app_user SET enabled = @e, security_stamp = security_stamp + 1 WHERE user_id = @u AND row_ver = @v;"))
            {
                u.Parameters.Add("@e", SqlDbType.Bit).Value = request.Enabled;
                u.Parameters.Add("@u", SqlDbType.Int).Value = request.UserId;
                u.Parameters.Add("@v", SqlDbType.Binary, 8).Value = version;
                SqlValues.EnsureVersionMatched(await u.ExecuteNonQueryAsync(c).ConfigureAwait(false), "The user");
            }

            await t.AuditAsync(request.Enabled ? "USER_ENABLE" : "USER_DISABLE", $"user:{request.UserId}", $"{(request.Enabled ? "Enabled" : "Disabled")} '{target.Login}'.", c).ConfigureAwait(false);
            return new UserVersionResult(request.UserId, await VersionAsync(t, request.UserId, c).ConfigureAwait(false));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<UserVersionResult>> AssignRoleAsync(AuthenticatedSession actor, string idempotencyKey, AssignRoleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmUsers, ct: ct).ConfigureAwait(false);
        EnsureNotSelf(actor, request.UserId);
        var version = SqlValues.RowVersionFromWire(request.ExpectedVersion);

        return await runner.RunAsync(actor.CommandContext("AssignRole", idempotencyKey), request, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            await LockOwnersAsync(t, c).ConfigureAwait(false);
            var target = await LoadTargetAsync(t, actor, request.UserId, now, c).ConfigureAwait(false);
            var actorRoles = await ActiveRolesAsync(t, actor.UserId, now, c).ConfigureAwait(false);
            await EnsureMayAssignAsync(t, actorRoles, request.RoleCode, c).ConfigureAwait(false);
            await EnsureOwnerForOwnerAccountAsync(t, actor, target, now, c).ConfigureAwait(false);
            var from = request.ValidFromUtc ?? now;
            var stillActive = new RoleAssignment(request.RoleCode, from, request.ValidUntilUtc).IsActive(now);
            if (request.RoleCode == RoleCodes.Owner && target.IsActiveOwner && !stillActive)
                await EnsureAnotherActiveOwnerAsync(t, target.UserId, now, c).ConfigureAwait(false);

            await BumpAsync(t, request.UserId, version, c).ConfigureAwait(false);
            await UpsertRoleAsync(t, request.UserId, request.RoleCode, from, request.ValidUntilUtc, actor.UserId, now, c).ConfigureAwait(false);
            await t.AuditAsync("ROLE_ASSIGN", $"user:{request.UserId}", $"Role {request.RoleCode} from {AuthenticatedSession.Iso(from)} until {(request.ValidUntilUtc is { } u ? AuthenticatedSession.Iso(u) : "open")}.", c).ConfigureAwait(false);
            return new UserVersionResult(request.UserId, await VersionAsync(t, request.UserId, c).ConfigureAwait(false));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<UserVersionResult>> RevokeRoleAsync(AuthenticatedSession actor, string idempotencyKey, RevokeRoleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmUsers, ct: ct).ConfigureAwait(false);
        EnsureNotSelf(actor, request.UserId);
        var version = SqlValues.RowVersionFromWire(request.ExpectedVersion);

        return await runner.RunAsync(actor.CommandContext("RevokeRole", idempotencyKey), request, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            await LockOwnersAsync(t, c).ConfigureAwait(false);
            var target = await LoadTargetAsync(t, actor, request.UserId, now, c).ConfigureAwait(false);
            if (!target.AssignedRoles.Contains(request.RoleCode)) throw new HccException(HccErrorCode.NOT_FOUND, "The user does not have this role.");
            var actorRoles = await ActiveRolesAsync(t, actor.UserId, now, c).ConfigureAwait(false);
            await EnsureMayAssignAsync(t, actorRoles, request.RoleCode, c).ConfigureAwait(false);
            await EnsureOwnerForOwnerAccountAsync(t, actor, target, now, c).ConfigureAwait(false);
            if (request.RoleCode == RoleCodes.Owner && target.IsActiveOwner) await EnsureAnotherActiveOwnerAsync(t, target.UserId, now, c).ConfigureAwait(false);

            await BumpAsync(t, request.UserId, version, c).ConfigureAwait(false);
            await using (var d = t.Command("DELETE plat.user_role WHERE user_id = @u AND role_code = @r;"))
            {
                d.Parameters.Add("@u", SqlDbType.Int).Value = request.UserId;
                d.Parameters.Add("@r", SqlDbType.VarChar, 3).Value = request.RoleCode;
                await d.ExecuteNonQueryAsync(c).ConfigureAwait(false);
            }

            await t.AuditAsync("ROLE_REVOKE", $"user:{request.UserId}", $"Role {request.RoleCode} removed.", c).ConfigureAwait(false);
            return new UserVersionResult(request.UserId, await VersionAsync(t, request.UserId, c).ConfigureAwait(false));
        }, ct).ConfigureAwait(false);
    }

    // ---------- credentials ----------

    public async Task<CommandResult<UserVersionResult>> SetCredentialAsync(AuthenticatedSession actor, string idempotencyKey, SetCredentialRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmUsers, ct: ct).ConfigureAwait(false);
        if (request.UserId == actor.UserId) throw new HccException(HccErrorCode.FORBIDDEN, "Change your own password or PIN from your profile.");
        var (kind, secret) = OneSecret(request.Password, request.Pin);
        var version = SqlValues.RowVersionFromWire(request.ExpectedVersion);

        // The secret never enters the idempotency payload hash (a 6-digit PIN would be recoverable from a SHA-256).
        var payload = new { request.UserId, Kind = CredentialPolicy.ToWire(kind), request.ExpectedVersion };
        return await runner.RunAsync(actor.CommandContext("SetCredential", idempotencyKey), payload, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            var target = await LoadTargetAsync(t, actor, request.UserId, now, c).ConfigureAwait(false);
            await EnsureOwnerForOwnerAccountAsync(t, actor, target, now, c).ConfigureAwait(false);
            await StoreCredentialAsync(t, target, kind, secret, now, c).ConfigureAwait(false);
            await BumpAsync(t, request.UserId, version, c).ConfigureAwait(false);
            await t.AuditAsync("CREDENTIAL_SET", $"user:{request.UserId}", $"{CredentialPolicy.ToWire(kind)} set by administrator; lockout cleared.", c).ConfigureAwait(false);
            return new UserVersionResult(request.UserId, await VersionAsync(t, request.UserId, c).ConfigureAwait(false));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<UserVersionResult>> ChangeOwnCredentialAsync(AuthenticatedSession actor, string idempotencyKey, ChangeOwnCredentialRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        var (kind, secret) = OneSecret(request.NewPassword, request.NewPin);
        await VerifyCurrentSecretAsync(actor, kind, request.Current ?? string.Empty, ct).ConfigureAwait(false);

        var payload = new { actor.UserId, Kind = CredentialPolicy.ToWire(kind) };
        return await runner.RunAsync(actor.CommandContext("ChangeOwnCredential", idempotencyKey), payload, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            var target = await LoadTargetAsync(t, actor, actor.UserId, now, c).ConfigureAwait(false);
            await StoreCredentialAsync(t, target, kind, secret, now, c).ConfigureAwait(false);
            // Other sessions of this user end; the current one is carried over to the new stamp.
            await using (var u = t.Command("""
                UPDATE plat.app_user SET security_stamp = security_stamp + 1 WHERE user_id = @u;
                UPDATE plat.session SET security_stamp = (SELECT security_stamp FROM plat.app_user WHERE user_id = @u) WHERE session_id = @s;
                """))
            {
                u.Parameters.Add("@u", SqlDbType.Int).Value = actor.UserId;
                u.Parameters.Add("@s", SqlDbType.BigInt).Value = actor.SessionId;
                await u.ExecuteNonQueryAsync(c).ConfigureAwait(false);
            }

            await t.AuditAsync("CREDENTIAL_CHANGE", $"user:{actor.UserId}", $"Own {CredentialPolicy.ToWire(kind)} changed.", c).ConfigureAwait(false);
            return new UserVersionResult(actor.UserId, await VersionAsync(t, actor.UserId, c).ConfigureAwait(false));
        }, ct).ConfigureAwait(false);
    }

    // ---------- user exceptions ----------

    public async Task<CommandResult<ExceptionResult>> GrantExceptionAsync(AuthenticatedSession actor, string idempotencyKey, GrantExceptionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmRoles, ct: ct).ConfigureAwait(false);
        EnsureNotSelf(actor, request.UserId);
        if (request.Effect is not ("ALLOW" or "DENY")) throw HccException.Validation("Effect must be ALLOW or DENY.");
        if (request.Mode is not { Length: 1 } || "ALRVM".IndexOf(request.Mode[0], StringComparison.Ordinal) < 0) throw HccException.Validation("Mode must be A, L, R, V or M.");
        if (request.Scope is not { Length: 1 } || "PSAO".IndexOf(request.Scope[0], StringComparison.Ordinal) < 0) throw HccException.Validation("Scope must be P, S, A or O.");
        if ((request.Mode == "L") != (request.Limit is not null) || (request.Limit is null) != (request.LimitUnit is null) || request.Limit < 0)
            throw HccException.Validation("A limit (with unit PAISE or BPS) goes with mode L only.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 3 or > 400) throw HccException.Validation("Give a reason (3–400 characters).");
        var version = SqlValues.RowVersionFromWire(request.ExpectedVersion);

        return await runner.RunAsync(actor.CommandContext("GrantException", idempotencyKey), request, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            if (request.ValidUntilUtc <= now) throw HccException.Validation("The exception must expire in the future.");
            var target = await LoadTargetAsync(t, actor, request.UserId, now, c).ConfigureAwait(false);
            await EnsureOwnerForOwnerAccountAsync(t, actor, target, now, c).ConfigureAwait(false);
            await BumpAsync(t, request.UserId, version, c).ConfigureAwait(false);
            int id;
            await using (var ins = t.Command("""
                INSERT plat.user_exception(user_id, permission_id, effect, mode, scope, limit_value, limit_unit, reason, valid_until_utc, granted_by, granted_utc)
                OUTPUT INSERTED.exception_id VALUES (@u, @perm, @e, @m, @s, @lv, @lu, @r, @until, @by, @now);
                """))
            {
                ins.Parameters.Add("@u", SqlDbType.Int).Value = request.UserId;
                ins.Parameters.Add("@perm", SqlDbType.VarChar, 20).Value = request.PermissionId;
                ins.Parameters.Add("@e", SqlDbType.VarChar, 5).Value = request.Effect;
                ins.Parameters.Add("@m", SqlDbType.Char, 1).Value = request.Mode;
                ins.Parameters.Add("@s", SqlDbType.Char, 1).Value = request.Scope;
                ins.Parameters.Add("@lv", SqlDbType.BigInt).Value = (object?)request.Limit ?? DBNull.Value;
                ins.Parameters.Add("@lu", SqlDbType.VarChar, 5).Value = (object?)request.LimitUnit ?? DBNull.Value;
                ins.Parameters.Add("@r", SqlDbType.NVarChar, 400).Value = request.Reason.Trim();
                ins.Parameters.Add("@until", SqlDbType.DateTime2).Value = request.ValidUntilUtc;
                ins.Parameters.Add("@by", SqlDbType.Int).Value = actor.UserId;
                ins.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
                try
                {
                    id = (int)(await ins.ExecuteScalarAsync(c).ConfigureAwait(false))!;
                }
                catch (SqlException ex) when (ex.Number == 547)
                {
                    throw HccException.Validation("Unknown permission id.");
                }
            }

            await t.AuditAsync("EXCEPTION_GRANT", $"user:{request.UserId}", $"{request.Effect} {request.PermissionId} {request.Mode}·{request.Scope} until {AuthenticatedSession.Iso(request.ValidUntilUtc)}: {request.Reason.Trim()}", c).ConfigureAwait(false);
            return new ExceptionResult(id, request.UserId, await VersionAsync(t, request.UserId, c).ConfigureAwait(false));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ExceptionResult>> RevokeExceptionAsync(AuthenticatedSession actor, string idempotencyKey, RevokeExceptionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmRoles, ct: ct).ConfigureAwait(false);
        EnsureNotSelf(actor, request.UserId);
        var version = SqlValues.RowVersionFromWire(request.ExpectedVersion);

        return await runner.RunAsync(actor.CommandContext("RevokeException", idempotencyKey), request, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            var target = await LoadTargetAsync(t, actor, request.UserId, now, c).ConfigureAwait(false);
            await EnsureOwnerForOwnerAccountAsync(t, actor, target, now, c).ConfigureAwait(false);
            await BumpAsync(t, request.UserId, version, c).ConfigureAwait(false);
            await using (var u = t.Command("UPDATE plat.user_exception SET revoked_utc = @now, revoked_by = @by WHERE exception_id = @id AND user_id = @u AND revoked_utc IS NULL;"))
            {
                u.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
                u.Parameters.Add("@by", SqlDbType.Int).Value = actor.UserId;
                u.Parameters.Add("@id", SqlDbType.Int).Value = request.ExceptionId;
                u.Parameters.Add("@u", SqlDbType.Int).Value = request.UserId;
                if (await u.ExecuteNonQueryAsync(c).ConfigureAwait(false) == 0) throw new HccException(HccErrorCode.NOT_FOUND, "No such active exception.");
            }

            await t.AuditAsync("EXCEPTION_REVOKE", $"user:{request.UserId}", $"Exception {request.ExceptionId} revoked.", c).ConfigureAwait(false);
            return new ExceptionResult(request.ExceptionId, request.UserId, await VersionAsync(t, request.UserId, c).ConfigureAwait(false));
        }, ct).ConfigureAwait(false);
    }

    // ---------- devices (DEC-062: registered by Owner / Tech admin = PERM-ADM-001) ----------

    public async Task<CommandResult<RegisterDeviceResult>> RegisterDeviceAsync(AuthenticatedSession actor, string idempotencyKey, RegisterDeviceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmUsers, ct: ct).ConfigureAwait(false);
        var label = (request.Label ?? string.Empty).Trim();
        if (label.Length is < 1 or > 60) throw HccException.Validation("The device name must be 1–60 characters.");
        var token = AccessTokens.NewToken();

        var result = await runner.RunAsync(actor.CommandContext("RegisterDevice", idempotencyKey), new { Label = label }, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            int id;
            await using (var ins = t.Command("""
                INSERT plat.device(property_id, label, token_sha256, registered_by, registered_utc) OUTPUT INSERTED.device_id VALUES (@p, @l, @h, @by, @now);
                """))
            {
                ins.Parameters.Add("@p", SqlDbType.Int).Value = actor.PropertyId;
                ins.Parameters.Add("@l", SqlDbType.NVarChar, 60).Value = label;
                ins.Parameters.Add("@h", SqlDbType.Binary, 32).Value = AccessTokens.Sha256(token);
                ins.Parameters.Add("@by", SqlDbType.Int).Value = actor.UserId;
                ins.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
                id = (int)(await ins.ExecuteScalarAsync(c).ConfigureAwait(false))!;
            }

            await t.AuditAsync("DEVICE_REGISTER", $"device:{id}", $"Registered device '{label}'.", c).ConfigureAwait(false);
            return new RegisterDeviceResult(id, null);   // the token is never stored, not even in the idempotency record
        }, ct).ConfigureAwait(false);
        return result.Replayed ? result : new CommandResult<RegisterDeviceResult>(result.Value with { DeviceToken = token }, false);
    }

    public async Task<CommandResult<DeviceRevokedResult>> RevokeDeviceAsync(AuthenticatedSession actor, string idempotencyKey, RevokeDeviceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmUsers, ct: ct).ConfigureAwait(false);

        return await runner.RunAsync(actor.CommandContext("RevokeDevice", idempotencyKey), request, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            int sessions;
            await using (var u = t.Command("""
                UPDATE plat.device SET revoked_utc = @now, revoked_by = @by WHERE device_id = @d AND property_id = @p AND revoked_utc IS NULL;
                IF @@ROWCOUNT = 0 THROW 51100, 'No such active device.', 1;
                UPDATE plat.session SET revoked_utc = @now, revoked_reason = 'DEVICE_REVOKED' WHERE device_id = @d AND revoked_utc IS NULL;
                SELECT @@ROWCOUNT;
                """))
            {
                u.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
                u.Parameters.Add("@by", SqlDbType.Int).Value = actor.UserId;
                u.Parameters.Add("@d", SqlDbType.Int).Value = request.DeviceId;
                u.Parameters.Add("@p", SqlDbType.Int).Value = actor.PropertyId;
                try
                {
                    sessions = (int)(await u.ExecuteScalarAsync(c).ConfigureAwait(false))!;
                }
                catch (SqlException ex) when (ex.Number == 51100)
                {
                    throw new HccException(HccErrorCode.NOT_FOUND, "No such active device.");
                }
            }

            await t.AuditAsync("DEVICE_REVOKE", $"device:{request.DeviceId}", $"Device revoked; {sessions} open session(s) ended.", c).ConfigureAwait(false);
            return new DeviceRevokedResult(request.DeviceId, sessions);
        }, ct).ConfigureAwait(false);
    }

    // ---------- queries ----------

    public async Task<PagedResult<UserSummary>> ListUsersAsync(AuthenticatedSession actor, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmUsers, ct: ct).ConfigureAwait(false);
        (page, pageSize) = Paging(page, pageSize);
        var now = Now();
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command("""
            SELECT COUNT(*) FROM plat.app_user WHERE property_id = @p;
            SELECT u.user_id, u.login_name, u.display_name, u.enabled, u.row_ver,
                   CASE WHEN EXISTS (SELECT 1 FROM plat.credential c WHERE c.user_id = u.user_id AND c.locked_until_utc > @now) THEN 1 ELSE 0 END
              FROM plat.app_user u WHERE u.property_id = @p ORDER BY u.display_name, u.user_id OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            SELECT r.user_id, r.role_code, r.valid_from_utc, r.valid_until_utc FROM plat.user_role r JOIN plat.app_user u ON u.user_id = r.user_id WHERE u.property_id = @p;
            """, conn);
        q.Parameters.Add("@p", SqlDbType.Int).Value = actor.PropertyId;
        q.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
        q.Parameters.Add("@skip", SqlDbType.Int).Value = (page - 1) * pageSize;
        q.Parameters.Add("@take", SqlDbType.Int).Value = pageSize;
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        var total = rd.GetInt32(0);
        var rows = new List<(int Id, string Login, string Name, bool Enabled, byte[] Ver, bool Locked)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) rows.Add((rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetBoolean(3), (byte[])rd[4], rd.GetInt32(5) == 1));
        var roles = new List<(int UserId, UserRoleView Role)>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) roles.Add((rd.GetInt32(0), new UserRoleView(rd.GetString(1), rd.GetDateTime(2), rd.IsDBNull(3) ? null : rd.GetDateTime(3))));
        var items = rows.Select(r =>
        {
            var mine = roles.Where(x => x.UserId == r.Id).Select(x => x.Role).ToList();
            var kind = CredentialPolicy.RequiredKind(mine.Select(m => m.RoleCode));
            return new UserSummary(r.Id, r.Login, r.Name, r.Enabled, kind is null ? "NONE" : CredentialPolicy.ToWire(kind.Value), r.Locked, mine, SqlValues.RowVersionToWire(r.Ver));
        }).ToList();
        return new PagedResult<UserSummary>(items, page, pageSize, total);
    }

    public async Task<PagedResult<DeviceSummary>> ListDevicesAsync(AuthenticatedSession actor, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmUsers, ct: ct).ConfigureAwait(false);
        (page, pageSize) = Paging(page, pageSize);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command("""
            SELECT COUNT(*) FROM plat.device WHERE property_id = @p;
            SELECT device_id, label, registered_utc, last_seen_utc, revoked_utc FROM plat.device WHERE property_id = @p
             ORDER BY device_id OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """, conn);
        q.Parameters.Add("@p", SqlDbType.Int).Value = actor.PropertyId;
        q.Parameters.Add("@skip", SqlDbType.Int).Value = (page - 1) * pageSize;
        q.Parameters.Add("@take", SqlDbType.Int).Value = pageSize;
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        var total = rd.GetInt32(0);
        var items = new List<DeviceSummary>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            items.Add(new DeviceSummary(rd.GetInt32(0), rd.GetString(1), rd.GetDateTime(2), rd.IsDBNull(3) ? null : rd.GetDateTime(3), rd.IsDBNull(4) ? null : rd.GetDateTime(4)));
        return new PagedResult<DeviceSummary>(items, page, pageSize, total);
    }

    // ---------- bootstrap ----------

    public async Task<UserVersionResult> BootstrapOwnerAsync(BootstrapOwnerRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var login = UserAdminRules.NormalizeLogin(request.LoginName);
        var display = UserAdminRules.DisplayName(request.DisplayName);
        PasswordRules.Validate(request.Password, login);
        var hash = hasher.Hash(request.Password, options.PasswordIterations);
        var now = Now();
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await using (var lk = connections.Command("EXEC sp_getapplock @Resource = @r, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;", conn, tx))
            {
                lk.Parameters.Add("@r", SqlDbType.NVarChar, 255).Value = OwnersLock;
                await lk.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            int userId;
            await using (var ins = connections.Command("""
                IF EXISTS (SELECT 1 FROM plat.app_user) THROW 51101, 'Users already exist.', 1;
                IF NOT EXISTS (SELECT 1 FROM inv.property WHERE property_id = @p) THROW 51102, 'Unknown property.', 1;
                DECLARE @id TABLE(user_id INT);
                INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id INTO @id VALUES (@p, @l, @n, @now);
                DECLARE @u INT = (SELECT user_id FROM @id);
                INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, 'OWN', @now, @now);
                INSERT plat.credential(user_id, kind, algorithm, iterations, salt, secret_hash, changed_utc) VALUES (@u, 'PASSWORD', @alg, @it, @salt, @hash, @now);
                INSERT plat.audit_event(property_id, principal_id, action, target, detail, operation)
                  VALUES (@p, @u, 'OWNER_BOOTSTRAP', CONCAT('user:', @u), CONCAT(N'First Owner ''', @l, N''' created on an empty installation.'), 'BootstrapOwner');
                SELECT @u;
                """, conn, tx))
            {
                ins.Parameters.Add("@p", SqlDbType.Int).Value = request.PropertyId;
                ins.Parameters.Add("@l", SqlDbType.VarChar, 40).Value = login;
                ins.Parameters.Add("@n", SqlDbType.NVarChar, 100).Value = display;
                ins.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
                ins.Parameters.Add("@alg", SqlDbType.VarChar, 20).Value = hash.Algorithm;
                ins.Parameters.Add("@it", SqlDbType.Int).Value = hash.Iterations;
                ins.Parameters.Add("@salt", SqlDbType.VarBinary, 32).Value = hash.Salt;
                ins.Parameters.Add("@hash", SqlDbType.VarBinary, 64).Value = hash.Hash;
                try
                {
                    userId = (int)(await ins.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
                }
                catch (SqlException ex) when (ex.Number is 51101 or 51102)
                {
                    throw new HccException(HccErrorCode.FORBIDDEN, ex.Number == 51101 ? "The first Owner already exists." : "Unknown property.");
                }
            }

            string version;
            await using (var v = connections.Command("SELECT row_ver FROM plat.app_user WHERE user_id = @u;", conn, tx))
            {
                v.Parameters.Add("@u", SqlDbType.Int).Value = userId;
                version = SqlValues.RowVersionToWire((byte[])(await v.ExecuteScalarAsync(ct).ConfigureAwait(false))!);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return new UserVersionResult(userId, version);
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    // ---------- helpers ----------

    private sealed record Target(int UserId, string Login, bool Enabled, IReadOnlyList<string> AssignedRoles, bool IsActiveOwner);

    private DateTime Now() => SqlSessionService.Truncate(clock.GetUtcNow().UtcDateTime);

    private static (int, int) Paging(int page, int pageSize) =>
        (Math.Max(page, 1), pageSize is < 1 or > ApiConventions.MaxPageSize ? throw HccException.Validation($"pageSize must be 1–{ApiConventions.MaxPageSize}.") : pageSize);

    private static void EnsureNotSelf(AuthenticatedSession actor, int userId)
    {
        if (actor.UserId == userId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot change your own access (PERM-SOD-002).");
    }

    private static (CredentialKind, string) OneSecret(string? password, string? pin)
    {
        var hasPassword = !string.IsNullOrEmpty(password);
        if (hasPassword == !string.IsNullOrEmpty(pin)) throw HccException.Validation("Give either a password or a PIN.");
        return hasPassword ? (CredentialKind.Password, password!) : (CredentialKind.Pin, pin!);
    }

    private static async Task LockOwnersAsync(SqlCommandTransaction t, CancellationToken ct)
    {
        await using var lk = t.Command("EXEC sp_getapplock @Resource = @r, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;");
        lk.Parameters.Add("@r", SqlDbType.NVarChar, 255).Value = OwnersLock;
        await lk.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> ActiveRolesAsync(SqlCommandTransaction t, int userId, DateTime now, CancellationToken ct)
    {
        await using var q = t.Command("""
            SELECT r.role_code FROM plat.user_role r JOIN plat.app_user u ON u.user_id = r.user_id
             WHERE r.user_id = @u AND u.enabled = 1 AND r.valid_from_utc <= @now AND (r.valid_until_utc IS NULL OR r.valid_until_utc > @now)
               AND (u.valid_from_utc IS NULL OR u.valid_from_utc <= @now) AND (u.valid_until_utc IS NULL OR u.valid_until_utc > @now);
            """);
        q.Parameters.Add("@u", SqlDbType.Int).Value = userId;
        q.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) set.Add(rd.GetString(0));
        return set;
    }

    private static async Task<Target> LoadTargetAsync(SqlCommandTransaction t, AuthenticatedSession actor, int userId, DateTime now, CancellationToken ct)
    {
        string login;
        bool enabled;
        await using (var q = t.Command("SELECT login_name, enabled FROM plat.app_user WITH (UPDLOCK, ROWLOCK) WHERE user_id = @u AND property_id = @p;"))
        {
            q.Parameters.Add("@u", SqlDbType.Int).Value = userId;
            q.Parameters.Add("@p", SqlDbType.Int).Value = actor.PropertyId;
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "User not found.");
            login = rd.GetString(0);
            enabled = rd.GetBoolean(1);
        }

        var assigned = new List<string>();
        await using (var q = t.Command("SELECT role_code FROM plat.user_role WHERE user_id = @u;"))
        {
            q.Parameters.Add("@u", SqlDbType.Int).Value = userId;
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) assigned.Add(rd.GetString(0));
        }

        var active = await ActiveRolesAsync(t, userId, now, ct).ConfigureAwait(false);
        return new Target(userId, login, enabled, assigned, active.Contains(RoleCodes.Owner));
    }

    /// <summary>DEC-058: roles flagged owner_only_assign (money or admin permissions) are assigned/revoked only by an active Owner.</summary>
    private static async Task EnsureMayAssignAsync(SqlCommandTransaction t, IReadOnlySet<string> actorRoles, string roleCode, CancellationToken ct)
    {
        await using var q = t.Command("SELECT owner_only_assign FROM plat.role WHERE role_code = @r;");
        q.Parameters.Add("@r", SqlDbType.VarChar, 3).Value = roleCode ?? string.Empty;
        var flag = await q.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (flag is null) throw HccException.Validation($"Unknown role '{roleCode}'.");
        if ((bool)flag && !actorRoles.Contains(RoleCodes.Owner))
            throw new HccException(HccErrorCode.FORBIDDEN, $"Only an Owner can assign or remove the {roleCode} role (DEC-058).");
    }

    /// <summary>Changes to an Owner account (enable, credential, roles, exceptions) are made by another Owner only.</summary>
    private static async Task EnsureOwnerForOwnerAccountAsync(SqlCommandTransaction t, AuthenticatedSession actor, Target target, DateTime now, CancellationToken ct)
    {
        if (!target.AssignedRoles.Contains(RoleCodes.Owner)) return;
        var actorRoles = await ActiveRolesAsync(t, actor.UserId, now, ct).ConfigureAwait(false);
        if (!actorRoles.Contains(RoleCodes.Owner)) throw new HccException(HccErrorCode.FORBIDDEN, "Only an Owner can change an Owner account (DEC-058).");
    }

    /// <summary>PERM-SOD-003 / DEC-033: at least one other enabled, valid, active Owner must remain. Caller holds the owners lock.</summary>
    private static async Task EnsureAnotherActiveOwnerAsync(SqlCommandTransaction t, int exceptUserId, DateTime now, CancellationToken ct)
    {
        await using var q = t.Command("""
            SELECT COUNT(*) FROM plat.user_role r JOIN plat.app_user u ON u.user_id = r.user_id
             WHERE r.role_code = 'OWN' AND r.user_id <> @u AND u.enabled = 1
               AND r.valid_from_utc <= @now AND (r.valid_until_utc IS NULL OR r.valid_until_utc > @now)
               AND (u.valid_from_utc IS NULL OR u.valid_from_utc <= @now) AND (u.valid_until_utc IS NULL OR u.valid_until_utc > @now);
            """);
        q.Parameters.Add("@u", SqlDbType.Int).Value = exceptUserId;
        q.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
        if ((int)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))! == 0)
            throw new HccException(HccErrorCode.FORBIDDEN, "The last active Owner cannot be disabled or lose the Owner role (PERM-SOD-003).");
    }

    private static async Task UpsertRoleAsync(SqlCommandTransaction t, int userId, string roleCode, DateTime from, DateTime? until, int assignedBy, DateTime now, CancellationToken ct)
    {
        if (until is not null && until <= from) throw HccException.Validation("A role must end after it starts.");
        await using (var q = t.Command("""
            UPDATE plat.user_role SET valid_from_utc = @f, valid_until_utc = @t, assigned_by = @by, assigned_utc = @now WHERE user_id = @u AND role_code = @r;
            IF @@ROWCOUNT = 0
              INSERT plat.user_role(user_id, role_code, valid_from_utc, valid_until_utc, assigned_by, assigned_utc) VALUES (@u, @r, @f, @t, @by, @now);
            """))
        {
            q.Parameters.Add("@u", SqlDbType.Int).Value = userId;
            q.Parameters.Add("@r", SqlDbType.VarChar, 3).Value = roleCode;
            q.Parameters.Add("@f", SqlDbType.DateTime2).Value = from;
            q.Parameters.Add("@t", SqlDbType.DateTime2).Value = (object?)until ?? DBNull.Value;
            q.Parameters.Add("@by", SqlDbType.Int).Value = assignedBy;
            q.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
            await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (RoleCodes.PasswordRoles.Contains(roleCode))
        {
            // DEC-062: a user holding a password role can never sign in by PIN — drop any PIN credential now.
            await using var d = t.Command("DELETE plat.credential WHERE user_id = @u AND kind = 'PIN';");
            d.Parameters.Add("@u", SqlDbType.Int).Value = userId;
            await d.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task BumpAsync(SqlCommandTransaction t, int userId, byte[] expectedVersion, CancellationToken ct)
    {
        await using var u = t.Command("UPDATE plat.app_user SET security_stamp = security_stamp + 1 WHERE user_id = @u AND row_ver = @v;");
        u.Parameters.Add("@u", SqlDbType.Int).Value = userId;
        u.Parameters.Add("@v", SqlDbType.Binary, 8).Value = expectedVersion;
        SqlValues.EnsureVersionMatched(await u.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "The user");
    }

    private static async Task<string> VersionAsync(SqlCommandTransaction t, int userId, CancellationToken ct)
    {
        await using var q = t.Command("SELECT row_ver FROM plat.app_user WHERE user_id = @u;");
        q.Parameters.Add("@u", SqlDbType.Int).Value = userId;
        return SqlValues.RowVersionToWire((byte[])(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))!);
    }

    /// <summary>DEC-062 credential kind per role, PIN/password rules, PIN history (no reuse of the last N), lockout cleared.</summary>
    private async Task StoreCredentialAsync(SqlCommandTransaction t, Target target, CredentialKind kind, string secret, DateTime now, CancellationToken ct)
    {
        var required = CredentialPolicy.RequiredKind(target.AssignedRoles);
        if (required is not null && required != kind)
            throw HccException.Validation(required == CredentialKind.Password
                ? "Owner, Accountant and Tech admin accounts sign in with a password, never a PIN (DEC-062)."
                : "Staff accounts sign in with a PIN on a registered hotel device (DEC-062).");

        if (kind == CredentialKind.Pin)
        {
            PinRules.Validate(secret);
            await using var q = t.Command("""
                SELECT TOP (@n) algorithm, iterations, salt, secret_hash FROM plat.pin_history WHERE user_id = @u ORDER BY pin_history_id DESC;
                """);
            q.Parameters.Add("@n", SqlDbType.Int).Value = Math.Max(options.PinHistory, 1);
            q.Parameters.Add("@u", SqlDbType.Int).Value = target.UserId;
            var recent = new List<HashedSecret>();
            await using (var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) recent.Add(new HashedSecret(rd.GetString(0), rd.GetInt32(1), (byte[])rd[2], (byte[])rd[3]));
            }

            if (recent.Any(h => hasher.Verify(secret, h))) throw HccException.Validation($"Choose a PIN you have not used for your last {options.PinHistory} PINs.");
        }
        else
        {
            PasswordRules.Validate(secret, target.Login);
        }

        var hash = hasher.Hash(secret, kind == CredentialKind.Pin ? options.PinIterations : options.PasswordIterations);
        await using var up = t.Command("""
            UPDATE plat.credential SET algorithm = @alg, iterations = @it, salt = @salt, secret_hash = @hash, failed_count = 0, locked_until_utc = NULL, changed_utc = @now
             WHERE user_id = @u AND kind = @k;
            IF @@ROWCOUNT = 0
              INSERT plat.credential(user_id, kind, algorithm, iterations, salt, secret_hash, changed_utc) VALUES (@u, @k, @alg, @it, @salt, @hash, @now);
            IF @k = 'PIN'
              INSERT plat.pin_history(user_id, algorithm, iterations, salt, secret_hash, changed_utc) VALUES (@u, @alg, @it, @salt, @hash, @now);
            """);
        up.Parameters.Add("@u", SqlDbType.Int).Value = target.UserId;
        up.Parameters.Add("@k", SqlDbType.VarChar, 8).Value = CredentialPolicy.ToWire(kind);
        up.Parameters.Add("@alg", SqlDbType.VarChar, 20).Value = hash.Algorithm;
        up.Parameters.Add("@it", SqlDbType.Int).Value = hash.Iterations;
        up.Parameters.Add("@salt", SqlDbType.VarBinary, 32).Value = hash.Salt;
        up.Parameters.Add("@hash", SqlDbType.VarBinary, 64).Value = hash.Hash;
        up.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
        await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Checks the current secret before a self-service change; failures count towards the lockout and are committed.</summary>
    private async Task VerifyCurrentSecretAsync(AuthenticatedSession actor, CredentialKind kind, string current, CancellationToken ct)
    {
        var now = Now();
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        HccException? failure = null;
        await using (var q = connections.Command("""
            SELECT algorithm, iterations, salt, secret_hash, failed_count, locked_until_utc FROM plat.credential WITH (UPDLOCK, ROWLOCK) WHERE user_id = @u AND kind = @k;
            """, conn, tx))
        {
            q.Parameters.Add("@u", SqlDbType.Int).Value = actor.UserId;
            q.Parameters.Add("@k", SqlDbType.VarChar, 8).Value = CredentialPolicy.ToWire(kind);
            HashedSecret? stored = null;
            var failed = 0;
            DateTime? locked = null;
            await using (var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (await rd.ReadAsync(ct).ConfigureAwait(false))
                {
                    stored = new HashedSecret(rd.GetString(0), rd.GetInt32(1), (byte[])rd[2], (byte[])rd[3]);
                    failed = rd.GetInt32(4);
                    locked = rd.IsDBNull(5) ? null : rd.GetDateTime(5);
                }
            }

            if (stored is null) failure = HccException.Validation($"Your account does not use a {CredentialPolicy.ToWire(kind)}.");
            else if (locked is not null && now < locked) failure = new HccException(HccErrorCode.FORBIDDEN, "Too many wrong attempts. Try again later.");
            else if (!hasher.Verify(current, stored))
            {
                failed++;
                var lockNow = failed >= options.MaxFailedAttempts;
                await using var u = connections.Command("UPDATE plat.credential SET failed_count = @f, locked_until_utc = @l WHERE user_id = @u AND kind = @k;", conn, tx);
                u.Parameters.Add("@f", SqlDbType.Int).Value = lockNow ? 0 : failed;
                u.Parameters.Add("@l", SqlDbType.DateTime2).Value = lockNow ? now + options.LockoutDuration : DBNull.Value;
                u.Parameters.Add("@u", SqlDbType.Int).Value = actor.UserId;
                u.Parameters.Add("@k", SqlDbType.VarChar, 8).Value = CredentialPolicy.ToWire(kind);
                await u.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                failure = new HccException(HccErrorCode.FORBIDDEN, "The current password or PIN is wrong.");
            }
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        if (failure is not null) throw failure;
    }
}
