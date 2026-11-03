using System.Data;
using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Access;

/// <summary>
/// ADM-2 roles as data (02 Planning/agents/ADM_Briefs.md §2.2, migration 4800). Owner only: PERM-ADM-002 and an active OWN role (DEC-218).
/// Custom roles C01…C99 copy their base's grants and denies at creation into plat.role_origin_grant / plat.role_deny; plat.role_grant of a
/// custom role is always materialised by <see cref="RoleRules.Materialize"/> from that frozen copy + the grid (plat.role_feature) + the money
/// flag + the Advanced cells (plat.role_grant_override), so a later change to the base never flows down. Built-in roles are never edited
/// (DEC-216); the actor never edits a role they hold (DEC-218). Each command bumps plat.role.version, appends one plat.policy_version row
/// (who / when / summary / before / after JSON) and audits ROLE_&lt;VERB&gt; through <see cref="ICommandRunner"/> (Idempotency-Key, one transaction).
/// </summary>
public sealed class SqlRoleAdministration(SqlConnectionFactory connections, ICommandRunner runner, IAccessPolicy policy, TimeProvider clock) : IRoleAdministration
{
    private const string AdmRoles = "PERM-ADM-002";
    private const string RolesLock = "hcc.access.roles";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CommandResult<RoleChangeResult>> CreateRoleAsync(AuthenticatedSession actor, string idempotencyKey, CreateRoleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmRoles, ct: ct).ConfigureAwait(false);
        var name = RoleRules.NormalizeName(request.Name);
        var baseRole = (request.BaseRole ?? string.Empty).Trim().ToUpperInvariant();
        if (baseRole == RoleCodes.Owner) throw HccException.Validation("The Owner role is never a base for a custom role (DEC-216).");
        if (!RoleRules.CanBeBase(baseRole)) throw HccException.Validation($"Unknown base role '{request.BaseRole}'. Pick one of the built-in roles except Owner.");
        var money = RoleRules.EffectiveMoney(baseRole, request.Money ?? true);

        return await runner.RunAsync(actor.CommandContext("CreateRole", idempotencyKey), request with { Name = name, BaseRole = baseRole }, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            await LockAsync(t, c).ConfigureAwait(false);
            await EnsureOwnerAsync(t, actor, now, c).ConfigureAwait(false);
            await EnsureNameFreeAsync(t, name, null, c).ConfigureAwait(false);

            var used = new List<string>();
            await using (var q = t.Command("SELECT role_code FROM plat.role WITH (UPDLOCK, HOLDLOCK) WHERE is_custom = 1;"))
            {
                await using var rd = await q.ExecuteReaderAsync(c).ConfigureAwait(false);
                while (await rd.ReadAsync(c).ConfigureAwait(false)) used.Add(rd.GetString(0));
            }

            var code = RoleRules.NextCode(used);
            await using (var ins = t.Command("""
                INSERT plat.role(role_code, name, credential_kind, owner_only_assign, sort_order, is_custom, base_role, money, version, created_by, created_utc)
                SELECT @c, @n, credential_kind, owner_only_assign, @sort, 1, role_code, @m, 1, @by, @now FROM plat.role WHERE role_code = @b AND is_custom = 0;
                INSERT plat.role_origin_grant(role_code, permission_id, mode, scope, limit_value, limit_unit)
                SELECT @c, permission_id, mode, scope, limit_value, limit_unit FROM plat.role_grant WHERE role_code = @b;
                INSERT plat.role_deny(role_code, permission_id, reason) SELECT @c, permission_id, reason FROM plat.role_deny WHERE role_code = @b;
                """))
            {
                ins.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = code;
                ins.Parameters.Add("@n", SqlDbType.NVarChar, 60).Value = name;
                ins.Parameters.Add("@sort", SqlDbType.TinyInt).Value = (byte)(100 + int.Parse(code.AsSpan(1), CultureInfo.InvariantCulture));
                ins.Parameters.Add("@m", SqlDbType.Bit).Value = money;
                ins.Parameters.Add("@by", SqlDbType.Int).Value = actor.UserId;
                ins.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
                ins.Parameters.Add("@b", SqlDbType.VarChar, 3).Value = baseRole;
                await ins.ExecuteNonQueryAsync(c).ConfigureAwait(false);
            }

            await RebuildGrantsAsync(t, code, c).ConfigureAwait(false);
            var after = await SnapshotAsync(t, code, c).ConfigureAwait(false);
            var summary = $"Created role {code} '{name}' from {baseRole}{(money ? string.Empty : ", no money")}.";
            return await RecordAsync(t, actor, code, "ROLE_CREATE", summary, null, after, null, now, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<RoleChangeResult>> UpdateRoleAsync(AuthenticatedSession actor, string idempotencyKey, UpdateRoleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmRoles, ct: ct).ConfigureAwait(false);
        var name = request.Name is null ? null : RoleRules.NormalizeName(request.Name);
        if (name is null && request.Money is null) throw HccException.Validation("Give a new name or a money setting.");

        return await runner.RunAsync(actor.CommandContext("UpdateRole", idempotencyKey), request with { Name = name }, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            await LockAsync(t, c).ConfigureAwait(false);
            var role = await LoadEditableAsync(t, actor, request.RoleCode, request.ExpectedVersion, now, c).ConfigureAwait(false);
            if (request.Money == true && RoleRules.MoneyLocked(role.BaseRole!))
                throw new HccException(HccErrorCode.FORBIDDEN, $"A role based on {role.BaseRole} never holds money permissions (DEC-061).");
            if (name is not null) await EnsureNameFreeAsync(t, name, role.RoleCode, c).ConfigureAwait(false);
            var before = await SnapshotAsync(t, role.RoleCode, c).ConfigureAwait(false);
            var money = request.Money ?? role.Money;
            await using (var u = t.Command("UPDATE plat.role SET name = @n, money = @m WHERE role_code = @c;"))
            {
                u.Parameters.Add("@n", SqlDbType.NVarChar, 60).Value = name ?? role.Name;
                u.Parameters.Add("@m", SqlDbType.Bit).Value = money;
                u.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = role.RoleCode;
                await u.ExecuteNonQueryAsync(c).ConfigureAwait(false);
            }

            await RebuildGrantsAsync(t, role.RoleCode, c).ConfigureAwait(false);
            var after = await SnapshotAsync(t, role.RoleCode, c).ConfigureAwait(false);
            var parts = new List<string>();
            if (name is not null && name != role.Name) parts.Add($"renamed '{role.Name}' → '{name}'");
            if (money != role.Money) parts.Add(money ? "money access on" : "money access off");
            var summary = $"Role {role.RoleCode}: {(parts.Count == 0 ? "no change" : string.Join(", ", parts))}.";
            return await RecordAsync(t, actor, role.RoleCode, "ROLE_UPDATE", summary, before, after, null, now, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<RoleChangeResult>> SetRoleFeatureAsync(AuthenticatedSession actor, string idempotencyKey, SetRoleFeatureRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmRoles, ct: ct).ConfigureAwait(false);
        var access = RoleRules.Parse(request.Access);
        var feature = FeatureCatalog.Feature(request.FeatureId ?? string.Empty) ?? throw HccException.Validation($"Unknown feature '{request.FeatureId}'.");
        if (feature.Core) throw HccException.Validation($"{feature.Label} is a core sub-function: it follows the base role unchanged (DEC-215).");

        return await runner.RunAsync(actor.CommandContext("SetRoleFeature", idempotencyKey), request with { Access = RoleRules.ToWire(access) }, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            await LockAsync(t, c).ConfigureAwait(false);
            var role = await LoadEditableAsync(t, actor, request.RoleCode, request.ExpectedVersion, now, c).ConfigureAwait(false);
            var before = await SnapshotAsync(t, role.RoleCode, c).ConfigureAwait(false);
            var next = RoleRules.ApplyFeature(role.Features, feature.Id, access);
            await using (var d = t.Command("DELETE plat.role_feature WHERE role_code = @c;"))
            {
                d.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = role.RoleCode;
                await d.ExecuteNonQueryAsync(c).ConfigureAwait(false);
            }

            foreach (var (id, level) in next)
            {
                await using var ins = t.Command("INSERT plat.role_feature(role_code, feature_id, access) VALUES (@c, @f, @a);");
                ins.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = role.RoleCode;
                ins.Parameters.Add("@f", SqlDbType.VarChar, 40).Value = id;
                ins.Parameters.Add("@a", SqlDbType.VarChar, 4).Value = RoleRules.ToDb(level);
                await ins.ExecuteNonQueryAsync(c).ConfigureAwait(false);
            }

            await RebuildGrantsAsync(t, role.RoleCode, c).ConfigureAwait(false);
            var after = await SnapshotAsync(t, role.RoleCode, c).ConfigureAwait(false);
            var cascaded = next.Keys.Where(k => k != feature.Id && RoleRules.LevelOf(role.Features, k) != RoleRules.LevelOf(next, k)).ToList();
            var summary = $"Role {role.RoleCode}: {feature.Id} → {RoleRules.ToWire(access)}{(cascaded.Count == 0 ? string.Empty : $" (also {string.Join(", ", cascaded)})")}.";
            return await RecordAsync(t, actor, role.RoleCode, "ROLE_FEATURE", summary, before, after, null, now, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<RoleChangeResult>> SetRoleGrantAsync(AuthenticatedSession actor, string idempotencyKey, SetRoleGrantRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmRoles, ct: ct).ConfigureAwait(false);
        var permissionId = (request.PermissionId ?? string.Empty).Trim();
        GrantMode? mode = string.IsNullOrWhiteSpace(request.Mode) ? null
            : request.Mode.Trim() is { Length: 1 } m && "ALRVM".Contains(m[0], StringComparison.Ordinal) ? (GrantMode)m[0]
            : throw HccException.Validation("Mode must be A, L, R, V or M (or empty to remove the permission).");
        var scope = string.IsNullOrWhiteSpace(request.Scope) ? GrantScope.Property
            : request.Scope.Trim() is { Length: 1 } s && "PSAO".Contains(s[0], StringComparison.Ordinal) ? (GrantScope)s[0]
            : throw HccException.Validation("Scope must be P, S, A or O.");
        var cell = new GrantOverride(permissionId, mode, scope, mode is null ? null : request.Limit, mode is null ? null : request.LimitUnit);

        return await runner.RunAsync(actor.CommandContext("SetRoleGrant", idempotencyKey), request, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            await LockAsync(t, c).ConfigureAwait(false);
            var role = await LoadEditableAsync(t, actor, request.RoleCode, request.ExpectedVersion, now, c).ConfigureAwait(false);
            var permissions = await PermissionsAsync(t, c).ConfigureAwait(false);
            var info = permissions.FirstOrDefault(p => p.PermissionId == permissionId) ?? throw HccException.Validation($"Unknown permission id '{permissionId}'.");
            var origin = await OriginAsync(t, role.RoleCode, c).ConfigureAwait(false);
            RoleRules.ValidateOverride(role.BaseRole!, role.Money, info, cell, origin.FirstOrDefault(g => g.PermissionId == permissionId));
            var before = await SnapshotAsync(t, role.RoleCode, c).ConfigureAwait(false);

            await using (var up = t.Command("""
                UPDATE plat.role_grant_override SET mode = @m, scope = @s, limit_value = @lv, limit_unit = @lu WHERE role_code = @c AND permission_id = @perm;
                IF @@ROWCOUNT = 0
                  INSERT plat.role_grant_override(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES (@c, @perm, @m, @s, @lv, @lu);
                """))
            {
                up.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = role.RoleCode;
                up.Parameters.Add("@perm", SqlDbType.VarChar, 20).Value = permissionId;
                up.Parameters.Add("@m", SqlDbType.Char, 1).Value = cell.Mode is { } gm ? ((char)gm).ToString() : DBNull.Value;
                up.Parameters.Add("@s", SqlDbType.Char, 1).Value = ((char)cell.Scope).ToString();
                up.Parameters.Add("@lv", SqlDbType.BigInt).Value = (object?)cell.Limit ?? DBNull.Value;
                up.Parameters.Add("@lu", SqlDbType.VarChar, 5).Value = (object?)cell.LimitUnit ?? DBNull.Value;
                await up.ExecuteNonQueryAsync(c).ConfigureAwait(false);
            }

            await RebuildGrantsAsync(t, role.RoleCode, c).ConfigureAwait(false);
            var after = await SnapshotAsync(t, role.RoleCode, c).ConfigureAwait(false);
            var cellText = cell.Mode is { } md ? $"{(char)md}·{(char)cell.Scope}{(cell.Limit is { } l ? $" ≤ {l} {cell.LimitUnit}" : string.Empty)}" : "removed";
            var summary = $"Role {role.RoleCode}: {permissionId} {cellText} (Advanced).";
            return await RecordAsync(t, actor, role.RoleCode, "ROLE_GRANT", summary, before, after, RoleRules.AdvancedWarning(info, cell), now, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<RoleChangeResult>> RetireRoleAsync(AuthenticatedSession actor, string idempotencyKey, RetireRoleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        await policy.RequireAsync(actor.PropertyId, actor.UserId, AdmRoles, ct: ct).ConfigureAwait(false);

        return await runner.RunAsync(actor.CommandContext("RetireRole", idempotencyKey), request, async (itx, c) =>
        {
            var t = (SqlCommandTransaction)itx;
            var now = Now();
            await LockAsync(t, c).ConfigureAwait(false);
            var role = await LoadEditableAsync(t, actor, request.RoleCode, request.ExpectedVersion, now, c).ConfigureAwait(false);
            await using (var q = t.Command("""
                SELECT COUNT(*) FROM plat.user_role ur JOIN plat.app_user u ON u.user_id = ur.user_id
                 WHERE ur.role_code = @c AND u.enabled = 1 AND (ur.valid_until_utc IS NULL OR ur.valid_until_utc > @now);
                """))
            {
                q.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = role.RoleCode;
                q.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
                var held = (int)(await q.ExecuteScalarAsync(c).ConfigureAwait(false))!;
                if (held > 0) throw new HccException(HccErrorCode.FORBIDDEN, $"{held} enabled user(s) still hold the role '{role.Name}'. Give them another role first.");
            }

            var before = await SnapshotAsync(t, role.RoleCode, c).ConfigureAwait(false);
            await using (var u = t.Command("UPDATE plat.role SET retired_utc = @now WHERE role_code = @c; DELETE plat.role_grant WHERE role_code = @c;"))
            {
                u.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
                u.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = role.RoleCode;
                await u.ExecuteNonQueryAsync(c).ConfigureAwait(false);
            }

            return await RecordAsync(t, actor, role.RoleCode, "ROLE_RETIRE", $"Retired role {role.RoleCode} '{role.Name}'.", before, null, null, now, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<PolicyHistoryResult> HistoryAsync(AuthenticatedSession actor, int take, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var allowed = false;
        foreach (var p in new[] { "PERM-ADM-001", AdmRoles, "PERM-ADM-004" })
            allowed = allowed || await policy.HasAsync(actor.PropertyId, actor.UserId, p, ct: ct).ConfigureAwait(false);
        if (!allowed) throw new HccException(HccErrorCode.FORBIDDEN, "Not permitted: No permission.", SqlAccessPolicy.Details(("permissionId", AdmRoles)));
        if (take is < 1 or > ApiConventions.MaxPageSize) throw HccException.Validation($"take must be 1–{ApiConventions.MaxPageSize}.");
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command("""
            SELECT TOP (@take) v.version, v.role_code, v.action, v.changed_by, COALESCE(u.display_name, N'System'), v.changed_utc, v.summary, v.before_json, v.after_json
              FROM plat.policy_version v LEFT JOIN plat.app_user u ON u.user_id = v.changed_by
             WHERE v.property_id = @p ORDER BY v.version DESC;
            """, conn);
        q.Parameters.Add("@take", SqlDbType.Int).Value = take;
        q.Parameters.Add("@p", SqlDbType.Int).Value = actor.PropertyId;
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var items = new List<PolicyChangeView>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            items.Add(new PolicyChangeView(rd.GetInt32(0), rd.IsDBNull(1) ? null : rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetString(4),
                DateTime.SpecifyKind(rd.GetDateTime(5), DateTimeKind.Utc), rd.GetString(6), rd.IsDBNull(7) ? null : rd.GetString(7), rd.IsDBNull(8) ? null : rd.GetString(8)));
        return new PolicyHistoryResult(items);
    }

    // ---------- helpers ----------

    private sealed record EditableRole(string RoleCode, string Name, string? BaseRole, bool Money, int Version, IReadOnlyDictionary<string, RoleFeatureAccess> Features);

    private DateTime Now() => SqlSessionService.Truncate(clock.GetUtcNow().UtcDateTime);

    private static async Task LockAsync(SqlCommandTransaction t, CancellationToken ct)
    {
        await using var lk = t.Command("EXEC sp_getapplock @Resource = @r, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;");
        lk.Parameters.Add("@r", SqlDbType.NVarChar, 255).Value = RolesLock;
        await lk.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Held role codes of the actor (assigned, active now), for the Owner check and the own-role rule.</summary>
    private static async Task<HashSet<string>> ActorRolesAsync(SqlCommandTransaction t, int userId, DateTime now, CancellationToken ct)
    {
        await using var q = t.Command("""
            SELECT r.role_code FROM plat.user_role r JOIN plat.app_user u ON u.user_id = r.user_id
             WHERE r.user_id = @u AND u.enabled = 1 AND r.valid_from_utc <= @now AND (r.valid_until_utc IS NULL OR r.valid_until_utc > @now);
            """);
        q.Parameters.Add("@u", SqlDbType.Int).Value = userId;
        q.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) set.Add(rd.GetString(0));
        return set;
    }

    /// <summary>DEC-218: roles are edited by an active Owner only (PERM-ADM-002 alone is not enough).</summary>
    private static async Task<HashSet<string>> EnsureOwnerAsync(SqlCommandTransaction t, AuthenticatedSession actor, DateTime now, CancellationToken ct)
    {
        var mine = await ActorRolesAsync(t, actor.UserId, now, ct).ConfigureAwait(false);
        if (!mine.Contains(RoleCodes.Owner)) throw new HccException(HccErrorCode.FORBIDDEN, "Only an Owner can create or change roles (DEC-218).");
        return mine;
    }

    private static async Task EnsureNameFreeAsync(SqlCommandTransaction t, string name, string? exceptCode, CancellationToken ct)
    {
        await using var q = t.Command("SELECT COUNT(*) FROM plat.role WHERE UPPER(name) = UPPER(@n) AND (@x IS NULL OR role_code <> @x);");
        q.Parameters.Add("@n", SqlDbType.NVarChar, 60).Value = name;
        q.Parameters.Add("@x", SqlDbType.VarChar, 3).Value = (object?)exceptCode ?? DBNull.Value;
        if ((int)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0) throw HccException.Validation($"A role named '{name}' already exists.");
    }

    /// <summary>The custom role being edited: built-ins and retired roles are refused, so is a role the actor holds; the version must match when given.</summary>
    private static async Task<EditableRole> LoadEditableAsync(SqlCommandTransaction t, AuthenticatedSession actor, string? roleCode, int? expectedVersion, DateTime now, CancellationToken ct)
    {
        var mine = await EnsureOwnerAsync(t, actor, now, ct).ConfigureAwait(false);
        var code = (roleCode ?? string.Empty).Trim().ToUpperInvariant();
        string name;
        string? baseRole;
        bool custom, money, retired;
        int version;
        await using (var q = t.Command("SELECT name, is_custom, base_role, money, version, retired_utc FROM plat.role WITH (UPDLOCK, ROWLOCK) WHERE role_code = @c;"))
        {
            q.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = code;
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, $"Unknown role '{roleCode}'.");
            name = rd.GetString(0);
            custom = rd.GetBoolean(1);
            baseRole = rd.IsDBNull(2) ? null : rd.GetString(2);
            money = rd.GetBoolean(3);
            version = rd.GetInt32(4);
            retired = !rd.IsDBNull(5);
        }

        if (!custom || baseRole is null) throw new HccException(HccErrorCode.FORBIDDEN, $"{name} is a built-in role: built-in roles are read-only templates (DEC-216). Create a custom role from it instead.");
        if (retired) throw HccException.Validation($"The role '{name}' is retired.");
        if (mine.Contains(code)) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot change a role you hold yourself (DEC-218).");
        if (expectedVersion is { } v && v != version) throw new HccException(HccErrorCode.STALE_VERSION, $"The role '{name}' was changed by someone else. Reload and try again.");

        var features = new Dictionary<string, RoleFeatureAccess>(StringComparer.Ordinal);
        await using (var q = t.Command("SELECT feature_id, access FROM plat.role_feature WHERE role_code = @c;"))
        {
            q.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = code;
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) features[rd.GetString(0)] = RoleRules.Parse(rd.GetString(1));
        }

        return new EditableRole(code, name, baseRole, money, version, features);
    }

    private static async Task<IReadOnlyList<PermissionInfo>> PermissionsAsync(SqlCommandTransaction t, CancellationToken ct)
    {
        var list = new List<PermissionInfo>();
        await using var q = t.Command("SELECT permission_id, is_money FROM plat.permission;");
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new PermissionInfo(rd.GetString(0), rd.GetBoolean(1)));
        return list;
    }

    private static async Task<IReadOnlyList<RoleGrant>> OriginAsync(SqlCommandTransaction t, string roleCode, CancellationToken ct)
    {
        var list = new List<RoleGrant>();
        await using var q = t.Command("SELECT permission_id, mode, scope, limit_value, limit_unit FROM plat.role_origin_grant WHERE role_code = @c;");
        q.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = roleCode;
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new RoleGrant(roleCode, rd.GetString(0), (GrantMode)rd.GetString(1)[0], (GrantScope)rd.GetString(2)[0], rd.IsDBNull(3) ? null : rd.GetInt64(3), rd.IsDBNull(4) ? null : rd.GetString(4)));
        return list;
    }

    /// <summary>Recomputes plat.role_grant of a custom role from its frozen base copy, grid, money flag and Advanced cells (<see cref="RoleRules.Materialize"/>).</summary>
    private static async Task RebuildGrantsAsync(SqlCommandTransaction t, string roleCode, CancellationToken ct)
    {
        bool money;
        await using (var q = t.Command("SELECT money FROM plat.role WHERE role_code = @c;"))
        {
            q.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = roleCode;
            money = (bool)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        var features = new Dictionary<string, RoleFeatureAccess>(StringComparer.Ordinal);
        var overrides = new List<GrantOverride>();
        await using (var q = t.Command("""
            SELECT feature_id, access FROM plat.role_feature WHERE role_code = @c;
            SELECT permission_id, mode, scope, limit_value, limit_unit FROM plat.role_grant_override WHERE role_code = @c;
            """))
        {
            q.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = roleCode;
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) features[rd.GetString(0)] = RoleRules.Parse(rd.GetString(1));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                overrides.Add(new GrantOverride(rd.GetString(0), rd.IsDBNull(1) ? null : (GrantMode)rd.GetString(1)[0], (GrantScope)rd.GetString(2)[0],
                    rd.IsDBNull(3) ? null : rd.GetInt64(3), rd.IsDBNull(4) ? null : rd.GetString(4)));
        }

        var grants = RoleRules.Materialize(roleCode, money, features, await OriginAsync(t, roleCode, ct).ConfigureAwait(false), overrides,
            await PermissionsAsync(t, ct).ConfigureAwait(false));
        await using (var d = t.Command("DELETE plat.role_grant WHERE role_code = @c;"))
        {
            d.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = roleCode;
            await d.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var g in grants)
        {
            await using var ins = t.Command("INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES (@c, @perm, @m, @s, @lv, @lu);");
            ins.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = roleCode;
            ins.Parameters.Add("@perm", SqlDbType.VarChar, 20).Value = g.PermissionId;
            ins.Parameters.Add("@m", SqlDbType.Char, 1).Value = ((char)g.Mode).ToString();
            ins.Parameters.Add("@s", SqlDbType.Char, 1).Value = ((char)g.Scope).ToString();
            ins.Parameters.Add("@lv", SqlDbType.BigInt).Value = (object?)g.Limit ?? DBNull.Value;
            ins.Parameters.Add("@lu", SqlDbType.VarChar, 5).Value = (object?)g.LimitUnit ?? DBNull.Value;
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>The role as JSON text for plat.policy_version before / after: name, base, money, non-full features and the effective grants.</summary>
    private static async Task<string> SnapshotAsync(SqlCommandTransaction t, string roleCode, CancellationToken ct)
    {
        string name = string.Empty;
        string? baseRole = null;
        var money = false;
        var features = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var grants = new List<string>();
        await using var q = t.Command("""
            SELECT name, base_role, money FROM plat.role WHERE role_code = @c;
            SELECT feature_id, access FROM plat.role_feature WHERE role_code = @c ORDER BY feature_id;
            SELECT permission_id, mode, scope, limit_value, limit_unit FROM plat.role_grant WHERE role_code = @c ORDER BY permission_id;
            """);
        q.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = roleCode;
        await using (var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                name = rd.GetString(0);
                baseRole = rd.IsDBNull(1) ? null : rd.GetString(1);
                money = rd.GetBoolean(2);
            }

            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) features[rd.GetString(0)] = rd.GetString(1).ToUpperInvariant() == "VIEW" ? "view" : rd.GetString(1).ToUpperInvariant() == "OFF" ? "off" : "full";
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                grants.Add($"{rd.GetString(0)} {rd.GetString(1)}·{rd.GetString(2)}{(rd.IsDBNull(3) ? string.Empty : " ≤ " + rd.GetInt64(3).ToString(CultureInfo.InvariantCulture) + " " + (rd.IsDBNull(4) ? string.Empty : rd.GetString(4)))}");
        }

        return JsonSerializer.Serialize(new { roleCode, name, baseRole, money, features, grants }, Json);
    }

    /// <summary>Bumps the role version, appends the policy version row and the audit row; returns the result.</summary>
    private static async Task<RoleChangeResult> RecordAsync(SqlCommandTransaction t, AuthenticatedSession actor, string roleCode, string action, string summary,
        string? before, string? after, string? warning, DateTime now, CancellationToken ct)
    {
        int roleVersion, policyVersion;
        await using (var q = t.Command("""
            UPDATE plat.role SET version = version + 1 WHERE role_code = @c AND @a <> 'ROLE_CREATE';
            DECLARE @v TABLE(version INT);
            INSERT plat.policy_version(property_id, version, role_code, action, changed_by, changed_utc, summary, before_json, after_json)
            OUTPUT INSERTED.version INTO @v
            SELECT @p, COALESCE(MAX(version), 0) + 1, @c, @a, @by, @now, @s, @before, @after FROM plat.policy_version WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p;
            SELECT (SELECT version FROM plat.role WHERE role_code = @c), (SELECT version FROM @v);
            """))
        {
            q.Parameters.Add("@c", SqlDbType.VarChar, 3).Value = roleCode;
            q.Parameters.Add("@a", SqlDbType.VarChar, 30).Value = action;
            q.Parameters.Add("@p", SqlDbType.Int).Value = actor.PropertyId;
            q.Parameters.Add("@by", SqlDbType.Int).Value = actor.UserId;
            q.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
            q.Parameters.Add("@s", SqlDbType.NVarChar, 200).Value = Clip(summary, 200);
            q.Parameters.Add("@before", SqlDbType.NVarChar, -1).Value = (object?)before ?? DBNull.Value;
            q.Parameters.Add("@after", SqlDbType.NVarChar, -1).Value = (object?)after ?? DBNull.Value;
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            roleVersion = rd.GetInt32(0);
            policyVersion = rd.GetInt32(1);
        }

        await t.AuditAsync(action, $"role:{roleCode}", Clip($"{summary} Policy version {policyVersion.ToString(CultureInfo.InvariantCulture)}.", 1000), ct).ConfigureAwait(false);
        return new RoleChangeResult(roleCode, roleVersion, policyVersion, warning);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>ADM-2 per-role feature levels (brief §2.3), read per request from plat.user_role / plat.role / plat.role_feature.</summary>
public sealed class SqlFeatureAccess(SqlConnectionFactory connections, TimeProvider clock) : IFeatureAccess
{
    public async Task<RoleFeatureAccess> LevelAsync(int propertyId, int principalId, string featureId, CancellationToken ct = default) =>
        RoleRules.UserLevel(await HeldAsync(propertyId, principalId, ct).ConfigureAwait(false), featureId);

    public async Task<IReadOnlyDictionary<string, RoleFeatureAccess>> LevelsAsync(int propertyId, int principalId, CancellationToken ct = default) =>
        RoleRules.UserLevels(await HeldAsync(propertyId, principalId, ct).ConfigureAwait(false));

    public async Task EnsureFullAsync(int propertyId, int principalId, string featureId, CancellationToken ct = default)
    {
        var feature = FeatureCatalog.Feature(featureId);
        if (feature is null || feature.Core) return;
        var held = await HeldAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var level = RoleRules.UserLevel(held, featureId);
        if (level == RoleFeatureAccess.Full) return;
        var role = held.First(r => RoleRules.LevelOf(r, featureId) == level);
        throw RoleRules.FeatureRefusal(feature, level, role.RoleCode, role.Name);
    }

    /// <summary>The roles the user holds now (assigned, active), each with its non-full feature levels.</summary>
    private async Task<IReadOnlyList<RoleDefinition>> HeldAsync(int propertyId, int principalId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command("""
            SELECT ro.role_code, ro.name, ro.is_custom, ro.base_role, ro.money, ro.version, rf.feature_id, rf.access
              FROM plat.user_role r JOIN plat.app_user u ON u.user_id = r.user_id JOIN plat.role ro ON ro.role_code = r.role_code
              LEFT JOIN plat.role_feature rf ON rf.role_code = ro.role_code
             WHERE r.user_id = @u AND u.property_id = @p AND r.valid_from_utc <= @now AND (r.valid_until_utc IS NULL OR r.valid_until_utc > @now);
            """, conn);
        q.Parameters.Add("@u", SqlDbType.Int).Value = principalId;
        q.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        q.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
        var roles = new Dictionary<string, (string Name, bool Custom, string? Base, bool Money, int Version, Dictionary<string, RoleFeatureAccess> Features)>(StringComparer.Ordinal);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var code = rd.GetString(0);
            if (!roles.TryGetValue(code, out var r))
            {
                r = (rd.GetString(1), rd.GetBoolean(2), rd.IsDBNull(3) ? null : rd.GetString(3), rd.GetBoolean(4), rd.GetInt32(5), new Dictionary<string, RoleFeatureAccess>(StringComparer.Ordinal));
                roles[code] = r;
            }

            if (!rd.IsDBNull(6)) r.Features[rd.GetString(6)] = RoleRules.Parse(rd.GetString(7));
        }

        return roles.Select(kv => new RoleDefinition(kv.Key, kv.Value.Name, kv.Value.Custom, kv.Value.Base, kv.Value.Money, kv.Value.Features, kv.Value.Version)).ToList();
    }
}
